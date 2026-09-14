using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FPS.SaveGame
{
    public static class SnapshotChecksum
    {
        // A length-prefixed, fixed-order binary representation avoids culture, JSON
        // whitespace, dictionary enumeration and ambiguous string concatenation.
        public static string Compute(RunSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(snapshot.SchemaVersion);
                writer.Write(snapshot.Seed);
                WriteFloat(writer, snapshot.Health);
                WriteFloat(writer, snapshot.Armor);
                writer.Write(snapshot.CurrentWeaponId ?? string.Empty);
                writer.Write(snapshot.Weapons?.Count ?? 0);
                if (snapshot.Weapons != null)
                    foreach (var weapon in snapshot.Weapons)
                    {
                        writer.Write(weapon.WeaponId ?? string.Empty);
                        writer.Write(weapon.Magazine);
                        writer.Write(weapon.Reserve);
                    }
                writer.Write(snapshot.Upgrades?.Count ?? 0);
                if (snapshot.Upgrades != null)
                    foreach (var upgrade in snapshot.Upgrades)
                    {
                        writer.Write(upgrade.UpgradeId ?? string.Empty);
                        writer.Write(upgrade.Level);
                    }
                writer.Write(snapshot.UpgradeSelectionHistory?.Count ?? 0);
                if (snapshot.UpgradeSelectionHistory != null)
                    foreach (var id in snapshot.UpgradeSelectionHistory) writer.Write(id ?? string.Empty);
                WriteInventory(writer, snapshot.InventorySlots);
                WriteQuickSlots(writer, snapshot.QuickSlots);
                writer.Write(snapshot.SelectedQuickSlotIndex);
                WriteFloat(writer, snapshot.InventoryCooldownRemainingSeconds);
                WriteMission(writer, snapshot.Mission);
                WriteWave(writer, snapshot.Wave);
                WriteEnemies(writer, snapshot.Enemies);
                WriteEffects(writer, snapshot.PlayerEffects);
                WriteVector(writer, snapshot.PlayerPosition);
                WriteRotation(writer, snapshot.PlayerRotation);
                WriteFloat(writer, snapshot.CameraPitch);
                writer.Write(snapshot.PlayerCrouching);
                WriteSimulationClock(writer, snapshot.Simulation);
                WriteCombatBuild(writer, snapshot.CombatBuild);
                WriteCombatDirector(writer, snapshot.CombatDirector);
                WriteEncounter(writer, snapshot.Encounter);
                WriteLayout(writer, snapshot.Layout);
                writer.Flush();
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(stream.ToArray());
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
        }

        private static void WriteInventory(BinaryWriter writer, List<InventorySlotSnapshot> values)
        {
            writer.Write(values?.Count ?? -1);
            if (values == null) return;
            foreach (var value in values)
            {
                writer.Write(value?.SlotIndex ?? -1);
                writer.Write(value?.ItemId ?? string.Empty);
                writer.Write(value?.Quantity ?? -1);
            }
        }

        private static void WriteQuickSlots(BinaryWriter writer, List<QuickSlotSnapshot> values)
        {
            writer.Write(values?.Count ?? -1);
            if (values == null) return;
            foreach (var value in values)
            {
                writer.Write(value?.SlotIndex ?? -1);
                writer.Write(value?.ItemId ?? string.Empty);
            }
        }

        private static void WriteMission(BinaryWriter writer, MissionSnapshot value)
        {
            writer.Write(value != null);
            if (value == null) return;
            writer.Write(value.Phase);
            writer.Write(value.TerminalCompleted);
            WriteFloat(writer, value.TerminalProgressNormalized);
            writer.Write(value.EliminatedTargets);
            writer.Write(value.RequiredTargets);
            writer.Write(value.ExperienceLevel);
            WriteFloat(writer, value.CurrentExperience);
            WriteFloat(writer, value.TotalExperience);
            writer.Write(value.LevelUpCount);
            writer.Write(value.RewardedKillCount);
            writer.Write(value.WaveRewardCount);
            writer.Write(value.FinalRewardCount);
            writer.Write(value.FinalRewardRequested);
            writer.Write(value.StatisticsKills);
            WriteFloat(writer, value.StatisticsDamage);
            WriteFloat(writer, value.ElapsedSeconds);
            WriteIntegers(writer, value.ProgressionRewardedSpawnIds);
            writer.Write(value.ProgressionRunEnded);
            WriteIntegers(writer, value.LootProcessedSpawnIds);
            WriteIntegers(writer, value.LootRewardedWaves);
            writer.Write(value.EnemyRewardCount);
            writer.Write(value.SpawnedRewardStackCount);
            writer.Write(value.LootAcceptingRewards);
            writer.Write(value.HasLastDeathPosition);
            WriteVector(writer, value.LastDeathPosition);
            writer.Write(value.StatisticsShotsFired);
            writer.Write(value.StatisticsHits);
            writer.Write(value.StatisticsCompletedWaves);
            writer.Write(value.StatisticsDamageTakenCount);
            writer.Write(value.StatisticsAuthoritativeKillTracking);
            WriteIntegers(writer, value.StatisticsRewardedSpawnIds);
            writer.Write(value.ExperienceToNextLevel);
        }

        private static void WriteWave(BinaryWriter writer, WaveSnapshot value)
        {
            writer.Write(value != null);
            if (value == null) return;
            writer.Write(value.CurrentWave);
            writer.Write(value.Phase);
            writer.Write(value.TotalEnemyCount);
            writer.Write(value.MaximumAliveCount);
            WriteIntegers(writer, value.SpawnedIds);
            WriteIntegers(writer, value.ActiveIds);
            WriteIntegers(writer, value.SettledIds);
            WriteFloat(writer, value.IntermissionRemaining);
            WriteFloat(writer, value.SpawnCooldownRemaining);
            writer.Write(value.NextSpawnId);
            WriteFloat(writer, value.RemainingThreatBudget);
        }

        private static void WriteIntegers(BinaryWriter writer, List<int> values)
        {
            writer.Write(values?.Count ?? -1);
            if (values == null) return;
            foreach (int value in values) writer.Write(value);
        }

        private static void WriteEnemies(BinaryWriter writer, List<EnemySnapshot> values)
        {
            writer.Write(values?.Count ?? -1);
            if (values == null) return;
            foreach (var value in values)
            {
                writer.Write(value != null);
                if (value == null) continue;
                writer.Write(value.WaveNumber);
                writer.Write(value.SpawnId);
                writer.Write(value.EnemyTypeId ?? string.Empty);
                WriteVector(writer, value.Position);
                WriteRotation(writer, value.Rotation);
                WriteFloat(writer, value.Health);
                WriteFloat(writer, value.Armor);
                writer.Write(value.AwarenessState);
                writer.Write(value.AttackState);
                WriteEffects(writer, value.Effects);
            }

            // Append the optional identity extension only when it is present.
            // This deliberately preserves the byte stream (and checksum) of
            // schema-v2 saves written before archetype identity was persisted.
            bool hasArchetypeIdentity = false;
            for (int index = 0; index < values.Count; index++)
            {
                if (!string.IsNullOrEmpty(values[index]?.ArchetypeStableId))
                {
                    hasArchetypeIdentity = true;
                    break;
                }
            }
            if (!hasArchetypeIdentity) return;

            writer.Write("enemy-archetype-identity:v1");
            writer.Write(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                writer.Write(values[index]?.ArchetypeStableId ?? string.Empty);
            }
        }

        private static void WriteEffects(BinaryWriter writer, List<GameplayEffectSnapshot> values)
        {
            writer.Write(values?.Count ?? -1);
            if (values == null) return;
            foreach (var value in values)
            {
                writer.Write(value != null);
                if (value == null) continue;
                writer.Write(value.EffectId ?? string.Empty);
                writer.Write(value.SourceId ?? string.Empty);
                writer.Write(value.SourceKey ?? string.Empty);
                writer.Write(value.DurationPolicy);
                WriteFloat(writer, value.TickRemaining);
                writer.Write(value.Stacks?.Count ?? -1);
                if (value.Stacks == null) continue;
                foreach (var stack in value.Stacks)
                {
                    writer.Write(stack != null);
                    if (stack == null) continue;
                    writer.Write(stack.SourceId ?? string.Empty);
                    writer.Write(stack.SourceKey ?? string.Empty);
                    WriteFloat(writer, stack.RemainingDuration);
                    writer.Write(stack.Order);
                }
            }
        }

        private static void WriteVector(BinaryWriter writer, Float3Snapshot value)
        {
            writer.Write(value != null);
            if (value == null) return;
            WriteFloat(writer, value.X);
            WriteFloat(writer, value.Y);
            WriteFloat(writer, value.Z);
        }

        private static void WriteRotation(BinaryWriter writer, Float4Snapshot value)
        {
            writer.Write(value != null);
            if (value == null) return;
            WriteFloat(writer, value.X);
            WriteFloat(writer, value.Y);
            WriteFloat(writer, value.Z);
            WriteFloat(writer, value.W);
        }

        private static void WriteSimulationClock(
            BinaryWriter writer,
            SimulationClockSnapshot value)
        {
            // No null marker is written so pre-Issue-58 schema-v2 checksums remain
            // byte-for-byte valid. New saves append the authoritative clock block.
            if (value == null) return;
            writer.Write("FPS.Simulation.v1");
            writer.Write(value.Tick);
            writer.Write(value.NextEventSequence);
            writer.Write(value.FixedTickRate);
            writer.Write(value.Paused);
            WriteFloat(writer, value.PlayerHealth);
            WriteFloat(writer, value.PlayerArmor);
        }

        private static void WriteCombatBuild(
            BinaryWriter writer,
            CombatBuildSnapshot value)
        {
            // As with the simulation block, omit a null marker to preserve the
            // checksum of earlier schema-v2 saves.
            if (value == null) return;
            writer.Write("FPS.CombatBuild.v1");
            writer.Write(value.NextEventId);
            writer.Write(value.InstalledBuildIds?.Count ?? -1);
            if (value.InstalledBuildIds != null)
                foreach (string id in value.InstalledBuildIds)
                    writer.Write(id ?? string.Empty);
            writer.Write(value.Cooldowns?.Count ?? -1);
            if (value.Cooldowns != null)
                foreach (CombatRuleCooldownSaveSnapshot cooldown in
                         value.Cooldowns)
                {
                    writer.Write(cooldown != null);
                    if (cooldown == null) continue;
                    writer.Write(cooldown.RuleId ?? string.Empty);
                    writer.Write(cooldown.ReadyTick);
                }
            writer.Write(value.ProcessedEventIds?.Count ?? -1);
            if (value.ProcessedEventIds != null)
                foreach (long eventId in value.ProcessedEventIds)
                    writer.Write(eventId);
        }

        private static void WriteCombatDirector(
            BinaryWriter writer,
            CombatDirectorSaveSnapshot value)
        {
            if (value == null) return;
            writer.Write("FPS.CombatDirector.v1");
            writer.Write(value.Phase);
            writer.Write(value.RandomState);
            writer.Write(value.NextEvaluationTick);
            writer.Write(value.WarningEndTick);
            writer.Write(value.CooldownEndTick);
            writer.Write(value.NextEventId);
            writer.Write(value.LastIntensity);
            writer.Write(value.FailedSpawnAttempts);
            writer.Write(value.CurrentEvent != null);
            if (value.CurrentEvent == null) return;
            writer.Write(value.CurrentEvent.EventId);
            writer.Write(value.CurrentEvent.EnemyTypeId ?? string.Empty);
            writer.Write(value.CurrentEvent.RoleTag ?? string.Empty);
            writer.Write(value.CurrentEvent.RequestedCount);
            writer.Write(value.CurrentEvent.SpawnedCount);
            writer.Write(value.CurrentEvent.SignedDirectionDegrees);
            WriteFloat(writer, value.CurrentEvent.SelectedScore);
            writer.Write(value.CurrentEvent.Reason ?? string.Empty);
        }

        private static void WriteEncounter(
            BinaryWriter writer,
            EncounterSaveSnapshot value)
        {
            if (value == null) return;
            writer.Write("FPS.Encounter.v1");
            writer.Write(value.SequenceContentVersion);
            writer.Write(value.NextIndex);
            writer.Write(value.ActiveEncounterId ?? string.Empty);
            writer.Write(value.ActiveDefinitionVersion);
            writer.Write(value.Phase);
            writer.Write(value.StartedTick);
            writer.Write(value.ActiveTick);
            writer.Write(value.DeadlineTick);
            writer.Write(value.CurrentTick);
            writer.Write(value.Progress);
            writer.Write(value.ResolvedEncounterIds?.Count ?? -1);
            if (value.ResolvedEncounterIds != null)
                foreach (string id in value.ResolvedEncounterIds)
                    writer.Write(id ?? string.Empty);
            writer.Write(value.RewardedEncounterIds?.Count ?? -1);
            if (value.RewardedEncounterIds != null)
                foreach (string id in value.RewardedEncounterIds)
                    writer.Write(id ?? string.Empty);
            WriteIntegers(writer, value.ActiveRosterTokens);
        }

        private static void WriteLayout(
            BinaryWriter writer,
            LayoutSaveSnapshot value)
        {
            if (value == null) return;
            writer.Write("FPS.Layout.v1");
            writer.Write(value.LayoutId ?? string.Empty);
            writer.Write(value.ContentVersion);
            writer.Write(value.GeneratorVersion);
            writer.Write(value.Fingerprint ?? string.Empty);
            writer.Write(value.UsedFallback);
            writer.Write(value.Modules?.Count ?? -1);
            if (value.Modules != null)
                foreach (LayoutModuleSaveSnapshot module in value.Modules)
                {
                    writer.Write(module != null);
                    if (module == null) continue;
                    writer.Write(module.InstanceId ?? string.Empty);
                    writer.Write(module.DefinitionId ?? string.Empty);
                    writer.Write(module.Kind);
                    writer.Write(module.GridX);
                    writer.Write(module.GridZ);
                    writer.Write(module.QuarterTurns);
                }
            writer.Write(value.Connections?.Count ?? -1);
            if (value.Connections != null)
                foreach (LayoutConnectionSaveSnapshot connection in value.Connections)
                {
                    writer.Write(connection != null);
                    if (connection == null) continue;
                    writer.Write(connection.FromInstanceId ?? string.Empty);
                    writer.Write(connection.FromSocketId ?? string.Empty);
                    writer.Write(connection.ToInstanceId ?? string.Empty);
                    writer.Write(connection.ToSocketId ?? string.Empty);
                }
        }

        private static void WriteFloat(BinaryWriter writer, float value)
        {
            // JSON does not preserve IEEE-754's signed zero. Canonicalize both
            // representations so a save/load round trip keeps the same checksum.
            writer.Write(value == 0f ? 0f : value);
        }
    }

    public static class SnapshotValidation
    {
        public const int MaximumWeapons = 128;
        public const int MaximumUpgrades = 1024;
        public const int MaximumInventorySlots = 256;
        public const int MaximumQuickSlots = 32;
        public const int MaximumEnemies = 4096;
        public const int MaximumEffectsPerEntity = 256;
        public const int MaximumEffectStacks = 128;
        public const int MaximumCombatBuilds = 128;
        public const int MaximumCombatRuleCooldowns = 1024;
        public const int MaximumCombatRuleEvents = 256;
        public const int MaximumLayoutModules = 32;
        public const int MaximumLayoutConnections = 64;

        public static bool TryValidate(RunSnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null) error = "存档内容为空。";
            else if (snapshot.SchemaVersion != RunSnapshot.CurrentSchemaVersion)
                error = "不支持的存档版本：" + snapshot.SchemaVersion;
            else if (!FiniteNonNegative(snapshot.Health) || !FiniteNonNegative(snapshot.Armor))
                error = "生命或护甲数值无效。";
            else if (snapshot.Weapons == null || snapshot.Weapons.Count == 0 || snapshot.Weapons.Count > MaximumWeapons)
                error = "武器列表无效。";
            else if (snapshot.Upgrades == null || snapshot.Upgrades.Count > MaximumUpgrades)
                error = "升级列表无效。";
            else if (snapshot.InventorySlots == null || snapshot.InventorySlots.Count > MaximumInventorySlots)
                error = "背包槽位列表无效。";
            else if (snapshot.QuickSlots == null || snapshot.QuickSlots.Count > MaximumQuickSlots)
                error = "快捷栏列表无效。";
            else if (!FiniteNonNegative(snapshot.InventoryCooldownRemainingSeconds))
                error = "背包冷却时间无效。";
            else if (snapshot.Mission == null || snapshot.Wave == null || snapshot.Enemies == null || snapshot.PlayerEffects == null)
                error = "任务、波次、敌人或效果状态缺失。";
            else if (snapshot.PlayerPosition == null ||
                     snapshot.PlayerRotation == null ||
                     !FiniteVector(snapshot.PlayerPosition) ||
                     !FiniteRotation(snapshot.PlayerRotation) ||
                     !Finite(snapshot.CameraPitch) ||
                     snapshot.CameraPitch < -80f ||
                     snapshot.CameraPitch > 80f)
                error = "玩家位置、朝向或视角状态无效。";
            else if (snapshot.Enemies.Count > MaximumEnemies)
                error = "敌人列表超出容量限制。";
            else if (snapshot.Simulation != null &&
                     (snapshot.Simulation.Tick < 0 ||
                      snapshot.Simulation.NextEventSequence < 0 ||
                      snapshot.Simulation.FixedTickRate < 1 ||
                      snapshot.Simulation.FixedTickRate > 1000 ||
                      !FiniteNonNegative(snapshot.Simulation.PlayerHealth) ||
                      !FiniteNonNegative(snapshot.Simulation.PlayerArmor)))
                error = "权威战局时钟状态无效。";
            else if (!ValidateCombatBuild(snapshot.CombatBuild, out error))
                return false;
            else if (!ValidateCombatDirector(
                         snapshot.CombatDirector,
                         out error))
                return false;
            else if (!ValidateEncounter(snapshot.Encounter, out error))
                return false;
            else if (!ValidateLayout(snapshot.Layout, out error))
                return false;
            if (error != null) return false;

            var weapons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var weapon in snapshot.Weapons)
            {
                if (weapon == null || !ValidId(weapon.WeaponId) || !weapons.Add(weapon.WeaponId)
                    || weapon.Magazine < 0 || weapon.Reserve < 0)
                {
                    error = "武器标识重复、缺失或弹药数值无效。";
                    return false;
                }
            }
            if (snapshot.CurrentWeaponId == null || !weapons.Contains(snapshot.CurrentWeaponId))
            {
                error = "当前武器不在存档武器列表内。";
                return false;
            }
            var upgrades = new HashSet<string>(StringComparer.Ordinal);
            foreach (var upgrade in snapshot.Upgrades)
            {
                if (upgrade == null || !ValidId(upgrade.UpgradeId) || !upgrades.Add(upgrade.UpgradeId) || upgrade.Level <= 0)
                {
                    error = "升级标识重复、缺失或等级无效。";
                    return false;
                }
            }
            if (snapshot.UpgradeSelectionHistory == null || snapshot.UpgradeSelectionHistory.Count > 100000)
            {
                error = "升级选择历史无效。";
                return false;
            }
            var historyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in snapshot.UpgradeSelectionHistory)
            {
                if (id == null || !upgrades.Contains(id))
                {
                    error = "升级选择历史包含未知升级。";
                    return false;
                }
                historyCounts.TryGetValue(id, out var count);
                historyCounts[id] = count + 1;
            }
            foreach (var upgrade in snapshot.Upgrades)
            {
                if (!historyCounts.TryGetValue(upgrade.UpgradeId, out var count) || count != upgrade.Level)
                {
                    error = "升级选择历史与升级等级不一致。";
                    return false;
                }
            }

            if (!ValidateInventory(snapshot, out error) ||
                !ValidateMission(snapshot.Mission, out error) ||
                !ValidateWaveAndEnemies(snapshot.Wave, snapshot.Enemies, out error) ||
                !ValidateEffects(snapshot.PlayerEffects, "玩家", out error))
                return false;

            return true;
        }

        private static bool ValidateCombatBuild(
            CombatBuildSnapshot value,
            out string error)
        {
            error = null;
            if (value == null) return true;
            if (value.NextEventId < 0 || value.InstalledBuildIds == null ||
                value.Cooldowns == null || value.ProcessedEventIds == null ||
                value.InstalledBuildIds.Count > MaximumCombatBuilds ||
                value.Cooldowns.Count > MaximumCombatRuleCooldowns ||
                value.ProcessedEventIds.Count > MaximumCombatRuleEvents)
            {
                error = "构筑运行时状态缺失或超出容量限制。";
                return false;
            }

            var builds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in value.InstalledBuildIds)
            {
                if (!ValidId(id) || !builds.Add(id))
                {
                    error = "构筑标识缺失或重复。";
                    return false;
                }
            }

            var rules = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatRuleCooldownSaveSnapshot cooldown in
                     value.Cooldowns)
            {
                if (cooldown == null || !ValidId(cooldown.RuleId) ||
                    cooldown.ReadyTick < 0 || !rules.Add(cooldown.RuleId))
                {
                    error = "构筑规则冷却状态无效或重复。";
                    return false;
                }
            }

            var events = new HashSet<long>();
            foreach (long eventId in value.ProcessedEventIds)
            {
                if (eventId <= 0 || eventId > value.NextEventId ||
                    !events.Add(eventId))
                {
                    error = "构筑事件去重窗口无效。";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateCombatDirector(
            CombatDirectorSaveSnapshot value,
            out string error)
        {
            error = null;
            if (value == null) return true;
            if (value.Phase < 0 || value.Phase > 3 ||
                value.NextEvaluationTick < 0 || value.WarningEndTick < 0 ||
                value.CooldownEndTick < 0 || value.NextEventId < 1 ||
                value.LastIntensity < 0 || value.LastIntensity > 2 ||
                value.FailedSpawnAttempts < 0 ||
                value.FailedSpawnAttempts >= 3)
            {
                error = "动态战斗导演阶段或计数状态无效。";
                return false;
            }
            CombatDirectorEventSaveSnapshot current = value.CurrentEvent;
            if (current == null)
            {
                if (value.Phase == 1 || value.Phase == 2)
                {
                    error = "动态战斗导演活动事件缺失。";
                    return false;
                }
                return true;
            }
            int absoluteAngle = Math.Abs(current.SignedDirectionDegrees);
            if (current.EventId < 1 || current.EventId >= value.NextEventId ||
                !ValidId(current.EnemyTypeId) || !ValidId(current.RoleTag) ||
                current.RequestedCount < 1 || current.RequestedCount > 2 ||
                current.SpawnedCount < 0 ||
                current.SpawnedCount > current.RequestedCount ||
                absoluteAngle < 120 || absoluteAngle > 165 ||
                !Finite(current.SelectedScore) ||
                current.SelectedScore < 0f || current.SelectedScore > 1f ||
                current.Reason == null || current.Reason.Length > 512 ||
                (value.Phase == 1 && current.SpawnedCount != 0) ||
                (value.Phase == 2 &&
                 current.SpawnedCount >= current.RequestedCount))
            {
                error = "动态战斗导演事件状态无效。";
                return false;
            }
            return true;
        }

        private static bool ValidateEncounter(
            EncounterSaveSnapshot value,
            out string error)
        {
            error = null;
            if (value == null) return true;
            if (value.SequenceContentVersion < 1 || value.NextIndex < 0 ||
                value.NextIndex > 64 || value.Phase < 0 || value.Phase > 7 ||
                value.StartedTick < 0 || value.ActiveTick < 0 ||
                value.DeadlineTick < 0 || value.CurrentTick < 0 ||
                value.CurrentTick < value.StartedTick || value.Progress < 0 ||
                value.ResolvedEncounterIds == null ||
                value.RewardedEncounterIds == null ||
                value.ActiveRosterTokens == null ||
                value.ResolvedEncounterIds.Count > 64 ||
                value.RewardedEncounterIds.Count > 64 ||
                value.ActiveRosterTokens.Count > 256)
            {
                error = "遭遇事件存档阶段或容量无效。";
                return false;
            }
            bool hasActive = !string.IsNullOrEmpty(value.ActiveEncounterId);
            if (hasActive != (value.Phase == 1 || value.Phase == 2) ||
                hasActive && (!ValidId(value.ActiveEncounterId) ||
                              value.ActiveDefinitionVersion < 1) ||
                !hasActive && (value.ActiveDefinitionVersion != 0 ||
                               value.ActiveRosterTokens.Count > 0))
            {
                error = "遭遇事件活动标识与阶段不一致。";
                return false;
            }
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in value.ResolvedEncounterIds)
                if (!ValidId(id) || !resolved.Add(id))
                {
                    error = "遭遇事件结算账本包含无效或重复标识。";
                    return false;
                }
            var rewarded = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in value.RewardedEncounterIds)
                if (!resolved.Contains(id) || !rewarded.Add(id))
                {
                    error = "遭遇奖励账本包含未结算或重复标识。";
                    return false;
                }
            var tokens = new HashSet<int>();
            foreach (int token in value.ActiveRosterTokens)
                if (token < 0 || !tokens.Add(token))
                {
                    error = "遭遇活动敌人槽位无效或重复。";
                    return false;
                }
            return true;
        }

        private static bool ValidateLayout(
            LayoutSaveSnapshot value,
            out string error)
        {
            error = null;
            if (value == null) return true;
            if (!ValidId(value.LayoutId) || value.ContentVersion < 1 ||
                value.GeneratorVersion < 1 ||
                string.IsNullOrWhiteSpace(value.Fingerprint) ||
                value.Fingerprint.Length != 16 || value.Modules == null ||
                value.Connections == null ||
                value.Modules.Count > MaximumLayoutModules ||
                value.Connections.Count > MaximumLayoutConnections)
            {
                error = "模块化布局标识、版本、指纹或容量无效。";
                return false;
            }
            if (value.UsedFallback && value.LayoutId != "city_new.layout.safe")
            {
                error = "安全回退布局标识无效。";
                return false;
            }
            if (!value.UsedFallback && value.Modules.Count < 5)
            {
                error = "模块化布局缺少完成任务所需的区域。";
                return false;
            }

            var instances = new HashSet<string>(StringComparer.Ordinal);
            var cells = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new int[4];
            foreach (LayoutModuleSaveSnapshot module in value.Modules)
            {
                string cell = module == null ? null : module.GridX + ":" + module.GridZ;
                if (module == null || !ValidId(module.InstanceId) ||
                    !ValidId(module.DefinitionId) ||
                    module.Kind < 0 || module.Kind > 3 ||
                    module.QuarterTurns < 0 || module.QuarterTurns > 3 ||
                    !instances.Add(module.InstanceId) || !cells.Add(cell))
                {
                    error = "模块化布局包含无效、重复或重叠的模块。";
                    return false;
                }
                kinds[module.Kind]++;
            }
            if (!value.UsedFallback &&
                (kinds[0] != 1 || kinds[1] < 2 || kinds[2] != 1 || kinds[3] != 1))
            {
                error = "模块化布局的出生、战斗、事件或撤离区域数量无效。";
                return false;
            }
            var sockets = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutConnectionSaveSnapshot connection in value.Connections)
            {
                if (connection == null ||
                    !instances.Contains(connection.FromInstanceId) ||
                    !instances.Contains(connection.ToInstanceId) ||
                    !ValidId(connection.FromSocketId) ||
                    !ValidId(connection.ToSocketId) ||
                    !sockets.Add(connection.FromInstanceId + "/" + connection.FromSocketId) ||
                    !sockets.Add(connection.ToInstanceId + "/" + connection.ToSocketId))
                {
                    error = "模块化布局连接引用无效或连接口重复使用。";
                    return false;
                }
            }
            string expected = LayoutSaveFingerprint.Compute(value);
            if (!string.Equals(expected, value.Fingerprint, StringComparison.Ordinal))
            {
                error = "模块化布局指纹与模块清单不一致。";
                return false;
            }
            if (!value.UsedFallback &&
                value.LayoutId != "city_new.layout." + expected.Substring(0, 12))
            {
                error = "模块化布局 ID 与指纹不一致。";
                return false;
            }
            return true;
        }

        private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
        private static bool OptionalId(string value) => string.IsNullOrEmpty(value) || ValidId(value);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

        private static bool ValidateInventory(RunSnapshot snapshot, out string error)
        {
            error = null;
            var inventoryIndices = new HashSet<int>();
            foreach (var slot in snapshot.InventorySlots)
            {
                if (slot == null || slot.SlotIndex < 0 || !inventoryIndices.Add(slot.SlotIndex) ||
                    !OptionalId(slot.ItemId) || slot.Quantity < 0 ||
                    (string.IsNullOrEmpty(slot.ItemId) != (slot.Quantity == 0)))
                {
                    error = "背包槽位索引重复，或物品与数量组合无效。";
                    return false;
                }
            }
            for (int index = 0; index < snapshot.InventorySlots.Count; index++)
            {
                if (!inventoryIndices.Contains(index))
                {
                    error = "背包槽位必须按固定容量连续保存。";
                    return false;
                }
            }

            var quickIndices = new HashSet<int>();
            foreach (var slot in snapshot.QuickSlots)
            {
                if (slot == null || slot.SlotIndex < 0 || !quickIndices.Add(slot.SlotIndex) || !OptionalId(slot.ItemId))
                {
                    error = "快捷栏槽位索引重复或物品标识无效。";
                    return false;
                }
            }
            for (int index = 0; index < snapshot.QuickSlots.Count; index++)
            {
                if (!quickIndices.Contains(index))
                {
                    error = "快捷栏槽位必须按固定容量连续保存。";
                    return false;
                }
            }

            if (snapshot.SelectedQuickSlotIndex < -1 ||
                (snapshot.SelectedQuickSlotIndex >= 0 && !quickIndices.Contains(snapshot.SelectedQuickSlotIndex)))
            {
                error = "选中的快捷栏槽位无效。";
                return false;
            }
            return true;
        }

        private static bool ValidateMission(MissionSnapshot mission, out string error)
        {
            error = null;
            if (mission.Phase < 0 || mission.Phase > 4 || mission.RequiredTargets < 1 ||
                mission.EliminatedTargets < 0 || mission.EliminatedTargets > mission.RequiredTargets ||
                !Finite(mission.TerminalProgressNormalized) || mission.TerminalProgressNormalized < 0f ||
                mission.TerminalProgressNormalized > 1f || mission.ExperienceLevel < 1 ||
                mission.CurrentExperience < 0 || mission.TotalExperience < mission.CurrentExperience ||
                mission.LevelUpCount < 0 || mission.RewardedKillCount < 0 || mission.WaveRewardCount < 0 ||
                mission.FinalRewardCount < 0 || mission.FinalRewardCount > 1 || mission.StatisticsKills < 0 ||
                !FiniteNonNegative(mission.StatisticsDamage) || !FiniteNonNegative(mission.ElapsedSeconds))
            {
                error = "任务、经验、奖励或统计状态无效。";
                return false;
            }

            if (mission.TerminalCompleted && mission.TerminalProgressNormalized < 1f)
            {
                error = "终端完成标记与接入进度不一致。";
                return false;
            }
            if (!ValidUniquePositiveIds(
                    mission.ProgressionRewardedSpawnIds) ||
                !ValidUniquePositiveIds(mission.LootProcessedSpawnIds) ||
                !ValidUniquePositiveIds(mission.StatisticsRewardedSpawnIds) ||
                !ValidUniquePositiveIds(mission.LootRewardedWaves) ||
                mission.RewardedKillCount !=
                    mission.ProgressionRewardedSpawnIds.Count ||
                mission.EnemyRewardCount !=
                    mission.LootProcessedSpawnIds.Count ||
                mission.WaveRewardCount != mission.LootRewardedWaves.Count ||
                mission.EnemyRewardCount < 0 ||
                mission.SpawnedRewardStackCount < 0 ||
                mission.FinalRewardRequested !=
                    (mission.FinalRewardCount == 1) ||
                mission.LastDeathPosition == null ||
                !FiniteVector(mission.LastDeathPosition) ||
                mission.StatisticsShotsFired < 0 ||
                mission.StatisticsHits < 0 ||
                mission.StatisticsHits > mission.StatisticsShotsFired ||
                mission.StatisticsCompletedWaves < 0 ||
                mission.StatisticsDamageTakenCount < 0 ||
                mission.ExperienceToNextLevel < 0 ||
                mission.StatisticsAuthoritativeKillTracking &&
                mission.StatisticsKills !=
                    mission.StatisticsRewardedSpawnIds.Count)
            {
                error = "经验、奖励或任务统计账本无效。";
                return false;
            }
            return true;
        }

        private static bool ValidUniquePositiveIds(List<int> values)
        {
            if (values == null || values.Count > MaximumEnemies)
            {
                return false;
            }
            var unique = new HashSet<int>();
            foreach (int value in values)
            {
                if (value <= 0 || !unique.Add(value))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateWaveAndEnemies(
            WaveSnapshot wave,
            List<EnemySnapshot> enemies,
            out string error)
        {
            error = null;
            if (wave.Phase < 0 || wave.Phase > 5 || wave.CurrentWave < 0 || wave.TotalEnemyCount < 0 ||
                wave.MaximumAliveCount < 0 || wave.MaximumAliveCount > wave.TotalEnemyCount ||
                wave.NextSpawnId < 1 || wave.RemainingThreatBudget < 0 ||
                !FiniteNonNegative(wave.IntermissionRemaining) || !FiniteNonNegative(wave.SpawnCooldownRemaining) ||
                wave.SpawnedIds == null || wave.ActiveIds == null || wave.SettledIds == null ||
                wave.SpawnedIds.Count > MaximumEnemies || wave.ActiveIds.Count > MaximumEnemies ||
                wave.SettledIds.Count > MaximumEnemies)
            {
                error = "波次基础状态无效。";
                return false;
            }

            if (wave.Phase == 0)
            {
                if (wave.CurrentWave != 0 || wave.TotalEnemyCount != 0 || wave.MaximumAliveCount != 0 || wave.SpawnedIds.Count != 0 ||
                    wave.ActiveIds.Count != 0 || wave.SettledIds.Count != 0 || enemies.Count != 0)
                {
                    error = "未开始波次包含运行中状态。";
                    return false;
                }
                return true;
            }

            if (wave.CurrentWave < 1 || wave.TotalEnemyCount < 1 || wave.MaximumAliveCount < 1 ||
                wave.SpawnedIds.Count > wave.TotalEnemyCount || wave.ActiveIds.Count > wave.MaximumAliveCount)
            {
                error = "运行中波次编号或敌人总数无效。";
                return false;
            }

            var spawned = new HashSet<int>();
            foreach (int id in wave.SpawnedIds)
                if (id < 1 || !spawned.Add(id)) { error = "已生成敌人标识重复或无效。"; return false; }

            foreach (int id in spawned)
                if (id >= wave.NextSpawnId) { error = "下一生成标识没有领先于已生成实体。"; return false; }

            var active = new HashSet<int>();
            foreach (int id in wave.ActiveIds)
                if (!spawned.Contains(id) || !active.Add(id)) { error = "存活敌人标识不是唯一的已生成实体。"; return false; }

            var settled = new HashSet<int>();
            foreach (int id in wave.SettledIds)
                if (!spawned.Contains(id) || active.Contains(id) || !settled.Add(id))
                { error = "已结算敌人标识重复或与存活实体冲突。"; return false; }

            if (active.Count + settled.Count != spawned.Count)
            {
                error = "已生成敌人未被完整划分为存活或已结算状态。";
                return false;
            }

            if ((wave.Phase == 1 && spawned.Count >= wave.TotalEnemyCount) ||
                (wave.Phase == 2 && (spawned.Count != wave.TotalEnemyCount || active.Count == 0)) ||
                ((wave.Phase == 3 || wave.Phase == 4) &&
                 (spawned.Count != wave.TotalEnemyCount || active.Count != 0)))
            {
                error = "波次阶段与生成、存活及结算数量不一致。";
                return false;
            }
            if ((wave.Phase == 3) != (wave.IntermissionRemaining > 0f))
            {
                error = "波次阶段与幕间倒计时不一致。";
                return false;
            }

            var enemyKeys = new HashSet<string>(StringComparer.Ordinal);
            var enemyIds = new HashSet<int>();
            foreach (var enemy in enemies)
            {
                string key = enemy == null ? null : enemy.WaveNumber + ":" + enemy.SpawnId;
                if (enemy == null || enemy.WaveNumber != wave.CurrentWave || !active.Contains(enemy.SpawnId) ||
                    !enemyIds.Add(enemy.SpawnId) || !enemyKeys.Add(key) || !ValidId(enemy.EnemyTypeId) ||
                    !OptionalId(enemy.ArchetypeStableId) ||
                    enemy.Position == null || enemy.Rotation == null || !FiniteVector(enemy.Position) ||
                    !FiniteRotation(enemy.Rotation) || !FiniteNonNegative(enemy.Health) || enemy.Health <= 0f ||
                    !FiniteNonNegative(enemy.Armor) || enemy.AwarenessState < 0 || enemy.AttackState < 0 ||
                    !ValidateEffects(enemy.Effects, "敌人 " + key, out error))
                {
                    if (error == null) error = "敌人实体键、变换、生命或运行状态无效。";
                    return false;
                }
            }

            if (!enemyIds.SetEquals(active))
            {
                error = "波次存活标识与敌人快照不一致。";
                return false;
            }
            return true;
        }

        private static bool ValidateEffects(List<GameplayEffectSnapshot> effects, string owner, out string error)
        {
            error = null;
            if (effects == null || effects.Count > MaximumEffectsPerEntity)
            {
                error = owner + "的效果列表无效。";
                return false;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in effects)
            {
                string key = effect == null ? null : effect.EffectId + "\n" + (effect.SourceId ?? string.Empty) +
                    "\n" + (effect.SourceKey ?? string.Empty);
                if (effect == null || !ValidId(effect.EffectId) || !OptionalId(effect.SourceId) || !ValidId(effect.SourceKey) ||
                    !keys.Add(key) || (effect.DurationPolicy != 0 && effect.DurationPolicy != 2) ||
                    !FiniteNonNegative(effect.TickRemaining) || effect.Stacks == null ||
                    effect.Stacks.Count > MaximumEffectStacks)
                {
                    error = owner + "的效果键、类型或计时无效。";
                    return false;
                }

                if ((effect.DurationPolicy == 0 && effect.Stacks.Count != 0) ||
                    (effect.DurationPolicy == 2 && effect.Stacks.Count == 0))
                {
                    error = owner + "的效果层数与持续类型不一致。";
                    return false;
                }

                var orders = new HashSet<long>();
                foreach (var stack in effect.Stacks)
                {
                    if (stack == null || !OptionalId(stack.SourceId) || !ValidId(stack.SourceKey) ||
                        !FiniteNonNegative(stack.RemainingDuration) ||
                        stack.RemainingDuration <= 0f || stack.Order < 1 || !orders.Add(stack.Order))
                    {
                        error = owner + "的效果层计时或顺序无效。";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool FiniteVector(Float3Snapshot value) =>
            Finite(value.X) && Finite(value.Y) && Finite(value.Z);

        private static bool FiniteRotation(Float4Snapshot value)
        {
            if (!Finite(value.X) || !Finite(value.Y) || !Finite(value.Z) || !Finite(value.W)) return false;
            double lengthSquared = (double)value.X * value.X + (double)value.Y * value.Y +
                                   (double)value.Z * value.Z + (double)value.W * value.W;
            return lengthSquared > 0.000001d && lengthSquared < 4d;
        }
    }

    public static class LayoutSaveFingerprint
    {
        public static string Compute(LayoutSaveSnapshot value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var text = new StringBuilder();
            text.Append(value.ContentVersion).Append('|')
                .Append(value.GeneratorVersion).Append('|')
                .Append(value.UsedFallback ? 1 : 0);
            if (value.Modules != null)
                foreach (LayoutModuleSaveSnapshot module in value.Modules
                             .FindAll(item => item != null)
                             .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
                {
                    text.Append('|').Append(module.InstanceId).Append(':')
                        .Append(module.DefinitionId).Append(':')
                        .Append(module.Kind).Append(':')
                        .Append(module.GridX).Append(':').Append(module.GridZ)
                        .Append(':').Append(module.QuarterTurns);
                }
            if (value.Connections != null)
                foreach (LayoutConnectionSaveSnapshot connection in value.Connections
                             .FindAll(item => item != null)
                             .OrderBy(item => item.FromInstanceId, StringComparer.Ordinal)
                             .ThenBy(item => item.ToInstanceId, StringComparer.Ordinal))
                {
                    text.Append('|').Append(connection.FromInstanceId).Append(':')
                        .Append(connection.FromSocketId).Append('>')
                        .Append(connection.ToInstanceId).Append(':')
                        .Append(connection.ToSocketId);
                }
            ulong hash = 14695981039346656037UL;
            foreach (byte octet in Encoding.UTF8.GetBytes(text.ToString()))
            {
                hash ^= octet;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }
}
