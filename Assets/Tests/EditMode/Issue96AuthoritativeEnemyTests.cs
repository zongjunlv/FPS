using System;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

namespace FPS.Tests.Architecture
{
    public sealed class Issue96AuthoritativeEnemyTests
    {
        [Test]
        public void ScheduledSpawnQueueUsesFixedCapacityAndPublishesRole()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                new CoopTargetSpawn(1, new NetVector3(0d, 0d, 8d),
                    0.5d, 20d, role: AuthoritativeEnemyRole.Raider,
                    spawnTick: 3, moveSpeed: 2d));

            AuthoritativeWorldSnapshot initial = simulation.CaptureSnapshot();
            Assert.That(initial.EnemyPoolCapacity, Is.EqualTo(1));
            Assert.That(initial.ActiveTargets, Is.Zero);
            Assert.That(initial.PendingTargets, Is.EqualTo(1));
            Assert.That(initial.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Spawning));

            simulation.Step(Array.Empty<PlayerInputCommand>());
            simulation.Step(Array.Empty<PlayerInputCommand>());
            AuthoritativeTickResult spawned = simulation.Step(
                Array.Empty<PlayerInputCommand>());

            Assert.That(spawned.Snapshot.ActiveTargets, Is.EqualTo(1));
            Assert.That(spawned.Snapshot.PendingTargets, Is.Zero);
            Assert.That(spawned.Snapshot.Target(1).Role,
                Is.EqualTo(AuthoritativeEnemyRole.Raider));
            Assert.That(spawned.Snapshot.Target(1).SpawnGeneration,
                Is.EqualTo(1));
            Assert.That(spawned.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.TargetSpawned));
        }

        [Test]
        public void UtilityChoosesNearestPlayerAndServerMovesEnemy()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 10),
                new[]
                {
                    new CoopPlayerSpawn(1, new NetVector3(-8d, 0d, 0d)),
                    new CoopPlayerSpawn(2, new NetVector3(2d, 0d, 0d))
                },
                new[]
                {
                    new CoopTargetSpawn(1, default, 0.5d, 20d,
                        role: AuthoritativeEnemyRole.Assault,
                        moveSpeed: 2d, attackDamage: 0d)
                });

            AuthoritativeTargetState before = simulation.CaptureSnapshot()
                .Target(1);
            AuthoritativeTargetState after = simulation.Step(
                Array.Empty<PlayerInputCommand>()).Snapshot.Target(1);

            Assert.That(after.TargetPlayerId, Is.EqualTo(2));
            Assert.That(after.Behavior,
                Is.EqualTo(AuthoritativeEnemyBehavior.Pursue));
            Assert.That(after.Position.X, Is.GreaterThan(before.Position.X));
            Assert.That(after.Position.X, Is.EqualTo(0.2d).Within(0.0001d));
        }

        [Test]
        public void EnemyAttackUsesServerCooldownAndCanFailWave()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 10),
                new[] { new CoopPlayerSpawn(1, default, 10d) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 1d), 0.5d, 20d,
                        attackRange: 2d, attackDamage: 5d,
                        attackIntervalTicks: 2)
                });

            AuthoritativeTickResult first = simulation.Step(
                Array.Empty<PlayerInputCommand>());
            Assert.That(first.Snapshot.Player(1).Health, Is.EqualTo(5d));
            Assert.That(first.Events.Count(value => value.Kind ==
                AuthoritativeEventKind.TargetAttacked), Is.EqualTo(1));

            AuthoritativeTickResult cooldown = simulation.Step(
                Array.Empty<PlayerInputCommand>());
            Assert.That(cooldown.Snapshot.Player(1).Health, Is.EqualTo(5d));

            AuthoritativeTickResult lethal = simulation.Step(
                Array.Empty<PlayerInputCommand>());
            Assert.That(lethal.Snapshot.Player(1).Health, Is.Zero);
            Assert.That(lethal.Snapshot.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Failed));
        }

        [Test]
        public void HeadlessKernelCanSpawnKillAndCompleteWholeWave()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 10d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    Target(1, 0), Target(2, 2), Target(3, 4)
                }, requiredKills: 3);

            uint sequence = 1;
            ulong nonce = 100;
            Fire(simulation, sequence++, nonce++, 1);
            Fire(simulation, sequence++, nonce++, 2);
            simulation.Step(Array.Empty<PlayerInputCommand>());
            AuthoritativeTickResult completed = Fire(
                simulation, sequence, nonce, 4);

            Assert.That(completed.Snapshot.KilledTargets, Is.EqualTo(3));
            Assert.That(completed.Snapshot.RemainingTargets, Is.Zero);
            Assert.That(completed.Snapshot.ActiveTargets, Is.Zero);
            Assert.That(completed.Snapshot.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Completed));
            Assert.That(completed.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.WaveCompleted));

            for (int index = 0; index < 15; index++)
                simulation.Step(Array.Empty<PlayerInputCommand>());
            Assert.That(simulation.CaptureSnapshot().Targets.All(value =>
                    value.Behavior == AuthoritativeEnemyBehavior.Pooled),
                Is.True);
        }

        [Test]
        public void SameInputsProduceSameEnemyTargetsAndPositions()
        {
            AuthoritativeCoopSimulation left = Simulation(
                new CoopTargetSpawn(1, new NetVector3(4d, 0d, 8d),
                    0.5d, 20d, role: AuthoritativeEnemyRole.Elite,
                    moveSpeed: 2.5d));
            AuthoritativeCoopSimulation right = Simulation(
                new CoopTargetSpawn(1, new NetVector3(4d, 0d, 8d),
                    0.5d, 20d, role: AuthoritativeEnemyRole.Elite,
                    moveSpeed: 2.5d));

            for (int index = 0; index < 90; index++)
            {
                AuthoritativeTargetState a = left.Step(
                    Array.Empty<PlayerInputCommand>()).Snapshot.Target(1);
                AuthoritativeTargetState b = right.Step(
                    Array.Empty<PlayerInputCommand>()).Snapshot.Target(1);
                Assert.That(a.Position, Is.EqualTo(b.Position));
                Assert.That(a.TargetPlayerId, Is.EqualTo(b.TargetPlayerId));
                Assert.That(a.Behavior, Is.EqualTo(b.Behavior));
            }
        }

        [Test]
        public void ClientPayloadMutationCannotChangeServerSnapshot()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                Target(1, 0));
            AuthoritativeTargetState server = simulation.CaptureSnapshot()
                .Target(1);
            NetcodeTargetState client = NetcodeTargetState.FromDomain(server);
            client.Health = 0f;
            client.Active = false;

            AuthoritativeTargetState unchanged = simulation.CaptureSnapshot()
                .Target(1);
            Assert.That(unchanged.Health, Is.EqualTo(10d));
            Assert.That(unchanged.IsAlive, Is.True);
        }

        [Test]
        public void TargetWirePayloadKeepsRoleBehaviorAndGeneration()
        {
            var source = new NetcodeTargetState
            {
                TargetId = 7,
                Position = new UnityEngine.Vector3(1f, 2f, 3f),
                Radius = 1.2f,
                Health = 42f,
                MaximumHealth = 80f,
                DropDefinitionId = "armor_plate",
                HeadOffset = UnityEngine.Vector3.up,
                HeadRadius = 0.3f,
                Active = true,
                YawDegrees = 127f,
                Role = AuthoritativeEnemyRole.Suppressor,
                Behavior = AuthoritativeEnemyBehavior.Attack,
                TargetPlayerId = 2,
                SpawnGeneration = 3
            };
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteNetworkSerializable(source);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetcodeTargetState copy);

            Assert.That(copy, Is.EqualTo(source));
            Assert.That(copy.IsAlive, Is.True);
            Assert.That(copy.MaximumHealth, Is.EqualTo(80f));
        }

        [Test]
        public void WorldWirePayloadKeepsPoolAndWaveCounters()
        {
            var source = new NetcodeWorldState
            {
                ServerTick = 90,
                WaveStatus = AuthoritativeWaveStatus.Fighting,
                KilledTargets = 2,
                RequiredKills = 6,
                EnemyPoolCapacity = 6,
                ActiveEnemyCount = 3,
                PendingEnemyCount = 1,
                RemainingEnemyCount = 4,
                LastEventSequence = 77
            };
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteNetworkSerializable(source);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetcodeWorldState copy);

            Assert.That(copy, Is.EqualTo(source));
        }

        private static AuthoritativeCoopSimulation Simulation(
            CoopTargetSpawn target)
        {
            return new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 10,
                    fireCooldownTicks: 1, shotDamage: 10d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { target });
        }

        private static CoopTargetSpawn Target(int id, long spawnTick)
        {
            return new CoopTargetSpawn(id,
                new NetVector3(0d, 0d, 10d), 0.5d, 10d,
                role: AuthoritativeEnemyRole.Assault,
                spawnTick: spawnTick,
                moveSpeed: 0d,
                attackDamage: 0d);
        }

        private static AuthoritativeTickResult Fire(
            AuthoritativeCoopSimulation simulation,
            uint sequence,
            ulong nonce,
            long tick)
        {
            return simulation.Step(new[]
            {
                new PlayerInputCommand(1, sequence, nonce, tick,
                    0d, 0d, 0d, 0d, true, default,
                    false, false, false, "weapon.rifle", default)
            });
        }
    }
}
