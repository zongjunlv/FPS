using System;
using UnityEngine;

public enum EnemyAiLodTier
{
    Near,
    Mid,
    Far
}

public enum EnemyAiLodChannel
{
    Perception,
    Combat,
    Ability,
    Navigation
}

public readonly struct EnemyAiLodPolicy
{
    public EnemyAiLodPolicy(float nearDistance, float midDistance)
    {
        NearDistance = Mathf.Max(1f, nearDistance);
        MidDistance = Mathf.Max(NearDistance + 1f, midDistance);
    }

    public float NearDistance { get; }
    public float MidDistance { get; }

    public EnemyAiLodTier Classify(
        float distance,
        bool hasVisualContact,
        bool wasRecentlyDamaged,
        EnemyAwarenessState awareness)
    {
        float safeDistance = Mathf.Max(0f, distance);

        if (wasRecentlyDamaged || safeDistance <= NearDistance ||
            hasVisualContact && safeDistance <= NearDistance * 1.5f)
        {
            return EnemyAiLodTier.Near;
        }

        if (safeDistance <= MidDistance || hasVisualContact ||
            awareness == EnemyAwarenessState.Suspicious ||
            awareness == EnemyAwarenessState.Alert ||
            awareness == EnemyAwarenessState.Search)
        {
            return EnemyAiLodTier.Mid;
        }

        return EnemyAiLodTier.Far;
    }

    public static int DecisionInterval(EnemyAiLodTier tier)
    {
        return tier switch
        {
            EnemyAiLodTier.Near => 2,
            EnemyAiLodTier.Mid => 6,
            EnemyAiLodTier.Far => 20,
            _ => 20
        };
    }

    public static int SightInterval(EnemyAiLodTier tier)
    {
        return tier switch
        {
            EnemyAiLodTier.Near => 2,
            EnemyAiLodTier.Mid => 8,
            EnemyAiLodTier.Far => 24,
            _ => 24
        };
    }

    public static bool IsScheduledFrame(
        int frame,
        int stableSlot,
        int interval)
    {
        int safeInterval = Math.Max(1, interval);
        int normalizedSlot = Math.Abs(stableSlot % safeInterval);
        int normalizedFrame = Math.Abs(frame % safeInterval);
        return normalizedFrame == normalizedSlot;
    }
}
