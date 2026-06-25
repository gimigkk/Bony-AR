using System;
using UnityEngine;

public enum ARAppMode
{
    Skeleton,
    Quiz
}

public enum SkeletonSource
{
    ManualPlacement
}

public sealed class SkeletonRegistry : MonoBehaviour
{
    private static SkeletonRegistry instance;

    private GameObject manualSkeleton;
    private GameObject activeSkeleton;

    public event Action<GameObject> ActiveSkeletonChanged;

    public static SkeletonRegistry Instance
    {
        get { return instance; }
    }

    public GameObject ActiveSkeleton
    {
        get { return activeSkeleton; }
    }

    public GameObject ManualSkeleton
    {
        get { return manualSkeleton; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject registryObject = new GameObject("Skeleton Registry");
        registryObject.AddComponent<SkeletonRegistry>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void Register(GameObject skeleton, SkeletonSource source)
    {
        if (skeleton == null)
        {
            return;
        }

        EnsureTransformHandle(skeleton);

        if (source == SkeletonSource.ManualPlacement)
        {
            manualSkeleton = skeleton;
            SetActive(skeleton);
        }
    }

    public void Unregister(GameObject skeleton)
    {
        if (skeleton == null)
        {
            return;
        }

        if (manualSkeleton == skeleton)
        {
            manualSkeleton = null;
        }

        if (activeSkeleton == skeleton)
        {
            SetActive(null);
        }
    }

    public void SetActive(GameObject skeleton)
    {
        if (activeSkeleton == skeleton)
        {
            return;
        }

        activeSkeleton = skeleton;
        ActiveSkeletonChanged?.Invoke(activeSkeleton);
    }

    public void ClearManualSkeleton()
    {
        if (manualSkeleton == null)
        {
            return;
        }

        GameObject skeletonToDestroy = manualSkeleton;
        manualSkeleton = null;

        if (activeSkeleton == skeletonToDestroy)
        {
            SetActive(null);
        }

        Destroy(skeletonToDestroy);
    }

    public void ClearAllRuntimeState()
    {
        ClearManualSkeleton();
        SetActive(null);
    }

    private static void EnsureTransformHandle(GameObject skeleton)
    {
        if (skeleton.GetComponent<SkeletonTransformHandle>() == null)
        {
            skeleton.AddComponent<SkeletonTransformHandle>();
        }
    }
}
