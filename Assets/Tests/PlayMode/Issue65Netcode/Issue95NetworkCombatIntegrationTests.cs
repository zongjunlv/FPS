using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue95NetworkCombatIntegrationTests
    {
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null)
                    UnityEngine.Object.Destroy(created[index]);
            created.Clear();
            yield return null;
            yield return null;
        }

        [Test]
        public void TwoClientsUseAuthoritativeAimAmmoReloadSwitchAndKill()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority(
                targetHealth: 30d);
            authority.RegisterPlayerClient(10, 1);
            authority.RegisterPlayerClient(11, 2);
            NetcodePlayerCommand shot = Payload(
                1, 1, 101, 1, fire: true, weapon: "weapon.rifle");
            shot.AimingHeld = true;
            Assert.That(authority.TryQueueCommand(10, shot, true), Is.True);
            Assert.That(authority.TryQueueCommand(11,
                Payload(2, 1, 201, 1, false, "weapon.rifle"), false),
                Is.True);

            AuthoritativeTickResult first = authority.ServerStep();
            Assert.That(first.Commands.All(value => value.Accepted), Is.True);
            Assert.That(first.Snapshot.Target(1).Health, Is.EqualTo(20d));
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState rifleState), Is.True);
            Assert.That(rifleState.MagazineAmmo, Is.EqualTo(49));
            Assert.That(rifleState.Aiming, Is.True);

            Assert.That(authority.TryApplyPresentationCommand(10,
                Presentation(1, NetworkPresentationAction.Reload,
                    NetworkPresentationIds.Rifle)), Is.True);
            NetcodePlayerCommand duringReload = Payload(
                1, 2, 102, 2, true, "weapon.rifle");
            authority.TryQueueCommand(10, duringReload, true);
            Assert.That(authority.ServerStep().Commands.Single()
                .RejectionReason, Is.EqualTo(
                    CommandRejectionReason.Reloading));

            while (authority.LastAuthoritativeSnapshot.Tick < 145)
                authority.ServerStep();
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState reloaded), Is.True);
            Assert.That(reloaded.MagazineAmmo, Is.EqualTo(50));
            Assert.That(reloaded.ReserveAmmo, Is.EqualTo(149));
            Assert.That(reloaded.Reloading, Is.False);

            Assert.That(authority.TryApplyPresentationCommand(10,
                Presentation(2, NetworkPresentationAction.SwitchWeapon,
                    NetworkPresentationIds.Handgun)), Is.True);
            while (authority.LastAuthoritativeSnapshot.Player(1).Switching)
                authority.ServerStep();
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState pistolState), Is.True);
            Assert.That(pistolState.CombatWeaponId.ToString(),
                Is.EqualTo("weapon.pistol"));
            Assert.That(pistolState.MagazineAmmo, Is.EqualTo(12));

            long tick = authority.LastAuthoritativeSnapshot.Tick + 1;
            NetcodePlayerCommand pistolShot = Payload(
                1, 3, 103, tick, true, "weapon.pistol");
            authority.TryQueueCommand(10, pistolShot, true);
            AuthoritativeTickResult killed = authority.ServerStep();
            Assert.That(killed.Commands.Single().Shot.Kind,
                Is.EqualTo(ShotResolutionKind.Killed));
            Assert.That(killed.Commands.Single().Shot.AppliedDamage,
                Is.EqualTo(20d));
            Assert.That(killed.Snapshot.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Completed));

            var feedback = new List<NetcodeShotFeedbackEvent>();
            authority.GetShotEventsAfter(1, 0, feedback);
            Assert.That(feedback.Count, Is.EqualTo(2));
            Assert.That(feedback[0].WeaponId.ToString(),
                Is.EqualTo("weapon.rifle"));
            Assert.That(feedback[1].WeaponId.ToString(),
                Is.EqualTo("weapon.pistol"));
            Assert.That(feedback[1].Kind,
                Is.EqualTo(ShotResolutionKind.Killed));
        }

        [Test]
        public void ReplicaConsumesEachConfirmedShotOnceAndLateJoinSkipsHistory()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            authority.TryQueueCommand(10,
                Payload(1, 1, 501, 1, true, "weapon.rifle"), true);
            authority.ServerStep();

            NetworkPlayerReplica replica = CreateReplica();
            replica.EnableOwnerTestHook(authority, 1);
            int received = 0;
            replica.ShotFeedbackReceived += _ => received++;
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState initial), Is.True);
            replica.ConsumeServerState(initial, true, initial.ServerTick);
            Assert.That(received, Is.Zero,
                "迟加入只建立射击事件基线，不重播旧枪。 ");

            for (int index = 0; index < 5; index++) authority.ServerStep();
            long tick = authority.LastAuthoritativeSnapshot.Tick + 1;
            authority.TryQueueCommand(10,
                Payload(1, 2, 502, tick, true, "weapon.rifle"), true);
            authority.ServerStep();
            authority.TryGetPlayerState(1, out NetcodePlayerState current);
            replica.ConsumeServerState(current, true, current.ServerTick);

            Assert.That(received, Is.EqualTo(1));
            var duplicate = new NetcodeShotFeedbackEvent
            {
                ShooterPlayerId = 1,
                Sequence = replica.LastShotEventSequence,
                Kind = ShotResolutionKind.Hit
            };
            Assert.That(replica.ConsumeShotFeedbackEvent(duplicate), Is.False);
            Assert.That(received, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FeedbackPresenterShowsConfirmedTracerOnlyOnce()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica();
            replica.Bind(authority, 1);
            NetworkCombatFeedbackPresenter presenter =
                replica.gameObject.AddComponent<
                    NetworkCombatFeedbackPresenter>();
            presenter.Configure(replica);
            var feedback = new NetcodeShotFeedbackEvent
            {
                ServerTick = 1,
                Sequence = 1,
                ShooterPlayerId = 1,
                ShotCommandSequence = 1,
                WeaponId = "weapon.rifle",
                Kind = ShotResolutionKind.Hit,
                TargetId = 1,
                Origin = Vector3.zero,
                EndPoint = Vector3.forward * 5f,
                Normal = Vector3.back,
                HitRegion = AuthoritativeHitRegion.Body,
                Surface = AuthoritativeSurface.Flesh,
                AppliedDamage = 10f
            };

            Assert.That(replica.ConsumeShotFeedbackEvent(feedback), Is.True);
            Assert.That(replica.ConsumeShotFeedbackEvent(feedback), Is.False);
            yield return null;

            Assert.That(presenter.PresentedShotCount, Is.EqualTo(1));
            Assert.That(presenter.PresentedHitCount, Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindFirstObjectByType<
                    ShotTracerPool>().ActiveCount,
                Is.EqualTo(1));
        }

        [TestCase(0, 0)]
        [TestCase(80, 0)]
        [TestCase(150, 0)]
        [TestCase(80, 500)]
        public void ReliableShotFeedbackConvergesUnderAcceptanceProfiles(
            int latencyMilliseconds,
            int lossBasisPoints)
        {
            var model = new DeterministicNetworkConditionModel(
                9500 + latencyMilliseconds + lossBasisPoints,
                60,
                new NetworkConditionProfile("combat",
                    latencyMilliseconds, lossBasisPoints, 8));
            var delivered = new HashSet<long>();
            var logicalByPacket = new Dictionary<long, long>();
            long packet = 1;
            for (long sequence = 1; sequence <= 20; sequence++)
            {
                bool sent = false;
                for (int retry = 0; retry < 8 && !sent; retry++)
                {
                    long packetId = packet++;
                    logicalByPacket[packetId] = sequence;
                    NetworkTransmission transmission = model.Transmit(
                        new NetworkPacket(packetId,
                            NetworkPacketKind.HitFeedback,
                            0, 1, sequence * 12 + retry, 72));
                    sent = !transmission.Dropped;
                }
            }
            foreach (NetworkTransmission transmission in model.Drain(600))
                delivered.Add(logicalByPacket[transmission.Packet.PacketId]);
            Assert.That(delivered.Count, Is.EqualTo(20));
        }

        private NetworkCoopSessionAuthority CreateAuthority(
            double targetHealth = 100d)
        {
            GameObject host = Track(new GameObject("Issue95 Authority"));
            NetworkCoopSessionAuthority authority =
                host.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(tickRate: 60,
                    maximumPastCommandTicks: 200,
                    historyCapacity: 256,
                    fireCooldownTicks: 6,
                    shotDamage: 10d),
                new[]
                {
                    new CoopPlayerSpawn(1, default),
                    new CoopPlayerSpawn(2, new NetVector3(2d, 0d, 0d))
                },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d,
                        targetHealth)
                }, requiredKills: 1);
            return authority;
        }

        private NetworkPlayerReplica CreateReplica()
        {
            return Track(new GameObject("Issue95 Replica"))
                .AddComponent<NetworkPlayerReplica>();
        }

        private static NetcodePlayerCommand Payload(
            int playerId,
            uint sequence,
            ulong nonce,
            long tick,
            bool fire,
            string weapon)
        {
            NetVector3 position = playerId == 1
                ? default
                : new NetVector3(2d, 0d, 0d);
            return NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                playerId, sequence, nonce, tick, 0d, 0d, 0d, 0d,
                fire, position, false, false, false, weapon, position));
        }

        private static NetcodePresentationCommand Presentation(
            uint sequence,
            NetworkPresentationAction action,
            string weaponId)
        {
            return new NetcodePresentationCommand
            {
                PlayerId = 1,
                Sequence = sequence,
                Action = action,
                WeaponId = weaponId
            };
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }
    }
}
