using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public sealed class SkeletonPlacementController : MonoBehaviour
{
    private static readonly List<ARRaycastHit> RaycastHits = new List<ARRaycastHit>();
    [SerializeField]
    private float ManualDefaultScaleMultiplier = 1f;

    private ARRaycastManager raycastManager;
    private Camera mainCamera;
    private GameObject skeletonPrefab;
    private bool waitingForSurfaceTap;
    private float lastRaycastTime = 0f;

    public bool IsWaitingForSurfaceTap
    {
        get { return waitingForSurfaceTap; }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        Force60FpsCamera();
    }

    private void Force60FpsCamera()
    {
        ARCameraManager cameraManager = FindFirstObjectByType<ARCameraManager>();
        if (cameraManager == null || cameraManager.subsystem == null) return;

        using (Unity.Collections.NativeArray<XRCameraConfiguration> configurations = cameraManager.GetConfigurations(Unity.Collections.Allocator.Temp))
        {
            if (!configurations.IsCreated || configurations.Length <= 0) return;

            XRCameraConfiguration highestFramerateConfig = configurations[0];
            foreach (XRCameraConfiguration config in configurations)
            {
                if (config.framerate.HasValue)
                {
                    if (!highestFramerateConfig.framerate.HasValue || config.framerate.Value > highestFramerateConfig.framerate.Value)
                    {
                        highestFramerateConfig = config;
                    }
                }
            }

            if (highestFramerateConfig.framerate.HasValue && highestFramerateConfig.framerate.Value > 30)
            {
                cameraManager.currentConfiguration = highestFramerateConfig;
            }
        }
    }

    private void Update()
    {
        if (!waitingForSurfaceTap)
        {
            return;
        }

        ARAppModeController appMode = ARAppModeController.Instance;
        if (appMode != null && appMode.CurrentMode != ARAppMode.Skeleton)
        {
            waitingForSurfaceTap = false;
            return;
        }

        // Raycast from center of screen to detect floor for dynamic status text, throttle to 10fps
        if (raycastManager != null && mainCamera != null && Time.time - lastRaycastTime > 0.1f)
        {
            lastRaycastTime = Time.time;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (raycastManager.Raycast(center, RaycastHits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
            {
                SetStatus("Lantai terdeteksi! Sentuh untuk meletakkan.");
            }
            else
            {
                SetStatus("Gerakkan HP perlahan untuk memindai lantai.");
            }
        }

        if (!TryGetPointerPress(out Vector2 screenPosition, out int pointerId))
        {
            return;
        }

        if (IsPointerOverUI(screenPosition))
        {
            return;
        }

        TryPlaceAt(screenPosition);
    }

    public void BeginPlacement()
    {
        ResolveReferences();
        waitingForSurfaceTap = true;
    }

    public void CancelPlacement()
    {
        waitingForSurfaceTap = false;
    }

    private void TryPlaceAt(Vector2 screenPosition)
    {
        if (raycastManager == null)
        {
            SetStatus("AR Raycast Manager belum ditemukan.");
            return;
        }

        if (!ResolveSkeletonPrefab())
        {
            SetStatus("Prefab skeleton belum ditemukan.");
            return;
        }

        if (!raycastManager.Raycast(screenPosition, RaycastHits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
        {
            SetStatus("Permukaan belum terdeteksi. Pindai lagi.");
            return;
        }

        Pose pose = RaycastHits[0].pose;
        Quaternion rotation = GetFacingCameraRotation(pose.position);

        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry != null)
        {
            registry.ClearManualSkeleton();
        }

        GameObject skeleton = Instantiate(skeletonPrefab, pose.position, rotation);
        skeleton.name = "skeleton";

        SkeletonTransformHandle transformHandle = skeleton.GetComponent<SkeletonTransformHandle>();
        if (transformHandle == null)
        {
            transformHandle = skeleton.AddComponent<SkeletonTransformHandle>();
        }

        transformHandle.SetScaleMultiplier(ManualDefaultScaleMultiplier);

        SkeletonTransformGestureController gestureController = skeleton.GetComponent<SkeletonTransformGestureController>();
        if (gestureController == null)
        {
            gestureController = skeleton.AddComponent<SkeletonTransformGestureController>();
        }

        if (registry != null)
        {
            registry.Register(skeleton, SkeletonSource.ManualPlacement);
        }

        BoneInteractionUI interactionUI = BoneInteractionUI.Instance;
        if (interactionUI != null)
        {
            interactionUI.SetTrackedSkeleton(skeleton);
        }

        waitingForSurfaceTap = false;

        ARAppModeController appMode = ARAppModeController.Instance;
        if (appMode != null)
        {
            appMode.SetMode(ARAppMode.Skeleton);
            // The text "Aim the camera at a bone, and tap the screen!" will be set by ARAppModeController.SetMode
        }
    }

    private Quaternion GetFacingCameraRotation(Vector3 position)
    {
        ResolveCamera();

        if (mainCamera == null)
        {
            return Quaternion.identity;
        }

        Vector3 direction = mainCamera.transform.position - position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void ResolveReferences()
    {
        if (raycastManager == null)
        {
            raycastManager = FindFirstObjectByType<ARRaycastManager>();
        }

        ResolveCamera();
        ResolveSkeletonPrefab();
    }

    private void ResolveCamera()
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

    private bool ResolveSkeletonPrefab()
    {
        if (skeletonPrefab != null)
        {
            return true;
        }

        skeletonPrefab = Resources.Load<GameObject>("skeleton");
        return skeletonPrefab != null;
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
            pointerId = -1;
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
        
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        
        return results.Count > 0;
    }

    private static void SetStatus(string message)
    {
        ARAppModeController appMode = ARAppModeController.Instance;
        if (appMode != null)
        {
            appMode.SetStatus(message);
        }
    }
}
