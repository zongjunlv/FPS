using UnityEngine;

public readonly struct EnemySquadAlert
{
    public EnemySquadAlert(
        GameObject source,
        Vector3 sourcePosition,
        Vector3 lastKnownPosition,
        float timestamp,
        float confidence,
        float radius,
        int sequence)
    {
        Source = source;
        SourcePosition = sourcePosition;
        LastKnownPosition = lastKnownPosition;
        Timestamp = timestamp;
        Confidence = Mathf.Clamp01(confidence);
        Radius = Mathf.Max(0f, radius);
        Sequence = sequence;
    }

    public GameObject Source { get; }
    public Vector3 SourcePosition { get; }
    public Vector3 LastKnownPosition { get; }
    public float Timestamp { get; }
    public float Confidence { get; }
    public float Radius { get; }
    public int Sequence { get; }

    public bool IsExpired(float now, float lifetime)
    {
        return now - Timestamp > Mathf.Max(0f, lifetime);
    }

    public bool IsInRange(Vector3 receiverPosition)
    {
        return Radius > 0f &&
            Vector3.Distance(SourcePosition, receiverPosition) <= Radius;
    }
}
