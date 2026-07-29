using UnityEngine;

public readonly struct SurfaceImpactStyle
{
    private SurfaceImpactStyle(
        Color color,
        int sparkCount,
        float audioPitch,
        float uniqueMarkerLifetime,
        float uniqueMarkerScale)
    {
        Color = color;
        SparkCount = sparkCount;
        AudioPitch = audioPitch;
        UniqueMarkerLifetime = uniqueMarkerLifetime;
        UniqueMarkerScale = uniqueMarkerScale;
    }

    public Color Color { get; }
    public int SparkCount { get; }
    public float AudioPitch { get; }
    public float UniqueMarkerLifetime { get; }
    public float UniqueMarkerScale { get; }

    public static SurfaceImpactStyle For(SurfaceType surface)
    {
        return surface == SurfaceType.Metal
            ? new SurfaceImpactStyle(
                new Color(1f, 0.72f, 0.18f, 1f),
                32,
                1.15f,
                3f,
                0.16f)
            : new SurfaceImpactStyle(
                new Color(0.6f, 0.55f, 0.48f, 1f),
                0,
                0.92f,
                3.05f,
                0.08f);
    }
}

public sealed class SurfaceImpactVisualMarker : MonoBehaviour
{
    public SurfaceType Surface { get; private set; }

    public void Configure(SurfaceType surface)
    {
        Surface = surface;
    }
}
