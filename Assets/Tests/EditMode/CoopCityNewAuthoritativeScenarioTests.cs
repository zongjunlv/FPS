using System;
using System.Linq;
using FPS.Core.GameModes;
using FPS.Networking;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class CoopCityNewAuthoritativeScenarioTests
    {
        [SetUp]
        public void ResetGameMode()
        {
            GameModeContext.ResetForTests();
        }

        [Test]
        public void CooperativeBattleIsNotMisreportedAsTheLobby()
        {
            GameModeContext.BeginTransition(
                GameModeId.Coop,
                GameModeStage.CoopBattle);

            Assert.That(GameModeContext.TryActivate(
                GameModeId.Coop,
                GameModeStage.CoopBattle,
                out string error), Is.True, error);
            Assert.That(GameModeContext.IsActive(
                GameModeId.Coop,
                GameModeStage.CoopBattle), Is.True);
            Assert.That(GameModeContext.IsActive(
                GameModeId.Coop,
                GameModeStage.CoopLobby), Is.False);
        }

        [Test]
        public void AdapterUsesTheSoloCatalogAsTheOnlyScenarioSource()
        {
            CoopScenarioConfiguration scenario =
                CityNewAuthoritativeScenarioAdapter.Build(18018, 2);
            CityNewContentCatalog catalog =
                CityNewContentCatalog.LoadDefault();

            Assert.That(scenario.ContentId, Is.EqualTo(catalog.StableId));
            Assert.That(scenario.Players.Length, Is.EqualTo(2));
            Assert.That(scenario.Waves.Length,
                Is.EqualTo(catalog.WaveSequence.WaveCount));
            Assert.That(scenario.Targets.Length,
                Is.EqualTo(scenario.Waves.Sum(wave =>
                    scenario.Targets.Count(target =>
                        target.WaveIndex == wave.WaveIndex))));
            Assert.That(scenario.Targets,
                Has.All.Matches<CoopTargetSpawnDefinition>(target =>
                    !string.IsNullOrWhiteSpace(target.ArchetypeId) &&
                    !string.IsNullOrWhiteSpace(
                        target.PresentationAddress)));
            Assert.That(scenario.Targets.Select(target =>
                    target.PresentationAddress).Distinct(),
                Is.EquivalentTo(new[]
                {
                    "enemy/spider",
                    "enemy/trilobite-assault",
                    "enemy/eye-drone-support",
                    "enemy/eye-drone-suppressor",
                    "enemy/quad-shell-elite"
                }));
            Assert.That(scenario.Targets.Any(target =>
                target.DropDefinitionId == "medkit"), Is.False);
        }

        [Test]
        public void SameSeedBuildsTheSameAuthoritativeManifest()
        {
            CoopScenarioConfiguration first =
                CityNewAuthoritativeScenarioAdapter.Build(77123, 2);
            CoopScenarioConfiguration second =
                CityNewAuthoritativeScenarioAdapter.Build(77123, 2);

            Assert.That(second.Targets.Length, Is.EqualTo(first.Targets.Length));
            for (int index = 0; index < first.Targets.Length; index++)
            {
                Assert.That(second.Targets[index].ArchetypeId,
                    Is.EqualTo(first.Targets[index].ArchetypeId));
                Assert.That(second.Targets[index].Position,
                    Is.EqualTo(first.Targets[index].Position));
                Assert.That(second.Targets[index].DropDefinitionId,
                    Is.EqualTo(first.Targets[index].DropDefinitionId));
            }
        }

        [Test]
        public void ServerWaveSequenceEnforcesAliveCapAndIntermission()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(
                    tickRate: 10,
                    fireCooldownTicks: 1,
                    shotDamage: 10d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    Target(1, 1, 0),
                    Target(2, 1, 1),
                    Target(3, 2, 0)
                },
                requiredKills: 3,
                configuredWaves: new[]
                {
                    new AuthoritativeWaveDefinition(1, 1, 0, 2),
                    new AuthoritativeWaveDefinition(2, 1, 0, 0)
                });

            AuthoritativeWorldSnapshot spawned = simulation.Step(
                Array.Empty<PlayerInputCommand>()).Snapshot;
            Assert.That(spawned.CurrentWave, Is.EqualTo(1));
            Assert.That(spawned.ActiveTargets, Is.EqualTo(1));
            Assert.That(spawned.WaveMaximumAlive, Is.EqualTo(1));

            Fire(simulation, 1, 1001, 2);
            AuthoritativeWorldSnapshot secondSpawn = simulation.Step(
                Array.Empty<PlayerInputCommand>()).Snapshot;
            Assert.That(secondSpawn.ActiveTargets, Is.EqualTo(1));
            Fire(simulation, 2, 1002, 4);
            AuthoritativeWorldSnapshot intermission =
                simulation.CaptureSnapshot();
            Assert.That(intermission.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Intermission));
            Assert.That(intermission.IntermissionRemainingTicks,
                Is.GreaterThan(0));

            simulation.Step(Array.Empty<PlayerInputCommand>());
            AuthoritativeWorldSnapshot secondWave = simulation.Step(
                Array.Empty<PlayerInputCommand>()).Snapshot;
            Assert.That(secondWave.CurrentWave, Is.EqualTo(2));
            Assert.That(secondWave.ActiveTargets, Is.EqualTo(1));
        }

        [Test]
        public void RaiderFirstCommitsToARealSideFlank()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 10),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(
                        1,
                        new NetVector3(0d, 0d, 10d),
                        0.5d,
                        20d,
                        role: AuthoritativeEnemyRole.Raider,
                        moveSpeed: 3d,
                        attackDamage: 0d)
                });

            AuthoritativeTargetState moved = simulation.Step(
                Array.Empty<PlayerInputCommand>()).Snapshot.Target(1);

            Assert.That(moved.Position.X, Is.LessThan(-0.1d),
                "Raider 不应只沿玩家正前方直线追击。");
        }

        [Test]
        public void ServerPathResolverReceivesTheTacticalDestination()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 10),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(
                        1,
                        new NetVector3(0d, 0d, 10d),
                        0.5d,
                        20d,
                        moveSpeed: 2d,
                        attackDamage: 0d)
                });
            NetVector3 receivedDestination = default;
            double receivedTravel = 0d;
            simulation.SetEnemyMovementResolver(
                (_, current, destination, maximumTravel) =>
                {
                    receivedDestination = destination;
                    receivedTravel = maximumTravel;
                    return current;
                });

            simulation.Step(Array.Empty<PlayerInputCommand>());

            Assert.That(receivedDestination.Z, Is.EqualTo(0d).Within(0.001d));
            Assert.That(receivedTravel, Is.GreaterThan(0d));
            Assert.That(receivedTravel, Is.LessThan(1d));
        }

        [Test]
        public void SupportAuraReducesDamageForNearbyAlly()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(
                    tickRate: 10,
                    fireCooldownTicks: 1,
                    shotDamage: 10d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(
                        1,
                        new NetVector3(0d, 0d, 10d),
                        0.5d,
                        100d,
                        role: AuthoritativeEnemyRole.Assault,
                        moveSpeed: 0d,
                        attackDamage: 0d),
                    new CoopTargetSpawn(
                        2,
                        new NetVector3(5d, 0d, 10d),
                        0.5d,
                        100d,
                        role: AuthoritativeEnemyRole.Support,
                        moveSpeed: 0d,
                        attackDamage: 0d)
                });

            Fire(simulation, 1, 2001, 1);

            Assert.That(simulation.CaptureSnapshot().Target(1).Health,
                Is.EqualTo(92d).Within(0.001d));
        }

        private static CoopTargetSpawn Target(
            int id,
            int wave,
            int order)
        {
            return new CoopTargetSpawn(
                id,
                new NetVector3(0d, 0d, 10d),
                0.5d,
                10d,
                role: AuthoritativeEnemyRole.Assault,
                moveSpeed: 0d,
                attackDamage: 0d,
                archetypeId: "enemy.archetype.test",
                presentationAddress: "enemy/test",
                waveIndex: wave,
                spawnOrder: order);
        }

        private static void Fire(
            AuthoritativeCoopSimulation simulation,
            uint sequence,
            ulong nonce,
            long tick)
        {
            simulation.Step(new[]
            {
                new PlayerInputCommand(
                    1,
                    sequence,
                    nonce,
                    tick,
                    0d,
                    0d,
                    0d,
                    0d,
                    true,
                    default,
                    false,
                    false,
                    false,
                    "weapon.rifle",
                    default)
            });
        }
    }
}
