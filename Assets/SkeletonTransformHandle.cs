using UnityEngine;

public sealed class SkeletonTransformHandle : MonoBehaviour
{
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 baseLocalScale;
    private Vector3 positionOffset;
    private Vector3 rotationOffset;
    private float scaleMultiplier = 1f;

    public Vector3 PositionOffset
    {
        get { return positionOffset; }
    }

    public Vector3 RotationOffset
    {
        get { return rotationOffset; }
    }

    public float ScaleMultiplier
    {
        get { return scaleMultiplier; }
    }

    private void Awake()
    {
        CaptureBaseTransform();
    }

    public void CaptureBaseTransform()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
        baseLocalScale = transform.localScale;
    }

    public void SetPositionOffset(Vector3 offset)
    {
        positionOffset = offset;
        Apply();
    }

    public void SetRotationOffset(Vector3 eulerOffset)
    {
        rotationOffset = eulerOffset;
        Apply();
    }

    public void SetScaleMultiplier(float multiplier)
    {
        scaleMultiplier = Mathf.Max(0.01f, multiplier);
        Apply();
    }

    public void ResetToBase()
    {
        positionOffset = Vector3.zero;
        rotationOffset = Vector3.zero;
        scaleMultiplier = 1f;
        Apply();
    }

    private void Apply()
    {
        transform.localPosition = baseLocalPosition + positionOffset;
        transform.localRotation = baseLocalRotation * Quaternion.Euler(rotationOffset);
        transform.localScale = baseLocalScale * scaleMultiplier;
    }
}
