using System;
using System.Collections.Generic;
using System.Linq;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue66EnemySnapshotIdentityTests
    {
        private readonly List<UnityEngine.Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = owned.Count - 1; index >= 0; index--)
            {
                if (owned[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(owned[index]);
                }
            }
            owned.Clear();
        }

        [Test]
        public void StableArchetypeIdRestoresExactEntryWhenTypeIdsCollide()
        {
            EnemyArchetypeDefinition assault = Archetype(
                "enemy.archetype.trilobite.assault",
                LootRewardTier.Normal);
            EnemyArchetypeDefinition elite = Archetype(
                "enemy.archetype.quad-shell.elite",
                LootRewardTier.Elite);
            WaveDirector director = Director(
                assault,
                elite,
                out RecordingEnemyFactory factory);

            WaveRuntimeSnapshot snapshot = RuntimeSnapshot(
                assault.StableId,
                elite.StableId);
            Assert.That(
                director.TryRestoreRuntimeState(snapshot, out string error),
                Is.True,
                error);
            Assert.That(factory.SpawnedArchetypeIds, Is.EqualTo(new[]
            {
                assault.StableId,
                elite.StableId
            }));

            WaveRuntimeSnapshot captured = director.CaptureRuntimeState();
            Assert.That(
                captured.Enemies.Select(enemy => enemy.ArchetypeStableId),
                Is.EqualTo(new[] { assault.StableId, elite.StableId }));
        }

        [Test]
        public void LegacySnapshotWithoutArchetypeIdFallsBackToEnemyTypeId()
        {
            EnemyArchetypeDefinition assault = Archetype(
                "enemy.archetype.trilobite.assault",
                LootRewardTier.Normal);
            EnemyArchetypeDefinition elite = Archetype(
                "enemy.archetype.quad-shell.elite",
                LootRewardTier.Elite);
            WaveDirector director = Director(
                assault,
                elite,
                out RecordingEnemyFactory factory);

            Assert.That(
                director.TryRestoreRuntimeState(
                    RuntimeSnapshot(null, null),
                    out string error),
                Is.True,
                error);
            Assert.That(factory.SpawnedArchetypeIds, Is.EqualTo(new[]
            {
                assault.StableId,
                assault.StableId
            }), "旧存档继续使用既有 EnemyTypeId 首项回退语义。");
        }

        [Test]
        public void ArchetypeIdentityParticipatesInChecksumButNullKeepsLegacyDigest()
        {
            var snapshot = new RunSnapshot();
            var enemy = new EnemySnapshot
            {
                WaveNumber = 1,
                SpawnId = 1,
                EnemyTypeId = "spider_bot",
                ArchetypeStableId = null
            };
            snapshot.Enemies.Add(enemy);

            string legacy = SnapshotChecksum.Compute(snapshot);
            enemy.ArchetypeStableId = "enemy.archetype.trilobite.assault";
            string assault = SnapshotChecksum.Compute(snapshot);
            enemy.ArchetypeStableId = "enemy.archetype.quad-shell.elite";
            string elite = SnapshotChecksum.Compute(snapshot);
            enemy.ArchetypeStableId = null;

            Assert.That(assault, Is.Not.EqualTo(legacy));
            Assert.That(elite, Is.Not.EqualTo(assault));
            Assert.That(SnapshotChecksum.Compute(snapshot), Is.EqualTo(legacy),
                "缺少新字段时不得改变旧 schema-v2 存档校验流。");
        }

        [Test]
        public void SnapshotCodecPersistsIdentityAndOmitsItForLegacyPayload()
        {
            RunSnapshot snapshot = SaveSnapshot(
                "enemy.archetype.quad-shell.elite");
            string json = RunSnapshotCodec.Serialize(snapshot);

            Assert.That(json, Does.Contain("ArchetypeStableId"));
            Assert.That(
                RunSnapshotCodec.TryDeserialize(
                    json,
                    out RunSnapshot loaded,
                    out string error),
                Is.True,
                error);
            Assert.That(
                loaded.Enemies[0].ArchetypeStableId,
                Is.EqualTo("enemy.archetype.quad-shell.elite"));

            RunSnapshot legacy = SaveSnapshot(null);
            string legacyJson = RunSnapshotCodec.Serialize(legacy);
            Assert.That(legacyJson, Does.Not.Contain("ArchetypeStableId"));
            Assert.That(
                RunSnapshotCodec.TryDeserialize(
                    legacyJson,
                    out loaded,
                    out error),
                Is.True,
                error);
            Assert.That(loaded.Enemies[0].ArchetypeStableId, Is.Null);
        }

        [Test]
        public void PresentButUnknownArchetypeIdDoesNotSilentlyRestoreWrongModel()
        {
            EnemyArchetypeDefinition assault = Archetype(
                "enemy.archetype.trilobite.assault",
                LootRewardTier.Normal);
            EnemyArchetypeDefinition elite = Archetype(
                "enemy.archetype.quad-shell.elite",
                LootRewardTier.Elite);
            WaveDirector director = Director(
                assault,
                elite,
                out RecordingEnemyFactory factory);

            Assert.That(
                director.TryRestoreRuntimeState(
                    RuntimeSnapshot("enemy.archetype.missing", elite.StableId),
                    out string error),
                Is.False);
            Assert.That(error, Does.Contain("identity"));
            Assert.That(factory.SpawnedArchetypeIds, Is.Empty);
        }

        private EnemyArchetypeDefinition Archetype(
            string stableId,
            LootRewardTier rewardTier)
        {
            var value = ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();
            value.Configure(
                stableId,
                "spider_bot",
                null,
                rewardTier,
                null,
                null,
                rewardTier == LootRewardTier.Elite ? 5 : 2,
                rewardTier == LootRewardTier.Elite ? "elite" : "assault");
            owned.Add(value);
            return value;
        }

        private WaveDirector Director(
            EnemyArchetypeDefinition assault,
            EnemyArchetypeDefinition elite,
            out RecordingEnemyFactory factory)
        {
            var definition = ScriptableObject.CreateInstance<WaveDefinition>();
            definition.ConfigureIdentity("issue66.identity.wave");
            definition.Configure(
                2,
                2,
                0f,
                new[]
                {
                    new WaveEnemyEntry(assault),
                    new WaveEnemyEntry(elite)
                });
            owned.Add(definition);

            var player = new GameObject("Issue66 Player");
            var directorObject = new GameObject("Issue66 Wave Director");
            owned.Add(player);
            owned.Add(directorObject);
            factory = new RecordingEnemyFactory(owned);
            var director = directorObject.AddComponent<WaveDirector>();
            director.Configure(
                definition,
                factory,
                new FixedSpawnPointResolver(),
                player.transform);
            return director;
        }

        private static WaveRuntimeSnapshot RuntimeSnapshot(
            string firstStableId,
            string secondStableId)
        {
            var flow = new MultiWaveFlowStateSnapshot(
                1,
                WaveRunPhase.Fighting,
                new SingleWaveStateSnapshot(
                    2,
                    2,
                    new[] { 1, 2 },
                    new[] { 1, 2 },
                    Array.Empty<int>()),
                0f);
            return new WaveRuntimeSnapshot(
                flow,
                0f,
                3,
                new[]
                {
                    Enemy(1, firstStableId),
                    Enemy(2, secondStableId)
                });
        }

        private static EnemyRuntimeSnapshot Enemy(int spawnId, string stableId)
        {
            return new EnemyRuntimeSnapshot(
                1,
                spawnId,
                "spider_bot",
                new Vector3(spawnId, 0f, 0f),
                Quaternion.identity,
                100f,
                0f,
                null,
                stableId);
        }

        private static RunSnapshot SaveSnapshot(string stableId)
        {
            return new RunSnapshot
            {
                Seed = 6601,
                Health = 100f,
                Armor = 0f,
                CurrentWeaponId = "rifle",
                Weapons = new List<WeaponAmmoSnapshot>
                {
                    new()
                    {
                        WeaponId = "rifle",
                        Magazine = 30,
                        Reserve = 90
                    }
                },
                Wave = new WaveSnapshot
                {
                    CurrentWave = 1,
                    Phase = (int)WaveRunPhase.Fighting,
                    TotalEnemyCount = 1,
                    MaximumAliveCount = 1,
                    SpawnedIds = new List<int> { 1 },
                    ActiveIds = new List<int> { 1 },
                    SettledIds = new List<int>(),
                    NextSpawnId = 2
                },
                Enemies = new List<EnemySnapshot>
                {
                    new()
                    {
                        WaveNumber = 1,
                        SpawnId = 1,
                        EnemyTypeId = "spider_bot",
                        ArchetypeStableId = stableId,
                        Position = new Float3Snapshot(1f, 0f, 0f),
                        Rotation = Float4Snapshot.Identity,
                        Health = 100f,
                        Armor = 0f,
                        Effects = new List<GameplayEffectSnapshot>()
                    }
                }
            };
        }

        private sealed class FixedSpawnPointResolver : IEnemySpawnPointResolver
        {
            public void Configure(WaveDefinition definition) { }

            public bool TryResolve(
                Transform player,
                IReadOnlyList<Vector3> occupiedPositions,
                out Vector3 spawnPoint)
            {
                spawnPoint = Vector3.zero;
                return true;
            }
        }

        private sealed class RecordingEnemyFactory : IEnemyFactory
        {
            private readonly List<UnityEngine.Object> owned;

            public RecordingEnemyFactory(List<UnityEngine.Object> owned)
            {
                this.owned = owned;
            }

            public List<string> SpawnedArchetypeIds { get; } = new();

            public bool TrySpawn(
                EnemySpawnRequest request,
                Action<EnemySpawnHandle, EnemyExitReason> onEnded,
                out EnemySpawnHandle handle)
            {
                SpawnedArchetypeIds.Add(request.Entry.Archetype.StableId);
                var instance = new GameObject(
                    "Issue66 " + request.Entry.Archetype.StableId);
                instance.SetActive(false);
                Health health = instance.AddComponent<Health>();
                health.Initialize(100f, 0f);
                EnemyController controller = instance.AddComponent<EnemyController>();
                WaveEnemyLifecycle lifecycle =
                    instance.AddComponent<WaveEnemyLifecycle>();
                lifecycle.Arm(
                    request.SpawnId,
                    request.WaveNumber,
                    controller,
                    request.Entry.EnemyTypeId,
                    request.Entry.RewardTier,
                    onEnded);
                handle = new EnemySpawnHandle(
                    request.SpawnId,
                    request.WaveNumber,
                    controller,
                    lifecycle,
                    request.Entry.EnemyTypeId,
                    request.Entry.RewardTier);
                owned.Add(instance);
                return true;
            }

            public void Release(EnemySpawnHandle handle)
            {
                handle.Lifecycle?.Disarm();
            }
        }
    }
}
