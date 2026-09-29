using System.Collections.Generic;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class NetworkShotFastFeedbackTests
    {
        private GameObject authorityObject;
        private GameObject replicaObject;
        private NetworkPlayerReplica replica;

        [SetUp]
        public void SetUp()
        {
            authorityObject = new GameObject("Fast feedback authority");
            var authority = authorityObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0d, 0d, 20d), 1d, 1000d) }, 1);
            authority.RegisterPlayerClient(10, 1);
            replicaObject = new GameObject("Fast feedback replica");
            replica = replicaObject.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            authority.TryGetPlayerState(1, out NetcodePlayerState state);
            replica.ConsumeServerState(state, true, state.ServerTick);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(replicaObject);
            Object.DestroyImmediate(authorityObject);
        }

        [TestCase(10)]
        [TestCase(20)]
        public void OutOfOrderFastFeedbackDoesNotSkipOlderFeedbackOrRollBackAmmo(long olderTick)
        {
            var received = new List<long>();
            replica.ShotFeedbackReceived += value => received.Add(value.Sequence);
            Assert.That(ConsumeFast(Feedback(2, 20, 48)), Is.True);
            Assert.That(ConsumeFast(Feedback(1, olderTick, 49)), Is.True);
            Assert.That(received, Is.EqualTo(new long[] { 2, 1 }));
            Assert.That(replica.PresentedMagazineAmmo, Is.EqualTo(48));
            Assert.That(replica.LastShotEventSequence, Is.Zero,
                "快速通道不能提前推进可靠日志游标，导致丢失早一枪的反馈。");
            Assert.That(replica.ConsumeShotFeedbackEvent(Feedback(1, olderTick, 49)), Is.False);
            Assert.That(replica.ConsumeShotFeedbackEvent(Feedback(2, 20, 48)), Is.False);
            Assert.That(replica.LastShotEventSequence, Is.EqualTo(2));
            Assert.That(received.Count, Is.EqualTo(2));
        }

        [Test]
        public void FastCopiesAndReliableFallbackProduceExactlyOneEffectPerShot()
        {
            int received = 0;
            replica.ShotFeedbackReceived += _ => received++;
            NetcodeShotFeedbackEvent value = Feedback(1, 10, 49);
            Assert.That(ConsumeFast(value), Is.True);
            Assert.That(ConsumeFast(value), Is.False);
            Assert.That(ConsumeFast(value), Is.False);
            Assert.That(replica.ConsumeShotFeedbackEvent(value), Is.False);
            Assert.That(ConsumeFast(value), Is.False);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(replica.ConsumeShotFeedbackEvent(Feedback(2, 20, 48)), Is.True,
                "快速副本全丢时，可靠日志仍应补回本枪反馈。");
            Assert.That(received, Is.EqualTo(2));
            replica.ResetPresentation();
            Assert.That(ConsumeFast(Feedback(3, 30, 47)), Is.False,
                "尚未建立当前战局射击基线时不得重播旧反馈。");
        }

        [Test]
        public void LateShotAtTheSameTickDoesNotOverwriteNewerCompleteAmmoState()
        {
            Assert.That(ConsumeFast(Feedback(2, 20, 48)), Is.True);
            replica.Session.TryGetPlayerState(1, out NetcodePlayerState state);
            state.ServerTick = 30;
            state.MagazineAmmo = 50;
            state.LastShotEventSequence = 2;
            replica.ConsumeServerState(state, true, 30);
            Assert.That(ConsumeFast(Feedback(3, 30, 47)), Is.True);
            Assert.That(replica.PresentedMagazineAmmo, Is.EqualTo(50),
                "完整同Tick快照包含换弹/切枪后的最终状态，迟到射击反馈只能播放效果。");
        }

        [Test]
        public void FastFeedbackWaitsForTheNewRunBaselineAfterRestart()
        {
            Assert.That(ConsumeFast(Feedback(1, 10, 49)), Is.True);
            replica.Session.ApplyServerDamageToPlayer(1, 1000d);
            Assert.That(replica.Session.TryRestartMission(10), Is.True);
            Assert.That(ConsumeFast(Feedback(2, 20, 48)), Is.False,
                "新局metadata到达不能让保留的旧局基线接受反馈。");
            replica.Session.TryGetPlayerState(1, out NetcodePlayerState state);
            replica.ConsumeServerState(state, true, state.ServerTick);
            Assert.That(ConsumeFast(Feedback(2, 20, 48)), Is.True);
        }

        private bool ConsumeFast(NetcodeShotFeedbackEvent value)
        {
            MethodInfo method = typeof(NetworkPlayerReplica).GetMethod("ConsumeFastShotFeedbackEvent");
            Assert.That(method, Is.Not.Null,
                "射击反馈需要独立于可靠世界列表的有界快速通道。");
            return (bool)method.Invoke(replica, new object[] { value });
        }

        private static NetcodeShotFeedbackEvent Feedback(long sequence, long tick, int magazine) => new()
        {
            Sequence = sequence,
            ServerTick = tick,
            ShooterPlayerId = 1,
            ShotCommandSequence = (uint)sequence,
            Kind = ShotResolutionKind.Hit,
            HasAmmoState = true,
            AmmoWeaponId = NetworkPresentationIds.RifleGameplay,
            MagazineAmmo = magazine,
            ReserveAmmo = 150
        };
    }
}
