using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue51RewardAndStatisticsRestoreTests
    {
        [Test]
        public void ProgressionRestoreIsSilentAndPreservesRewardedSpawnLedger()
        {
            var player = new GameObject("Issue51 Progression");

            try
            {
                PlayerRunProgression progression =
                    player.AddComponent<PlayerRunProgression>();
                progression.ConfigureThresholds(new[] { 100, 200 });
                int progressEvents = 0;
                int levelEvents = 0;
                progression.ProgressChanged += _ => progressEvents++;
                progression.LevelsGained += _ => levelEvents++;
                var snapshot = new RunProgressionRestoreSnapshot
                {
                    Progress = new RunExperienceSnapshot(2, 50, 200, 150, 1),
                    RewardedSpawnIds = new List<int> { 4, 9 },
                    RewardedKillCount = 2
                };

                Assert.That(
                    progression.TryRestoreSnapshot(snapshot, out string error),
                    Is.True,
                    error);
                Assert.That(progression.CurrentProgress.Level, Is.EqualTo(2));
                Assert.That(progression.CurrentProgress.CurrentExperience,
                    Is.EqualTo(50));
                Assert.That(progression.RewardedKillCount, Is.EqualTo(2));
                Assert.That(progression.CaptureRestoreSnapshot().RewardedSpawnIds,
                    Is.EqualTo(new[] { 4, 9 }));
                Assert.That(progressEvents, Is.Zero);
                Assert.That(levelEvents, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void InvalidProgressionRestoreDoesNotMutateCurrentState()
        {
            var player = new GameObject("Issue51 Invalid Progression");

            try
            {
                PlayerRunProgression progression =
                    player.AddComponent<PlayerRunProgression>();
                progression.ConfigureThresholds(new[] { 100, 200 });
                RunProgressionRestoreSnapshot before =
                    progression.CaptureRestoreSnapshot();
                var invalid = new RunProgressionRestoreSnapshot
                {
                    Progress = new RunExperienceSnapshot(2, 50, 200, 150, 1),
                    RewardedSpawnIds = new List<int> { 5, 5 },
                    RewardedKillCount = 2
                };

                Assert.That(progression.TryRestoreSnapshot(invalid, out _), Is.False);
                Assert.That(progression.CurrentProgress.TotalExperience,
                    Is.EqualTo(before.Progress.TotalExperience));
                Assert.That(progression.RewardedKillCount,
                    Is.EqualTo(before.RewardedKillCount));
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void LootRestorePreservesStableLedgersWithoutSettlingRewards()
        {
            var player = new GameObject("Issue51 Loot");
            LootDropTableDefinition table =
                ScriptableObject.CreateInstance<LootDropTableDefinition>();

            try
            {
                table.Configure(System.Array.Empty<LootDropRule>());
                PlayerLootRewardController rewards =
                    player.AddComponent<PlayerLootRewardController>();
                rewards.ConfigureForTesting(table, 77, 3);
                int settledEvents = 0;
                rewards.RewardSettled += _ => settledEvents++;
                var snapshot = new LootRewardRestoreSnapshot
                {
                    RunSeed = 77,
                    ConfiguredTotalWaves = 3,
                    ProcessedSpawnIds = new List<int> { 7, 11 },
                    RewardedWaves = new List<int> { 1 },
                    EnemySettlementCount = 2,
                    WaveRewardCount = 1,
                    FinalRewardCount = 0,
                    SpawnedStackCount = 0,
                    FinalRewardRequested = false,
                    AcceptingRewards = true
                };

                Assert.That(
                    rewards.TryRestoreSnapshot(snapshot, out string error),
                    Is.True,
                    error);
                Assert.That(settledEvents, Is.Zero);
                Assert.That(rewards.ProcessEnemyDeath(Death(7)), Is.False);
                Assert.That(rewards.ProcessWaveEnded(1), Is.False);
                Assert.That(rewards.ProcessEnemyDeath(Death(12)), Is.True);
                Assert.That(rewards.EnemySettlementCount, Is.EqualTo(3));
                Assert.That(rewards.TryCaptureSnapshot(
                    out LootRewardRestoreSnapshot captured,
                    out error), Is.True, error);
                Assert.That(captured.ProcessedSpawnIds,
                    Is.EqualTo(new[] { 7, 11, 12 }));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void StatisticsRestoreIsAtomicAndPreservesAuthoritativeKillLedger()
        {
            var statistics = new MissionRunStatistics();
            var valid = new MissionRunStatisticsSnapshot
            {
                ShotsFired = 20,
                Hits = 13,
                Kills = 2,
                CompletedWaves = 1,
                DamageTakenCount = 3,
                DamageTakenAmount = 42.5f,
                ElapsedSeconds = 91f,
                AuthoritativeKillTracking = true,
                RewardedSpawnIds = new List<int> { 3, 8 }
            };

            Assert.That(
                statistics.TryRestoreSnapshot(valid, out string error),
                Is.True,
                error);
            MissionRunStatisticsSnapshot captured = statistics.CaptureSnapshot();
            Assert.That(captured.ShotsFired, Is.EqualTo(20));
            Assert.That(captured.Hits, Is.EqualTo(13));
            Assert.That(captured.RewardedSpawnIds, Is.EqualTo(new[] { 3, 8 }));

            var invalid = new MissionRunStatisticsSnapshot
            {
                ShotsFired = 5,
                Hits = 6,
                Kills = 0,
                RewardedSpawnIds = new List<int>()
            };
            Assert.That(statistics.TryRestoreSnapshot(invalid, out _), Is.False);
            Assert.That(statistics.ShotsFired, Is.EqualTo(20));
            Assert.That(statistics.Kills, Is.EqualTo(2));
        }

        private static EnemyDeathEvent Death(int spawnId)
        {
            return new EnemyDeathEvent(
                null,
                spawnId,
                2,
                default,
                10,
                Vector3.zero,
                "spider",
                LootRewardTier.Normal);
        }
    }
}
