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

public sealed class SingleWaveStateSnapshot
{
    public SingleWaveStateSnapshot(
        int totalCount,
        int maximumAliveCount,
        IReadOnlyList<int> spawnedIds,
        IReadOnlyList<int> activeIds,
        IReadOnlyList<int> settledIds)
    {
        TotalCount = totalCount;
        MaximumAliveCount = maximumAliveCount;
        SpawnedIds = Copy(spawnedIds);
        ActiveIds = Copy(activeIds);
        SettledIds = Copy(settledIds);
    }

    public int TotalCount { get; }
    public int MaximumAliveCount { get; }
    public IReadOnlyList<int> SpawnedIds { get; }
    public IReadOnlyList<int> ActiveIds { get; }
    public IReadOnlyList<int> SettledIds { get; }

    private static int[] Copy(IReadOnlyList<int> values)
    {
        if (values == null) return null;
        var copy = new int[values.Count];
        for (int index = 0; index < values.Count; index++) copy[index] = values[index];
        return copy;
    }
}

public sealed class SingleWaveState
{
    private readonly HashSet<int> spawnedIds = new();
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
        if (spawnId < 1 || !CanSpawn || spawnedIds.Contains(spawnId))
        {
            return false;
        }

        spawnedIds.Add(spawnId);
        activeSpawnIds.Add(spawnId);
        SpawnedCount++;
        return true;
    }

    public SingleWaveStateSnapshot CaptureState()
    {
        return new SingleWaveStateSnapshot(
            TotalCount,
            MaximumAliveCount,
            Sorted(spawnedIds),
            Sorted(activeSpawnIds),
            Sorted(settledSpawnIds));
    }

    /// <summary>Hydrates state atomically and never publishes Completed.</summary>
    public bool TryRestore(SingleWaveStateSnapshot snapshot, out string error)
    {
        if (!TryValidateRestore(snapshot, out var restoredSpawned,
                out var restoredActive, out var restoredSettled, out error))
        {
            return false;
        }

        spawnedIds.Clear();
        activeSpawnIds.Clear();
        settledSpawnIds.Clear();
        spawnedIds.UnionWith(restoredSpawned);
        activeSpawnIds.UnionWith(restoredActive);
        settledSpawnIds.UnionWith(restoredSettled);
        SpawnedCount = spawnedIds.Count;
        completionPublished = IsComplete;
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

    private bool TryValidateRestore(
        SingleWaveStateSnapshot snapshot,
        out HashSet<int> restoredSpawned,
        out HashSet<int> restoredActive,
        out HashSet<int> restoredSettled,
        out string error)
    {
        restoredSpawned = new HashSet<int>();
        restoredActive = new HashSet<int>();
        restoredSettled = new HashSet<int>();
        error = null;

        if (snapshot == null || snapshot.SpawnedIds == null ||
            snapshot.ActiveIds == null || snapshot.SettledIds == null)
        {
            error = "Wave restore state is incomplete.";
            return false;
        }
        if (snapshot.TotalCount != TotalCount ||
            snapshot.MaximumAliveCount != MaximumAliveCount)
        {
            error = "Wave restore rules do not match the configured wave.";
            return false;
        }
        if (snapshot.SpawnedIds.Count > TotalCount ||
            snapshot.ActiveIds.Count > MaximumAliveCount)
        {
            error = "Wave restore counts exceed configured limits.";
            return false;
        }

        for (int index = 0; index < snapshot.SpawnedIds.Count; index++)
        {
            int id = snapshot.SpawnedIds[index];
            if (id < 1 || !restoredSpawned.Add(id))
            {
                error = "Spawned IDs must be positive and unique.";
                return false;
            }
        }
        for (int index = 0; index < snapshot.ActiveIds.Count; index++)
        {
            int id = snapshot.ActiveIds[index];
            if (!restoredSpawned.Contains(id) || !restoredActive.Add(id))
            {
                error = "Active IDs must be unique spawned entities.";
                return false;
            }
        }
        for (int index = 0; index < snapshot.SettledIds.Count; index++)
        {
            int id = snapshot.SettledIds[index];
            if (!restoredSpawned.Contains(id) || restoredActive.Contains(id) ||
                !restoredSettled.Add(id))
            {
                error = "Settled IDs must be unique and disjoint from active IDs.";
                return false;
            }
        }
        if (restoredActive.Count + restoredSettled.Count != restoredSpawned.Count)
        {
            error = "Every spawned entity must be active or settled.";
            return false;
        }
        return true;
    }

    private static int[] Sorted(HashSet<int> values)
    {
        var copy = new int[values.Count];
        values.CopyTo(copy);
        Array.Sort(copy);
        return copy;
    }
}
