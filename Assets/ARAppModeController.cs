using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ARAppModeController : MonoBehaviour
{
    private static readonly Color BarColor = new Color(0.03f, 0.035f, 0.045f, 0.88f);
    private static readonly Color StatusColor = new Color(0.02f, 0.025f, 0.03f, 0.82f);
    private static readonly Color ButtonColor = new Color(0.12f, 0.13f, 0.15f, 0.96f);
    private static readonly Color SelectedColor = new Color(0.37f, 0.09f, 0.92f, 1f);
    private static readonly Color ResetColor = new Color(0.46f, 0.08f, 0.08f, 0.96f);
    private static readonly Color TextColor = Color.white;
    private const float ManualDefaultScaleMultiplier = 3f;
    private const float ModelScaleMin = 0.1f;
    private const float ModelScaleMax = 3f;
    private const float ModelMoveMin = -1.5f;
    private const float ModelMoveMax = 1.5f;
    private const float ModelScaleStep = 0.25f;
    private const float ModelMoveStep = 0.05f;
    private const float ModelRotationStep = 30f;

    private static ARAppModeController instance;

    private readonly Dictionary<ARAppMode, Image> modeButtonImages = new Dictionary<ARAppMode, Image>();
    private SkeletonPlacementController placementController;
    private BoneQuizController quizController;
    private Canvas canvas;
    public RectTransform overlayRoot;
    private TMP_Text statusText;
    private GameObject instructionPanelObject;
    private GameObject modelControlPanel;
    private GameObject modelControlButtonObject;
    private GameObject helpButtonObject;
    private GameObject actionBarObject;
    private GameObject statusPanelObject;
    private Image modelControlButtonImage;
    private Slider scaleSlider;
    private Slider yawSlider;
    private bool isUpdatingSliders = false;
    private ARAppMode currentMode = (ARAppMode)(-1);
    private bool modelControlsVisible;

    public static ARAppModeController Instance
    {
        get { return instance; }
    }

    public ARAppMode CurrentMode
    {
        get { return currentMode; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject("AR App Mode Controller");
        controllerObject.AddComponent<ARAppModeController>();
    }

    private void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // Force URP Render Scale down for GPU performance
        var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urpAsset != null)
        {
            urpAsset.renderScale = 0.75f;
            Debug.Log("[Performance] URP Render Scale reduced to 0.75x");
        }
    }

    private IEnumerator Start()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            yield break;
        }

        instance = this;
        placementController = GetComponent<SkeletonPlacementController>();
        if (placementController == null)
        {
            placementController = gameObject.AddComponent<SkeletonPlacementController>();
        }

        quizController = GetComponent<BoneQuizController>();
        if (quizController == null)
        {
            quizController = gameObject.AddComponent<BoneQuizController>();
        }

        for (int i = 0; i < 120 && !ResolveCanvas(); i++)
        {
            yield return null;
        }

        if (canvas == null)
        {
            Debug.LogWarning("ARAppModeController: App UI canvas was not found.");
            yield break;
        }

        BuildRuntimeUi();
        MoveLabelButtonAboveActionBar();

        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null)
        {
            registry.ActiveSkeletonChanged += HandleActiveSkeletonChanged;
        }

        SetMode(ARAppMode.Skeleton);

        StartCoroutine(Force60FPSCamera());
    }

    private void OnDestroy()
    {
        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null)
        {
            registry.ActiveSkeletonChanged -= HandleActiveSkeletonChanged;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    private IEnumerator Force60FPSCamera()
    {
        // Wait until AR is fully initialized
        yield return new WaitForSeconds(1f);
        
        var arSession = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
        if (arSession != null)
        {
            arSession.matchFrameRateRequested = false;
        }

        var arManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (arManager != null && arManager.subsystem != null)
        {
            var configurations = arManager.GetConfigurations(Unity.Collections.Allocator.Temp);
            if (configurations.IsCreated)
            {
                UnityEngine.XR.ARSubsystems.XRCameraConfiguration? bestConfig = null;
                int highestFramerate = 0;
                
                foreach (var config in configurations)
                {
                    if (config.framerate.HasValue && config.framerate.Value > highestFramerate)
                    {
                        highestFramerate = config.framerate.Value;
                        bestConfig = config;
                    }
                }
                
                if (bestConfig.HasValue && highestFramerate > 30)
                {
                    arManager.currentConfiguration = bestConfig.Value;
                    Debug.Log($"[ARAppModeController] Forced camera to {highestFramerate} FPS.");
                }
                configurations.Dispose();
            }
        }
        
        Application.targetFrameRate = 60;
    }

    private void LateUpdate()
    {
        bool isModalActive = BoneInteractionUI.Instance != null && BoneInteractionUI.Instance.IsInfoPanelActive;
        
        if (!isModalActive && overlayRoot != null && overlayRoot.parent != null &&
            overlayRoot.GetSiblingIndex() != overlayRoot.parent.childCount - 1)
        {
            overlayRoot.SetAsLastSibling();
        }
    }

    public void SetMode(ARAppMode mode)
    {
        if (currentMode == mode)
        {
            return;
        }

        currentMode = mode;
        UpdateModeButtonColors();

        bool isQuiz = (mode == ARAppMode.Quiz);
        if (helpButtonObject != null) helpButtonObject.SetActive(!isQuiz);
        if (actionBarObject != null) actionBarObject.SetActive(!isQuiz);
        if (statusPanelObject != null) statusPanelObject.SetActive(!isQuiz);
        
        bool isSkeletonPresent = SkeletonRegistry.Instance != null && SkeletonRegistry.Instance.ActiveSkeleton != null;
        if (modelControlButtonObject != null) modelControlButtonObject.SetActive(!isQuiz && isSkeletonPresent);

        BoneInteractionUI interactionUI = BoneInteractionUI.Instance;
        SkeletonRegistry registry = SkeletonRegistry.Instance;

        if (mode != ARAppMode.Skeleton && placementController != null)
        {
            placementController.CancelPlacement();
        }

        if (mode != ARAppMode.Quiz && quizController != null)
        {
            quizController.ClearQuiz();
        }

        if (mode == ARAppMode.Skeleton)
        {
            bool hasSkeleton = registry != null && registry.ActiveSkeleton != null;

            if (hasSkeleton)
            {
                if (placementController != null)
                {
                    placementController.CancelPlacement();
                }

                if (interactionUI != null)
                {
                    interactionUI.SetTrackedSkeleton(registry.ActiveSkeleton);
                }

                SetStatus("Arahkan HP ke tulang, lalu sentuh!");
            }
            else
            {
                if (interactionUI != null)
                {
                    interactionUI.ClearInteractions();
                }

                if (placementController != null)
                {
                    placementController.BeginPlacement();
                }
            }
        }
        else if (mode == ARAppMode.Quiz)
        {
            if (interactionUI != null)
            {
                interactionUI.ClearInteractions();
            }

            if (quizController != null)
            {
                quizController.BeginQuiz();
            }
        }
    }

    public void HandleBoneTap(BoneTapResult tapResult)
    {
        if (currentMode == ARAppMode.Quiz && quizController != null)
        {
            quizController.SubmitAnswer(tapResult);
        }
    }

    public void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }



    private bool ResolveCanvas()
    {
        GameObject appUi = GameObject.Find("App UI");
        if (appUi == null)
        {
            return false;
        }

        canvas = appUi.GetComponent<Canvas>();
        return canvas != null;
    }

    private void BuildRuntimeUi()
    {
        overlayRoot = CreateRect("AR App Mode Overlay", canvas.transform);
        overlayRoot.anchorMin = Vector2.zero;
        overlayRoot.anchorMax = Vector2.one;
        overlayRoot.offsetMin = Vector2.zero;
        overlayRoot.offsetMax = Vector2.zero;
        overlayRoot.SetAsLastSibling();

        RectTransform statusPanel = CreatePanel("Mode Status Panel", overlayRoot, StatusColor);
        statusPanel.anchorMin = new Vector2(0.04f, 0f);
        statusPanel.anchorMax = new Vector2(0.96f, 0f);
        statusPanel.pivot = new Vector2(0.5f, 0f);
        statusPanel.anchoredPosition = new Vector2(0f, 140f);
        statusPanel.sizeDelta = new Vector2(0f, 50f);
        statusPanelObject = statusPanel.gameObject;

        statusText = CreateText("Mode Status Text", statusPanel, "", 26f, FontStyles.Normal, TextAlignmentOptions.Center);

        RectTransform bar = CreatePanel("Bottom Action Bar", overlayRoot, BarColor);
        bar.anchorMin = new Vector2(0.04f, 0f);
        bar.anchorMax = new Vector2(0.96f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 14f);
        bar.sizeDelta = new Vector2(0f, 110f);
        actionBarObject = bar.gameObject;

        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        AddModeButton(bar, "Skeleton", ARAppMode.Skeleton);
        AddModeButton(bar, "Kuis", ARAppMode.Quiz);
        AddButton(bar, "Reset", ResetColor, ResetApp);

        modelControlButtonObject = CreateButtonObject("Kontrol Button", overlayRoot, new Color(0, 0, 0, 0), out Button kontrolBtn, out TMP_Text kontrolLabel);
        kontrolLabel.gameObject.SetActive(false); // Hide the text label

        // Create the cog icon directly using the provided white gear image
        GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconObj.transform.SetParent(modelControlButtonObject.transform, false);
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.15f, 0.15f);
        iconRect.anchorMax = new Vector2(0.85f, 0.85f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        
        Image iconImage = iconObj.GetComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>("White Gear");
        iconImage.preserveAspect = true;
        iconImage.color = Color.white;

        kontrolBtn.transition = Selectable.Transition.None;
        kontrolBtn.onClick.AddListener(ToggleModelControls);
        modelControlButtonImage = modelControlButtonObject.GetComponent<Image>();
        modelControlButtonImage.gameObject.SetActive(false); // Hide initially, show when model is active

        RectTransform kontrolRect = modelControlButtonObject.GetComponent<RectTransform>();
        kontrolRect.anchorMin = new Vector2(1f, 1f);
        kontrolRect.anchorMax = new Vector2(1f, 1f);
        kontrolRect.pivot = new Vector2(1f, 1f);
        kontrolRect.anchoredPosition = new Vector2(-180f, -50f); // Placed beside Help Button
        kontrolRect.sizeDelta = new Vector2(110f, 110f);

        // --- NEW HELP BUTTON (?) ---
        helpButtonObject = CreateButtonObject("Help Button", overlayRoot, new Color(0, 0, 0, 0), out Button helpBtn, out TMP_Text helpLabel);
        helpBtn.transition = Selectable.Transition.None;
        helpLabel.text = "?";
        helpLabel.fontSize = 75f;
        helpLabel.color = Color.white;
        helpLabel.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        helpLabel.GetComponent<RectTransform>().offsetMax = Vector2.zero;

        RectTransform helpRect = helpButtonObject.GetComponent<RectTransform>();
        helpRect.anchorMin = new Vector2(1f, 1f);
        helpRect.anchorMax = new Vector2(1f, 1f);
        helpRect.pivot = new Vector2(1f, 1f);
        // Position it exactly at the corner
        helpRect.anchoredPosition = new Vector2(-50f, -50f); 
        helpRect.sizeDelta = new Vector2(110f, 110f);

        helpBtn.onClick.AddListener(ToggleInstructionPanel);

        // Hide the old prefab "Instruction Icon"
        GameObject appUi = GameObject.Find("App UI");
        if (appUi != null)
        {
            Transform oldIcon = appUi.transform.Find("Instruction Icon");
            if (oldIcon != null)
            {
                oldIcon.gameObject.SetActive(false);
            }
        }
        // ---------------------------

        BuildModelControlPanel();
        BuildInstructionPanel();
    }

    private void BuildInstructionPanel()
    {
        // 1. Create a fullscreen overlay background that intercepts clicks to dismiss
        instructionPanelObject = new GameObject("Instruction Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
        instructionPanelObject.transform.SetParent(overlayRoot, false);
        RectTransform overlayRect = instructionPanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayBg = instructionPanelObject.GetComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0.6f); // Dark semi-transparent background

        Button overlayBtn = instructionPanelObject.GetComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(ToggleInstructionPanel);

        // 2. Create the actual modal panel inside the overlay
        GameObject modalBox = new GameObject("Instruction Panel Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(ModalAnimator));
        modalBox.transform.SetParent(instructionPanelObject.transform, false);

        RectTransform rect = modalBox.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.08f, 0.5f);
        rect.anchorMax = new Vector2(0.92f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        Image panelImage = modalBox.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.09f, 0.1f, 0.98f);
        panelImage.sprite = GetRoundedRectSprite();
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

        CreateLayoutText("Inst Title", rect, "Cara Menggunakan", 38f, FontStyles.Bold, TextAlignmentOptions.Center);
        CreateLayoutText("Inst 1", rect, "• Sentuh tulang pada rangka untuk melihat informasi detailnya.", 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        CreateLayoutText("Inst 2", rect, "• Gunakan slider di sisi kanan untuk memutar dan memperbesar/memperkecil model.", 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        CreateLayoutText("Inst 3", rect, "• Beralih ke mode Kuis untuk menguji pengetahuan Anda tentang kerangka!", 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        GameObject closeBtnObj = Create3DButtonObject("Tutup Instruksi", rect, "Tutup", out Button closeBtn, out TMP_Text closeLbl);
        closeBtn.onClick.AddListener(ToggleInstructionPanel);
        SetLayoutSize(closeBtnObj, 0f, 60f, 0f, 0f);

        instructionPanelObject.SetActive(false);
    }

    private void ToggleInstructionPanel()
    {
        if (instructionPanelObject != null)
        {
            instructionPanelObject.SetActive(!instructionPanelObject.activeSelf);
        }
    }

    private void BuildModelControlPanel()
    {
        // Create an invisible fullscreen overlay that intercepts clicks to dismiss the panel
        GameObject controlOverlayObject = new GameObject("Model Control Overlay", typeof(RectTransform), typeof(Image), typeof(Button));
        controlOverlayObject.transform.SetParent(overlayRoot, false);
        RectTransform overlayRect = controlOverlayObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayBg = controlOverlayObject.GetComponent<Image>();
        overlayBg.color = new Color(0f, 0f, 0f, 0f); // Completely transparent, no dimming

        Button overlayBtn = controlOverlayObject.GetComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(() => SetModelControlPanelVisible(false));
        
        modelControlPanel = controlOverlayObject; // The overlay is what gets toggled

        // Create the actual panel inside the overlay
        RectTransform panel = CreatePanel("Model Control Panel Box", controlOverlayObject.transform, StatusColor);
        panel.anchorMin = new Vector2(0.68f, 0.12f);
        panel.anchorMax = new Vector2(0.98f, 0.88f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        // Single horizontal layout: just the two sliders, filling the entire panel
        HorizontalLayoutGroup sliderLayout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        sliderLayout.padding = new RectOffset(24, 24, 32, 32);
        sliderLayout.childAlignment = TextAnchor.MiddleCenter;
        sliderLayout.childControlWidth = true;
        sliderLayout.childControlHeight = true;
        sliderLayout.childForceExpandWidth = true;
        sliderLayout.childForceExpandHeight = true;
        sliderLayout.spacing = 6f;

        // Create Scale Slider
        scaleSlider = CreateVerticalSlider("Skala", panel, ModelScaleMin, ModelScaleMax, 1.0f);
        scaleSlider.onValueChanged.AddListener((val) => {
            if (isUpdatingSliders) return;
            SkeletonTransformHandle handle = GetActiveTransformHandle(true);
            if (handle != null) handle.SetScaleMultiplier(val);
        });

        // Create Yaw Slider
        yawSlider = CreateVerticalSlider("Rotasi", panel, -180f, 180f, 0f);
        yawSlider.onValueChanged.AddListener((val) => {
            if (isUpdatingSliders) return;
            SkeletonTransformHandle handle = GetActiveTransformHandle(true);
            if (handle != null) {
                Vector3 rot = handle.RotationOffset;
                handle.SetRotationOffset(new Vector3(rot.x, val, rot.z));
            }
        });

        SetModelControlPanelVisible(false);
    }

    private Slider CreateVerticalSlider(string label, RectTransform parent, float min, float max, float defaultVal)
    {
        RectTransform container = CreateRect(label + " Container", parent);
        VerticalLayoutGroup vLayout = container.gameObject.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.UpperCenter;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandHeight = false;
        vLayout.spacing = 16f;

        TMP_Text labelText = CreateLayoutText(label + " Label", container, label, 24f, FontStyles.Bold, TextAlignmentOptions.Center);
        SetLayoutSize(labelText.gameObject, 0f, 30f, 0f, 0f); // Fixed height, no flex

        GameObject sliderObj = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderObj.transform.SetParent(container, false);
        Slider slider = sliderObj.GetComponent<Slider>();
        slider.direction = Slider.Direction.BottomToTop;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = defaultVal;

        // The slider eats all remaining vertical space
        SetLayoutSize(sliderObj, 0f, 0f, 1f, 1f);
        
        // Format to look like a normal thin vertical slider instead of a thick box
        RectTransform bgRect = sliderObj.transform.Find("Background").GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 1f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        bgRect.sizeDelta = new Vector2(8f, 0f);
        Image bgImage = bgRect.GetComponent<Image>();
        bgImage.color = new Color(1f, 1f, 1f, 0.15f);

        RectTransform fillAreaRect = sliderObj.transform.Find("Fill Area").GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0.5f, 0f);
        fillAreaRect.anchorMax = new Vector2(0.5f, 1f);
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;
        fillAreaRect.sizeDelta = new Vector2(8f, 0f);

        RectTransform fillRect = sliderObj.transform.Find("Fill Area/Fill").GetComponent<RectTransform>();
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fillRect.GetComponent<Image>();
        fillImage.color = SelectedColor;

        RectTransform handleAreaRect = sliderObj.transform.Find("Handle Slide Area").GetComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0.5f, 0f);
        handleAreaRect.anchorMax = new Vector2(0.5f, 1f);
        handleAreaRect.offsetMin = Vector2.zero;
        handleAreaRect.offsetMax = Vector2.zero;
        handleAreaRect.sizeDelta = new Vector2(8f, 0f);

        RectTransform handleRect = sliderObj.transform.Find("Handle Slide Area/Handle").GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(44f, 44f);
        handleRect.localScale = Vector3.one; // Ensure no weird scaling from Slider orientation flip
        
        Image handleImage = handleRect.GetComponent<Image>();
        handleImage.sprite = GetCircleSprite();
        handleImage.type = Image.Type.Simple;
        handleImage.preserveAspect = true; // Force the circle to never stretch into an oval!

        return slider;
    }

    private static Sprite cachedCircleSprite;

    private static Sprite GetCircleSprite()
    {
        if (cachedCircleSprite != null) return cachedCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size * 0.5f;
        float radius = center - 1f;
        Color clear = new Color(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, dist <= radius ? Color.white : clear);
            }
        }

        tex.Apply();
        cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return cachedCircleSprite;
    }

    private static Sprite cachedRoundedRectSprite;

    public static Sprite GetRoundedRectSprite()
    {
        if (cachedRoundedRectSprite != null) return cachedRoundedRectSprite;

        int size = 32;
        int radius = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color clear = new Color(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int cx = x < radius ? radius : (x >= size - radius ? size - radius - 1 : x);
                int cy = y < radius ? radius : (y >= size - radius ? size - radius - 1 : y);
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, dist <= radius ? Color.white : clear);
            }
        }

        tex.Apply();
        cachedRoundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return cachedRoundedRectSprite;
    }



    private void AddModeButton(RectTransform parent, string text, ARAppMode mode)
    {
        GameObject buttonObject = AddButton(parent, text, ButtonColor, () => SetMode(mode));
        modeButtonImages[mode] = buttonObject.transform.Find("Top Layer").GetComponent<Image>();
        
        if (mode == ARAppMode.Quiz)
        {
            Button btn = buttonObject.GetComponent<Button>();
            if (btn != null) btn.interactable = false;
        }
    }

    private GameObject AddButton(RectTransform parent, string text, Color color, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = Create3DButtonObject(text + " Button", parent, text, out Button button, out TMP_Text label);
        label.fontSize = 30f;
        button.onClick.AddListener(action);

        LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
        layoutElement.minWidth = 140f;
        layoutElement.preferredWidth = 200f;

        return buttonObject;
    }

    private void ToggleModelControls()
    {
        SetModelControlPanelVisible(!modelControlsVisible);
    }

    private void SetModelControlPanelVisible(bool visible)
    {
        modelControlsVisible = visible;

        if (modelControlPanel != null)
        {
            modelControlPanel.SetActive(visible);
        }

        if (visible)
        {
            RefreshModelControlPanel();
        }
    }

    private void RefreshModelControlPanel()
    {
        if (modelControlPanel == null || !modelControlPanel.activeSelf)
        {
            return;
        }

        SkeletonTransformHandle transformHandle = GetActiveTransformHandle(false);
        bool hasActiveSkeleton = transformHandle != null;
        SetModelControlsInteractable(hasActiveSkeleton);

        if (!hasActiveSkeleton)
        {
            return;
        }

        UpdateSliders(transformHandle);
    }

    private void ResetActiveModelTransform()
    {
        SkeletonTransformHandle transformHandle = GetActiveTransformHandle(false);
        if (transformHandle == null)
        {
            return;
        }

        transformHandle.SetPositionOffset(Vector3.zero);
        transformHandle.SetRotationOffset(Vector3.zero);
        transformHandle.SetScaleMultiplier(GetDefaultScaleMultiplierForActiveSkeleton());
        RefreshModelControlPanel();
    }

    private void UpdateSliders(SkeletonTransformHandle transformHandle)
    {
        if (transformHandle == null || scaleSlider == null || yawSlider == null)
        {
            return;
        }

        isUpdatingSliders = true;
        scaleSlider.value = transformHandle.ScaleMultiplier;
        yawSlider.value = transformHandle.RotationOffset.y;
        isUpdatingSliders = false;
    }

    private void SetModelControlsInteractable(bool interactable)
    {
        if (scaleSlider != null) scaleSlider.interactable = interactable;
        if (yawSlider != null) yawSlider.interactable = interactable;
    }

    private SkeletonTransformHandle GetActiveTransformHandle(bool createIfMissing)
    {
        SkeletonRegistry registry = SkeletonRegistry.Instance;
        GameObject activeSkeleton = registry != null ? registry.ActiveSkeleton : null;
        if (activeSkeleton == null)
        {
            return null;
        }

        SkeletonTransformHandle transformHandle = activeSkeleton.GetComponent<SkeletonTransformHandle>();
        if (transformHandle == null && createIfMissing)
        {
            transformHandle = activeSkeleton.AddComponent<SkeletonTransformHandle>();
        }

        return transformHandle;
    }

    private float GetDefaultScaleMultiplierForActiveSkeleton()
    {
        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null && registry.ActiveSkeleton != null && registry.ActiveSkeleton == registry.ManualSkeleton)
        {
            return ManualDefaultScaleMultiplier;
        }

        return 1f;
    }

    private static float NormalizeSignedAngle(float angle)
    {
        return Mathf.Repeat(angle + 180f, 360f) - 180f;
    }

    private void ResetApp()
    {
        if (placementController != null)
        {
            placementController.CancelPlacement();
        }

        if (quizController != null)
        {
            quizController.ClearQuiz();
        }

        BoneInteractionUI interactionUI = BoneInteractionUI.Instance;
        if (interactionUI != null)
        {
            interactionUI.ClearInteractions();
        }


        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null)
        {
            registry.ClearManualSkeleton();
            registry.SetActive(null);
        }

        SetModelControlPanelVisible(false);
        currentMode = (ARAppMode)(-1); // Force state change so SetMode executes
        SetMode(ARAppMode.Skeleton);
        SetStatus("Reset selesai. Scan lantai untuk menaruh skeleton.");
        ToggleARVisualizers(true);
    }

    public void ToggleARVisualizers(bool show)
    {
        var planeManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARPlaneManager>();
        if (planeManager != null)
        {
            planeManager.enabled = show;
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(show);
            }
        }

        var pointCloudManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARPointCloudManager>();
        if (pointCloudManager != null)
        {
            pointCloudManager.enabled = show;
            foreach (var cloud in pointCloudManager.trackables)
            {
                cloud.gameObject.SetActive(show);
            }
        }
    }



    private void HandleActiveSkeletonChanged(GameObject skeleton)
    {
        BoneInteractionUI interactionUI = BoneInteractionUI.Instance;
        if (interactionUI != null)
        {
            interactionUI.SetTrackedSkeleton(skeleton);
        }

        if (currentMode == ARAppMode.Quiz && quizController != null)
        {
            quizController.BeginQuiz();
        }

        if (modelControlButtonObject != null)
        {
            modelControlButtonObject.SetActive(skeleton != null && currentMode != ARAppMode.Quiz);
        }

        if (modeButtonImages.TryGetValue(ARAppMode.Quiz, out Image quizImage))
        {
            Button quizBtn = quizImage.transform.parent.GetComponent<Button>();
            if (quizBtn != null)
            {
                quizBtn.interactable = (skeleton != null);
            }
        }

        RefreshModelControlPanel();
    }

    private void UpdateModeButtonColors()
    {
        foreach (KeyValuePair<ARAppMode, Image> pair in modeButtonImages)
        {
            if (pair.Value != null)
            {
                // Active mode is slightly darker gray, inactive is white/e8e8e8
                pair.Value.color = pair.Key == currentMode ? new Color(0.8f, 0.8f, 0.8f, 1f) : new Color(0.91f, 0.91f, 0.91f, 1f);
            }
        }
    }

    private void MoveLabelButtonAboveActionBar()
    {
        GameObject appUi = GameObject.Find("App UI");
        if (appUi == null)
        {
            return;
        }

        Button[] buttons = appUi.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button.name != "Label Button")
            {
                continue;
            }

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 154f);
            rect.sizeDelta = new Vector2(220f, 58f);

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.fontSize = 24f;
            }

            return;
        }
    }



    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
    }

    public static RectTransform CreatePanel(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = GetRoundedRectSprite();
        image.type = Image.Type.Sliced;
        return rect;
    }

    public static TMP_Text CreateText(
        string name,
        Transform parent,
        string text,
        float fontSize,
        FontStyles fontStyle,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 4f);
        rect.offsetMax = new Vector2(-12f, -4f);

        TMP_Text textComponent = textObject.GetComponent<TMP_Text>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.alignment = alignment;
        textComponent.color = TextColor;
        textComponent.textWrappingMode = TextWrappingModes.Normal;
        textComponent.raycastTarget = false;
        return textComponent;
    }

    public static TMP_Text CreateLayoutText(
        string name,
        Transform parent,
        string text,
        float fontSize,
        FontStyles fontStyle,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        TMP_Text textComponent = textObject.GetComponent<TMP_Text>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.alignment = alignment;
        textComponent.color = TextColor;
        textComponent.textWrappingMode = TextWrappingModes.Normal;
        textComponent.raycastTarget = false;
        return textComponent;
    }

    public static void SetLayoutSize(GameObject target, float preferredWidth, float preferredHeight, float flexibleWidth)
    {
        SetLayoutSize(target, preferredWidth, preferredHeight, flexibleWidth, -1f);
    }

    public static void SetLayoutSize(GameObject target, float preferredWidth, float preferredHeight, float flexibleWidth, float flexibleHeight)
    {
        LayoutElement layoutElement = target.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = target.AddComponent<LayoutElement>();
        }

        if (preferredWidth > 0f)
        {
            layoutElement.minWidth = preferredWidth;
            layoutElement.preferredWidth = preferredWidth;
        }

        if (preferredHeight > 0f)
        {
            layoutElement.minHeight = preferredHeight;
            layoutElement.preferredHeight = preferredHeight;
        }

        layoutElement.flexibleWidth = flexibleWidth;
        if (flexibleHeight >= 0f)
        {
            layoutElement.flexibleHeight = flexibleHeight;
        }
    }

    public static GameObject CreateButtonObject(
        string name,
        Transform parent,
        Color color,
        out Button button,
        out TMP_Text label)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        image.sprite = GetRoundedRectSprite();
        image.type = Image.Type.Sliced;

        button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 2f);
        textRect.offsetMax = new Vector2(-6f, -2f);

        label = textObject.GetComponent<TMP_Text>();
        label.color = TextColor;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;

        return buttonObject;
    }

    public static GameObject Create3DButtonObject(
        string name,
        Transform parent,
        string buttonText,
        out Button button,
        out TMP_Text label)
    {
        // 1. Base Layer (Gray shadow/depth)
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Button3DAnimator));
        buttonObject.transform.SetParent(parent, false);

        Image baseImage = buttonObject.GetComponent<Image>();
        baseImage.color = new Color(0.5f, 0.5f, 0.5f, 1f); // Gray
        baseImage.sprite = GetRoundedRectSprite();
        baseImage.type = Image.Type.Sliced;

        button = buttonObject.GetComponent<Button>();
        button.transition = Selectable.Transition.None;

        // 2. Top Layer (#e8e8e8 with black outline)
        GameObject topLayer = new GameObject("Top Layer", typeof(RectTransform), typeof(Image), typeof(UnityEngine.UI.Outline));
        topLayer.transform.SetParent(buttonObject.transform, false);

        RectTransform topRect = topLayer.GetComponent<RectTransform>();
        topRect.anchorMin = Vector2.zero;
        topRect.anchorMax = Vector2.one;
        topRect.offsetMin = Vector2.zero;
        topRect.offsetMax = Vector2.zero;

        Image topImage = topLayer.GetComponent<Image>();
        topImage.color = new Color(0.91f, 0.91f, 0.91f, 1f); // #e8e8e8
        topImage.sprite = GetRoundedRectSprite();
        topImage.type = Image.Type.Sliced;
        
        button.targetGraphic = topImage;

        UnityEngine.UI.Outline outline = topLayer.GetComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0.5f, 0.5f, 0.5f, 1f); // Gray border
        outline.effectDistance = new Vector2(2f, -2f);

        Button3DAnimator animator = buttonObject.GetComponent<Button3DAnimator>();
        animator.Initialize(topRect);

        // 3. Text (Black, Bold)
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(topLayer.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 2f);
        textRect.offsetMax = new Vector2(-6f, -2f);

        label = textObject.GetComponent<TMP_Text>();
        label.text = buttonText;
        label.fontSize = 28f;
        label.color = new Color(0.05f, 0.05f, 0.05f, 1f);
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        return buttonObject;
    }
}
