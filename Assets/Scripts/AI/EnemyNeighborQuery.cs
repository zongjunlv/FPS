using System.Collections.Generic;
using UnityEngine;

public interface IEnemyNeighborQuery
{
    int CollectAliveNeighbors(
        EnemyController source,
        float radius,
        string requiredTag,
        List<EnemyController> results,
        int maximumResults = int.MaxValue);
}

public static class EnemyNeighborQueryUtility
{
    public static bool IsInRange(
        EnemyController source,
        EnemyController candidate,
        float radius)
    {
        if (source == null || candidate == null || source == candidate)
        {
            return false;
        }

        Health health = candidate.GetComponent<Health>();

        if (!candidate.isActiveAndEnabled ||
            health == null ||
            health.IsDead)
        {
            return false;
        }

        Vector3 offset = candidate.transform.position -
            source.transform.position;
        offset.y = 0f;
        float safeRadius = Mathf.Max(0f, radius);
        return offset.sqrMagnitude <= safeRadius * safeRadius;
    }
}
