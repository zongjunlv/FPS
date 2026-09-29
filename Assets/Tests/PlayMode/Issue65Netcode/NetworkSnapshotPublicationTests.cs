using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class NetworkSnapshotPublicationTests
    {
        private OptionalNetworkBootstrap bootstrap;
        private NetworkCoopSessionAuthority authority;
        private GameObject localReplicaObject;

        [TestCase("enemy.archetype.spider_raider", "enemy/spider", 184)]
        [TestCase("enemy.archetype.spider_suppressor", "enemy/eye-drone-suppressor", 202)]
        public void TargetPayloadByteCostUsesActualSerializer(
            string archetype, string address, int expectedBytes)
        {
            var state = new NetcodeTargetState
            {
                ArchetypeId = archetype,
                PresentationAddress = address,
                DropDefinitionId = "handgun_ammo"
            };
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteNetworkSerializable(state);
            Assert.That(writer.Length, Is.EqualTo(expectedBytes),
                "核对真实wire serializer，不使用结构体内存布局估算网络字节。");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (localReplicaObject != null) Object.Destroy(localReplicaObject);
            localReplicaObject = null;
            if (bootstrap != null)
            {
                bootstrap.Shutdown();
                for (int frame = 0; frame < 30 && bootstrap.IsListening; frame++)
                    yield return null;
                Object.Destroy(bootstrap.gameObject);
            }
            bootstrap = null;
            authority = null;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoritySixtyHzSimulationCommitsTwentyAtomicSnapshotsAndKeepsLatestTick()
        {
            yield return StartRealAuthority();
            long previousCommit = authority.WorldState.ServerTick;
            int commitCount = 0;
            for (int tick = 1; tick <= 60; tick++)
            {
                AuthoritativeTickResult result = authority.ServerStep();
                Assert.That(result.Snapshot.Tick, Is.EqualTo(tick));
                Assert.That(authority.LastAuthoritativeSnapshot.Tick,
                    Is.EqualTo(tick), "发布节流不得冻结实时权威状态。");
                if (authority.WorldState.ServerTick != previousCommit)
                {
                    commitCount++;
                    previousCommit = authority.WorldState.ServerTick;
                    Assert.That(previousCommit, Is.EqualTo(tick));
                    Assert.That(authority.IsReplicatedSnapshotComplete, Is.True);
                }

                for (int index = 0; index < authority.ReplicatedTargetCount; index++)
                    Assert.That(authority.GetReplicatedTarget(index).SnapshotTick,
                        Is.EqualTo(previousCommit),
                        "未提交世界帧时不能持续往目标列表积累60Hz脏事件。");
                Assert.That(authority.TryGetPlayerState(1,
                    out NetcodePlayerState player), Is.True);
                Assert.That(player.ServerTick, Is.EqualTo(previousCommit),
                    "玩家/敌人应与同一个世界提交Tick对齐。");
            }

            Assert.That(commitCount, Is.EqualTo(20),
                "60Hz权威模拟应只提交20Hz最新完整世界帧。");
            Assert.That(authority.WorldState.ServerTick, Is.EqualTo(60));
            Assert.That(authority.LastAuthoritativeSnapshot.Tick, Is.EqualTo(60));
        }

        [UnityTest]
        public IEnumerator AuthorityShotFeedbackRemainsImmediateBetweenSnapshotCommits()
        {
            yield return StartRealAuthority();
            authority.RegisterPlayerClient(10, 1);
            localReplicaObject = new GameObject("Snapshot cadence replica consumer");
            NetworkPlayerReplica replica = localReplicaObject.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState baseline), Is.True);
            replica.ConsumeServerState(baseline, true, 0d);
            int consumedFeedback = 0;
            replica.ShotFeedbackReceived += _ => consumedFeedback++;
            Assert.That(authority.TryQueueCommand(10, Shot(1), true), Is.True);
            authority.ServerStep();
            var feedback = new List<NetcodeShotFeedbackEvent>();
            authority.GetShotEventsAfter(1, 0, feedback);
            Assert.That(feedback.Count, Is.EqualTo(1));
            Assert.That(feedback.Single().Accepted, Is.True);
            Assert.That(feedback.Single().ServerTick, Is.EqualTo(1));
            Assert.That(authority.LastAuthoritativeSnapshot.Player(1).MagazineAmmo,
                Is.EqualTo(49));
            Assert.That(authority.WorldState.ServerTick, Is.Zero,
                "开火反馈不应迫使全部目标重新做60Hz世界发布。");
            replica.ConsumeServerState(baseline, true, 0d);
            Assert.That(consumedFeedback, Is.EqualTo(1),
                "实际Replica消费不应等待新的世界提交Tick。");
            Assert.That(replica.PresentedMagazineAmmo, Is.EqualTo(49));

            Assert.That(authority.TryQueueCommand(10, Shot(2), true), Is.True);
            authority.ServerStep();
            authority.GetShotEventsAfter(1, 0, feedback);
            Assert.That(feedback.Count, Is.EqualTo(2));
            Assert.That(feedback[1].RejectionReason,
                Is.EqualTo(CommandRejectionReason.FireRateExceeded));
            Assert.That(feedback[1].ShotCommandSequence, Is.EqualTo(2));
            Assert.That(feedback[1].HasAmmoState, Is.True);
            Assert.That(feedback[1].MagazineAmmo, Is.EqualTo(49));
            Assert.That(authority.WorldState.ServerTick, Is.Zero);
            replica.ConsumeServerState(baseline, true, 0d);
            Assert.That(consumedFeedback, Is.EqualTo(2));
            Assert.That(replica.PresentedMagazineAmmo, Is.EqualTo(49),
                "重复读取旧完整快照不能覆盖更新的射击弹药对账。");

            authority.ServerStep();
            Assert.That(authority.WorldState.ServerTick, Is.EqualTo(3));
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState committed), Is.True);
            Assert.That(committed.ServerTick, Is.EqualTo(3));
            Assert.That(committed.MagazineAmmo, Is.EqualTo(49));
            replica.ConsumeServerState(committed, true, 3d);
            Assert.That(consumedFeedback, Is.EqualTo(2));
            authority.GetShotEventsAfter(1, 0, feedback);
            Assert.That(feedback.Count, Is.EqualTo(2),
                "世界帧提交不能重复发布已确认或已拒绝的射击。");
        }

        [UnityTest]
        public IEnumerator RestartClearsRealScheduledCopiesFromThePreviousRun()
        {
            yield return StartRealAuthority();
            authority.RegisterPlayerClient(10, 1);
            Assert.That(authority.TryQueueCommand(10, Shot(1), true), Is.True);
            authority.ServerStep();
            Assert.That(authority.PendingFastShotCopyCount, Is.EqualTo(1),
                "真实已Spawn的authority应为本枪安排有界UDP副本。");
            authority.ApplyServerDamageToPlayer(1, 1000d);
            Assert.That(authority.WorldState.MissionPhase, Is.EqualTo(AuthoritativeMissionPhase.Defeat));
            Assert.That(authority.TryRestartMission(10), Is.True);
            Assert.That(authority.WorldState.RunGeneration, Is.EqualTo(2));
            Assert.That(authority.PendingFastShotCopyCount, Is.Zero,
                "重开不能将上一局未到期副本重新贴成新局反馈。");
            for (int tick = 0; tick < 8; tick++) authority.ServerStep();
            Assert.That(authority.PendingFastShotCopyCount, Is.Zero);
        }

        private IEnumerator StartRealAuthority()
        {
            bootstrap = OptionalNetworkBootstrap.CreateRuntime(
                new NetworkEndpointSettings
                {
                    Address = "127.0.0.1",
                    ListenAddress = "127.0.0.1",
                    Port = 17984,
                    TickRate = 60
                });
            var installer = bootstrap.gameObject.AddComponent<CoopNetworkRuntimeInstaller>();
            NetworkObject authorityPrefab = Resources.Load<GameObject>(
                "Networking/CoopSessionAuthority")?.GetComponent<NetworkObject>();
            NetworkObject replicaPrefab = Resources.Load<GameObject>(
                "Networking/CoopPlayerReplica")?.GetComponent<NetworkObject>();
            Assert.That(authorityPrefab, Is.Not.Null);
            Assert.That(replicaPrefab, Is.Not.Null);
            installer.ConfigurePrefabs(authorityPrefab, replicaPrefab);
            Assert.That(installer.RegisterConfiguredPrefabs(), Is.True,
                installer.LastFailure);
            Assert.That(bootstrap.StartServer(), Is.True, bootstrap.LastFailure);
            yield return null;
            Assert.That(installer.SessionAuthority, Is.Not.Null, installer.LastFailure);
            authority = installer.SessionAuthority;
            authority.AutoSimulate = false;
            Assert.That(authority.IsSpawned, Is.True,
                "必须测试真实NetworkList写入分支，而非离线测试钩子。");
            CoopTargetSpawn[] targets = Enumerable.Range(1, 22).Select(index =>
                new CoopTargetSpawn(index, new NetVector3(0d, 0d, 10d + index),
                    0.5d, 1000d, spawnTick: index <= 4 ? 0 : 600,
                    moveSpeed: 0d)).ToArray();
            authority.ConfigureServer(new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 6),
                new[] { new CoopPlayerSpawn(1, default) }, targets, 22);
            Assert.That(authority.WorldState.ServerTick, Is.Zero);
            Assert.That(authority.ReplicatedTargetCount, Is.EqualTo(22));
        }

        private static NetcodePlayerCommand Shot(uint sequence) =>
            NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                1, sequence, sequence, sequence, 0d, 0d, 0d, 0d,
                true, default, false, false, false,
                NetworkPresentationIds.RifleGameplay, default));
    }
}
