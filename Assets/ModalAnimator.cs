using System.Collections;
using UnityEngine;

public class ModalAnimator : MonoBehaviour
{
    public float duration = 0.35f;
    public float startOffset = -1000f;

    private RectTransform rectTransform;
    private Vector2 targetPosition;
    private Coroutine animationCoroutine;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        targetPosition = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        if (rectTransform == null) return;

        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        
        animationCoroutine = StartCoroutine(SlideUpRoutine());
    }

    private IEnumerator SlideUpRoutine()
    {
        float time = 0f;
        Vector2 startPos = new Vector2(targetPosition.x, targetPosition.y + startOffset);
        rectTransform.anchoredPosition = startPos;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            
            // Ease-out cubic: 1 - (1-t)^3
            float easeT = 1f - Mathf.Pow(1f - t, 3f);
            
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPosition, easeT);
            yield return null;
        }

        rectTransform.anchoredPosition = targetPosition;
    }
}
