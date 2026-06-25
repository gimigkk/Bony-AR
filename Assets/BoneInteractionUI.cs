using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public struct BoneTapResult
{
    public readonly BoneInfo Bone;
    public readonly Transform BoneTransform;
    public readonly GameObject Skeleton;

    public BoneTapResult(BoneInfo bone, Transform boneTransform, GameObject skeleton)
    {
        Bone = bone;
        BoneTransform = boneTransform;
        Skeleton = skeleton;
    }
}

public class BoneInteractionUI : MonoBehaviour
{
    private const float LineThickness = 4f;
    private const float LabelHeight = 52f;
    private const float TapColliderPadding = 1.35f;
    private const float MinimumTapColliderLocalSize = 4f;

    private static readonly Color Purple = new Color(0.37f, 0.09f, 0.92f, 1f);
    private static readonly Color Black = Color.black;
    private static readonly Color White = Color.white;
    private static readonly Color PanelBlack = new Color(0f, 0f, 0f, 0.94f);
    private static readonly Color HighlightColor = new Color(1f, 0.18f, 0.12f, 1f);

    private static BoneInteractionUI instance;

    public static BoneInteractionUI Instance
    {
        get { return instance; }
    }



    private readonly Dictionary<string, Transform> boneTransforms = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Collider, string> colliderObjectNames = new Dictionary<Collider, string>();
    private readonly List<TrackedLabel> trackedLabels = new List<TrackedLabel>();
    private readonly List<TrackedLabel> labelPool = new List<TrackedLabel>();
    private readonly List<Renderer> highlightedRenderers = new List<Renderer>();

    private Material sharedInstancedMaterial;
    private const int BonePhysicsLayer = 8;

    private BoneInfoDatabase database;
    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform overlayRoot;
    private Camera mainCamera;
    private GameObject trackedObject;

    private GameObject infoPanelObject;
    public bool IsInfoPanelActive => infoPanelObject != null && infoPanelObject.activeSelf;
    private TMP_Text infoTitleText;
    private TMP_Text infoLatinText;
    private TMP_Text infoDescriptionText;
    private TMP_Text infoFactText;

    private MaterialPropertyBlock highlightBlock;
    private BoneViewer boneViewer;

    private string selectedBoneName;
    private string hoveredBoneName;
    private Transform hoveredBoneTransform;

    private GameObject crosshairObject;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject runtimeObject = new GameObject("Bone Interaction UI Runtime");
        instance = runtimeObject.AddComponent<BoneInteractionUI>();
    }

    private IEnumerator Start()
    {
        database = BoneInfoDatabase.Load();

        for (int i = 0; i < 90 && !ResolveSceneUi(); i++)
        {
            yield return null;
        }

        if (canvas == null)
        {
            Debug.LogWarning("BoneInteractionUI: App UI canvas was not found.");
            enabled = false;
            yield break;
        }

        BuildRuntimeControls();

        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null)
        {
            registry.ActiveSkeletonChanged += SetTrackedSkeleton;
        }

        TryFindActiveSkeleton();

        GameObject labelBtn = GameObject.Find("Label Button");
        if (labelBtn != null)
        {
            Destroy(labelBtn);
        }
    }

    private void OnDestroy()
    {
        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null)
        {
            registry.ActiveSkeletonChanged -= SetTrackedSkeleton;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    private void LateUpdate()
    {
        if (IsInfoPanelActive && overlayRoot != null && overlayRoot.parent != null &&
            overlayRoot.GetSiblingIndex() != overlayRoot.parent.childCount - 1)
        {
            overlayRoot.SetAsLastSibling();
        }
    }

    private void Update()
    {
        if (canvas == null)
        {
            return;
        }

        UpdateCameraReference();

        if (trackedObject == null || !trackedObject.activeInHierarchy)
        {
            TryFindActiveSkeleton();
        }
        UpdateCrosshairRaycast();
        UpdateTrackedLabelPositions();
        HandleBoneTap();
    }

    private float lastRaycastTime = 0f;
    private const float RaycastInterval = 0.066f;

    private void UpdateCrosshairRaycast()
    {
        if (mainCamera == null || trackedObject == null)
        {
            if (hoveredBoneName != null)
            {
                ClearHighlight();
                ClearTrackedLabels();
                hoveredBoneName = null;
                hoveredBoneTransform = null;
            }
            if (crosshairObject != null && crosshairObject.activeSelf) crosshairObject.SetActive(false);
            return;
        }

        ARAppModeController appMode = ARAppModeController.Instance;
        bool isInteractiveMode = appMode == null || appMode.CurrentMode == ARAppMode.Skeleton || appMode.CurrentMode == ARAppMode.Quiz;
        if (!isInteractiveMode)
        {
            if (crosshairObject != null && crosshairObject.activeSelf) crosshairObject.SetActive(false);
            return;
        }

        if (crosshairObject != null && !crosshairObject.activeSelf) crosshairObject.SetActive(true);

        if (Time.time - lastRaycastTime < RaycastInterval) return;
        lastRaycastTime = Time.time;

        int layerMask = 1 << BonePhysicsLayer;
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, layerMask))
        {
            string objectName;
            if (!colliderObjectNames.TryGetValue(hit.collider, out objectName))
            {
                objectName = FindKnownBoneName(hit.collider.transform);
            }

            if (!string.IsNullOrWhiteSpace(objectName))
            {
                if (hoveredBoneName != objectName)
                {
                    // Hit a NEW bone — swap label/highlight
                    ClearHighlight();
                    ClearTrackedLabels();

                    hoveredBoneName = objectName;
                    hoveredBoneTransform = hit.collider.transform;

                    if (hoveredBoneName != selectedBoneName)
                    {
                        HighlightBone(hoveredBoneTransform);
                        CreateTrackedLabel(GetDisplayName(hoveredBoneName), hoveredBoneTransform, CalculateDynamicLabelOffset(), null);
                    }
                }
                else if (hoveredBoneName == selectedBoneName && highlightedRenderers.Count > 0)
                {
                    ClearHighlight();
                }
                return;
            }
        }

        // Raycast missed — keep the existing label/highlight visible until we land on a new bone.
        // Only fully reset if there was never a hovered bone to begin with.
    }

    private Vector2 CalculateDynamicLabelOffset()
    {
        if (trackedObject == null || mainCamera == null)
            return new Vector2(320f, 160f);

        Vector3 skeletonCenter = trackedObject.transform.position;
        Vector3 skeletonScreenPos = mainCamera.WorldToScreenPoint(skeletonCenter);

        float signX = skeletonScreenPos.x > (Screen.width * 0.5f) ? -1f : 1f;
        return new Vector2(signX * 320f, 160f);
    }

    private bool ResolveSceneUi()
    {
        GameObject appUi = GameObject.Find("App UI");
        if (appUi == null) return false;

        canvas = appUi.GetComponent<Canvas>();
        canvasRect = appUi.GetComponent<RectTransform>();
        UpdateCameraReference();

        return canvas != null && canvasRect != null;
    }

    private static T FindChildComponent<T>(Transform parent, string childName) where T : Component
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child.GetComponent<T>();
            }
        }

        return null;
    }

    private void BuildRuntimeControls()
    {
        overlayRoot = CreateRect("Bone Interaction Overlay", canvas.transform);
        overlayRoot.anchorMin = Vector2.zero;
        overlayRoot.anchorMax = Vector2.one;
        overlayRoot.offsetMin = Vector2.zero;
        overlayRoot.offsetMax = Vector2.zero;
        overlayRoot.SetAsLastSibling();

        crosshairObject = new GameObject("Crosshair", typeof(RectTransform));
        crosshairObject.transform.SetParent(overlayRoot, false);
        RectTransform crosshairRect = crosshairObject.GetComponent<RectTransform>();
        crosshairRect.anchorMin = new Vector2(0.5f, 0.5f);
        crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
        crosshairRect.sizeDelta = new Vector2(20f, 20f);

        GameObject hLine = new GameObject("HLine", typeof(RectTransform), typeof(Image));
        hLine.transform.SetParent(crosshairObject.transform, false);
        RectTransform hRect = hLine.GetComponent<RectTransform>();
        hRect.anchorMin = new Vector2(0.5f, 0.5f);
        hRect.anchorMax = new Vector2(0.5f, 0.5f);
        hRect.anchoredPosition = Vector2.zero;
        hRect.sizeDelta = new Vector2(20f, 2f);
        Image hImage = hLine.GetComponent<Image>();
        hImage.color = new Color(1f, 1f, 1f, 0.8f);

        GameObject vLine = new GameObject("VLine", typeof(RectTransform), typeof(Image));
        vLine.transform.SetParent(crosshairObject.transform, false);
        RectTransform vRect = vLine.GetComponent<RectTransform>();
        vRect.anchorMin = new Vector2(0.5f, 0.5f);
        vRect.anchorMax = new Vector2(0.5f, 0.5f);
        vRect.anchoredPosition = Vector2.zero;
        vRect.sizeDelta = new Vector2(2f, 20f);
        Image vImage = vLine.GetComponent<Image>();
        vImage.color = new Color(1f, 1f, 1f, 0.8f);

        crosshairObject.SetActive(false);

        CreateInfoPanel();
    }

    private void TryFindActiveSkeleton()
    {
        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null && registry.ActiveSkeleton != null && registry.ActiveSkeleton.activeInHierarchy)
        {
            SetTrackedSkeleton(registry.ActiveSkeleton);
            return;
        }

        TryFindPreviewSkeleton();
    }

    private void TryFindPreviewSkeleton()
    {
        GameObject previewSkeleton = GameObject.Find("skeleton");
        if (previewSkeleton == null || !previewSkeleton.activeInHierarchy)
        {
            return;
        }

        SetTrackedSkeleton(previewSkeleton);
    }

    public void SetTrackedSkeleton(GameObject skeleton)
    {
        if (trackedObject == skeleton && (skeleton == null || boneTransforms.Count > 0))
        {
            return;
        }

        trackedObject = skeleton;
        RebuildBoneIndex();
        ClearInteractions();
    }

    private void RebuildBoneIndex()
    {
        boneTransforms.Clear();
        colliderObjectNames.Clear();

        if (trackedObject == null)
        {
            return;
        }

        foreach (Transform child in trackedObject.GetComponentsInChildren<Transform>(true))
        {
            if (!boneTransforms.ContainsKey(child.name))
            {
                boneTransforms.Add(child.name, child);
            }
        }

        if (sharedInstancedMaterial == null)
        {
            Renderer firstRenderer = trackedObject.GetComponentInChildren<Renderer>();
            if (firstRenderer != null && firstRenderer.sharedMaterial != null)
            {
                sharedInstancedMaterial = Instantiate(firstRenderer.sharedMaterial);
                sharedInstancedMaterial.enableInstancing = true;
            }
        }

        AddTapColliders(trackedObject.transform);
    }

    private void AddTapColliders(Transform parent)
    {
        foreach (KeyValuePair<string, Transform> pair in boneTransforms)
        {
            Transform bone = pair.Value;
            if (bone == null || bone == trackedObject.transform)
            {
                continue;
            }

            Renderer renderer = bone.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (sharedInstancedMaterial == null && renderer.sharedMaterial != null)
                {
                    sharedInstancedMaterial = Instantiate(renderer.sharedMaterial);
                    sharedInstancedMaterial.enableInstancing = true;
                }
                
                if (sharedInstancedMaterial != null)
                {
                    renderer.sharedMaterial = sharedInstancedMaterial;
                }
            }

            bone.gameObject.layer = BonePhysicsLayer;

            MeshFilter meshFilter = bone.GetComponent<MeshFilter>();

            Collider existingCollider = bone.GetComponent<Collider>();
            if (existingCollider != null)
            {
                if (!(existingCollider is MeshCollider))
                {
                    Destroy(existingCollider);
                    existingCollider = null;
                }
                else
                {
                    colliderObjectNames[existingCollider] = pair.Key;
                    continue;
                }
            }

            if (meshFilter == null || meshFilter.sharedMesh == null || renderer == null)
            {
                continue;
            }

            MeshCollider collider = bone.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            colliderObjectNames[collider] = pair.Key;
        }
    }

    private static Vector3 GetTapColliderSize(Vector3 meshSize)
    {
        Vector3 paddedSize = meshSize * TapColliderPadding;
        return new Vector3(
            Mathf.Max(paddedSize.x, MinimumTapColliderLocalSize),
            Mathf.Max(paddedSize.y, MinimumTapColliderLocalSize),
            Mathf.Max(paddedSize.z, MinimumTapColliderLocalSize));
    }

    private void SelectBone(string boneName, Transform boneTransform)
    {
        selectedBoneName = boneName;
        ClearHighlight();
        ClearTrackedLabels();

        if (boneViewer != null)
        {
            boneViewer.ShowBone(boneTransform, trackedObject != null ? trackedObject.transform : null);
        }

        BoneInfo boneInfo = null;
        if (database != null)
        {
            database.TryGet(boneName, out boneInfo);
        }

        UpdateInfoPanel(boneInfo, boneName);
        infoPanelObject.SetActive(true);
    }

    private void CloseInfoPanel()
    {
        if (infoPanelObject != null) infoPanelObject.SetActive(false);
        if (boneViewer != null) boneViewer.Clear();
        selectedBoneName = null;
    }

    private void HideInteractions()
    {
        CloseInfoPanel();
        ClearTrackedLabels();
        ClearHighlight();
    }

    private void HandleBoneTap()
    {
        if (mainCamera == null || trackedObject == null || Mouse.current == null && Touchscreen.current == null) return;
        if (!TryGetPointerPress(out Vector2 screenPosition, out int pointerId)) return;
        if (IsPointerOverUI(screenPosition)) return;
        
        if (hoveredBoneName == null || hoveredBoneTransform == null)
        {
            if (selectedBoneName != null)
            {
                CloseInfoPanel();
            }
            return;
        }

        string tappedBoneName = hoveredBoneName;
        Transform tappedTransform = hoveredBoneTransform;

        BoneInfo tappedBoneInfo = null;
        if (database != null) database.TryGet(tappedBoneName, out tappedBoneInfo);

        BoneTapResult tapResult = new BoneTapResult(tappedBoneInfo, tappedTransform, trackedObject);
        ARAppModeController appMode = ARAppModeController.Instance;
        if (appMode != null)
        {
            if (appMode.CurrentMode == ARAppMode.Quiz)
            {
                appMode.HandleBoneTap(tapResult);
                return;
            }
            if (appMode.CurrentMode != ARAppMode.Skeleton) return;
        }

        SelectBone(tappedBoneName, tappedTransform);
    }

    public Transform GetBoneTransform(string objectName)
    {
        return FindBone(objectName);
    }

    private static bool TryGetPointerPress(out Vector2 screenPosition, out int pointerId)
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            pointerId = Touchscreen.current.primaryTouch.touchId.ReadValue();
            return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            pointerId = -1; // Standard mouse left button ID in Unity UI
            return true;
        }

        screenPosition = Vector2.zero;
        pointerId = -1;
        return false;
    }

    private static bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;
        
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = screenPosition
        };
        
        System.Collections.Generic.List<RaycastResult> results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        
        return results.Count > 0;
    }

    private string FindKnownBoneName(Transform transform)
    {
        while (transform != null)
        {
            if (boneTransforms.ContainsKey(transform.name)) return transform.name;
            transform = transform.parent;
        }
        return null;
    }

    private Transform FindBone(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return null;
        return boneTransforms.TryGetValue(objectName, out Transform boneTransform) ? boneTransform : null;
    }

    public static string GetDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "Unknown Bone";

        string formatted = rawName.Replace('_', ' ');
        string[] words = formatted.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
        }
        formatted = string.Join(" ", words);

        if (formatted.EndsWith(".l", StringComparison.OrdinalIgnoreCase))
        {
            formatted = formatted.Substring(0, formatted.Length - 2) + " Left";
        }
        else if (formatted.EndsWith(".r", StringComparison.OrdinalIgnoreCase))
        {
            formatted = formatted.Substring(0, formatted.Length - 2) + " Right";
        }

        return formatted;
    }

    private void UpdateCameraReference()
    {
        if (mainCamera != null && mainCamera.gameObject.name != "BoneViewer_Camera")
        {
            return;
        }

        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            var arManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>();
            if (arManager != null)
            {
                mainCamera = arManager.GetComponent<Camera>();
            }
        }

        if (mainCamera == null)
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera cam in cameras)
            {
                if (cam.gameObject.name != "BoneViewer_Camera" && cam.enabled)
                {
                    mainCamera = cam;
                    break;
                }
            }
        }
    }

    private void CreateTrackedLabel(string text, Transform target, Vector2 offset, UnityEngine.Events.UnityAction onClick)
    {
        TrackedLabel trackedLabel;

        if (labelPool.Count > 0)
        {
            trackedLabel = labelPool[labelPool.Count - 1];
            labelPool.RemoveAt(labelPool.Count - 1);

            trackedLabel.Label.gameObject.SetActive(true);
            trackedLabel.Line.gameObject.SetActive(true);
            trackedLabel.Arrow.gameObject.SetActive(true);

            trackedLabel.LabelText.text = text;
            trackedLabel.Label.sizeDelta = new Vector2(GetLabelWidth(text), LabelHeight);

            trackedLabel.LabelButton.onClick.RemoveAllListeners();
            if (onClick != null)
            {
                trackedLabel.LabelButton.onClick.AddListener(onClick);
            }
            
            trackedLabel.Target = target;
            trackedLabel.CachedRenderer = target.GetComponent<Renderer>();
            trackedLabel.Offset = offset;
        }
        else
        {
            GameObject labelObject = CreateButton(
                "Bone Label - " + text,
                overlayRoot,
                text,
                Black,
                22f,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(GetLabelWidth(text), LabelHeight),
                out Button button,
                out TMP_Text labelText);

            labelText.fontStyle = FontStyles.Bold;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            RectTransform line = CreateRect("Bone Label Line - " + text, overlayRoot);
            Image lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = Black;
            line.SetAsFirstSibling();

            RectTransform arrow = CreateArrowHead("Bone Label Arrow - " + text, overlayRoot);

            trackedLabel = new TrackedLabel
            {
                Label = labelObject.GetComponent<RectTransform>(),
                Line = line,
                Arrow = arrow,
                LabelText = labelText,
                LabelButton = button,
                Target = target,
                CachedRenderer = target.GetComponent<Renderer>(),
                Offset = offset
            };
        }

        trackedLabels.Add(trackedLabel);
        UpdateSingleLabelPosition(trackedLabel);
    }

    private static float GetLabelWidth(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 150f;
        }

        return Mathf.Clamp(58f + text.Length * 12f, 150f, 310f);
    }

    private void UpdateTrackedLabelPositions()
    {
        for (int i = 0; i < trackedLabels.Count; i++)
        {
            UpdateSingleLabelPosition(trackedLabels[i]);
        }
    }

    private void UpdateSingleLabelPosition(TrackedLabel trackedLabel)
    {
        if (trackedLabel == null || trackedLabel.Target == null || mainCamera == null)
        {
            return;
        }

        Vector3 worldPos = trackedLabel.CachedRenderer != null 
            ? trackedLabel.CachedRenderer.bounds.center 
            : trackedLabel.Target.position;

        bool visible = TryWorldToScreenPoint(worldPos, out Vector2 anchorPosition);
        trackedLabel.Label.gameObject.SetActive(visible);
        trackedLabel.Line.gameObject.SetActive(visible);
        trackedLabel.Arrow.gameObject.SetActive(visible);

        if (!visible)
        {
            return;
        }

        Vector2 labelPosition = ClampToScreen(anchorPosition + trackedLabel.Offset, trackedLabel.Label);
        trackedLabel.Label.position = labelPosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, anchorPosition, null, out Vector2 anchorCanvasPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, labelPosition, null, out Vector2 labelCanvasPosition);

        PositionLine(trackedLabel.Line, anchorCanvasPosition, labelCanvasPosition);
        PositionArrow(trackedLabel.Arrow, anchorPosition, labelPosition);
    }

    private bool TryWorldToScreenPoint(Vector3 worldPosition, out Vector2 screenPosition)
    {
        Vector3 projectedPosition = mainCamera.WorldToScreenPoint(worldPosition);
        if (projectedPosition.z < 0f)
        {
            screenPosition = Vector2.zero;
            return false;
        }

        screenPosition = projectedPosition;
        return true;
    }

    private Vector2 ClampToScreen(Vector2 position, RectTransform target)
    {
        float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
        // Added extra padding so it doesn't hug the very edge of the screen
        float halfWidth = target.rect.width * 0.5f * scaleFactor + 30f;
        float halfHeight = target.rect.height * 0.5f * scaleFactor + 40f;

        position.x = Mathf.Clamp(position.x, halfWidth, Screen.width - halfWidth);
        position.y = Mathf.Clamp(position.y, halfHeight, Screen.height - halfHeight);
        return position;
    }

    private static void PositionArrow(RectTransform arrow, Vector2 targetScreenPosition, Vector2 labelScreenPosition)
    {
        Vector2 labelToTarget = targetScreenPosition - labelScreenPosition;
        if (labelToTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector2 targetToLabel = -labelToTarget.normalized;
        arrow.position = targetScreenPosition + targetToLabel * 18f;
        arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(labelToTarget.y, labelToTarget.x) * Mathf.Rad2Deg);
    }

    private static RectTransform CreateArrowHead(string name, Transform parent)
    {
        RectTransform arrow = CreateRect(name, parent);
        arrow.sizeDelta = new Vector2(34f, 34f);

        TMP_Text arrowText = arrow.gameObject.AddComponent<TextMeshProUGUI>();
        arrowText.text = ">";
        arrowText.color = Black;
        arrowText.fontSize = 34f;
        arrowText.fontStyle = FontStyles.Bold;
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.textWrappingMode = TextWrappingModes.NoWrap;
        arrowText.raycastTarget = false;
        return arrow;
    }

    private static void PositionLine(RectTransform line, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        line.anchoredPosition = start + delta * 0.5f;
        line.sizeDelta = new Vector2(delta.magnitude, LineThickness);
        line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }


    private void HighlightBone(Transform bone)
    {
        HighlightBone(bone, HighlightColor);
    }

    public void HighlightBoneForFeedback(Transform bone, Color color)
    {
        HighlightBone(bone, color);
    }

    private void HighlightBone(Transform bone, Color color)
    {
        ClearHighlight();

        if (bone == null)
        {
            return;
        }

        if (highlightBlock == null)
        {
            highlightBlock = new MaterialPropertyBlock();
        }

        foreach (Renderer renderer in bone.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(highlightBlock);
            highlightBlock.SetColor("_BaseColor", color);
            highlightBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(highlightBlock);
            highlightedRenderers.Add(renderer);
        }
    }

    public void ClearHighlights()
    {
        ClearHighlight();
    }

    private void ClearHighlight()
    {
        foreach (Renderer renderer in highlightedRenderers)
        {
            if (renderer != null)
            {
                renderer.SetPropertyBlock(null);
            }
        }

        highlightedRenderers.Clear();
    }

    private void CreateInfoPanel()
    {
        infoPanelObject = new GameObject("Bone Info Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
        infoPanelObject.transform.SetParent(overlayRoot, false);

        RectTransform overlayRect = infoPanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayImage = infoPanelObject.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0f); // Fully transparent background

        Button closeButton = infoPanelObject.GetComponent<Button>();
        closeButton.targetGraphic = overlayImage;
        closeButton.onClick.AddListener(() => 
        {
            ClearHighlight();
            ClearTrackedLabels();
            hoveredBoneName = null;
            hoveredBoneTransform = null;
            selectedBoneName = null;
            HideInfoPanelOnly();
        });

        GameObject modalBox = new GameObject("Bone Info Modal", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(ModalAnimator));
        modalBox.transform.SetParent(overlayRect, false);

        RectTransform rect = modalBox.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.08f, 0.5f);
        rect.anchorMax = new Vector2(0.92f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        Image panelImage = modalBox.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.09f, 0.1f, 0.98f);
        panelImage.sprite = ARAppModeController.GetRoundedRectSprite();
        panelImage.type = Image.Type.Sliced;

        VerticalLayoutGroup layout = modalBox.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 40, 40);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter panelFitter = modalBox.GetComponent<ContentSizeFitter>();
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject containerObj = new GameObject("Viewer Container", typeof(RectTransform), typeof(LayoutElement));
        containerObj.transform.SetParent(rect, false);
        LayoutElement containerLayout = containerObj.GetComponent<LayoutElement>();
        containerLayout.minHeight = 350f;
        containerLayout.preferredHeight = 350f;
        containerLayout.flexibleHeight = 0f;

        GameObject rawImageObj = new GameObject("Viewer RawImage", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        rawImageObj.transform.SetParent(containerObj.transform, false);
        
        AspectRatioFitter fitter = rawImageObj.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1f; // 1:1 square to match the 512x512 RenderTexture

        boneViewer = rawImageObj.AddComponent<BoneViewer>();

        infoTitleText = CreateLayoutText("Info Title", rect, "", 38f, FontStyles.Bold, TextAlignmentOptions.Center, White);
        infoLatinText = CreateLayoutText("Info Latin", rect, "", 24f, FontStyles.Italic, TextAlignmentOptions.Center, new Color(0.88f, 0.88f, 0.88f, 1f));
        infoDescriptionText = CreateLayoutText("Info Description", rect, "", 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft, White);
        infoFactText = CreateLayoutText("Info Fact", rect, "", 22f, FontStyles.Normal, TextAlignmentOptions.TopLeft, new Color(0.9f, 0.9f, 0.9f, 1f));

        infoPanelObject.SetActive(false);
    }

    private void UpdateInfoPanel(BoneInfo bone, string boneName)
    {
        infoTitleText.text = GetDisplayName(boneName);
        if (bone != null)
        {
            infoLatinText.text = !string.IsNullOrWhiteSpace(bone.namaAnatomiLatin) ? $"<i>{bone.namaAnatomiLatin}</i>" : "";
            infoDescriptionText.text = !string.IsNullOrWhiteSpace(bone.deskripsi) ? bone.deskripsi : "Deskripsi tidak tersedia.";
            infoFactText.text = !string.IsNullOrWhiteSpace(bone.faktaAnatomiSederhana) ? $"<b>Fakta Menarik:</b> {bone.faktaAnatomiSederhana}" : "";
        }
        else
        {
            infoLatinText.text = "";
            infoDescriptionText.text = "Deskripsi tidak tersedia.";
            infoFactText.text = "";
        }
    }

    private Sprite CreateRoundedRectSprite(int width, int height, int radius)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float d = 0;
                if (x < radius && y < radius) d = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius));
                else if (x > width - radius - 1 && y < radius) d = Vector2.Distance(new Vector2(x, y), new Vector2(width - radius - 1, radius));
                else if (x < radius && y > height - radius - 1) d = Vector2.Distance(new Vector2(x, y), new Vector2(radius, height - radius - 1));
                else if (x > width - radius - 1 && y > height - radius - 1) d = Vector2.Distance(new Vector2(x, y), new Vector2(width - radius - 1, height - radius - 1));
                
                pixels[y * width + x] = (d > radius) ? Color.clear : Color.white;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private void HideInfoPanelOnly()
    {
        if (infoPanelObject != null)
        {
            infoPanelObject.SetActive(false);
        }
    }

    private void ClearTrackedLabels()
    {
        foreach (TrackedLabel trackedLabel in trackedLabels)
        {
            if (trackedLabel.Label != null)
            {
                trackedLabel.Label.gameObject.SetActive(false);
            }

            if (trackedLabel.Line != null)
            {
                trackedLabel.Line.gameObject.SetActive(false);
            }

            if (trackedLabel.Arrow != null)
            {
                trackedLabel.Arrow.gameObject.SetActive(false);
            }
            
            labelPool.Add(trackedLabel);
        }

        trackedLabels.Clear();
    }

    public void ClearInteractions()
    {
        HideInteractions();
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
    }

    private static GameObject CreateButton(
        string name,
        Transform parent,
        string text,
        Color backgroundColor,
        float fontSize,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        out Button button,
        out TMP_Text label)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = backgroundColor;

        button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);

        label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.color = White;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        return buttonObject;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string text,
        float fontSize,
        FontStyles fontStyle,
        TextAlignmentOptions alignment,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TMP_Text textComponent = textObject.GetComponent<TMP_Text>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.alignment = alignment;
        textComponent.color = color;
        textComponent.textWrappingMode = TextWrappingModes.Normal;
        textComponent.raycastTarget = false;
        return textComponent;
    }

    private static TMP_Text CreateLayoutText(
        string name,
        Transform parent,
        string text,
        float fontSize,
        FontStyles fontStyle,
        TextAlignmentOptions alignment,
        Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement), typeof(ContentSizeFitter));
        textObject.transform.SetParent(parent, false);

        TMP_Text textComponent = textObject.GetComponent<TMP_Text>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.alignment = alignment;
        textComponent.color = color;
        textComponent.textWrappingMode = TextWrappingModes.Normal;
        textComponent.raycastTarget = false;

        ContentSizeFitter sizeFitter = textObject.GetComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return textComponent;
    }

    private class TrackedLabel
    {
        public RectTransform Label;
        public RectTransform Line;
        public RectTransform Arrow;
        public TMP_Text LabelText;
        public Button LabelButton;
        public Transform Target;
        public Renderer CachedRenderer;
        public Vector2 Offset;
    }
}
