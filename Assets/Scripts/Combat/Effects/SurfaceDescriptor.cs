using UnityEngine;

public sealed class SurfaceDescriptor : MonoBehaviour
{
    [SerializeField] private SurfaceType surfaceType =
        SurfaceType.Concrete;

    public SurfaceType Type => surfaceType;

    public void Configure(SurfaceType type)
    {
        surfaceType = type;
    }
}

public static class SurfaceResolver
{
    public static SurfaceType Resolve(Collider collider)
    {
        if (collider == null)
        {
            return SurfaceType.Concrete;
        }

        SurfaceDescriptor descriptor =
            collider.GetComponentInParent<SurfaceDescriptor>();

        if (descriptor != null)
        {
            return descriptor.Type;
        }

        string objectName = collider.gameObject.name.ToLowerInvariant();
        Renderer renderer = collider.GetComponentInParent<Renderer>();
        string materialName =
            renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial.name.ToLowerInvariant()
                : string.Empty;

        return objectName.Contains("metal") ||
            materialName.Contains("metal")
            ? SurfaceType.Metal
            : SurfaceType.Concrete;
    }
}
