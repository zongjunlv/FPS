using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue94PlayerPresentationSyncTests
    {
        private readonly List<GameObject> created = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
            {
                if (created[index] != null)
                    UnityEngine.Object.DestroyImmediate(created[index]);
            }
            created.Clear();
        }

        [Test]
        public void ServerWhitelistFallsBackAppearanceAndRejectsUnknownWeapon()
        {
            Assert.That(NetworkPresentationIds.ResolveAppearance("hacked"),
                Is.EqualTo(NetworkPresentationIds.DefaultAppearance));
            Assert.That(NetworkPresentationIds.ResolveAppearance(
                    "character.quaternius.female-dark"),
                Is.EqualTo("character.quaternius.female-dark"));
            Assert.That(NetworkPresentationIds.TryResolveWeapon(
                "weapon.pistol", out string handgun), Is.True);
            Assert.That(handgun, Is.EqualTo(NetworkPresentationIds.Handgun));
            Assert.That(NetworkPresentationIds.TryResolveWeapon(
                "weapon.admin", out string fallback), Is.False);
            Assert.That(fallback, Is.EqualTo(
                NetworkPresentationIds.DefaultWeapon));
        }

        [Test]
        public void SnapshotWireFormatQuantizesContinuousValuesAndKeepsFlags()
        {
            var source = new NetcodePlayerState
            {
                ServerTick = 42,
                PlayerId = 2,
                Position = new Vector3(123.456f, 7.891f, -42.227f),
                Velocity = new Vector3(6.234f, -2.116f, 0.005f),
                Health = 87.26f,
                AcknowledgedSequence = 19,
                AimYawDegrees = 359.876f,
                AimPitchDegrees = -42.225f,
                Stance = (byte)PlayerStance.Crouching,
                Grounded = false,
                LastJumpTick = 38,
                GroundHeight = 1.234f,
                Sprinting = true,
                Aiming = true,
                AppearanceId = "character.quaternius.female-dark",
                WeaponId = NetworkPresentationIds.Handgun,
                AcknowledgedPresentationCommandSequence = 12,
                LastPresentationEventSequence = 7
            };

            using var writer = new FastBufferWriter(
                1024, Allocator.Temp);
            writer.WriteNetworkSerializable(source);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetcodePlayerState restored);

            Assert.That(restored.Position.x,
                Is.EqualTo(123.46f).Within(0.001f));
            Assert.That(restored.Position.z,
                Is.EqualTo(-42.23f).Within(0.001f));
            Assert.That(restored.Velocity.x,
                Is.EqualTo(6.23f).Within(0.001f));
            Assert.That(restored.Health,
                Is.EqualTo(87.3f).Within(0.001f));
            Assert.That(restored.AimYawDegrees,
                Is.EqualTo(359.88f).Within(0.001f));
            Assert.That(restored.AimPitchDegrees,
                Is.EqualTo(-42.22f).Within(0.001f));
            Assert.That(restored.Sprinting, Is.True);
            Assert.That(restored.Aiming, Is.True);
            Assert.That(restored.Grounded, Is.False);
            Assert.That(restored.AppearanceId.ToString(),
                Is.EqualTo("character.quaternius.female-dark"));
            Assert.That(restored.WeaponId.ToString(),
                Is.EqualTo(NetworkPresentationIds.Handgun));
            Assert.That(restored.AcknowledgedPresentationCommandSequence,
                Is.EqualTo(12));
        }

        [Test]
        public void AcceptedInputPublishesAimSprintJumpAndShootState()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetcodePlayerCommand command = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(
                    1, 1, 1001, 1, 0d, 0d, 0d, 0d,
                    fire: true, claimedPosition: default,
                    jumpPressed: true, sprintHeld: false,
                    crouchRequested: false));
            command.AimingHeld = true;

            Assert.That(authority.TryQueueCommand(10, command, true), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState state), Is.True);

            Assert.That(state.Aiming, Is.True);
            Assert.That(state.Sprinting, Is.False);
            Assert.That(state.Grounded, Is.False);
            Assert.That(state.LastPresentationEventSequence, Is.EqualTo(2));
            var events = new List<NetcodePresentationEvent>();
            authority.GetPresentationEventsAfter(1, 0, events);
            Assert.That(events.ConvertAll(value => value.Action),
                Is.EqualTo(new[]
                {
                    NetworkPresentationAction.Jump,
                    NetworkPresentationAction.Shoot
                }));
        }

        [Test]
        public void ReliablePresentationCommandsAreOwnedOrderedAndWhitelisted()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            var reload = new NetcodePresentationCommand
            {
                PlayerId = 1,
                Sequence = 1,
                Action = NetworkPresentationAction.Reload,
                WeaponId = NetworkPresentationIds.Rifle
            };
            var switchWeapon = new NetcodePresentationCommand
            {
                PlayerId = 1,
                Sequence = 2,
                Action = NetworkPresentationAction.SwitchWeapon,
                WeaponId = "weapon.pistol"
            };

            NetcodePlayerCommand shot = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(
                    1, 1, 1001, 1, 0d, 0d, 0d, 0d,
                    fire: true, claimedPosition: default));
            Assert.That(authority.TryQueueCommand(10, shot, true), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True);

            Assert.That(authority.TryApplyPresentationCommand(
                10, reload), Is.True);
            Assert.That(authority.TryApplyPresentationCommand(
                10, reload), Is.False, "重复序列不得重复播放动作。");
            Assert.That(authority.TryApplyPresentationCommand(
                11, switchWeapon), Is.False, "非拥有者不得切换武器。");
            switchWeapon.WeaponId = "weapon.invalid";
            Assert.That(authority.TryApplyPresentationCommand(
                10, switchWeapon), Is.False, "未知武器必须拒绝。");
            switchWeapon.WeaponId = "weapon.pistol";
            Assert.That(authority.TryApplyPresentationCommand(
                10, switchWeapon), Is.True);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState state), Is.True);
            Assert.That(state.WeaponId.ToString(),
                Is.EqualTo(NetworkPresentationIds.Handgun));
            Assert.That(state.LastPresentationEventSequence, Is.EqualTo(3));

            NetworkPlayerReplica rejoined = CreateReplica();
            rejoined.EnableOwnerTestHook(authority, 1);
            NetcodePresentationCommand next =
                rejoined.BuildPresentationCommand(
                    NetworkPresentationAction.Reload);
            Assert.That(next.Sequence, Is.EqualTo(3),
                "重连副本必须从服务端确认序列之后继续编号。");
        }

        [Test]
        public void LateReplicaBootstrapsCurrentStateWithoutReplayingOldTriggers()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetcodePlayerCommand shot = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(
                    1, 1, 1001, 1, 0d, 0d, 0d, 0d,
                    fire: true, claimedPosition: default));
            Assert.That(authority.TryQueueCommand(10, shot, true), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True);
            Assert.That(authority.TryApplyPresentationCommand(10,
                Presentation(1, NetworkPresentationAction.Reload,
                    NetworkPresentationIds.Rifle)), Is.True);

            NetworkPlayerReplica replica = CreateReplica();
            replica.Bind(authority, 1);
            int received = 0;
            replica.PresentationActionReceived += _ => received++;
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState initial), Is.True);
            replica.ConsumeServerState(initial, false, initial.ServerTick);
            Assert.That(received, Is.Zero,
                "迟加入者只恢复基础姿态，不重播历史 trigger。");

            Assert.That(authority.TryApplyPresentationCommand(10,
                Presentation(2, NetworkPresentationAction.SwitchWeapon,
                    NetworkPresentationIds.Handgun)), Is.True);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState current), Is.True);
            replica.ConsumeServerState(current, false, current.ServerTick);

            Assert.That(received, Is.EqualTo(1));
            Assert.That(replica.WeaponId,
                Is.EqualTo(NetworkPresentationIds.Handgun));
            var duplicate = new NetcodePresentationEvent
            {
                PlayerId = 1,
                Sequence = replica.LastPresentationEventSequence,
                Action = NetworkPresentationAction.SwitchWeapon,
                WeaponId = NetworkPresentationIds.Handgun
            };
            Assert.That(replica.ConsumePresentationEvent(duplicate), Is.False);
            Assert.That(received, Is.EqualTo(1));
        }

        [TestCase(0, 0)]
        [TestCase(80, 0)]
        [TestCase(150, 0)]
        [TestCase(80, 500)]
        public void SequencedReliableActionsConvergeAcrossRequiredConditions(
            int latencyMilliseconds,
            int lossBasisPoints)
        {
            NetworkConditionProfile profile = lossBasisPoints == 0
                ? new NetworkConditionProfile(
                    $"{latencyMilliseconds}ms", latencyMilliseconds, 0)
                : new NetworkConditionProfile(
                    "80ms-loss", latencyMilliseconds, lossBasisPoints, 8);
            var model = new DeterministicNetworkConditionModel(
                94, 60, profile);
            var deliveredSequences = new HashSet<long>();
            var sequenceByPacket = new Dictionary<long, long>();
            long packetId = 1;
            long actionSequence = 1;
            for (long tick = 0; tick < 360; tick++)
            {
                if (actionSequence <= 24 && tick % 10 == 0)
                {
                    long logicalSequence = actionSequence++;
                    bool delivered = false;
                    for (int retry = 0; retry < 8 && !delivered; retry++)
                    {
                        long currentPacketId = packetId++;
                        sequenceByPacket[currentPacketId] = logicalSequence;
                        NetworkTransmission transmission = model.Transmit(
                            new NetworkPacket(currentPacketId,
                                NetworkPacketKind.WorldSnapshot,
                                1, 2, tick + retry, 24));
                        delivered = !transmission.Dropped;
                    }
                }
                foreach (NetworkTransmission transmission in model.Drain(tick))
                    deliveredSequences.Add(
                        sequenceByPacket[transmission.Packet.PacketId]);
            }

            Assert.That(deliveredSequences.Count, Is.EqualTo(24));
            Assert.That(deliveredSequences,
                Is.EquivalentTo(Enumerable.Range(1, 24)
                    .Select(value => (long)value)));
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            GameObject gameObject = Track(new GameObject("Issue94 Authority"));
            NetworkCoopSessionAuthority authority =
                gameObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(
                    tickRate: 60,
                    maximumPastCommandTicks: 16,
                    historyCapacity: 32,
                    fireCooldownTicks: 1),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 12d), 1d, 100d)
                });
            return authority;
        }

        private NetworkPlayerReplica CreateReplica()
        {
            GameObject gameObject = Track(new GameObject("Issue94 Replica"));
            return gameObject.AddComponent<NetworkPlayerReplica>();
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

        private GameObject Track(GameObject gameObject)
        {
            created.Add(gameObject);
            return gameObject;
        }
    }
}
