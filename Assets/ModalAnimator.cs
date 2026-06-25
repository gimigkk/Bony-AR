using System.Collections;
using UnityEngine;

public class ModalAnimator : MonoBehaviour
{
    public float duration = 0.35f;
    public float startOffset = -150f; // Shorter distance looks much better with fade

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 targetPosition;
    private Coroutine animationCoroutine;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        targetPosition = rectTransform.anchoredPosition;
        
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        if (rectTransform == null) return;

        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        
        animationCoroutine = StartCoroutine(SlideUpAndFadeRoutine());
    }

    private IEnumerator SlideUpAndFadeRoutine()
    {
        float time = 0f;
        Vector2 startPos = new Vector2(targetPosition.x, targetPosition.y + startOffset);
        rectTransform.anchoredPosition = startPos;
        
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            
            // Ease-out cubic: 1 - (1-t)^3
            float easeT = 1f - Mathf.Pow(1f - t, 3f);
            
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPosition, easeT);
            if (canvasGroup != null) canvasGroup.alpha = easeT;
            
            yield return null;
        }

        rectTransform.anchoredPosition = targetPosition;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }
}
