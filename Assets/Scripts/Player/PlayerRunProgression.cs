using System;
using System.Collections.Generic;
using UnityEngine;

public interface IRunProgressionSource
{
    event Action<RunExperienceSnapshot> ProgressChanged;
    RunExperienceSnapshot CurrentProgress { get; }
}

public sealed class RunProgressionRestoreSnapshot
{
    public RunExperienceSnapshot Progress;
    public List<int> RewardedSpawnIds = new();
    public int RewardedKillCount;
    public bool RunEnded;
}

public sealed class PlayerRunProgression : MonoBehaviour, IRunProgressionSource
{
    private static readonly int[] DefaultThresholds =
    {
        100,
        150,
        225,
        325,
        450,
        600,
        800,
        1050,
        1350
    };

    [SerializeField] private ExperienceCurveDefinition curve;

    private readonly HashSet<int> rewardedSpawnIds = new();
    private RunExperienceState state;
    private WaveDirector killSource;
    private bool subscribed;
    private bool runEnded;
    private int[] activeThresholds;

    public event Action<RunExperienceSnapshot> ProgressChanged;
    public event Action<int> LevelsGained;

    public RunExperienceSnapshot CurrentProgress => State.Current;
    public int RewardedKillCount { get; private set; }
    public int ProgressEventCount { get; private set; }

    private RunExperienceState State
    {
        get
        {
            state ??= CreateState();
            return state;
        }
    }

    private void Awake()
    {
        _ = State;
    }

    private void OnEnable()
    {
        if (killSource == null && WaveDirector.Active != null)
        {
            killSource = WaveDirector.Active;
        }

        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();

        if (state != null)
        {
            state.Changed -= HandleStateChanged;
        }
    }

    public void ConfigureThresholds(IReadOnlyList<int> thresholds)
    {
        int[] validatedThresholds = CopyThresholds(thresholds);

        if (state != null)
        {
            state.Changed -= HandleStateChanged;
        }

        activeThresholds = validatedThresholds;
        state = new RunExperienceState(activeThresholds);
        state.Changed += HandleStateChanged;
        rewardedSpawnIds.Clear();
        RewardedKillCount = 0;
        PublishCurrent();
    }

    public RunProgressionRestoreSnapshot CaptureRestoreSnapshot()
    {
        var spawnIds = new List<int>(rewardedSpawnIds);
        spawnIds.Sort();
        return new RunProgressionRestoreSnapshot
        {
            Progress = CurrentProgress,
            RewardedSpawnIds = spawnIds,
            RewardedKillCount = RewardedKillCount,
            RunEnded = runEnded
        };
    }

    public bool CanRestoreSnapshot(
        RunProgressionRestoreSnapshot snapshot,
        out string error)
    {
        return TryPrepareRestore(snapshot, out _, out _, out error);
    }

    public bool TryRestoreSnapshot(
        RunProgressionRestoreSnapshot snapshot,
        out string error)
    {
        if (!TryPrepareRestore(
                snapshot,
                out RunExperienceState restoredState,
                out HashSet<int> restoredSpawnIds,
                out error))
        {
            return false;
        }

        if (state != null)
        {
            state.Changed -= HandleStateChanged;
        }

        state = restoredState;
        state.Changed += HandleStateChanged;
        rewardedSpawnIds.Clear();

        foreach (int spawnId in restoredSpawnIds)
        {
            rewardedSpawnIds.Add(spawnId);
        }

        RewardedKillCount = snapshot.RewardedKillCount;
        runEnded = snapshot.RunEnded;

        if (runEnded)
        {
            Unsubscribe();
        }
        else
        {
            Subscribe();
        }

        // Restoring replaces the state object, so no RunExperienceState.Changed
        // event is raised automatically. Presentation subscribers still need
        // the authoritative restored snapshot immediately.
        PublishCurrent();
        error = string.Empty;
        return true;
    }

    public void BindKillSource(WaveDirector source)
    {
        if (runEnded)
        {
            return;
        }

        if (killSource == source)
        {
            Subscribe();
            return;
        }

        Unsubscribe();
        killSource = source;
        Subscribe();
    }

    public bool TryApplyEnemyDeath(EnemyDeathEvent death)
    {
        if (runEnded || death.Enemy == null ||
            death.RewardExperience <= 0 ||
            rewardedSpawnIds.Contains(death.SpawnId) ||
            !IsPlayerOwned(death.DamageSource))
        {
            return false;
        }

        rewardedSpawnIds.Add(death.SpawnId);
        RewardedKillCount++;
        int levelsGained = State.GrantExperience(death.RewardExperience);

        if (levelsGained > 0)
        {
            LevelsGained?.Invoke(levelsGained);
        }

        return true;
    }

    public void EndRun()
    {
        if (runEnded)
        {
            return;
        }

        runEnded = true;
        Unsubscribe();
    }

    private RunExperienceState CreateState()
    {
        activeThresholds ??= CopyThresholds(
            curve != null && curve.Thresholds.Count > 0
                ? curve.Thresholds
                : DefaultThresholds);
        var nextState = new RunExperienceState(activeThresholds);
        nextState.Changed += HandleStateChanged;
        return nextState;
    }

    private bool TryPrepareRestore(
        RunProgressionRestoreSnapshot snapshot,
        out RunExperienceState restoredState,
        out HashSet<int> restoredSpawnIds,
        out string error)
    {
        restoredState = null;
        restoredSpawnIds = null;

        if (snapshot == null || snapshot.RewardedSpawnIds == null)
        {
            error = "经验快照为空或奖励账本缺失。";
            return false;
        }

        RunExperienceSnapshot progress = snapshot.Progress;

        if (progress.Level < 1 || progress.CurrentExperience < 0 ||
            progress.TotalExperience < 0 || progress.LevelUpCount < 0 ||
            snapshot.RewardedKillCount < 0)
        {
            error = "经验快照包含负数或非法等级。";
            return false;
        }

        var validatedSpawnIds = new HashSet<int>();

        for (int index = 0; index < snapshot.RewardedSpawnIds.Count; index++)
        {
            int spawnId = snapshot.RewardedSpawnIds[index];

            if (spawnId <= 0 || !validatedSpawnIds.Add(spawnId))
            {
                error = "经验奖励账本包含非法或重复的 spawnId。";
                return false;
            }
        }

        if (snapshot.RewardedKillCount != validatedSpawnIds.Count)
        {
            error = "奖励击杀计数与 spawnId 账本不一致。";
            return false;
        }

        activeThresholds ??= CopyThresholds(
            curve != null && curve.Thresholds.Count > 0
                ? curve.Thresholds
                : DefaultThresholds);
        var candidate = new RunExperienceState(activeThresholds);
        candidate.GrantExperience(progress.TotalExperience);
        RunExperienceSnapshot rebuilt = candidate.Current;

        if (rebuilt.Level != progress.Level ||
            rebuilt.CurrentExperience != progress.CurrentExperience ||
            rebuilt.ExperienceToNextLevel != progress.ExperienceToNextLevel ||
            rebuilt.TotalExperience != progress.TotalExperience ||
            rebuilt.LevelUpCount != progress.LevelUpCount)
        {
            error = "经验快照与当前经验曲线不一致。";
            return false;
        }

        restoredState = candidate;
        restoredSpawnIds = validatedSpawnIds;
        error = string.Empty;
        return true;
    }

    private static int[] CopyThresholds(IReadOnlyList<int> thresholds)
    {
        if (thresholds == null || thresholds.Count == 0)
        {
            throw new ArgumentException(
                "At least one level threshold is required.",
                nameof(thresholds));
        }

        var copied = new int[thresholds.Count];

        for (int index = 0; index < thresholds.Count; index++)
        {
            if (thresholds[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(thresholds),
                    "Experience thresholds must be positive.");
            }

            copied[index] = thresholds[index];
        }

        return copied;
    }

    private bool IsPlayerOwned(GameObject source)
    {
        return source != null &&
               (source == gameObject ||
                source.transform.IsChildOf(transform));
    }

    private void Subscribe()
    {
        if (runEnded || !isActiveAndEnabled || subscribed ||
            killSource == null)
        {
            return;
        }

        killSource.EnemyDied += HandleEnemyDied;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (subscribed && killSource != null)
        {
            killSource.EnemyDied -= HandleEnemyDied;
        }

        subscribed = false;
    }

    private void HandleEnemyDied(EnemyDeathEvent death)
    {
        TryApplyEnemyDeath(death);
    }

    private void HandleStateChanged(RunExperienceSnapshot progress)
    {
        Publish(progress);
    }

    private void PublishCurrent()
    {
        Publish(State.Current);
    }

    private void Publish(RunExperienceSnapshot progress)
    {
        ProgressEventCount++;
        ProgressChanged?.Invoke(progress);
    }
}
