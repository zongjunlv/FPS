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

public sealed class MultiWaveFlowStateSnapshot
{
    public MultiWaveFlowStateSnapshot(
        int currentWave,
        WaveRunPhase phase,
        SingleWaveStateSnapshot currentWaveState,
        float intermissionRemaining)
    {
        CurrentWave = currentWave;
        Phase = phase;
        CurrentWaveState = currentWaveState;
        IntermissionRemaining = intermissionRemaining;
    }

    public int CurrentWave { get; }
    public WaveRunPhase Phase { get; }
    public SingleWaveStateSnapshot CurrentWaveState { get; }
    public float IntermissionRemaining { get; }
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

    public MultiWaveFlowStateSnapshot CaptureState()
    {
        return new MultiWaveFlowStateSnapshot(
            CurrentWave,
            Phase,
            currentWaveState?.CaptureState(),
            IntermissionRemaining);
    }

    /// <summary>Validates against configured stages before replacing any live state.</summary>
    public bool TryRestore(MultiWaveFlowStateSnapshot snapshot, out string error)
    {
        error = null;
        if (snapshot == null || !Enum.IsDefined(typeof(WaveRunPhase), snapshot.Phase) ||
            float.IsNaN(snapshot.IntermissionRemaining) ||
            float.IsInfinity(snapshot.IntermissionRemaining) ||
            snapshot.IntermissionRemaining < 0f)
        {
            error = "Wave flow restore state is invalid.";
            return false;
        }

        if (snapshot.Phase == WaveRunPhase.Idle)
        {
            if (snapshot.CurrentWave != 0 || snapshot.CurrentWaveState != null ||
                snapshot.IntermissionRemaining != 0f)
            {
                error = "Idle wave flow cannot contain active wave state.";
                return false;
            }

            CurrentWave = 0;
            currentWaveState = null;
            IntermissionRemaining = 0f;
            Phase = WaveRunPhase.Idle;
            return true;
        }

        if (snapshot.CurrentWave < 1 || snapshot.CurrentWave > TotalWaves ||
            snapshot.CurrentWaveState == null)
        {
            error = "Current wave is outside the configured stage range.";
            return false;
        }

        WaveStageRules rules = stages[snapshot.CurrentWave - 1];
        var restoredWave = new SingleWaveState(
            rules.TotalEnemyCount,
            rules.MaximumAliveCount);
        if (!restoredWave.TryRestore(snapshot.CurrentWaveState, out error))
        {
            return false;
        }
        if (!ValidatePhase(snapshot, restoredWave, out error))
        {
            return false;
        }

        CurrentWave = snapshot.CurrentWave;
        currentWaveState = restoredWave;
        IntermissionRemaining = snapshot.IntermissionRemaining;
        Phase = snapshot.Phase;
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

    private bool ValidatePhase(
        MultiWaveFlowStateSnapshot snapshot,
        SingleWaveState wave,
        out string error)
    {
        error = null;
        switch (snapshot.Phase)
        {
            case WaveRunPhase.Spawning:
                if (wave.SpawnedCount >= wave.TotalCount || snapshot.IntermissionRemaining != 0f)
                    error = "Spawning phase has incompatible counts or intermission.";
                break;
            case WaveRunPhase.Fighting:
                if (wave.SpawnedCount != wave.TotalCount || wave.AliveCount == 0 ||
                    snapshot.IntermissionRemaining != 0f)
                    error = "Fighting phase requires all enemies spawned and at least one alive.";
                break;
            case WaveRunPhase.Intermission:
                if (!wave.IsComplete || snapshot.CurrentWave >= TotalWaves ||
                    snapshot.IntermissionRemaining <= 0f)
                    error = "Intermission phase requires a completed non-final wave and remaining time.";
                break;
            case WaveRunPhase.Completed:
                if (!wave.IsComplete || snapshot.CurrentWave != TotalWaves ||
                    snapshot.IntermissionRemaining != 0f)
                    error = "Completed phase requires the final wave to be settled.";
                break;
            case WaveRunPhase.Stopped:
                if (snapshot.IntermissionRemaining != 0f)
                    error = "Stopped phase cannot retain intermission time.";
                break;
            default:
                error = "Wave flow phase cannot be restored.";
                break;
        }
        return error == null;
    }
}
