using System;
using System.Collections.Generic;

public enum WaveRunPhase
{
    Idle,
    Spawning,
    Fighting,
    Intermission,
    Completed,
    Stopped
}

public readonly struct WaveStageRules
{
    public WaveStageRules(
        int totalEnemyCount,
        int maximumAliveCount,
        float intermissionAfterSeconds)
    {
        if (totalEnemyCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalEnemyCount));
        }

        if (maximumAliveCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAliveCount));
        }

        TotalEnemyCount = totalEnemyCount;
        MaximumAliveCount = Math.Min(
            maximumAliveCount,
            totalEnemyCount);
        IntermissionAfterSeconds = Math.Max(
            0f,
            intermissionAfterSeconds);
    }

    public int TotalEnemyCount { get; }
    public int MaximumAliveCount { get; }
    public float IntermissionAfterSeconds { get; }
}

public sealed class MultiWaveFlowState
{
    private readonly WaveStageRules[] stages;
    private SingleWaveState currentWaveState;

    public MultiWaveFlowState(IReadOnlyList<WaveStageRules> configuredStages)
    {
        if (configuredStages == null || configuredStages.Count == 0)
        {
            throw new ArgumentException(
                "A wave run requires at least one stage.",
                nameof(configuredStages));
        }

        stages = new WaveStageRules[configuredStages.Count];

        for (int index = 0; index < configuredStages.Count; index++)
        {
            stages[index] = configuredStages[index];
        }
    }

    public WaveRunPhase Phase { get; private set; } = WaveRunPhase.Idle;
    public int CurrentWave { get; private set; }
    public int TotalWaves => stages.Length;
    public float IntermissionRemaining { get; private set; }
    public bool IsRunning =>
        Phase == WaveRunPhase.Spawning ||
        Phase == WaveRunPhase.Fighting ||
        Phase == WaveRunPhase.Intermission;
    public bool IsCompleted => Phase == WaveRunPhase.Completed;
    public bool CanSpawn =>
        Phase == WaveRunPhase.Spawning &&
        currentWaveState != null &&
        currentWaveState.CanSpawn;
    public int TotalEnemyCount =>
        currentWaveState != null ? currentWaveState.TotalCount : 0;
    public int SpawnedCount =>
        currentWaveState != null ? currentWaveState.SpawnedCount : 0;
    public int AliveCount =>
        currentWaveState != null ? currentWaveState.AliveCount : 0;
    public int SettledCount =>
        currentWaveState != null ? currentWaveState.SettledCount : 0;
    public int RemainingCount => Math.Max(0, TotalEnemyCount - SettledCount);

    public bool StartRun()
    {
        if (Phase != WaveRunPhase.Idle)
        {
            return false;
        }

        CurrentWave = 1;
        BeginCurrentWave();
        return true;
    }

    public bool TryRegisterSpawn(int spawnId)
    {
        if (!CanSpawn || !currentWaveState.TryRegisterSpawn(spawnId))
        {
            return false;
        }

        if (currentWaveState.SpawnedCount == currentWaveState.TotalCount)
        {
            Phase = WaveRunPhase.Fighting;
        }

        return true;
    }

    public bool TrySettle(int spawnId)
    {
        if (currentWaveState == null ||
            !currentWaveState.TrySettle(spawnId))
        {
            return false;
        }

        if (!currentWaveState.IsComplete)
        {
            return true;
        }

        if (CurrentWave >= TotalWaves)
        {
            IntermissionRemaining = 0f;
            Phase = WaveRunPhase.Completed;
            return true;
        }

        IntermissionRemaining = stages[CurrentWave - 1]
            .IntermissionAfterSeconds;

        if (IntermissionRemaining <= 0f)
        {
            CurrentWave++;
            BeginCurrentWave();
            return true;
        }

        Phase = WaveRunPhase.Intermission;
        return true;
    }

    public bool Tick(float deltaTime)
    {
        if (Phase != WaveRunPhase.Intermission || deltaTime <= 0f)
        {
            return false;
        }

        IntermissionRemaining = Math.Max(
            0f,
            IntermissionRemaining - deltaTime);

        if (IntermissionRemaining > 0f)
        {
            return false;
        }

        CurrentWave++;
        BeginCurrentWave();
        return true;
    }

    public bool StopRun()
    {
        if (!IsRunning)
        {
            return false;
        }

        IntermissionRemaining = 0f;
        Phase = WaveRunPhase.Stopped;
        return true;
    }

    private void BeginCurrentWave()
    {
        WaveStageRules rules = stages[CurrentWave - 1];
        currentWaveState = new SingleWaveState(
            rules.TotalEnemyCount,
            rules.MaximumAliveCount);
        IntermissionRemaining = 0f;
        Phase = WaveRunPhase.Spawning;
    }
}
