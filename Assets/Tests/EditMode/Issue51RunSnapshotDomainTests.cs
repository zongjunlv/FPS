using System.Collections.Generic;
using FPS.SaveGame;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue51RunSnapshotDomainTests
    {
        [Test]
        public void VersionTwoRoundTripPreservesDeepRuntimeState()
        {
            RunSnapshot original = Example();
            string json = RunSnapshotCodec.Serialize(original);

            Assert.That(RunSnapshotCodec.TryDeserialize(json, out RunSnapshot loaded, out string error), Is.True, error);
            Assert.That(loaded.SchemaVersion, Is.EqualTo(2));
            Assert.That(loaded.InventorySlots[1].ItemId, Is.Null.Or.Empty);
            Assert.That(loaded.QuickSlots[0].ItemId, Is.EqualTo("medkit"));
            Assert.That(loaded.SelectedQuickSlotIndex, Is.EqualTo(0));
            Assert.That(loaded.Mission.TerminalProgressNormalized, Is.EqualTo(0.4f));
            Assert.That(loaded.Wave.ActiveIds, Is.EqualTo(new[] { 11 }));
            Assert.That(loaded.Enemies[0].Position.Z, Is.EqualTo(9f));
            Assert.That(loaded.Enemies[0].Effects[0].Stacks[0].RemainingDuration, Is.EqualTo(2.5f));
            Assert.That(loaded.PlayerEffects[0].SourceId, Is.EqualTo("upgrade:vitality"));
            Assert.That(loaded.Checksum, Is.EqualTo(SnapshotChecksum.Compute(loaded)));
        }

        [Test]
        public void ChecksumCoversNestedVersionTwoFields()
        {
            RunSnapshot snapshot = Example();
            string checksum = SnapshotChecksum.Compute(snapshot);
            snapshot.Enemies[0].Effects[0].Stacks[0].RemainingDuration -= 0.25f;
            Assert.That(SnapshotChecksum.Compute(snapshot), Is.Not.EqualTo(checksum));

            checksum = SnapshotChecksum.Compute(snapshot);
            snapshot.Wave.ActiveIds[0] = 99;
            Assert.That(SnapshotChecksum.Compute(snapshot), Is.Not.EqualTo(checksum));
        }

        [Test]
        public void JsonRoundTripKeepsChecksumWhenRuntimeContainsNegativeZero()
        {
            RunSnapshot original = Example();
            original.Enemies[0].Position.X = -0f;
            original.Enemies[0].Rotation.Z = -0f;
            original.Wave.SpawnCooldownRemaining = -0f;
            original.Checksum = SnapshotChecksum.Compute(original);

            string json = RunSnapshotCodec.Serialize(original);

            Assert.That(
                RunSnapshotCodec.TryDeserialize(
                    json,
                    out RunSnapshot loaded,
                    out string error),
                Is.True,
                error);
            Assert.That(
                SnapshotChecksum.Compute(loaded),
                Is.EqualTo(original.Checksum));
        }

        [Test]
        public void ValidationRejectsNonFiniteCapacityAndEntityKeyViolations()
        {
            RunSnapshot snapshot = Example();
            snapshot.Enemies[0].Position.X = float.NaN;
            Assert.That(SnapshotValidation.TryValidate(snapshot, out _), Is.False);

            snapshot = Example();
            snapshot.InventorySlots = new List<FPS.SaveGame.InventorySlotSnapshot>();
            for (int index = 0; index <= SnapshotValidation.MaximumInventorySlots; index++)
                snapshot.InventorySlots.Add(new FPS.SaveGame.InventorySlotSnapshot { SlotIndex = index });
            Assert.That(SnapshotValidation.TryValidate(snapshot, out _), Is.False);

            snapshot = Example();
            snapshot.Enemies.Add(snapshot.Enemies[0]);
            Assert.That(SnapshotValidation.TryValidate(snapshot, out string error), Is.False);
            Assert.That(error, Does.Contain("敌人"));
        }

        [Test]
        public void ValidationRequiresWaveActiveIdsToExactlyMatchEnemies()
        {
            RunSnapshot snapshot = Example();
            snapshot.Wave.ActiveIds.Clear();
            Assert.That(SnapshotValidation.TryValidate(snapshot, out string error), Is.False);
            Assert.That(error, Does.Contain("标识").Or.Contain("划分"));
        }

        private static RunSnapshot Example()
        {
            return new RunSnapshot
            {
                Seed = 51,
                Health = 80f,
                Armor = 12f,
                CurrentWeaponId = "rifle",
                Weapons = new List<WeaponAmmoSnapshot>
                {
                    new WeaponAmmoSnapshot { WeaponId = "rifle", Magazine = 20, Reserve = 100 }
                },
                Upgrades = new List<UpgradeLevelSnapshot>
                {
                    new UpgradeLevelSnapshot { UpgradeId = "vitality", Level = 1 }
                },
                UpgradeSelectionHistory = new List<string> { "vitality" },
                InventorySlots = new List<FPS.SaveGame.InventorySlotSnapshot>
                {
                    new FPS.SaveGame.InventorySlotSnapshot { SlotIndex = 0, ItemId = "medkit", Quantity = 2 },
                    new FPS.SaveGame.InventorySlotSnapshot { SlotIndex = 1, ItemId = null, Quantity = 0 }
                },
                QuickSlots = new List<FPS.SaveGame.QuickSlotSnapshot>
                {
                    new FPS.SaveGame.QuickSlotSnapshot { SlotIndex = 0, ItemId = "medkit" },
                    new FPS.SaveGame.QuickSlotSnapshot { SlotIndex = 1, ItemId = null }
                },
                SelectedQuickSlotIndex = 0,
                Mission = new MissionSnapshot
                {
                    Phase = 1,
                    TerminalProgressNormalized = 0.4f,
                    RequiredTargets = 2,
                    EliminatedTargets = 1,
                    ExperienceLevel = 2,
                    ExperienceToNextLevel = 150,
                    CurrentExperience = 5,
                    TotalExperience = 105,
                    LevelUpCount = 1,
                    RewardedKillCount = 1,
                    ProgressionRewardedSpawnIds = new List<int> { 12 },
                    LootProcessedSpawnIds = new List<int> { 12 },
                    EnemyRewardCount = 1,
                    StatisticsAuthoritativeKillTracking = true,
                    StatisticsRewardedSpawnIds = new List<int> { 12 },
                    StatisticsKills = 1,
                    StatisticsDamage = 42f,
                    ElapsedSeconds = 16f
                },
                Wave = new WaveSnapshot
                {
                    CurrentWave = 1,
                    Phase = 2,
                    TotalEnemyCount = 2,
                    MaximumAliveCount = 2,
                    SpawnedIds = new List<int> { 11, 12 },
                    ActiveIds = new List<int> { 11 },
                    SettledIds = new List<int> { 12 },
                    SpawnCooldownRemaining = 0.2f,
                    NextSpawnId = 13,
                    RemainingThreatBudget = 4
                },
                Enemies = new List<EnemySnapshot>
                {
                    new EnemySnapshot
                    {
                        WaveNumber = 1,
                        SpawnId = 11,
                        EnemyTypeId = "raider",
                        Position = new Float3Snapshot(1f, 2f, 9f),
                        Rotation = new Float4Snapshot(0f, 0.7f, 0f, 0.7f),
                        Health = 25f,
                        Armor = 3f,
                        AwarenessState = 1,
                        AttackState = 2,
                        Effects = new List<GameplayEffectSnapshot>
                        {
                            new GameplayEffectSnapshot
                            {
                                EffectId = "burn",
                                SourceId = "player",
                                SourceKey = "player",
                                DurationPolicy = 2,
                                TickRemaining = 0.5f,
                                Stacks = new List<GameplayEffectStackSnapshot>
                                {
                                    new GameplayEffectStackSnapshot
                                    {
                                        SourceId = "player", SourceKey = "player", RemainingDuration = 2.5f, Order = 7
                                    }
                                }
                            }
                        }
                    }
                },
                PlayerEffects = new List<GameplayEffectSnapshot>
                {
                    new GameplayEffectSnapshot
                    {
                        EffectId = "max-health",
                        SourceId = "upgrade:vitality",
                        SourceKey = "player",
                        DurationPolicy = 0
                    }
                }
            };
        }
    }
}
