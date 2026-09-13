using System;
using System.IO;
using FPS.SaveGame;
using FPS.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RunSnapshotSession
{
    private static RunSnapshot pending;
    private static RunSnapshot pendingWorld;
    private static int? newSeed;
    private static string pendingLoadNotice;
    private static int pendingOriginalSchemaVersion;
    public static string LastMessage { get; private set; } = "新战局已开始；按 ESC 打开暂停与存档菜单。";
    public static string DefaultPath => Path.Combine(Application.persistentDataPath, "run-snapshot.json");

    public static void PeekLayoutBootstrap(
        int fallbackSeed,
        out int seed,
        out LayoutSaveSnapshot layout)
    {
        seed = pending?.Seed ?? newSeed ?? fallbackSeed;
        layout = pending?.Layout;
    }

    public static int PeekSeed(int fallbackSeed) =>
        pending?.Seed ?? newSeed ?? fallbackSeed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        RunSnapshotPresentationGate.HideImmediately();
        pending = null;
        pendingWorld = null;
        newSeed = null;
        pendingLoadNotice = null;
        pendingOriginalSchemaVersion = 0;
        LastMessage = "新战局已开始；按 ESC 打开暂停与存档菜单。";
    }

    public static bool InitializePlayer(GameObject player, out string error)
    {
        var composition = player.GetComponent<PlayerCombatCompositionRoot>();
        if (composition == null || !composition.TryInitialize())
        {
            pending = null;
            RunSnapshotPresentationGate.HideImmediately();
            error = "玩家组件尚未准备完成，无法恢复快照。";
            return false;
        }
        var adapter = player.GetComponent<RunSnapshotRuntimeAdapter>() ?? player.AddComponent<RunSnapshotRuntimeAdapter>();
        if (player.GetComponent<RunSnapshotMenu>() == null) player.AddComponent<RunSnapshotMenu>();
        if (newSeed.HasValue)
        {
            var upgrades = player.GetComponent<PlayerUpgradeController>();
            upgrades.ConfigureRun(newSeed.Value, upgrades.AvailableUpgrades);
            player.GetComponent<PlayerCombatBuildController>()?
                .ResetForRun(newSeed.Value);
            newSeed = null;
        }
        RunSnapshot snapshot = pending;
        pending = null;
        error = string.Empty;
        if (snapshot == null)
        {
            RunSnapshotPresentationGate.HideImmediately();
            LastMessage = "新战局已开始，已有存档未删除。";
            return true;
        }
        bool legacy = pendingOriginalSchemaVersion > 0 &&
                      pendingOriginalSchemaVersion <
                      RunSnapshot.CurrentSchemaVersion;
        bool restored = legacy
            ? adapter.TryRestoreLegacyV1Snapshot(snapshot, out error)
            : adapter.TryRestore(snapshot, out error);
        pendingWorld = restored && !legacy ? snapshot : null;
        pendingOriginalSchemaVersion = 0;
        LastMessage = restored
            ? FormatLoadMessage(legacy
                ? "读取成功：已恢复旧版玩家状态，任务和波次从当前版本起点开始。"
                : "正在恢复保存时的波次与任务……")
            : "读取失败：" + error;
        if (legacy || !restored)
        {
            pendingLoadNotice = null;
            if (legacy && restored)
            {
                RunSnapshotPresentationGate
                    .ReleaseWhenRestoredHudIsReady();
            }
            else
            {
                RunSnapshotPresentationGate.HideImmediately();
            }
        }
        return restored;
    }

    public static bool TryRestoreWave(
        WaveDirector director,
        GameObject player,
        out string error)
    {
        if (pendingWorld == null)
        {
            error = string.Empty;
            return false;
        }
        error = string.Empty;

        WaveSnapshot saved = pendingWorld.Wave;
        MissionSnapshot mission = pendingWorld.Mission;
        PlayerRunProgression progression =
            player != null ? player.GetComponent<PlayerRunProgression>() : null;
        PlayerLootRewardController loot = player != null
            ? player.GetComponent<PlayerLootRewardController>()
            : null;
        var progressionState = new RunProgressionRestoreSnapshot
        {
            Progress = new RunExperienceSnapshot(
                mission.ExperienceLevel,
                mission.CurrentExperience,
                mission.ExperienceToNextLevel,
                mission.TotalExperience,
                mission.LevelUpCount),
            RewardedSpawnIds = new System.Collections.Generic.List<int>(
                mission.ProgressionRewardedSpawnIds),
            RewardedKillCount = mission.RewardedKillCount,
            RunEnded = mission.ProgressionRunEnded
        };
        var lootState = new LootRewardRestoreSnapshot
        {
            RunSeed = pendingWorld.Seed,
            ConfiguredTotalWaves = director.CurrentProgress.TotalWaves,
            ProcessedSpawnIds = new System.Collections.Generic.List<int>(
                mission.LootProcessedSpawnIds),
            RewardedWaves = new System.Collections.Generic.List<int>(
                mission.LootRewardedWaves),
            EnemySettlementCount = mission.EnemyRewardCount,
            WaveRewardCount = mission.WaveRewardCount,
            FinalRewardCount = mission.FinalRewardCount,
            SpawnedStackCount = mission.SpawnedRewardStackCount,
            FinalRewardRequested = mission.FinalRewardRequested,
            AcceptingRewards = mission.LootAcceptingRewards,
            HasLastDeathPosition = mission.HasLastDeathPosition,
            LastDeathPosition = new Vector3(
                mission.LastDeathPosition.X,
                mission.LastDeathPosition.Y,
                mission.LastDeathPosition.Z)
        };
        if (progression == null || loot == null ||
            !progression.CanRestoreSnapshot(progressionState, out error) ||
            !loot.CanRestoreSnapshot(lootState, out error))
        {
            if (string.IsNullOrEmpty(error))
            {
                error = "经验或奖励恢复依赖尚未准备完成。";
            }
            return false;
        }
        var single = new SingleWaveStateSnapshot(
            saved.TotalEnemyCount,
            saved.MaximumAliveCount,
            saved.SpawnedIds,
            saved.ActiveIds,
            saved.SettledIds);
        var flow = new MultiWaveFlowStateSnapshot(
            saved.CurrentWave,
            (WaveRunPhase)saved.Phase,
            single,
            saved.IntermissionRemaining);
        var missionFlow = new MissionFlowRestoreState(
            (MissionFlowState)mission.Phase,
            mission.RequiredTargets,
            mission.EliminatedTargets,
            mission.TerminalCompleted);
        SimulationClockSnapshot clock = pendingWorld.Simulation;
        if (clock != null &&
            clock.FixedTickRate != director.Simulation.Configuration.FixedTickRate)
        {
            error = "存档固定 Tick 频率与当前战局配置不一致。";
            return false;
        }
        var simulationState = new RunSimulationSnapshot(
            pendingWorld.Seed,
            clock?.Tick ?? 0,
            clock?.NextEventSequence ?? 0,
            clock?.Paused ?? false,
            clock?.PlayerHealth ?? pendingWorld.Health,
            clock?.PlayerArmor ?? pendingWorld.Armor,
            flow,
            missionFlow);
        var enemies = new EnemyRuntimeSnapshot[pendingWorld.Enemies.Count];
        for (int index = 0; index < enemies.Length; index++)
        {
            EnemySnapshot enemy = pendingWorld.Enemies[index];
            enemies[index] = new EnemyRuntimeSnapshot(
                enemy.WaveNumber,
                enemy.SpawnId,
                enemy.EnemyTypeId,
                new Vector3(
                    enemy.Position.X,
                    enemy.Position.Y,
                    enemy.Position.Z),
                new Quaternion(
                    enemy.Rotation.X,
                    enemy.Rotation.Y,
                    enemy.Rotation.Z,
                    enemy.Rotation.W),
                enemy.Health,
                enemy.Armor,
                RunSnapshotRuntimeAdapter.ToRuntimeEffects(
                    enemy.Effects));
        }
        if (!director.TryRestoreRuntimeState(
            new WaveRuntimeSnapshot(
                flow,
                saved.SpawnCooldownRemaining,
                saved.NextSpawnId,
                enemies,
                simulationState,
                RunSnapshotRuntimeAdapter.ToRuntimeCombatDirector(
                    pendingWorld.CombatDirector)),
            out error))
        {
            return false;
        }
        return progression.TryRestoreSnapshot(progressionState, out error) &&
               loot.TryRestoreSnapshot(lootState, out error);
    }

    public static bool TryRestoreMission(
        CityNewMissionController mission,
        out string error)
    {
        if (pendingWorld == null)
        {
            error = string.Empty;
            return false;
        }

        MissionSnapshot saved = pendingWorld.Mission;
        var statistics = new MissionRunStatisticsSnapshot
        {
            ShotsFired = saved.StatisticsShotsFired,
            Hits = saved.StatisticsHits,
            Kills = saved.StatisticsKills,
            CompletedWaves = saved.StatisticsCompletedWaves,
            DamageTakenCount = saved.StatisticsDamageTakenCount,
            DamageTakenAmount = saved.StatisticsDamage,
            ElapsedSeconds = saved.ElapsedSeconds,
            AuthoritativeKillTracking =
                saved.StatisticsAuthoritativeKillTracking,
            RewardedSpawnIds = new System.Collections.Generic.List<int>(
                saved.StatisticsRewardedSpawnIds)
        };
        if (!mission.Statistics.CanRestoreSnapshot(statistics, out error))
        {
            return false;
        }
        bool restored = mission.TryRestoreMissionSilently(
            new MissionFlowRestoreState(
                (MissionFlowState)saved.Phase,
                saved.RequiredTargets,
                saved.EliminatedTargets,
                saved.TerminalCompleted),
            saved.TerminalProgressNormalized,
            out error);
        if (restored)
        {
            restored = mission.Statistics.TryRestoreSnapshot(
                statistics,
                out error);
        }
        if (restored)
        {
            pendingWorld = null;
            LastMessage = FormatLoadMessage(
                "读取成功：已恢复保存时的玩家、背包、波次、敌人与任务状态。");
            pendingLoadNotice = null;
            RunSnapshotPresentationGate.ReleaseWhenRestoredHudIsReady();
        }
        else
        {
            LastMessage = "读取失败：" + error;
            RunSnapshotPresentationGate.HideImmediately();
        }
        return restored;
    }

    public static bool TryRestoreEncounter(
        EncounterRuntimeController encounters,
        out string error)
    {
        if (pendingWorld == null)
        {
            error = string.Empty;
            return false;
        }
        if (pendingWorld.Encounter == null)
        {
            // Pre-Issue-61 schema-v2 saves start encounters from current triggers.
            error = string.Empty;
            return true;
        }
        if (encounters == null || !encounters.IsConfigured)
        {
            error = "遭遇系统尚未准备完成。";
            return false;
        }
        return encounters.TryRestoreRuntimeState(
            RunSnapshotRuntimeAdapter.ToRuntimeEncounter(
                pendingWorld.Encounter),
            out error);
    }

    public static bool HasPendingWorldRestore => pendingWorld != null;

    public static void ReloadSnapshot(
        RunSnapshot snapshot,
        string loadNotice = null,
        int originalSchemaVersion = 0)
    {
        pending = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        RunSnapshotPresentationGate.Show();
        pendingWorld = null;
        newSeed = null;
        pendingLoadNotice = loadNotice;
        pendingOriginalSchemaVersion = originalSchemaVersion;
        Time.timeScale = 1f;
        SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
    }

    public static void AbortRestorePresentation()
    {
        RunSnapshotPresentationGate.HideImmediately();
    }

    public static void NewGame()
    {
        RunSnapshotPresentationGate.HideImmediately();
        pending = null;
        pendingWorld = null;
        newSeed = CityNewModularLayoutBootstrap.SelectNewRunSeed(
            Guid.NewGuid().GetHashCode());
        pendingLoadNotice = null;
        pendingOriginalSchemaVersion = 0;
        LastMessage = "新战局已开始，原存档未删除。";
        Time.timeScale = 1f;
        SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
    }

    private static string FormatLoadMessage(string detail)
    {
        return string.IsNullOrWhiteSpace(pendingLoadNotice)
            ? detail
            : pendingLoadNotice + " " + detail;
    }
}
