using System;
using System.Collections;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue98NetworkMissionIntegrationTests
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
        }

        [Test]
        public void SenderIdentityAndServerPositionProtectMissionCommands()
        {
            NetworkCoopSessionAuthority authority = Authority(
                terminalTicks: 1);
            authority.RegisterPlayerClient(1001, 1);
            authority.RegisterPlayerClient(1002, 2);

            Assert.That(authority.TryQueueMissionCommand(1002,
                Mission(1, 1, 101,
                    AuthoritativeMissionCommandKind.HoldTerminal)), Is.False);
            Assert.That(authority.TryQueueMissionCommand(1001,
                Mission(1, 1, 101,
                    AuthoritativeMissionCommandKind.HoldTerminal)), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();

            Assert.That(result.MissionCommands[0].Rejection,
                Is.EqualTo(AuthoritativeMissionRejection.WrongPhase));
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.ClearEnemies));
        }

        [Test]
        public void BothClientsObserveOneAuthoritativeVictoryAndSummary()
        {
            NetworkCoopSessionAuthority authority = Authority(
                terminalTicks: 2, extractionTicks: 1);
            authority.RegisterPlayerClient(1001, 1);
            authority.RegisterPlayerClient(1002, 2);
            Assert.That(authority.TryQueueCommand(1001, Shot(), true), Is.True);
            authority.ServerStep();
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.ActivateTerminal));

            for (uint sequence = 1; sequence <= 2; sequence++)
            {
                Assert.That(authority.TryQueueMissionCommand(1001,
                    Mission(1, sequence, sequence + 200,
                        AuthoritativeMissionCommandKind.HoldTerminal)),
                    Is.True);
                authority.ServerStep();
            }
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.Extraction));

            authority.TryQueueMissionCommand(1001,
                Mission(1, 3, 203,
                    AuthoritativeMissionCommandKind.StartExtraction));
            authority.ServerStep();

            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.Victory));
            Assert.That(authority.WorldState.MissionOutcomeReason,
                Is.EqualTo(AuthoritativeMissionOutcomeReason.Extracted));
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState first), Is.True);
            Assert.That(authority.TryGetPlayerState(2,
                out NetcodePlayerState second), Is.True);
            Assert.That(first.Kills, Is.EqualTo(1));
            Assert.That(first.DamageDealt, Is.EqualTo(10f));
            Assert.That(second.Kills, Is.Zero);
        }

        [Test]
        public void DownedReviveAndSquadWipeAreSharedServerFacts()
        {
            NetworkCoopSessionAuthority authority = Authority(reviveTicks: 1);
            authority.RegisterPlayerClient(1001, 1);
            authority.RegisterPlayerClient(1002, 2);
            authority.ApplyServerDamageToPlayer(1, 100d);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState downed), Is.True);
            Assert.That(downed.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Downed));

            authority.TryQueueMissionCommand(1002,
                Mission(2, 1, 201,
                    AuthoritativeMissionCommandKind.HoldRevive, 1));
            authority.ServerStep();
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState revived), Is.True);
            Assert.That(revived.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Alive));

            authority.ApplyServerDamageToPlayer(1, 100d);
            authority.ApplyServerDamageToPlayer(2, 100d);
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.Defeat));
            Assert.That(authority.WorldState.MissionOutcomeReason,
                Is.EqualTo(AuthoritativeMissionOutcomeReason.SquadWiped));
        }

        [Test]
        public void HostCanRestartButClientCannotAndRunStateIsReset()
        {
            NetworkCoopSessionAuthority authority = Authority();
            authority.RegisterPlayerClient(1001, 1);
            authority.RegisterPlayerClient(1002, 2);
            authority.ApplyServerDamageToPlayer(1, 100d);
            authority.ApplyServerDamageToPlayer(2, 100d);

            Assert.That(authority.TryRestartMission(1002), Is.False);
            Assert.That(authority.TryRestartMission(1001), Is.True);
            Assert.That(authority.WorldState.RunGeneration, Is.EqualTo(2));
            Assert.That(authority.WorldState.MissionPhase,
                Is.EqualTo(AuthoritativeMissionPhase.ClearEnemies));
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState reset), Is.True);
            Assert.That(reset.Health, Is.EqualTo(100f));
            Assert.That(reset.Kills, Is.Zero);
        }

        [Test]
        public void DepartedMemberIsRemovedFromMissionAndPendingCommands()
        {
            NetworkCoopSessionAuthority authority = Authority();
            authority.RegisterPlayerClient(1001, 1);
            authority.RegisterPlayerClient(1002, 2);
            authority.TryQueueMissionCommand(1002,
                Mission(2, 1, 201,
                    AuthoritativeMissionCommandKind.HoldTerminal));

            authority.UnregisterPlayerClient(1002);
            AuthoritativeTickResult result = authority.ServerStep();

            Assert.That(result.MissionCommands, Is.Empty);
            Assert.That(authority.TryGetPlayerState(2,
                out NetcodePlayerState departed), Is.True);
            Assert.That(departed.LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Disconnected));
        }

        private NetworkCoopSessionAuthority Authority(
            int terminalTicks = 2,
            int extractionTicks = 2,
            int reviveTicks = 2)
        {
            GameObject host = Track(new GameObject("Issue98 Authority"));
            NetworkCoopSessionAuthority authority = host.AddComponent<
                NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 100d),
                new[]
                {
                    new CoopPlayerSpawn(1, default),
                    new CoopPlayerSpawn(2, default)
                },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d, 10d,
                        moveSpeed: 0d, attackDamage: 0d)
                },
                1,
                new AuthoritativeMissionDefinition(default, default,
                    terminalRadius: 3d, extractionRadius: 3d,
                    reviveRadius: 2.5d,
                    terminalHoldTicks: terminalTicks,
                    extractionHoldTicks: extractionTicks,
                    reviveHoldTicks: reviveTicks,
                    revivedHealth: 40d));
            return authority;
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        private static NetcodeMissionCommand Mission(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeMissionCommandKind kind,
            int targetPlayerId = 0) => NetcodeMissionCommand.FromDomain(
                new AuthoritativeMissionCommand(playerId, sequence, nonce,
                    kind, targetPlayerId));

        private static NetcodePlayerCommand Shot() =>
            NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                1, 1, 901, 1, 0d, 0d, 0d, 0d, true, default,
                false, false, false, "weapon.rifle", default));
    }
}
