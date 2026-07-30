using UnityEngine;

public readonly struct SoundStimulus
{
    public SoundStimulus(
        Vector3 position,
        float radius,
        float intensity,
        GameObject source)
    {
        Position = position;
        Radius = Mathf.Max(0f, radius);
        Intensity = Mathf.Max(0f, intensity);
        Source = source;
    }

    public Vector3 Position { get; }
    public float Radius { get; }
    public float Intensity { get; }
    public GameObject Source { get; }

    public float StrengthAt(Vector3 listenerPosition)
    {
        if (Radius <= 0f || Intensity <= 0f)
        {
            return 0f;
        }

        float distance = Vector3.Distance(
            Position,
            listenerPosition);

        if (distance > Radius)
        {
            return 0f;
        }

        return Mathf.Clamp01(
            (1f - distance / Radius) * Intensity);
    }
}
