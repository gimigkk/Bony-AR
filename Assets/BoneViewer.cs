using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class BoneViewer : MonoBehaviour, IDragHandler
{
    private const int ViewerLayer = 31;
    
    private Camera viewerCamera;
    private Light viewerLight;
    private RenderTexture renderTexture;
    private RawImage rawImage;

    private Transform pivot;
    private GameObject currentClone;

    private void Awake()
    {
        rawImage = GetComponent<RawImage>();

        // Create RenderTexture
        renderTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.Default);
        renderTexture.Create();
        rawImage.texture = renderTexture;

        // Create Pivot for rotation
        GameObject pivotObj = new GameObject("BoneViewer_Pivot");
        pivot = pivotObj.transform;
        // Move pivot far away so it doesn't intersect with the AR scene
        pivot.position = new Vector3(0, -1000f, 0);

        // Create Camera
        GameObject camObj = new GameObject("BoneViewer_Camera");
        camObj.transform.SetParent(pivot.parent);
        camObj.transform.position = pivot.position + new Vector3(0, 0, 3f); // Move in front of the object
        camObj.transform.LookAt(pivot);

        viewerCamera = camObj.AddComponent<Camera>();
        viewerCamera.clearFlags = CameraClearFlags.SolidColor;
        viewerCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // Transparent background
        viewerCamera.cullingMask = 1 << ViewerLayer;
        viewerCamera.targetTexture = renderTexture;
        viewerCamera.orthographic = true;
        viewerCamera.orthographicSize = 1.25f; // Perfect size to frame a max dimension of 1.6 rotated
        viewerCamera.nearClipPlane = 0.1f;
        viewerCamera.farClipPlane = 10f;

        // Prevent global AR lights from illuminating our model and stealing the URP Main Light slot
        Light[] allLights = FindObjectsOfType<Light>();
        foreach (Light l in allLights)
        {
            if (l.type == LightType.Directional && l.gameObject.scene.IsValid())
            {
                l.cullingMask &= ~(1 << ViewerLayer); // Remove ViewerLayer from global lights
            }
        }

        // Create a single Directional Light for the viewer. 
        // Because it's the only one affecting ViewerLayer, URP will guarantee it acts as the Main Light!
        GameObject keyLightObj = new GameObject("BoneViewer_Light");
        keyLightObj.transform.SetParent(camObj.transform);
        keyLightObj.transform.localPosition = Vector3.zero;
        keyLightObj.transform.localRotation = Quaternion.Euler(20f, -30f, 0f);
        Light keyLight = keyLightObj.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.cullingMask = 1 << ViewerLayer;
        keyLight.intensity = 1.2f;
    }

    public void ShowBone(Transform originalBone, Transform skeletonRoot = null)
    {
        Clear();

        if (originalBone == null)
        {
            return;
        }

        // Clone the bone
        currentClone = Instantiate(originalBone.gameObject);
        currentClone.name = originalBone.name + "_ViewerClone";
        currentClone.transform.SetParent(pivot, false);
        
        // Reset local transformations so it faces a consistent "forward" direction
        currentClone.transform.localPosition = Vector3.zero;
        if (skeletonRoot != null)
        {
            // Calculate rotation relative to the skeleton root so it retains its anatomical upright orientation
            currentClone.transform.localRotation = Quaternion.Inverse(skeletonRoot.rotation) * originalBone.rotation;
        }
        else
        {
            currentClone.transform.localRotation = Quaternion.identity;
        }
        currentClone.transform.localScale = Vector3.one;
        
        // Strip out any child bones that got cloned along with it.
        // We must unparent them immediately, otherwise GetComponentsInChildren will still find them
        // because Destroy() is deferred until the end of the frame!
        for (int i = currentClone.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = currentClone.transform.GetChild(i);
            child.SetParent(null);
            Destroy(child.gameObject);
        }
        
        // Strip unnecessary components (like colliders)
        Collider[] colliders = currentClone.GetComponentsInChildren<Collider>(true);
        foreach (Collider col in colliders)
        {
            Destroy(col);
        }

        // Set Layer recursively
        SetLayerRecursively(currentClone, ViewerLayer);

        // Calculate bounds to center and scale it
        Bounds bounds = CalculateBounds(currentClone);
        
        // Center the clone relative to the pivot
        currentClone.transform.localPosition = -bounds.center;

        // Scale it so it fits nicely
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxDimension > 0.001f)
        {
            float targetScale = 1.6f / maxDimension; // Increased to make the bone larger
            currentClone.transform.localScale = Vector3.one * targetScale;
            currentClone.transform.localPosition *= targetScale; // scale the centering offset so it stays centered
        }

        // Reset pivot rotation
        pivot.localRotation = Quaternion.identity;
    }

    public void Clear()
    {
        if (currentClone != null)
        {
            Destroy(currentClone);
            currentClone = null;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pivot != null)
        {
            float rotX = eventData.delta.y * 0.5f;
            float rotY = -eventData.delta.x * 0.5f;

            // Rotate around world up and right axes relative to the camera to ensure intuitive rotation
            pivot.Rotate(Vector3.up, rotY, Space.World);
            pivot.Rotate(viewerCamera.transform.right, rotX, Space.World);
        }
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(obj.transform.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        // Return local bounds relative to the object's transform
        bounds.center = bounds.center - obj.transform.position;
        return bounds;
    }

    private void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (viewerCamera != null) Destroy(viewerCamera.gameObject);
        if (pivot != null) Destroy(pivot.gameObject);
    }
}
