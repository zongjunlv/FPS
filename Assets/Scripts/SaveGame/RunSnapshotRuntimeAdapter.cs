using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using FPS.SaveGame;
using FPS.Simulation;
using UnityEngine;

/// <summary>Maps data-only saves onto a prepared player, without replaying rewards.</summary>
public sealed class RunSnapshotRuntimeAdapter : MonoBehaviour
{
    private Health health;
    private PlayerUpgradeController upgrades;
    private PlayerRuntimeCombatStats stats;
    private WeaponLoadoutController loadout;
    private PlayerInventoryController inventory;
    private PlayerController player;
    private PlayerCombatBuildController combatBuilds;

    private bool Resolve(out string error)
    {
        health = GetComponent<Health>();
        upgrades = GetComponent<PlayerUpgradeController>();
        stats = GetComponent<PlayerRuntimeCombatStats>();
        loadout = GetComponent<WeaponLoadoutController>();
        inventory = GetComponent<PlayerInventoryController>();
        player = GetComponent<PlayerController>();
        combatBuilds = GetComponent<PlayerCombatBuildController>();
        error = health == null || upgrades == null || stats == null ||
                loadout == null || inventory == null || player == null ||
                combatBuilds == null
            ? "玩家存档依赖尚未就绪。" : string.Empty;
        return error.Length == 0;
    }

    public RunSnapshot Capture()
    {
        if (!Resolve(out string error)) throw new InvalidOperationException(error);
        if (health.IsDead || upgrades.IsChoiceOpen || upgrades.PendingChoiceCount > 0 || loadout.IsSwitching)
            throw new InvalidOperationException("请在存活且完成选卡、切枪后保存。");
        var snapshot = new RunSnapshot
        {
            Seed = upgrades.RunSeed,
            Health = health.CurrentHealth,
            Armor = health.CurrentArmor,
            CurrentWeaponId = loadout.CurrentWeapon.StableId,
            UpgradeSelectionHistory = new List<string>(upgrades.SelectionHistory),
            PlayerPosition = new Float3Snapshot(
                transform.position.x,
                transform.position.y,
                transform.position.z),
            PlayerRotation = new Float4Snapshot(
                transform.rotation.x,
                transform.rotation.y,
                transform.rotation.z,
                transform.rotation.w),
            CameraPitch = player.CameraPitch,
            PlayerCrouching = player.IsCrouching
        };
        CaptureInventory(snapshot);
        CaptureWorld(snapshot);
        for (int i = 0; i < loadout.WeaponCount; i++)
        {
            WeaponController weapon = loadout.GetWeapon(i);
            weapon.PrepareSnapshotState(stats);
            snapshot.Weapons.Add(new WeaponAmmoSnapshot
            {
                WeaponId = weapon.StableId,
                Magazine = weapon.CurrentAmmo,
                Reserve = weapon.ReserveAmmo
            });
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in upgrades.SelectionHistory)
            if (seen.Add(id)) snapshot.Upgrades.Add(new UpgradeLevelSnapshot
                { UpgradeId = id, Level = upgrades.GetUpgradeLevel(id) });
        if (!upgrades.TryCaptureGameplayEffectSnapshot(
                out GameplayEffectRuntimeSnapshot playerEffects,
                out error))
        {
            throw new InvalidOperationException(error);
        }
        snapshot.PlayerEffects = ToSaveEffects(playerEffects);
        snapshot.CombatBuild = ToSaveCombatBuild(
            combatBuilds.CaptureSnapshot());
        if (!ValidateSnapshot(snapshot, out error)) throw new InvalidOperationException(error);
        return snapshot;
    }

    public bool ValidateSnapshot(RunSnapshot snapshot, out string error)
    {
        if (!Resolve(out error)) return false;
        if (!SnapshotValidation.TryValidate(snapshot, out error))
        {
            return false;
        }
        if (snapshot == null || snapshot.SchemaVersion != RunSnapshot.CurrentSchemaVersion ||
            snapshot.Weapons == null || snapshot.Upgrades == null ||
            snapshot.UpgradeSelectionHistory == null ||
            snapshot.Weapons.Count != loadout.WeaponCount || snapshot.Upgrades.Count > 128 ||
            snapshot.UpgradeSelectionHistory.Count > 4096)
        {
            error = "快照版本、武器数量或升级结构无效。";
            return false;
        }
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (UpgradeLevelSnapshot entry in snapshot.Upgrades)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.UpgradeId) || entry.Level <= 0 ||
                !levels.TryAdd(entry.UpgradeId, entry.Level))
            {
                error = "快照升级条目无效或重复。";
                return false;
            }
        }
        foreach (string id in snapshot.UpgradeSelectionHistory)
        {
            if (id == null || !levels.TryGetValue(id, out int count) || count <= 0)
            {
                error = "升级历史与等级不一致。";
                return false;
            }
            levels[id] = count - 1;
        }
        foreach (int count in levels.Values)
            if (count != 0)
            {
                error = "升级历史与等级不一致。";
                return false;
            }
        if (!upgrades.TryPreviewSnapshotUpgrades(snapshot.UpgradeSelectionHistory,
                out RunUpgradeState restored, out float maxHealth, out float maxArmor, out error)) return false;
        if (!upgrades.CanRestoreGameplayEffectSnapshot(
                snapshot.UpgradeSelectionHistory,
                ToRuntimeEffects(snapshot.PlayerEffects),
                out error))
        {
            return false;
        }
        if (!Finite(snapshot.Health) || !Finite(snapshot.Armor) || snapshot.Health <= 0f ||
            snapshot.Health > maxHealth || snapshot.Armor < 0f || snapshot.Armor > maxArmor)
        {
            error = "生命或护甲超出已保存升级的有效范围。";
            return false;
        }
        var weapons = new Dictionary<string, WeaponController>(StringComparer.Ordinal);
        for (int i = 0; i < loadout.WeaponCount; i++)
        {
            WeaponController weapon = loadout.GetWeapon(i);
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.StableId) || !weapons.TryAdd(weapon.StableId, weapon))
            {
                error = "当前武器缺少唯一稳定 ID。";
                return false;
            }
        }
        if (snapshot.CurrentWeaponId == null || !weapons.ContainsKey(snapshot.CurrentWeaponId))
        {
            error = "当前武器 ID 不存在。";
            return false;
        }
        foreach (WeaponAmmoSnapshot entry in snapshot.Weapons)
        {
            if (entry == null || entry.WeaponId == null || !weapons.Remove(entry.WeaponId, out WeaponController weapon) ||
                entry.Magazine < 0 || entry.Magazine > Mathf.Max(1, Mathf.RoundToInt(
                    weapon.BaseMagazineCapacity * restored.WeaponModifiers.MagazineCapacityMultiplier)) ||
                entry.Reserve < 0 || entry.Reserve > weapon.BaseMaximumReserveAmmo)
            {
                error = "弹药数量越界或武器 ID 无效、重复。";
                return false;
            }
        }
        if (!inventory.CanRestoreSnapshot(ToRuntimeInventory(snapshot)))
        {
            error = "背包、快捷栏或物品冷却状态无法恢复。";
            return false;
        }
        if (snapshot.CombatBuild != null &&
            !combatBuilds.CanRestoreSnapshot(
                ToRuntimeCombatBuild(snapshot.CombatBuild),
                out error))
        {
            return false;
        }
        error = string.Empty;
        return true;
    }

    public bool TryRestore(RunSnapshot snapshot, out string error)
    {
        if (!ValidateSnapshot(snapshot, out error)) return false;
        if (!player.TryRestoreSnapshotPose(
                new Vector3(
                    snapshot.PlayerPosition.X,
                    snapshot.PlayerPosition.Y,
                    snapshot.PlayerPosition.Z),
                new Quaternion(
                    snapshot.PlayerRotation.X,
                    snapshot.PlayerRotation.Y,
                    snapshot.PlayerRotation.Z,
                    snapshot.PlayerRotation.W),
                snapshot.CameraPitch,
                snapshot.PlayerCrouching,
                out error))
        {
            return false;
        }
        upgrades.RestoreSnapshotUpgrades(snapshot.Seed, snapshot.UpgradeSelectionHistory);
        combatBuilds.ResetForRun(snapshot.Seed);
        if (!upgrades.TryRestoreGameplayEffectSnapshot(
                ToRuntimeEffects(snapshot.PlayerEffects),
                out error))
        {
            return false;
        }
        int equippedIndex = 0;
        for (int i = 0; i < loadout.WeaponCount; i++)
        {
            WeaponController weapon = loadout.GetWeapon(i);
            weapon.PrepareSnapshotState(stats);
            WeaponAmmoSnapshot ammo = snapshot.Weapons.Find(value => value.WeaponId == weapon.StableId);
            weapon.TryRestoreAmmo(ammo.Magazine, ammo.Reserve);
            if (weapon.StableId == snapshot.CurrentWeaponId) equippedIndex = i;
        }
        loadout.RestoreEquippedWeapon(equippedIndex);
        health.TryRestoreSnapshotVitals(snapshot.Health, snapshot.Armor);
        inventory.TryRestoreSnapshot(ToRuntimeInventory(snapshot));
        if (snapshot.CombatBuild != null &&
            !combatBuilds.TryRestoreSnapshot(
                ToRuntimeCombatBuild(snapshot.CombatBuild),
                out error))
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Version 1 only stored combat progression. It intentionally leaves the
    /// fresh scene's pose, inventory, mission and wave state untouched.
    /// </summary>
    public bool ValidateLegacyV1Snapshot(
        RunSnapshot snapshot,
        out string error)
    {
        if (!Resolve(out error) ||
            !SnapshotValidation.TryValidate(snapshot, out error))
        {
            return false;
        }
        if (snapshot.SchemaVersion != RunSnapshot.CurrentSchemaVersion ||
            snapshot.Weapons == null ||
            snapshot.Weapons.Count != loadout.WeaponCount ||
            snapshot.Upgrades == null ||
            snapshot.UpgradeSelectionHistory == null)
        {
            error = "旧版快照的武器或升级结构无效。";
            return false;
        }

        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (UpgradeLevelSnapshot entry in snapshot.Upgrades)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.UpgradeId) ||
                entry.Level <= 0 || !levels.TryAdd(entry.UpgradeId, entry.Level))
            {
                error = "旧版快照的升级条目无效或重复。";
                return false;
            }
        }
        foreach (string id in snapshot.UpgradeSelectionHistory)
        {
            if (id == null || !levels.TryGetValue(id, out int count) || count <= 0)
            {
                error = "旧版快照的升级历史与等级不一致。";
                return false;
            }
            levels[id] = count - 1;
        }
        foreach (int count in levels.Values)
        {
            if (count != 0)
            {
                error = "旧版快照的升级历史与等级不一致。";
                return false;
            }
        }

        if (!upgrades.TryPreviewSnapshotUpgrades(
                snapshot.UpgradeSelectionHistory,
                out RunUpgradeState restored,
                out float maxHealth,
                out float maxArmor,
                out error))
        {
            return false;
        }
        if (!Finite(snapshot.Health) || !Finite(snapshot.Armor) ||
            snapshot.Health <= 0f || snapshot.Health > maxHealth ||
            snapshot.Armor < 0f || snapshot.Armor > maxArmor)
        {
            error = "旧版快照的生命或护甲超出升级后的有效范围。";
            return false;
        }

        var weapons = new Dictionary<string, WeaponController>(
            StringComparer.Ordinal);
        for (int index = 0; index < loadout.WeaponCount; index++)
        {
            WeaponController weapon = loadout.GetWeapon(index);
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.StableId) ||
                !weapons.TryAdd(weapon.StableId, weapon))
            {
                error = "当前武器缺少唯一稳定 ID。";
                return false;
            }
        }
        if (snapshot.CurrentWeaponId == null ||
            !weapons.ContainsKey(snapshot.CurrentWeaponId))
        {
            error = "旧版快照的当前武器 ID 不存在。";
            return false;
        }
        foreach (WeaponAmmoSnapshot entry in snapshot.Weapons)
        {
            if (entry == null || entry.WeaponId == null ||
                !weapons.Remove(entry.WeaponId, out WeaponController weapon) ||
                entry.Magazine < 0 || entry.Magazine > Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        weapon.BaseMagazineCapacity *
                        restored.WeaponModifiers.MagazineCapacityMultiplier)) ||
                entry.Reserve < 0 ||
                entry.Reserve > weapon.BaseMaximumReserveAmmo)
            {
                error = "旧版快照的弹药数量越界或武器 ID 无效、重复。";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    public bool TryRestoreLegacyV1Snapshot(
        RunSnapshot snapshot,
        out string error)
    {
        if (!ValidateLegacyV1Snapshot(snapshot, out error)) return false;

        upgrades.RestoreSnapshotUpgrades(
            snapshot.Seed,
            snapshot.UpgradeSelectionHistory);
        combatBuilds.ResetForRun(snapshot.Seed);
        int equippedIndex = 0;
        for (int index = 0; index < loadout.WeaponCount; index++)
        {
            WeaponController weapon = loadout.GetWeapon(index);
            weapon.PrepareSnapshotState(stats);
            WeaponAmmoSnapshot ammo = snapshot.Weapons.Find(
                value => value.WeaponId == weapon.StableId);
            weapon.TryRestoreAmmo(ammo.Magazine, ammo.Reserve);
            if (weapon.StableId == snapshot.CurrentWeaponId)
                equippedIndex = index;
        }
        loadout.RestoreEquippedWeapon(equippedIndex);
        health.TryRestoreSnapshotVitals(snapshot.Health, snapshot.Armor);
        error = string.Empty;
        return true;
    }

    private void CaptureInventory(RunSnapshot snapshot)
    {
        PlayerInventorySnapshot runtime = inventory.CaptureSnapshot();
        snapshot.InventoryCooldownRemainingSeconds =
            runtime.CooldownRemainingSeconds;
        snapshot.SelectedQuickSlotIndex = runtime.SelectedIndex;
        snapshot.InventorySlots.Clear();
        for (int index = 0; index < runtime.Inventory.Slots.Count; index++)
        {
            global::InventorySlotSnapshot slot = runtime.Inventory.Slots[index];
            snapshot.InventorySlots.Add(new FPS.SaveGame.InventorySlotSnapshot
            {
                SlotIndex = index,
                ItemId = slot.StableId,
                Quantity = slot.Quantity
            });
        }
        snapshot.QuickSlots.Clear();
        for (int index = 0; index < runtime.QuickSlots.Bindings.Count; index++)
        {
            snapshot.QuickSlots.Add(new FPS.SaveGame.QuickSlotSnapshot
            {
                SlotIndex = index,
                ItemId = runtime.QuickSlots.Bindings[index]
            });
        }
    }

    private static PlayerInventorySnapshot ToRuntimeInventory(
        RunSnapshot snapshot)
    {
        var runtime = new PlayerInventorySnapshot
        {
            CooldownRemainingSeconds = snapshot.InventoryCooldownRemainingSeconds,
            SelectedIndex = snapshot.SelectedQuickSlotIndex,
            Inventory = new InventorySnapshot(),
            QuickSlots = new global::QuickSlotSnapshot()
        };
        var inventorySlots = new global::InventorySlotSnapshot[
            snapshot.InventorySlots.Count];
        foreach (FPS.SaveGame.InventorySlotSnapshot slot in
                 snapshot.InventorySlots)
        {
            inventorySlots[slot.SlotIndex] = new global::InventorySlotSnapshot
            {
                StableId = slot.ItemId,
                Quantity = slot.Quantity
            };
        }
        runtime.Inventory.Slots.AddRange(inventorySlots);
        var quickSlots = new string[snapshot.QuickSlots.Count];
        foreach (FPS.SaveGame.QuickSlotSnapshot slot in snapshot.QuickSlots)
        {
            quickSlots[slot.SlotIndex] = slot.ItemId;
        }
        runtime.QuickSlots.Bindings.AddRange(quickSlots);
        return runtime;
    }

    private void CaptureWorld(RunSnapshot snapshot)
    {
        CityNewModularLayoutBootstrap layout =
            CityNewModularLayoutBootstrap.Active;
        if (layout == null || !layout.IsReady || layout.CurrentPlan == null)
        {
            throw new InvalidOperationException(
                "模块化战斗区域尚未准备完成。");
        }
        snapshot.Layout = layout.CaptureSnapshot();

        WaveDirector director = WaveDirector.Active;
        CityNewMissionController mission =
            GetComponent<CityNewMissionController>();
        if (director == null || mission == null)
        {
            throw new InvalidOperationException("波次或任务尚未准备完成。");
        }

        WaveRuntimeSnapshot runtime = director.CaptureRuntimeState();
        RunSimulationSnapshot authoritative = runtime.Simulation;
        if (authoritative == null)
        {
            throw new InvalidOperationException(
                "权威战局仿真尚未准备完成。");
        }
        SingleWaveStateSnapshot wave = authoritative.Wave.CurrentWaveState;
        snapshot.Wave = new WaveSnapshot
        {
            CurrentWave = authoritative.Wave.CurrentWave,
            Phase = (int)authoritative.Wave.Phase,
            TotalEnemyCount = wave.TotalCount,
            MaximumAliveCount = wave.MaximumAliveCount,
            SpawnedIds = new List<int>(wave.SpawnedIds),
            ActiveIds = new List<int>(wave.ActiveIds),
            SettledIds = new List<int>(wave.SettledIds),
            IntermissionRemaining = authoritative.Wave.IntermissionRemaining,
            SpawnCooldownRemaining = runtime.SpawnCooldownRemaining,
            NextSpawnId = runtime.NextSpawnId,
            RemainingThreatBudget = 0
        };
        snapshot.Simulation = new SimulationClockSnapshot
        {
            Tick = authoritative.Tick,
            NextEventSequence = authoritative.NextEventSequence,
            FixedTickRate = director.Simulation.Configuration.FixedTickRate,
            Paused = authoritative.Paused,
            PlayerHealth = authoritative.PlayerHealth,
            PlayerArmor = authoritative.PlayerArmor
        };
        snapshot.CombatDirector = ToSaveCombatDirector(runtime.Director);
        EncounterRuntimeController encounter =
            GetComponent<EncounterRuntimeController>();
        if (encounter != null && encounter.IsConfigured)
        {
            EncounterRuntimeRestoreSnapshot encounterRuntime =
                encounter.CaptureRuntimeState();
            EncounterRuntimeSnapshot state = encounterRuntime.Sequence;
            snapshot.Encounter = new EncounterSaveSnapshot
            {
                SequenceContentVersion =
                    encounterRuntime.SequenceContentVersion,
                NextIndex = state.NextIndex,
                ActiveEncounterId = state.ActiveEncounterId,
                ActiveDefinitionVersion = state.ActiveDefinitionVersion,
                Phase = (int)state.Phase,
                StartedTick = state.StartedTick,
                ActiveTick = state.ActiveTick,
                DeadlineTick = state.DeadlineTick,
                CurrentTick = encounterRuntime.CurrentTick,
                Progress = state.Progress,
                ResolvedEncounterIds = new List<string>(
                    state.ResolvedEncounterIds),
                RewardedEncounterIds = new List<string>(
                    state.RewardedEncounterIds),
                ActiveRosterTokens = new List<int>(
                    encounterRuntime.ActiveRosterTokens)
            };
        }
        snapshot.Enemies.Clear();
        foreach (EnemyRuntimeSnapshot enemy in runtime.Enemies)
        {
            Quaternion rotation = enemy.Rotation;
            snapshot.Enemies.Add(new EnemySnapshot
            {
                WaveNumber = enemy.WaveNumber,
                SpawnId = enemy.SpawnId,
                EnemyTypeId = enemy.EnemyTypeId,
                Position = new Float3Snapshot(
                    enemy.Position.x, enemy.Position.y, enemy.Position.z),
                Rotation = new Float4Snapshot(
                    rotation.x, rotation.y, rotation.z, rotation.w),
                Health = enemy.Health,
                Armor = enemy.Armor,
                AwarenessState = 0,
                AttackState = 0,
                Effects = ToSaveEffects(enemy.Effects)
            });
        }

        MissionFlowRestoreState missionState = authoritative.Mission;
        PlayerRunProgression progression =
            GetComponent<PlayerRunProgression>();
        RunProgressionRestoreSnapshot progressionState =
            progression.CaptureRestoreSnapshot();
        RunExperienceSnapshot experience = progressionState.Progress;
        PlayerLootRewardController loot =
            GetComponent<PlayerLootRewardController>();
        string lootError = string.Empty;
        if (loot == null || !loot.TryCaptureSnapshot(
                out LootRewardRestoreSnapshot lootState,
                out lootError))
        {
            throw new InvalidOperationException(
                lootError ?? "掉落奖励状态尚未准备完成。");
        }
        MissionRunStatisticsSnapshot statistics =
            mission.Statistics.CaptureSnapshot();
        snapshot.Mission = new MissionSnapshot
        {
            Phase = (int)missionState.State,
            TerminalCompleted = missionState.TerminalCompleted,
            TerminalProgressNormalized = mission.CaptureTerminalProgress(),
            EliminatedTargets = missionState.EliminatedTargets,
            RequiredTargets = missionState.RequiredTargets,
            ExperienceLevel = experience.Level,
            ExperienceToNextLevel = experience.ExperienceToNextLevel,
            CurrentExperience = experience.CurrentExperience,
            TotalExperience = experience.TotalExperience,
            LevelUpCount = experience.LevelUpCount,
            RewardedKillCount = progressionState.RewardedKillCount,
            ProgressionRewardedSpawnIds = new List<int>(
                progressionState.RewardedSpawnIds),
            ProgressionRunEnded = progressionState.RunEnded,
            WaveRewardCount = lootState.WaveRewardCount,
            FinalRewardCount = lootState.FinalRewardCount,
            FinalRewardRequested = lootState.FinalRewardRequested,
            LootProcessedSpawnIds = new List<int>(
                lootState.ProcessedSpawnIds),
            LootRewardedWaves = new List<int>(lootState.RewardedWaves),
            EnemyRewardCount = lootState.EnemySettlementCount,
            SpawnedRewardStackCount = lootState.SpawnedStackCount,
            LootAcceptingRewards = lootState.AcceptingRewards,
            HasLastDeathPosition = lootState.HasLastDeathPosition,
            LastDeathPosition = new Float3Snapshot(
                lootState.LastDeathPosition.x,
                lootState.LastDeathPosition.y,
                lootState.LastDeathPosition.z),
            StatisticsKills = statistics.Kills,
            StatisticsShotsFired = statistics.ShotsFired,
            StatisticsHits = statistics.Hits,
            StatisticsCompletedWaves = statistics.CompletedWaves,
            StatisticsDamageTakenCount = statistics.DamageTakenCount,
            StatisticsDamage = statistics.DamageTakenAmount,
            ElapsedSeconds = statistics.ElapsedSeconds,
            StatisticsAuthoritativeKillTracking =
                statistics.AuthoritativeKillTracking,
            StatisticsRewardedSpawnIds = new List<int>(
                statistics.RewardedSpawnIds)
        };
    }

    internal static List<GameplayEffectSnapshot> ToSaveEffects(
        GameplayEffectRuntimeSnapshot runtime)
    {
        var result = new List<GameplayEffectSnapshot>();
        if (runtime == null)
        {
            return result;
        }

        foreach (GameplayEffectInstanceSnapshot instance in runtime.Instances)
        {
            var saved = new GameplayEffectSnapshot
            {
                EffectId = instance.DefinitionId,
                SourceId = instance.Context.SourceId,
                SourceKey = instance.Context.SourceKey,
                DurationPolicy = instance.TimedStacks.Count > 0
                    ? (int)GameplayEffectDurationPolicy.Timed
                    : (int)GameplayEffectDurationPolicy.Persistent,
                TickRemaining = instance.TickRemaining
            };
            foreach (GameplayEffectTimedStackSnapshot stack in
                     instance.TimedStacks)
            {
                saved.Stacks.Add(new GameplayEffectStackSnapshot
                {
                    SourceId = stack.Context.SourceId,
                    SourceKey = stack.Context.SourceKey,
                    RemainingDuration = stack.RemainingDuration,
                    Order = stack.Order
                });
            }
            result.Add(saved);
        }
        return result;
    }

    internal static GameplayEffectRuntimeSnapshot ToRuntimeEffects(
        IReadOnlyList<GameplayEffectSnapshot> saved)
    {
        if (saved == null || saved.Count == 0)
        {
            return new GameplayEffectRuntimeSnapshot(
                Array.Empty<GameplayEffectInstanceSnapshot>());
        }

        var instances = new GameplayEffectInstanceSnapshot[saved.Count];
        for (int index = 0; index < saved.Count; index++)
        {
            GameplayEffectSnapshot effect = saved[index];
            var stacks = new GameplayEffectTimedStackSnapshot[
                effect.Stacks.Count];
            for (int stackIndex = 0;
                 stackIndex < stacks.Length;
                 stackIndex++)
            {
                GameplayEffectStackSnapshot stack =
                    effect.Stacks[stackIndex];
                stacks[stackIndex] =
                    new GameplayEffectTimedStackSnapshot(
                        new GameplayEffectContextSnapshot(
                            stack.SourceId,
                            stack.SourceKey),
                        stack.RemainingDuration,
                        stack.Order);
            }
            instances[index] = new GameplayEffectInstanceSnapshot(
                effect.EffectId,
                new GameplayEffectContextSnapshot(
                    effect.SourceId,
                    effect.SourceKey),
                effect.TickRemaining,
                stacks);
        }
        return new GameplayEffectRuntimeSnapshot(instances);
    }

    internal static CombatBuildSnapshot ToSaveCombatBuild(
        CombatRuleRuntimeSnapshot runtime)
    {
        if (runtime == null)
        {
            return null;
        }

        var saved = new CombatBuildSnapshot
        {
            NextEventId = runtime.NextEventId,
            InstalledBuildIds = new List<string>(runtime.InstalledBuildIds),
            ProcessedEventIds = new List<long>(runtime.ProcessedEventIds)
        };
        for (int index = 0; index < runtime.Cooldowns.Count; index++)
        {
            CombatRuleCooldownSnapshot cooldown = runtime.Cooldowns[index];
            saved.Cooldowns.Add(new CombatRuleCooldownSaveSnapshot
            {
                RuleId = cooldown.RuleId,
                ReadyTick = cooldown.ReadyTick
            });
        }
        return saved;
    }

    internal static CombatDirectorSaveSnapshot ToSaveCombatDirector(
        CombatDirectorRuntimeSnapshot runtime)
    {
        if (runtime == null) return null;
        CombatDirectorEventState current = runtime.CurrentEvent;
        return new CombatDirectorSaveSnapshot
        {
            Phase = (int)runtime.Phase,
            RandomState = runtime.RandomState,
            NextEvaluationTick = runtime.NextEvaluationTick,
            WarningEndTick = runtime.WarningEndTick,
            CooldownEndTick = runtime.CooldownEndTick,
            NextEventId = runtime.NextEventId,
            LastIntensity = runtime.LastIntensity,
            FailedSpawnAttempts = runtime.FailedSpawnAttempts,
            CurrentEvent = current == null
                ? null
                : new CombatDirectorEventSaveSnapshot
                {
                    EventId = current.EventId,
                    EnemyTypeId = current.EnemyTypeId,
                    RoleTag = current.RoleTag,
                    RequestedCount = current.RequestedCount,
                    SpawnedCount = current.SpawnedCount,
                    SignedDirectionDegrees = current.SignedDirectionDegrees,
                    SelectedScore = current.SelectedScore,
                    Reason = current.Reason
                }
        };
    }

    internal static CombatDirectorRuntimeSnapshot ToRuntimeCombatDirector(
        CombatDirectorSaveSnapshot saved)
    {
        if (saved == null) return null;
        CombatDirectorEventSaveSnapshot current = saved.CurrentEvent;
        return new CombatDirectorRuntimeSnapshot(
            (CombatDirectorPhase)saved.Phase,
            saved.RandomState,
            saved.NextEvaluationTick,
            saved.WarningEndTick,
            saved.CooldownEndTick,
            saved.NextEventId,
            saved.LastIntensity,
            saved.FailedSpawnAttempts,
            current == null
                ? null
                : new CombatDirectorEventState(
                    current.EventId,
                    current.EnemyTypeId,
                    current.RoleTag,
                    current.RequestedCount,
                    current.SpawnedCount,
                    current.SignedDirectionDegrees,
                    current.SelectedScore,
                    current.Reason));
    }

    internal static EncounterRuntimeRestoreSnapshot ToRuntimeEncounter(
        EncounterSaveSnapshot saved)
    {
        if (saved == null) return null;
        return new EncounterRuntimeRestoreSnapshot(
            saved.SequenceContentVersion,
            new EncounterRuntimeSnapshot(
                saved.NextIndex,
                saved.ActiveEncounterId,
                saved.ActiveDefinitionVersion,
                (EncounterPhase)saved.Phase,
                saved.StartedTick,
                saved.ActiveTick,
                saved.DeadlineTick,
                saved.Progress,
                saved.ResolvedEncounterIds,
                saved.RewardedEncounterIds),
            saved.CurrentTick,
            saved.ActiveRosterTokens);
    }

    internal static CombatRuleRuntimeSnapshot ToRuntimeCombatBuild(
        CombatBuildSnapshot saved)
    {
        if (saved == null)
        {
            return null;
        }

        var cooldowns = new CombatRuleCooldownSnapshot[
            saved.Cooldowns.Count];
        for (int index = 0; index < cooldowns.Length; index++)
        {
            CombatRuleCooldownSaveSnapshot cooldown = saved.Cooldowns[index];
            cooldowns[index] = new CombatRuleCooldownSnapshot(
                cooldown.RuleId,
                cooldown.ReadyTick);
        }
        return new CombatRuleRuntimeSnapshot(
            saved.InstalledBuildIds,
            cooldowns,
            saved.ProcessedEventIds,
            saved.NextEventId);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
