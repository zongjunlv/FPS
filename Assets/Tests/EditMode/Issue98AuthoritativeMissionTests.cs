using System;
using System.Linq;
using FPS.Networking.Domain;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue98AuthoritativeMissionTests
    {
        [Test]
        public void ServerOwnsClearTerminalExtractionAndVictorySequence()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                terminalTicks: 3, extractionTicks: 2, twoPlayers: true);

            AuthoritativeTickResult killed = KillOnlyTarget(simulation);
            Assert.That(killed.Snapshot.Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.ActivateTerminal));

            for (uint sequence = 1; sequence <= 3; sequence++)
            {
                AuthoritativeTickResult holding = simulation.Step(
                    Array.Empty<PlayerInputCommand>(),
                    Array.Empty<AuthoritativeEconomyCommand>(),
                    new[] { Mission(1, sequence, sequence + 100,
                        AuthoritativeMissionCommandKind.HoldTerminal) });
                Assert.That(holding.MissionCommands.Single().Accepted, Is.True);
            }
            Assert.That(simulation.CaptureSnapshot().Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.Extraction));

            simulation.Step(Array.Empty<PlayerInputCommand>(),
                Array.Empty<AuthoritativeEconomyCommand>(),
                new[] { Mission(1, 4, 104,
                    AuthoritativeMissionCommandKind.StartExtraction) });
            AuthoritativeWorldSnapshot victory = simulation.Step(
                Array.Empty<PlayerInputCommand>()).Snapshot;

            Assert.That(victory.Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.Victory));
            Assert.That(victory.Mission.OutcomeReason,
                Is.EqualTo(AuthoritativeMissionOutcomeReason.Extracted));
            simulation.ApplyServerDamageToPlayer(1, 25d);
            Assert.That(simulation.CaptureSnapshot().Player(1).Health,
                Is.EqualTo(100d), "结算后迟到伤害不得修改冻结结果。");
        }

        [Test]
        public void TerminalCannotBeCompletedEarlyOrFromOutsideServerRadius()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                terminalTicks: 2,
                playerPosition: new NetVector3(10d, 0d, 0d));

            AuthoritativeTickResult beforeClear = simulation.Step(
                Array.Empty<PlayerInputCommand>(),
                Array.Empty<AuthoritativeEconomyCommand>(),
                new[] { Mission(1, 1, 101,
                    AuthoritativeMissionCommandKind.HoldTerminal) });
            Assert.That(beforeClear.MissionCommands.Single().Rejection,
                Is.EqualTo(AuthoritativeMissionRejection.WrongPhase));

            KillOnlyTarget(simulation, playerPosition:
                new NetVector3(10d, 0d, 0d), inputSequence: 1,
                clientTick: simulation.CurrentTick + 1);
            AuthoritativeTickResult far = simulation.Step(
                Array.Empty<PlayerInputCommand>(),
                Array.Empty<AuthoritativeEconomyCommand>(),
                new[] { Mission(1, 2, 102,
                    AuthoritativeMissionCommandKind.HoldTerminal) });

            Assert.That(far.MissionCommands.Single().Rejection,
                Is.EqualTo(AuthoritativeMissionRejection.OutOfRange));
            Assert.That(far.Snapshot.Mission.TerminalProgressTicks, Is.Zero);
        }

        [Test]
        public void OneDownedPlayerCanBeRevivedByNearbyLivingTeammate()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                reviveTicks: 2, twoPlayers: true);
            simulation.ApplyServerDamageToPlayer(1, 100d);

            Assert.That(simulation.CaptureSnapshot().Player(1).IsDowned,
                Is.True);
            Assert.That(simulation.CaptureSnapshot().Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.ClearEnemies));

            for (uint sequence = 1; sequence <= 2; sequence++)
                simulation.Step(Array.Empty<PlayerInputCommand>(),
                    Array.Empty<AuthoritativeEconomyCommand>(),
                    new[] { Mission(2, sequence, sequence + 200,
                        AuthoritativeMissionCommandKind.HoldRevive, 1) });

            AuthoritativePlayerState revived =
                simulation.CaptureSnapshot().Player(1);
            Assert.That(revived.IsAlive, Is.True);
            Assert.That(revived.Health, Is.EqualTo(40d));
        }

        [Test]
        public void ClearingFinalThreatRestoresDownedSquadForMissionPhase()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                twoPlayers: true);
            simulation.ApplyServerDamageToPlayer(2, 100d);

            AuthoritativeTickResult result = KillOnlyTarget(
                simulation,
                inputSequence: 1,
                clientTick: simulation.CurrentTick + 1);

            Assert.That(result.Snapshot.Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.ActivateTerminal));
            Assert.That(result.Snapshot.Player(2).LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Alive),
                "战斗结束后不能让倒地队员永久阻塞终端与全员撤离。 ");
            Assert.That(result.Snapshot.Player(2).Health,
                Is.EqualTo(40d));
            Assert.That(result.Events.Any(value =>
                    value.Kind == AuthoritativeEventKind.PlayerRevived &&
                    value.TargetId == 2),
                Is.True);
        }

        [Test]
        public void SquadWipeProducesOneSharedDefeatOutcome()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                twoPlayers: true);
            simulation.ApplyServerDamageToPlayer(1, 100d);
            simulation.ApplyServerDamageToPlayer(2, 100d);
            AuthoritativeMissionState mission =
                simulation.CaptureSnapshot().Mission;

            Assert.That(mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.Defeat));
            Assert.That(mission.OutcomeReason,
                Is.EqualTo(AuthoritativeMissionOutcomeReason.SquadWiped));
        }

        [Test]
        public void DisconnectedMemberDoesNotBlockRemainingPlayerExtraction()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                terminalTicks: 1, extractionTicks: 1, twoPlayers: true);
            KillOnlyTarget(simulation);
            simulation.Step(Array.Empty<PlayerInputCommand>(),
                Array.Empty<AuthoritativeEconomyCommand>(),
                new[] { Mission(1, 1, 101,
                    AuthoritativeMissionCommandKind.HoldTerminal) });
            simulation.SetPlayerConnected(2, false);

            AuthoritativeWorldSnapshot result = simulation.Step(
                Array.Empty<PlayerInputCommand>(),
                Array.Empty<AuthoritativeEconomyCommand>(),
                new[] { Mission(1, 2, 102,
                    AuthoritativeMissionCommandKind.StartExtraction) })
                .Snapshot;

            Assert.That(result.Player(2).LifeState,
                Is.EqualTo(AuthoritativePlayerLifeState.Disconnected));
            Assert.That(result.Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.Victory));
        }

        [Test]
        public void FrozenOutcomeStatisticsContainCombatAndUpgradeResults()
        {
            AuthoritativeCoopSimulation simulation = Simulation();
            simulation.ApplyServerDamageToPlayer(1, 25d);
            simulation.GrantServerExperience(1, 100);
            AuthoritativeProgressionState offer = simulation.CaptureSnapshot()
                .Economy.Player(1);
            simulation.Step(Array.Empty<PlayerInputCommand>(), new[]
            {
                new AuthoritativeEconomyCommand(1, 1, 501,
                    AuthoritativeEconomyCommandKind.SelectUpgrade,
                    candidateIndex: 0,
                    choiceGeneration: offer.ChoiceGeneration)
            });
            AuthoritativeWorldSnapshot result = KillOnlyTarget(
                simulation, inputSequence: 1,
                clientTick: simulation.CurrentTick + 1).Snapshot;
            AuthoritativePlayerMissionStats stats = result.Mission.Player(1);

            Assert.That(stats.Kills, Is.EqualTo(1));
            Assert.That(stats.DamageDealt, Is.EqualTo(10d));
            Assert.That(stats.DamageTaken, Is.EqualTo(25d));
            Assert.That(stats.UpgradesSelected, Is.EqualTo(1));
        }

        private static AuthoritativeCoopSimulation Simulation(
            int terminalTicks = 2,
            int extractionTicks = 2,
            int reviveTicks = 2,
            bool twoPlayers = false,
            NetVector3 playerPosition = default)
        {
            CoopPlayerSpawn[] players = twoPlayers
                ? new[]
                {
                    new CoopPlayerSpawn(1, playerPosition),
                    new CoopPlayerSpawn(2, playerPosition)
                }
                : new[] { new CoopPlayerSpawn(1, playerPosition) };
            return new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 100d),
                players,
                new[]
                {
                    new CoopTargetSpawn(1,
                        playerPosition + new NetVector3(0d, 0d, 10d),
                        0.5d, 10d, moveSpeed: 0d, attackDamage: 0d)
                },
                configuredMission: new AuthoritativeMissionDefinition(
                    default, default, terminalRadius: 3d,
                    extractionRadius: 3d, reviveRadius: 2.5d,
                    terminalHoldTicks: terminalTicks,
                    extractionHoldTicks: extractionTicks,
                    reviveHoldTicks: reviveTicks,
                    revivedHealth: 40d));
        }

        private static AuthoritativeTickResult KillOnlyTarget(
            AuthoritativeCoopSimulation simulation,
            NetVector3 playerPosition = default,
            uint inputSequence = 1,
            long clientTick = 1) => simulation.Step(new[]
            {
                new PlayerInputCommand(1, inputSequence, 900 + inputSequence,
                    clientTick, 0d, 0d, 0d, 0d, true, playerPosition,
                    false, false, false, "weapon.rifle", playerPosition)
            });

        private static AuthoritativeMissionCommand Mission(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeMissionCommandKind kind,
            int targetPlayerId = 0) => new(
                playerId, sequence, nonce, kind, targetPlayerId);
    }
}
