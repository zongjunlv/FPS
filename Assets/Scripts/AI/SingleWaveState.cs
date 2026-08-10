using System;
using System.Collections.Generic;

public enum WavePresentationCue
{
    None,
    WaveStarted,
    WaveCleared,
    RunCompleted
}

public readonly struct WaveProgressSnapshot
{
    public WaveProgressSnapshot(
        int totalCount,
        int spawnedCount,
        int aliveCount,
        int settledCount)
        : this(
            1,
            1,
            WaveRunPhase.Spawning,
            totalCount,
            spawnedCount,
            aliveCount,
            settledCount,
            0f,
            WavePresentationCue.None,
            0)
    {
    }

    public WaveProgressSnapshot(
        int currentWave,
        int totalWaves,
        WaveRunPhase phase,
        int totalCount,
        int spawnedCount,
        int aliveCount,
        int settledCount,
        float intermissionRemaining,
        WavePresentationCue presentationCue,
        int presentationWave)
    {
        CurrentWave = Math.Max(0, currentWave);
        TotalWaves = Math.Max(0, totalWaves);
        Phase = phase;
        TotalCount = Math.Max(0, totalCount);
        SpawnedCount = Math.Max(0, spawnedCount);
        AliveCount = Math.Max(0, aliveCount);
        SettledCount = Math.Max(0, settledCount);
        IntermissionRemaining = Math.Max(0f, intermissionRemaining);
        PresentationCue = presentationCue;
        PresentationWave = Math.Max(0, presentationWave);
    }

    public int CurrentWave { get; }
    public int TotalWaves { get; }
    public WaveRunPhase Phase { get; }
    public int TotalCount { get; }
    public int SpawnedCount { get; }
    public int AliveCount { get; }
    public int SettledCount { get; }
    public float IntermissionRemaining { get; }
    public WavePresentationCue PresentationCue { get; }
    public int PresentationWave { get; }
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
