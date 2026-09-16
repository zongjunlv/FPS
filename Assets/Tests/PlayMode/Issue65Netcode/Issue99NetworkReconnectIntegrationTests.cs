using System.Collections;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue99NetworkReconnectIntegrationTests
    {
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) Object.Destroy(created[index]);
            created.Clear();
            yield return null;
        }

        [Test]
        public void RebindPreservesSnapshotAndContinuesEveryCommandSequence()
        {
            NetworkCoopSessionAuthority authority = Authority(twoPlayers: true);
            authority.RegisterPlayerClient(101, 1);
            authority.RegisterPlayerClient(202, 2);
            Assert.That(authority.TryQueueCommand(101,
                PlayerCommand(1, 1, 1001), false), Is.True);
            authority.ServerStep();
            Assert.That(authority.TryQueueCommand(101,
                ShotCommand(1, 2, 1002), true), Is.True);
            authority.ServerStep();
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.ActivateTerminal));
            authority.ApplyServerDamageToPlayer(1, 25d);
            Assert.That(authority.TryApplyPresentationCommand(101,
                new NetcodePresentationCommand
                {
                    PlayerId = 1,
                    Sequence = 1,
                    Action = NetworkPresentationAction.SwitchWeapon,
                    WeaponId = NetworkPresentationIds.Handgun
                }), Is.True);
            int dropId = authority.SpawnServerWorldDrop(
                "medical_kit", 2, Vector3.zero, 1);
            Assert.That(authority.TryQueueEconomyCommand(101,
                Economy(1, 1, 2001,
                    AuthoritativeEconomyCommandKind.Pickup,
                    entityId: dropId)), Is.True);
            authority.ServerStep();
            authority.GrantServerExperience(1, 100);
            Assert.That(authority.TryGetProgression(1, out var offer), Is.True);
            Assert.That(authority.TryQueueEconomyCommand(101,
                Economy(1, 2, 2002,
                    AuthoritativeEconomyCommandKind.SelectUpgrade,
                    candidateIndex: 0,
                    choiceGeneration: offer.ChoiceGeneration)), Is.True);
            Assert.That(authority.TryQueueMissionCommand(101,
                Mission(1, 4, 3004)), Is.True);
            authority.ServerStep();

            AuthoritativeWorldSnapshot before =
                authority.LastAuthoritativeSnapshot;
            Assert.That(authority.SuspendPlayerClient(101), Is.True);
            authority.ServerStep();
            AuthoritativeWorldSnapshot whileDisconnected =
                authority.LastAuthoritativeSnapshot;
            Assert.That(whileDisconnected.Tick, Is.GreaterThan(before.Tick));
            Assert.That(authority.WorldState.MissionPhase,
                Is.Not.EqualTo(AuthoritativeMissionPhase.Defeat));
            Assert.That(authority.TryQueueCommand(101,
                PlayerCommand(1, 2, 1002), false), Is.False);

            authority.RegisterPlayerClient(303, 1);
            authority.UnregisterPlayerClient(101);
            Assert.That(authority.TryGetBoundClient(1, out ulong current),
                Is.True);
            Assert.That(current, Is.EqualTo(303));
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState restored), Is.True);
            AuthoritativePlayerState original = whileDisconnected.Player(1);
            Assert.That(restored.Position,
                Is.EqualTo(NetcodeConversions.ToUnity(original.Position)));
            Assert.That(restored.Health, Is.EqualTo((float)original.Health));
            Assert.That(restored.Armor, Is.EqualTo((float)original.Armor));
            Assert.That(restored.WeaponId.ToString(),
                Is.EqualTo(NetworkPresentationIds.Handgun));
            Assert.That(restored.MagazineAmmo,
                Is.EqualTo(original.MagazineAmmo));
            Assert.That(restored.ReserveAmmo,
                Is.EqualTo(original.ReserveAmmo));
            Assert.That(restored.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Alive));
            Assert.That(authority.ReplicatedInventorySlotCount,
                Is.EqualTo(whileDisconnected.Economy.InventorySlots.Count));
            Assert.That(authority.ReplicatedUpgradeCount,
                Is.EqualTo(whileDisconnected.Economy.Upgrades.Count));
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(whileDisconnected.Mission.Phase));
            Assert.That(authority.WorldState.KilledTargets,
                Is.EqualTo(whileDisconnected.KilledTargets));
            Assert.That(authority.WorldState.WaveStatus,
                Is.EqualTo(whileDisconnected.WaveStatus));
            Assert.That(authority.GetReplicatedTarget(0).Health,
                Is.EqualTo((float)whileDisconnected.Targets[0].Health));
            Assert.That(authority.GetReplicatedTarget(0).Active,
                Is.EqualTo(whileDisconnected.Targets[0].Active));
            Assert.That(authority.TryGetProgression(1,
                out NetcodeProgressionState progression), Is.True);
            Assert.That(progression.Level, Is.EqualTo(2));

            GameObject replicaObject = Track(new GameObject(
                "Issue99 Restored Replica"));
            NetworkPlayerReplica replica =
                replicaObject.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            replica.ConsumeServerState(restored, true, restored.ServerTick);
            NetcodePlayerCommand movement = replica.BuildPredictedCommand(
                0f, 0f, 0f, 0f, false, restored.ServerTick + 1);
            NetcodePresentationCommand presentation =
                replica.BuildPresentationCommand(
                    NetworkPresentationAction.Reload);
            NetcodeEconomyCommand economy = replica.BuildEconomyCommand(
                AuthoritativeEconomyCommandKind.Compact);
            NetcodeMissionCommand mission = replica.BuildMissionCommand(
                AuthoritativeMissionCommandKind.HoldTerminal);

            Assert.That(movement.Sequence,
                Is.EqualTo(restored.AcknowledgedSequence + 1));
            Assert.That(presentation.Sequence, Is.EqualTo(
                restored.AcknowledgedPresentationCommandSequence + 1));
            Assert.That(economy.Sequence,
                Is.EqualTo(progression.AcknowledgedEconomySequence + 1));
            Assert.That(mission.Sequence,
                Is.EqualTo(restored.AcknowledgedMissionSequence + 1));
            Assert.That(authority.TryQueueCommand(303, movement, false),
                Is.True);
        }

        [Test]
        public void GracePeriodKeepsRunAliveAndTimeoutFinalizesDeparture()
        {
            NetworkCoopSessionAuthority authority = Authority();
            authority.RegisterPlayerClient(11, 1);

            Assert.That(authority.SuspendPlayerClient(11), Is.True);
            authority.ServerStep();
            Assert.That(authority.TryGetPlayerState(1, out var reserved),
                Is.True);
            Assert.That(reserved.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Alive));
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.ClearEnemies));

            Assert.That(authority.FinalizeDisconnectedPlayer(1), Is.True);
            Assert.That(authority.TryGetPlayerState(1, out var expired),
                Is.True);
            Assert.That(expired.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Disconnected));
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.Defeat));
            Assert.That(authority.WorldState.MissionOutcomeReason,
                Is.EqualTo(AuthoritativeMissionOutcomeReason.AllPlayersLeft));
        }

        [Test]
        public void NewBindingMakesLateOldDisconnectHarmless()
        {
            NetworkCoopSessionAuthority authority = Authority();
            authority.RegisterPlayerClient(11, 1);
            Assert.That(authority.SuspendPlayerClient(11), Is.True);
            authority.RegisterPlayerClient(22, 1);

            authority.UnregisterPlayerClient(11);

            Assert.That(authority.TryGetBoundClient(1, out ulong bound),
                Is.True);
            Assert.That(bound, Is.EqualTo(22));
            Assert.That(authority.TryGetPlayerState(1, out var state), Is.True);
            Assert.That(state.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Alive));
            Assert.That(authority.TryQueueCommand(11,
                PlayerCommand(1, 1, 99), false), Is.False);
            Assert.That(authority.TryQueueCommand(22,
                PlayerCommand(1, 1, 100), false), Is.True);
        }

        private NetworkCoopSessionAuthority Authority(bool twoPlayers = false)
        {
            GameObject host = Track(new GameObject("Issue99 Authority"));
            NetworkCoopSessionAuthority authority = host.AddComponent<
                NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            CoopPlayerSpawn[] players = twoPlayers
                ? new[]
                {
                    new CoopPlayerSpawn(1, default, 100d, 50d, 100d),
                    new CoopPlayerSpawn(2,
                        new NetVector3(2d, 0d, 0d), 100d, 50d, 100d)
                }
                : new[]
                {
                    new CoopPlayerSpawn(1, default, 100d, 50d, 100d)
                };
            authority.ConfigureServer(
                new CoopServerRules(tickRate: 60, fireCooldownTicks: 1),
                players,
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 50d), 0.5d, 10d,
                        moveSpeed: 0d, attackDamage: 0d)
                },
                1,
                new AuthoritativeMissionDefinition(default, default,
                    terminalHoldTicks: 2, extractionHoldTicks: 2,
                    reviveHoldTicks: 2));
            return authority;
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        private static NetcodePlayerCommand PlayerCommand(
            int playerId, uint sequence, ulong nonce) =>
            NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                playerId, sequence, nonce, sequence,
                1d, 0d, 0d, 0d, false, default));

        private static NetcodePlayerCommand ShotCommand(
            int playerId, uint sequence, ulong nonce) =>
            NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                playerId, sequence, nonce, sequence,
                0d, 0d, 0d, 0d, true, default,
                false, false, false, "weapon.rifle", default));

        private static NetcodeEconomyCommand Economy(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeEconomyCommandKind kind,
            int entityId = 0,
            int candidateIndex = -1,
            int choiceGeneration = 0) =>
            NetcodeEconomyCommand.FromDomain(new AuthoritativeEconomyCommand(
                playerId, sequence, nonce, kind, entityId,
                candidateIndex: candidateIndex,
                choiceGeneration: choiceGeneration));

        private static NetcodeMissionCommand Mission(
            int playerId, uint sequence, ulong nonce) =>
            NetcodeMissionCommand.FromDomain(new AuthoritativeMissionCommand(
                playerId, sequence, nonce,
                AuthoritativeMissionCommandKind.HoldTerminal));
    }
}
