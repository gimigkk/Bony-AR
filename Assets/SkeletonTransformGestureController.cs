using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public sealed class SkeletonTransformGestureController : MonoBehaviour
{
    public bool AllowUserSpin = true;
    public float SpinSpeed = 0.6f;
    public bool AllowUserResize = true;
    public float ResizeSpeed = 0.001f;
    public float MinScaleMultiplier = 0.01f;
    public float MaxScaleMultiplier = 100f;

    private SkeletonTransformHandle transformHandle;

    private void Awake()
    {
        transformHandle = GetTransformHandle();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Update()
    {
        SkeletonRegistry registry = SkeletonRegistry.Instance;
        if (registry == null || registry.ActiveSkeleton != gameObject)
        {
            return;
        }

        ARAppModeController appMode = ARAppModeController.Instance;
        if (appMode != null && appMode.CurrentMode == ARAppMode.Quiz)
        {
            return;
        }

        if (IsPointerOverUI())
        {
            return;
        }

        if (AllowUserSpin && Touch.activeTouches.Count == 1)
        {
            Touch touch = Touch.activeTouches[0];
            if (touch.phase == TouchPhase.Moved)
            {
                Spin(touch.delta.x);
            }
        }
        else if (AllowUserResize && Touch.activeTouches.Count == 2)
        {
            Touch touch0 = Touch.activeTouches[0];
            Touch touch1 = Touch.activeTouches[1];
            if (touch0.phase == TouchPhase.Moved || touch1.phase == TouchPhase.Moved)
            {
                Resize(touch0, touch1);
            }
        }
    }

    public void ResetTransform()
    {
        SkeletonTransformHandle handle = GetTransformHandle();
        if (handle != null)
        {
            handle.ResetToBase();
        }
    }

    private void Spin(float dragDeltaX)
    {
        float spinDelta = -dragDeltaX * SpinSpeed;
        SkeletonTransformHandle handle = GetTransformHandle();
        if (handle == null)
        {
            return;
        }

        Vector3 rotationOffset = handle.RotationOffset;
        rotationOffset.y += spinDelta;
        handle.SetRotationOffset(rotationOffset);
    }

    private void Resize(Touch touch0, Touch touch1)
    {
        Vector2 previousTouch0Position = touch0.screenPosition - touch0.delta;
        Vector2 previousTouch1Position = touch1.screenPosition - touch1.delta;
        float previousDistance = Vector2.Distance(previousTouch0Position, previousTouch1Position);
        float currentDistance = Vector2.Distance(touch0.screenPosition, touch1.screenPosition);

        if (Mathf.Approximately(previousDistance, 0f))
        {
            return;
        }

        float scaleChange = 1f + (currentDistance - previousDistance) * ResizeSpeed;
        if (scaleChange <= 0f)
        {
            return;
        }

        float minScale = Mathf.Min(MinScaleMultiplier, MaxScaleMultiplier);
        float maxScale = Mathf.Max(MinScaleMultiplier, MaxScaleMultiplier);
        SkeletonTransformHandle handle = GetTransformHandle();
        if (handle == null)
        {
            return;
        }

        float scaleMultiplier = Mathf.Clamp(handle.ScaleMultiplier * scaleChange, minScale, maxScale);
        handle.SetScaleMultiplier(scaleMultiplier);
    }

    private SkeletonTransformHandle GetTransformHandle()
    {
        if (transformHandle != null)
        {
            return transformHandle;
        }
        transformHandle = GetComponent<SkeletonTransformHandle>();
        if (transformHandle == null)
        {
            transformHandle = gameObject.AddComponent<SkeletonTransformHandle>();
        }

        return transformHandle;
    }

    private static bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        
        foreach (var touch in Touch.activeTouches)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = touch.screenPosition
            };
            
            System.Collections.Generic.List<RaycastResult> results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            
            if (results.Count > 0) return true;
        }
        
        return false;
    }
}
