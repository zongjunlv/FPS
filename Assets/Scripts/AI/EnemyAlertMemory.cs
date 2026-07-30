using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyAlertMemory
{
    private readonly Dictionary<GameObject, int> latestSequenceBySource =
        new();
    private float lifetime = 3f;

    public bool HasAlert { get; private set; }
    public EnemySquadAlert LatestAlert { get; private set; }
    public int AcceptedCount { get; private set; }

    public void Configure(float configuredLifetime)
    {
        lifetime = Mathf.Max(0f, configuredLifetime);
        Reset();
    }

    public bool TryAccept(
        EnemySquadAlert alert,
        Vector3 receiverPosition,
        float now)
    {
        if (alert.Source == null ||
            alert.Confidence <= 0f ||
            !alert.IsInRange(receiverPosition) ||
            alert.IsExpired(now, lifetime))
        {
            return false;
        }

        if (latestSequenceBySource.TryGetValue(
                alert.Source,
                out int latestSequence) &&
            alert.Sequence <= latestSequence)
        {
            return false;
        }

        if (HasAlert && alert.Timestamp <= LatestAlert.Timestamp)
        {
            return false;
        }

        latestSequenceBySource[alert.Source] = alert.Sequence;
        LatestAlert = alert;
        HasAlert = true;
        AcceptedCount++;
        return true;
    }

    public void Reset()
    {
        latestSequenceBySource.Clear();
        HasAlert = false;
        LatestAlert = default;
        AcceptedCount = 0;
    }
}
