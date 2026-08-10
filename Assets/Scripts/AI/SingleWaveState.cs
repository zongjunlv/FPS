using System;
using System.Collections.Generic;

public readonly struct WaveProgressSnapshot
{
    public WaveProgressSnapshot(
        int totalCount,
        int spawnedCount,
        int aliveCount,
        int settledCount)
    {
        TotalCount = Math.Max(0, totalCount);
        SpawnedCount = Math.Max(0, spawnedCount);
        AliveCount = Math.Max(0, aliveCount);
        SettledCount = Math.Max(0, settledCount);
    }

    public int CurrentWave => 1;
    public int TotalWaves => 1;
    public int TotalCount { get; }
    public int SpawnedCount { get; }
    public int AliveCount { get; }
    public int SettledCount { get; }
    public int RemainingCount => Math.Max(0, TotalCount - SettledCount);
}

public interface IWaveProgressSource
{
    event Action<WaveProgressSnapshot> ProgressChanged;
    WaveProgressSnapshot CurrentProgress { get; }
}

public sealed class SingleWaveState
{
    private readonly HashSet<int> activeSpawnIds = new();
    private readonly HashSet<int> settledSpawnIds = new();
    private bool completionPublished;

    public event Action Completed;

    public SingleWaveState(int totalCount, int maximumAliveCount)
    {
        if (totalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        }

        if (maximumAliveCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAliveCount));
        }

        TotalCount = totalCount;
        MaximumAliveCount = Math.Min(
            maximumAliveCount,
            totalCount);
    }

    public int TotalCount { get; }
    public int MaximumAliveCount { get; }
    public int SpawnedCount { get; private set; }
    public int AliveCount => activeSpawnIds.Count;
    public int SettledCount => settledSpawnIds.Count;
    public bool CanSpawn =>
        SpawnedCount < TotalCount &&
        AliveCount < MaximumAliveCount;
    public bool IsComplete =>
        SpawnedCount == TotalCount &&
        AliveCount == 0;
    public WaveProgressSnapshot Progress =>
        new WaveProgressSnapshot(
            TotalCount,
            SpawnedCount,
            AliveCount,
            SettledCount);

    public bool TryRegisterSpawn(int spawnId)
    {
        if (!CanSpawn ||
            activeSpawnIds.Contains(spawnId) ||
            settledSpawnIds.Contains(spawnId))
        {
            return false;
        }

        activeSpawnIds.Add(spawnId);
        SpawnedCount++;
        return true;
    }

    public bool TrySettle(int spawnId)
    {
        if (!activeSpawnIds.Remove(spawnId))
        {
            return false;
        }

        settledSpawnIds.Add(spawnId);
        PublishCompletionIfNeeded();
        return true;
    }

    private void PublishCompletionIfNeeded()
    {
        if (completionPublished || !IsComplete)
        {
            return;
        }

        completionPublished = true;
        Completed?.Invoke();
    }
}
