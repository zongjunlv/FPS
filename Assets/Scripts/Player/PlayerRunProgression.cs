using System;
using System.Collections.Generic;
using UnityEngine;

public interface IRunProgressionSource
{
    event Action<RunExperienceSnapshot> ProgressChanged;
    RunExperienceSnapshot CurrentProgress { get; }
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
        if (state != null)
        {
            state.Changed -= HandleStateChanged;
        }

        state = new RunExperienceState(thresholds);
        state.Changed += HandleStateChanged;
        rewardedSpawnIds.Clear();
        RewardedKillCount = 0;
        PublishCurrent();
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
        IReadOnlyList<int> thresholds =
            curve != null && curve.Thresholds.Count > 0
                ? curve.Thresholds
                : DefaultThresholds;
        var nextState = new RunExperienceState(thresholds);
        nextState.Changed += HandleStateChanged;
        return nextState;
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
