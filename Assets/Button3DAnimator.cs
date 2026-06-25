using UnityEngine;
using UnityEngine.EventSystems;

public class Button3DAnimator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    private RectTransform topLayerRect;
    
    // Y-offsets matching the CSS transform: translateY
    private float idleY = 6f;       // -0.2em
    private float hoverY = 10f;     // -0.33em
    private float pressedY = 0f;    // 0em
    
    private float currentTargetY;
    private float animationSpeed = 25f; // transition 0.1s ease

    public void Initialize(RectTransform topLayer)
    {
        topLayerRect = topLayer;
        currentTargetY = idleY;
        
        if (topLayerRect != null)
        {
            Vector2 pos = topLayerRect.anchoredPosition;
            pos.y = currentTargetY;
            topLayerRect.anchoredPosition = pos;
        }
    }

    private void Update()
    {
        if (topLayerRect != null)
        {
            Vector2 currentPos = topLayerRect.anchoredPosition;
            if (Mathf.Abs(currentPos.y - currentTargetY) > 0.1f)
            {
                currentPos.y = Mathf.Lerp(currentPos.y, currentTargetY, Time.deltaTime * animationSpeed);
                topLayerRect.anchoredPosition = currentPos;
            }
            else
            {
                currentPos.y = currentTargetY;
                topLayerRect.anchoredPosition = currentPos;
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        currentTargetY = pressedY;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // If the pointer is still over the button after releasing, go to hover state
        if (eventData.pointerCurrentRaycast.gameObject != null && 
            (eventData.pointerCurrentRaycast.gameObject == gameObject || eventData.pointerCurrentRaycast.gameObject.transform.IsChildOf(transform)))
        {
            currentTargetY = hoverY;
        }
        else
        {
            currentTargetY = idleY;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Don't pop up if we're dragging into it while holding down
        if (currentTargetY != pressedY)
        {
            currentTargetY = hoverY;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (currentTargetY != pressedY)
        {
            currentTargetY = idleY;
        }
    }
}
