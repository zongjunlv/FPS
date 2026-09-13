using System;
using System.Collections.Generic;

namespace FPS.Simulation.Offline
{
    internal static class OfflineResultDigest
    {
        public static string Compute(OfflineRunResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            var hash = new StableHash64();
            hash.Add(result.ScenarioId);
            hash.Add(result.ContentVersion);
            hash.Add(result.RulesVersion);
            hash.Add(result.WorldModelVersion);
            hash.Add(result.EnemyAttackOpportunityBasisPoints);
            hash.Add(result.Seed);
            hash.Add((int)result.Outcome);
            hash.Add((int)result.FailureReason);
            hash.Add(result.FailureDetail);
            hash.Add(result.DurationTicks);
            hash.Add(result.FixedTickRate);
            hash.Add(result.AverageTtkTicks);
            hash.Add(result.EnemyTtkTicks.Count);
            for (int index = 0; index < result.EnemyTtkTicks.Count; index++)
                hash.Add(result.EnemyTtkTicks[index]);
            hash.Add(result.HealthLost);
            hash.Add(result.ArmorLost);
            hash.Add(result.AmmoConsumed);
            hash.Add(result.AmmoSupplied);
            AddWaves(hash, result.Waves);
            AddCounts(hash, result.WaveDistribution);
            AddCounts(hash, result.RoleDistribution);
            AddCounts(hash, result.EncounterDistribution);
            AddCounts(hash, result.CombatRuleTriggerDistribution);
            AddUpgrades(hash, result.UpgradeChoices);
            AddDirectorEvents(hash, result.DirectorEvents);
            AddEncounterEvents(hash, result.EncounterEvents);
            AddCombatRuleEvents(hash, result.CombatRuleEvents);
            AddCommands(hash, result.Commands);
            return hash.Value.ToString("x16");
        }

        public static string ComputeBatch(
            string scenarioId,
            int contentVersion,
            string rulesVersion,
            IReadOnlyList<OfflineRunResult> results)
        {
            var hash = new StableHash64();
            hash.Add(scenarioId);
            hash.Add(contentVersion);
            hash.Add(rulesVersion);
            hash.Add(results?.Count ?? 0);
            if (results != null)
            {
                for (int index = 0; index < results.Count; index++)
                {
                    hash.Add(results[index].Seed);
                    hash.Add(results[index].Digest);
                }
            }
            return hash.Value.ToString("x16");
        }

        private static void AddWaves(
            StableHash64 hash,
            IReadOnlyList<OfflineWaveResult> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                OfflineWaveResult value = values[index];
                hash.Add(value.WaveNumber);
                hash.Add(value.SpawnedCount);
                hash.Add(value.DefeatedCount);
                hash.Add(value.DurationTicks);
                hash.Add(value.AverageTtkTicks);
                AddCounts(hash, value.RoleDistribution);
            }
        }

        private static void AddCounts(
            StableHash64 hash,
            IReadOnlyList<OfflineNamedCount> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                hash.Add(values[index].Id);
                hash.Add(values[index].Count);
            }
        }

        private static void AddUpgrades(
            StableHash64 hash,
            IReadOnlyList<OfflineUpgradeChoice> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                hash.Add(values[index].AfterWave);
                hash.Add(values[index].UpgradeId);
            }
        }

        private static void AddDirectorEvents(
            StableHash64 hash,
            IReadOnlyList<OfflineDirectorEvent> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                OfflineDirectorEvent value = values[index];
                hash.Add(value.Tick);
                hash.Add((int)value.Signal);
                hash.Add(value.EventId);
                hash.Add(value.EnemyTypeId);
                hash.Add(value.RoleTag);
                hash.Add(value.RequestedCount);
            }
        }

        private static void AddEncounterEvents(
            StableHash64 hash,
            IReadOnlyList<OfflineEncounterEvent> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                OfflineEncounterEvent value = values[index];
                hash.Add(value.Tick);
                hash.Add(value.EncounterId);
                hash.Add((int)value.Signal);
                hash.Add((int)value.Phase);
                hash.Add(value.Progress);
                hash.Add(value.Target);
            }
        }

        private static void AddCombatRuleEvents(
            StableHash64 hash,
            IReadOnlyList<OfflineCombatRuleEvent> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                OfflineCombatRuleEvent value = values[index];
                hash.Add(value.EventId);
                hash.Add(value.Tick);
                hash.Add((int)value.Trigger);
                hash.Add(value.RuleId);
                hash.Add(value.SourceId);
                hash.Add(value.TargetId);
            }
        }

        private static void AddCommands(
            StableHash64 hash,
            IReadOnlyList<SimulationCommand> values)
        {
            hash.Add(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                SimulationCommand value = values[index];
                hash.Add(value.Tick);
                hash.Add((int)value.Type);
                hash.Add(value.EntityId);
                hash.Add(value.PrimaryValue);
                hash.Add(value.SecondaryValue);
            }
        }
    }

    internal sealed class StableHash64
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong value = Offset;

        public ulong Value => value;

        public void Add(string text)
        {
            if (text == null)
            {
                Add(-1);
                return;
            }
            Add(text.Length);
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                AddByte((byte)character);
                AddByte((byte)(character >> 8));
            }
        }

        public void Add(int number)
        {
            Add(unchecked((uint)number));
        }

        public void Add(long number)
        {
            Add(unchecked((ulong)number));
        }

        public void Add(float number)
        {
            byte[] bits = BitConverter.GetBytes(number);
            uint canonical = BitConverter.IsLittleEndian
                ? (uint)(bits[0] |
                    bits[1] << 8 |
                    bits[2] << 16 |
                    bits[3] << 24)
                : (uint)(bits[3] |
                    bits[2] << 8 |
                    bits[1] << 16 |
                    bits[0] << 24);
            Add(canonical);
        }

        private void Add(uint number)
        {
            AddByte((byte)number);
            AddByte((byte)(number >> 8));
            AddByte((byte)(number >> 16));
            AddByte((byte)(number >> 24));
        }

        private void Add(ulong number)
        {
            for (int shift = 0; shift < 64; shift += 8)
                AddByte((byte)(number >> shift));
        }

        private void AddByte(byte number)
        {
            value ^= number;
            value *= Prime;
        }
    }
}
