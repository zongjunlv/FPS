using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class NetworkTimingIntegrationTests
    {
        private readonly List<GameObject> objects = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject value in objects)
                if (value != null) Object.Destroy(value);
            objects.Clear();
            yield return null;
        }

        [Test]
        public void RejectedShotStillAcknowledgesItsSequenceAndAuthoritativeAmmo()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            authority.TryQueueCommand(10, Shot(1), true);
            authority.ServerStep();
            authority.TryQueueCommand(10, Shot(2), true);
            AuthoritativeTickResult second = authority.ServerStep();
            Assert.That(second.Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.FireRateExceeded));

            var feedback = new List<NetcodeShotFeedbackEvent>();
            authority.GetShotEventsAfter(1, 0, feedback);
            Assert.That(feedback.Count, Is.EqualTo(2),
                "被服务端拒绝的开火也必须结束客户端对这条输入的等待。");
            Assert.That(feedback[1].ShotCommandSequence, Is.EqualTo(2));
            Assert.That(feedback[1].Accepted, Is.False);
            Assert.That(feedback[1].RejectionReason,
                Is.EqualTo(CommandRejectionReason.FireRateExceeded));
            Assert.That(feedback[1].HasAmmoState, Is.True);
            Assert.That(feedback[1].MagazineAmmo, Is.EqualTo(49));
        }

        [Test]
        public void EachEnemySnapshotCarriesItsActualPublishTickAndRun()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.ServerStep();
            NetcodeTargetState state = authority.GetReplicatedTarget(0);
            Assert.That(state.SnapshotTick,
                Is.EqualTo(authority.LastAuthoritativeSnapshot.Tick));
            Assert.That(state.RunGeneration, Is.EqualTo(1));
        }

        [Test]
        public void SlightlyEarlyInputWaitsForItsTickWithoutBlockingAnotherPlayer()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.ConfigureServer(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default),
                    new CoopPlayerSpawn(2, new NetVector3(1d, 0d, 0d)) },
                new[] { new CoopTargetSpawn(1,
                    new NetVector3(0d, 0d, 10d), 0.5d, 100d) }, 1);
            authority.RegisterPlayerClient(10, 1);
            authority.RegisterPlayerClient(20, 2);
            var early = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(1, 1, 1, 5, 0d, 0d,
                    0d, 0d, true, default));
            Assert.That(authority.TryQueueCommand(10, early, true), Is.True);
            authority.TryQueueCommand(20, NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(2, 1, 1, 1, 0d, 0d,
                    0d, 0d, false, new NetVector3(1d, 0d, 0d))), false);
            AuthoritativeTickResult first = authority.ServerStep();
            Assert.That(first.Commands.Count, Is.EqualTo(1));
            Assert.That(first.Commands[0].Command.PlayerId, Is.EqualTo(2));
            Assert.That(authority.PendingInputCommandCount, Is.EqualTo(1));
            authority.ServerStep();
            authority.ServerStep();
            CommandResolution result = authority.ServerStep().Commands.Single();
            Assert.That(result.Command.PlayerId, Is.EqualTo(1));
            Assert.That(result.Accepted, Is.True);
        }

        [Test]
        public void FarFutureShotIsExplicitlyRejectedWithAmmoInsteadOfUnboundedWait()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            var farFuture = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(1, 1, 1, 1000, 0d, 0d,
                    0d, 0d, true, default));
            Assert.That(authority.TryQueueCommand(10, farFuture, true), Is.False);
            Assert.That(authority.PendingInputCommandCount, Is.Zero);
            var feedback = new List<NetcodeShotFeedbackEvent>();
            authority.GetShotEventsAfter(1, 0, feedback);
            Assert.That(feedback.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.TimestampInFuture));
            Assert.That(feedback[0].ShotCommandSequence, Is.EqualTo(1));
            Assert.That(feedback[0].MagazineAmmo, Is.EqualTo(50));
            Assert.That(feedback[0].DidImpact, Is.False);
        }

        [Test]
        public void ShotFrameIsConsumedOnceAndSeparatedFromMovementTick()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            NetworkVerticalSliceInputDriver driver =
                replica.gameObject.AddComponent<NetworkVerticalSliceInputDriver>();
            driver.SetShotFrame(Vector3.right, 0);
            NetcodePlayerCommand shot = replica.BuildPredictedCommand(
                0f, 0f, 0f, 0f, true, 1);
            Assert.That(shot.ClientTick, Is.EqualTo(1));
            Assert.That(shot.ShotViewTick, Is.EqualTo(0));
            Assert.That(shot.ShotDirection, Is.EqualTo(Vector3.right));
            NetcodePlayerCommand later = replica.BuildPredictedCommand(
                0f, 0f, 0f, 0f, true, 2);
            Assert.That(later.ShotDirection, Is.EqualTo(Vector3.zero),
                "上一条开火的显式命中帧不能被后续开火重复使用。");
        }

        [Test]
        public void RejectedShotAcknowledgesOnceButCannotRollBackNewerAmmoSnapshot()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            NetcodePlayerState state = NetcodePlayerState.FromDomain(3,
                authority.LastAuthoritativeSnapshot.Player(1));
            state.MagazineAmmo = 47;
            replica.ConsumeServerState(state, true, 3d);
            var rejected = new NetcodeShotFeedbackEvent
            {
                ShooterPlayerId = 1,
                Sequence = 1,
                ShotCommandSequence = 2,
                ServerTick = 2,
                RejectionReason = CommandRejectionReason.FireRateExceeded,
                HasAmmoState = true,
                AmmoWeaponId = "weapon.rifle",
                MagazineAmmo = 48,
                ReserveAmmo = 150
            };
            int count = 0;
            replica.ShotFeedbackReceived += _ => count++;
            Assert.That(replica.ConsumeShotFeedbackEvent(rejected), Is.True);
            Assert.That(replica.ConsumeShotFeedbackEvent(rejected), Is.False);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(replica.PresentedMagazineAmmo, Is.EqualTo(47));
        }

        [Test]
        public void SameRunClockRecoveryCannotReuseAlreadySubmittedInputTicks()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            for (int tick = 0; tick < 20; tick++) authority.ServerStep();
            NetworkPlayerReplica replica = CreateReplica(authority);
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);
            var driver = replica.gameObject.AddComponent<NetworkVerticalSliceInputDriver>();
            driver.enabled = false;
            Assert.That(driver.CanSubmitCurrentFrame, Is.True);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            double now = Time.realtimeSinceStartupAsDouble;
            var clock = new ClientServerClock(60);
            clock.Observe(authority.WorldState.ServerTick,
                authority.WorldState.RunGeneration, now - 0.6d, 0d);
            typeof(NetworkCoopSessionAuthority).GetField("clientClock", flags).SetValue(authority, clock);
            typeof(NetworkCoopSessionAuthority).GetField("clientClockTickRate", flags).SetValue(authority, 60);
            typeof(NetworkVerticalSliceInputDriver).GetField("lastClockRevision", flags).SetValue(driver, clock.Revision);
            // These are real predictions awaiting transport/ACK, not a
            // forged authority position. Their timestamps must not be reused
            // when a delayed observation establishes another time origin.
            replica.BuildPredictedCommand(0f, 0f, 0f, 0f, false, 21);
            replica.BuildPredictedCommand(0f, 0f, 0f, 0f, false, 22);
            replica.BuildPredictedCommand(0f, 0f, 0f, 0f, false, 23);
            typeof(NetworkVerticalSliceInputDriver).GetField("clientTick", flags).SetValue(driver, 23L);
            typeof(NetworkVerticalSliceInputDriver).GetField("fireQueued", flags).SetValue(driver, true);
            typeof(NetworkVerticalSliceInputDriver).GetField("jumpQueued", flags).SetValue(driver, true);
            authority.ServerStep();
            bool available = driver.CanSubmitCurrentFrame;
            Assert.That(clock.Revision, Is.EqualTo(2), "Use a real gap-driven clock epoch change.");
            Assert.That(driver.ClientTick, Is.GreaterThanOrEqualTo(23L),
                "Recovering the same run must not roll back timestamps and re-send ticks 21..23.");
            Assert.That(available, Is.False, "Wait for a new tick instead of inventing another past input.");
            Assert.That(typeof(NetworkVerticalSliceInputDriver).GetField("fireQueued", flags).GetValue(driver), Is.False);
            Assert.That(typeof(NetworkVerticalSliceInputDriver).GetField("jumpQueued", flags).GetValue(driver), Is.False);
        }

        [Test]
        public void RealMissionRestartStartsANewInputEpochInsteadOfWaitingForOldTicks()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            for (int tick = 0; tick < 20; tick++) authority.ServerStep();
            NetworkPlayerReplica replica = CreateReplica(authority);
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);
            var driver = replica.gameObject.AddComponent<NetworkVerticalSliceInputDriver>();
            driver.enabled = false;
            Assert.That(driver.CanSubmitCurrentFrame, Is.True);
            replica.BuildPredictedCommand(0f, 0f, 0f, 0f, false, 21);
            typeof(NetworkVerticalSliceInputDriver).GetField("clientTick",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(driver, 21L);
            authority.ApplyServerDamageToPlayer(1, 1000d);
            Assert.That(authority.TryRestartMission(10), Is.True);
            Assert.That(authority.WorldState.RunGeneration, Is.EqualTo(2));
            Assert.That(authority.TryGetPlayerState(1, out state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);
            Assert.That(driver.CanSubmitCurrentFrame, Is.True,
                "新战局不能等到旧战局21 Tick后才允许输入。");
            Assert.That(driver.ClientTick, Is.Zero);
            Assert.That(authority.TryGetNextClientInputTick(driver.ClientTick, out long next), Is.True);
            Assert.That(next, Is.EqualTo(1L));
        }

        private NetworkPlayerReplica CreateReplica(
            NetworkCoopSessionAuthority authority)
        {
            var host = new GameObject("Network Timing Replica");
            objects.Add(host);
            NetworkPlayerReplica replica = host.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            return replica;
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            var host = new GameObject("Network Timing Authority");
            objects.Add(host);
            NetworkCoopSessionAuthority authority =
                host.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1,
                    new NetVector3(0d, 0d, 10d), 0.5d, 100d) }, 1);
            return authority;
        }

        private static NetcodePlayerCommand Shot(uint sequence) =>
            NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                1, sequence, sequence, sequence, 0d, 0d, 0d, 0d,
                true, default, false, false, false,
                NetworkPresentationIds.RifleGameplay, default));
    }
}
