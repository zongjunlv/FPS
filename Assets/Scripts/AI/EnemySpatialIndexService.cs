using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

[DefaultExecutionOrder(200)]
public sealed class EnemySpatialIndexService : MonoBehaviour,
    IEnemyNeighborQuery
{
    [SerializeField, Min(0.5f)] private float cellSize = 10f;
    [SerializeField, Min(8)] private int initialCapacity = 128;

    private readonly List<EnemyPerceptionController> members = new(128);
    private readonly List<EnemyPerceptionController> queryBuffer = new(128);
    private EnemySpatialHash spatialHash;

    public static EnemySpatialIndexService Instance { get; private set; }
    public static bool GlobalEnabled { get; private set; } = true;
    public int RegisteredCount => spatialHash?.RegisteredCount ?? 0;
    public int PeakRegisteredCount { get; private set; }
    public long QueryCount { get; private set; }
    public long CandidateVisitCount { get; private set; }
    public long QueryElapsedTicks { get; private set; }
    public int CellMoveCount { get; private set; }

    public static EnemySpatialIndexService EnsureForActiveScene()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject host = new("Enemy Spatial Index");
        return host.AddComponent<EnemySpatialIndexService>();
    }

    public static void SetGlobalEnabled(bool enabled)
    {
        GlobalEnabled = enabled;
    }

    private void Awake()
    {
        Instance = this;
        spatialHash = new EnemySpatialHash(cellSize, initialCapacity);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void LateUpdate()
    {
        for (int index = members.Count - 1; index >= 0; index--)
        {
            EnemyPerceptionController member = members[index];

            if (member == null)
            {
                spatialHash.Unregister(member);
                members.RemoveAt(index);
                continue;
            }

            if (spatialHash.Update(member))
            {
                CellMoveCount++;
            }
        }
    }

    public bool Register(EnemyPerceptionController member)
    {
        if (member == null || members.Contains(member))
        {
            return false;
        }

        members.Add(member);
        bool registered = spatialHash.Register(member);
        PeakRegisteredCount = Mathf.Max(PeakRegisteredCount, RegisteredCount);
        return registered;
    }

    public bool Unregister(EnemyPerceptionController member)
    {
        members.Remove(member);
        return spatialHash.Unregister(member);
    }

    public bool Synchronize(EnemyPerceptionController member)
    {
        if (member == null)
        {
            return false;
        }

        if (!members.Contains(member))
        {
            return Register(member);
        }

        bool moved = spatialHash.Update(member);

        if (moved)
        {
            CellMoveCount++;
        }

        return moved;
    }

    public int CollectAliveNeighbors(
        EnemyController source,
        float radius,
        string requiredTag,
        List<EnemyController> results,
        int maximumResults = int.MaxValue)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        results.Clear();

        if (source == null || maximumResults <= 0)
        {
            return 0;
        }

        EnemyPerceptionController excluded =
            source.GetComponent<EnemyPerceptionController>();
        int count = CollectPerceptions(
            source.transform.position,
            radius,
            excluded,
            requiredTag,
            queryBuffer,
            maximumResults);

        for (int index = 0; index < count; index++)
        {
            EnemyController controller =
                queryBuffer[index].GetComponent<EnemyController>();

            if (controller != null)
            {
                results.Add(controller);
            }
        }

        return results.Count;
    }

    public int CollectPerceptions(
        Vector3 center,
        float radius,
        EnemyPerceptionController excluded,
        string requiredTag,
        List<EnemyPerceptionController> results,
        int maximumResults = int.MaxValue)
    {
        long startedAt = Stopwatch.GetTimestamp();
        int count = GlobalEnabled
            ? spatialHash.Query(
                center,
                radius,
                excluded,
                requiredTag,
                maximumResults,
                results)
            : spatialHash.QueryNaively(
                center,
                radius,
                excluded,
                requiredTag,
                maximumResults,
                results);
        QueryElapsedTicks += Stopwatch.GetTimestamp() - startedAt;
        QueryCount++;
        CandidateVisitCount += spatialHash.LastCandidateVisitCount;
        return count;
    }

    public void ResetMetrics()
    {
        QueryCount = 0;
        CandidateVisitCount = 0;
        QueryElapsedTicks = 0;
        CellMoveCount = 0;
        PeakRegisteredCount = RegisteredCount;
    }

}
