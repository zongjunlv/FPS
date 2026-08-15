using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct MissionRunSummary
{
    public MissionRunSummary(
        MissionFlowState outcome,
        int completedWaves,
        int totalWaves,
        int kills,
        int shotsFired,
        int hits,
        float elapsedSeconds,
        int damageTakenCount,
        float damageTakenAmount,
        int finalLevel,
        IReadOnlyList<string> selectedUpgrades)
    {
        Outcome = outcome;
        CompletedWaves = Mathf.Max(0, completedWaves);
        TotalWaves = Mathf.Max(0, totalWaves);
        Kills = Mathf.Max(0, kills);
        ShotsFired = Mathf.Max(0, shotsFired);
        Hits = Mathf.Clamp(hits, 0, ShotsFired);
        ElapsedSeconds = Mathf.Max(0f, elapsedSeconds);
        DamageTakenCount = Mathf.Max(0, damageTakenCount);
        DamageTakenAmount = Mathf.Max(0f, damageTakenAmount);
        FinalLevel = Mathf.Max(1, finalLevel);
        SelectedUpgrades = selectedUpgrades != null
            ? new List<string>(selectedUpgrades).ToArray()
            : Array.Empty<string>();
    }

    public MissionFlowState Outcome { get; }
    public int CompletedWaves { get; }
    public int TotalWaves { get; }
    public int Kills { get; }
    public int ShotsFired { get; }
    public int Hits { get; }
    public float Accuracy => ShotsFired <= 0
        ? 0f
        : (float)Hits / ShotsFired;
    public float ElapsedSeconds { get; }
    public int DamageTakenCount { get; }
    public float DamageTakenAmount { get; }
    public int FinalLevel { get; }
    public IReadOnlyList<string> SelectedUpgrades { get; }
    public bool IsValid =>
        Outcome == MissionFlowState.Victory ||
        Outcome == MissionFlowState.Defeat;
}

public sealed class MissionRunStatistics
{
    private readonly HashSet<int> rewardedSpawnIds = new();
    private bool authoritativeKillTracking;

    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public int Kills { get; private set; }
    public int CompletedWaves { get; private set; }
    public int DamageTakenCount { get; private set; }
    public float DamageTakenAmount { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public float Accuracy =>
        ShotsFired <= 0 ? 0f : (float)Hits / ShotsFired;

    public void RegisterShot(ShotResult result)
    {
        ShotsFired++;

        if (!result.Damage.WasApplied)
        {
            return;
        }

        Hits++;

        if (!authoritativeKillTracking &&
            result.Damage.WasKilled &&
            result.DamageTarget != null &&
            result.DamageTarget.GetComponentInParent<EnemyController>() !=
            null)
        {
            Kills++;
        }
    }

    public void RegisterDamageTaken()
    {
        DamageTakenCount++;
    }

    public void RegisterAppliedDamage(DamageResult result)
    {
        if (!result.WasApplied || result.AppliedAmount <= 0f)
        {
            return;
        }

        DamageTakenCount++;
        DamageTakenAmount += result.AppliedAmount;
    }

    public void UseAuthoritativeKillTracking()
    {
        authoritativeKillTracking = true;
        Kills = 0;
        rewardedSpawnIds.Clear();
    }

    public bool RegisterEnemyDeath(
        EnemyDeathEvent death,
        GameObject playerRoot)
    {
        GameObject source = death.DamageSource;

        if (!authoritativeKillTracking || death.SpawnId <= 0 ||
            playerRoot == null || source == null ||
            (source != playerRoot &&
             !source.transform.IsChildOf(playerRoot.transform)) ||
            !rewardedSpawnIds.Add(death.SpawnId))
        {
            return false;
        }

        Kills++;
        return true;
    }

    public void RegisterWaveCompleted(int waveNumber)
    {
        CompletedWaves = Mathf.Max(CompletedWaves, waveNumber);
    }

    public void AdvanceTime(float deltaTime)
    {
        ElapsedSeconds += Mathf.Max(0f, deltaTime);
    }

    public void Reset()
    {
        ShotsFired = 0;
        Hits = 0;
        Kills = 0;
        CompletedWaves = 0;
        DamageTakenCount = 0;
        DamageTakenAmount = 0f;
        ElapsedSeconds = 0f;
        authoritativeKillTracking = false;
        rewardedSpawnIds.Clear();
    }
}
