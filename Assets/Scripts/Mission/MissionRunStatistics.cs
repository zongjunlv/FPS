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

    public MissionRunStatisticsSnapshot CaptureSnapshot()
    {
        var spawnIds = new List<int>(rewardedSpawnIds);
        spawnIds.Sort();
        return new MissionRunStatisticsSnapshot
        {
            ShotsFired = ShotsFired,
            Hits = Hits,
            Kills = Kills,
            CompletedWaves = CompletedWaves,
            DamageTakenCount = DamageTakenCount,
            DamageTakenAmount = DamageTakenAmount,
            ElapsedSeconds = ElapsedSeconds,
            AuthoritativeKillTracking = authoritativeKillTracking,
            RewardedSpawnIds = spawnIds
        };
    }

    public bool CanRestoreSnapshot(
        MissionRunStatisticsSnapshot snapshot,
        out string error)
    {
        return TryValidateSnapshot(snapshot, out _, out error);
    }

    public bool TryRestoreSnapshot(
        MissionRunStatisticsSnapshot snapshot,
        out string error)
    {
        if (!TryValidateSnapshot(snapshot, out HashSet<int> spawnIds, out error))
        {
            return false;
        }

        ShotsFired = snapshot.ShotsFired;
        Hits = snapshot.Hits;
        Kills = snapshot.Kills;
        CompletedWaves = snapshot.CompletedWaves;
        DamageTakenCount = snapshot.DamageTakenCount;
        DamageTakenAmount = snapshot.DamageTakenAmount;
        ElapsedSeconds = snapshot.ElapsedSeconds;
        authoritativeKillTracking = snapshot.AuthoritativeKillTracking;
        rewardedSpawnIds.Clear();

        foreach (int spawnId in spawnIds)
        {
            rewardedSpawnIds.Add(spawnId);
        }

        error = string.Empty;
        return true;
    }

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

    private static bool TryValidateSnapshot(
        MissionRunStatisticsSnapshot snapshot,
        out HashSet<int> spawnIds,
        out string error)
    {
        spawnIds = null;

        if (snapshot == null || snapshot.RewardedSpawnIds == null)
        {
            error = "任务统计快照为空或击杀账本缺失。";
            return false;
        }

        if (snapshot.ShotsFired < 0 || snapshot.Hits < 0 ||
            snapshot.Hits > snapshot.ShotsFired || snapshot.Kills < 0 ||
            snapshot.CompletedWaves < 0 || snapshot.DamageTakenCount < 0 ||
            !IsFiniteNonNegative(snapshot.DamageTakenAmount) ||
            !IsFiniteNonNegative(snapshot.ElapsedSeconds))
        {
            error = "任务统计快照包含非法数值。";
            return false;
        }

        var validated = new HashSet<int>();

        for (int index = 0; index < snapshot.RewardedSpawnIds.Count; index++)
        {
            int spawnId = snapshot.RewardedSpawnIds[index];

            if (spawnId <= 0 || !validated.Add(spawnId))
            {
                error = "任务击杀账本包含非法或重复的 spawnId。";
                return false;
            }
        }

        if ((!snapshot.AuthoritativeKillTracking && validated.Count > 0) ||
            (snapshot.AuthoritativeKillTracking &&
             validated.Count != snapshot.Kills))
        {
            error = "任务击杀计数与权威 spawnId 账本不一致。";
            return false;
        }

        spawnIds = validated;
        error = string.Empty;
        return true;
    }

    private static bool IsFiniteNonNegative(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }
}

public sealed class MissionRunStatisticsSnapshot
{
    public int ShotsFired;
    public int Hits;
    public int Kills;
    public int CompletedWaves;
    public int DamageTakenCount;
    public float DamageTakenAmount;
    public float ElapsedSeconds;
    public bool AuthoritativeKillTracking;
    public List<int> RewardedSpawnIds = new();
}
