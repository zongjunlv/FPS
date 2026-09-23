using System;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

namespace FPS.Tests.Architecture
{
    public sealed class Issue97AuthoritativeEconomyTests
    {
        [Test]
        public void CompetingPickupAllowsOnlyOnePlayerToClaimWholeDrop()
        {
            AuthoritativeCoopSimulation simulation = Simulation(twoPlayers: true);
            int dropId = simulation.SpawnServerWorldDrop(
                "medical_kit", 2, default);

            AuthoritativeTickResult result = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    Command(2, 1, 201, AuthoritativeEconomyCommandKind.Pickup,
                        entityId: dropId, expectedDropRevision: 1),
                    Command(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                        entityId: dropId, expectedDropRevision: 1)
                });

            Assert.That(result.EconomyCommands.Count(value => value.Accepted),
                Is.EqualTo(1));
            Assert.That(result.EconomyCommands.Single(value => value.Accepted)
                .Command.PlayerId, Is.EqualTo(1));
            Assert.That(result.Snapshot.Economy.Drop(dropId).Available,
                Is.False);
            Assert.That(Quantity(result.Snapshot, 1, "medical_kit"),
                Is.EqualTo(2));
            Assert.That(Quantity(result.Snapshot, 2, "medical_kit"), Is.Zero);
        }

        [Test]
        public void DuplicateCommandCannotDuplicateInventoryOrWorldDrop()
        {
            AuthoritativeCoopSimulation simulation = Simulation();
            int dropId = simulation.SpawnServerWorldDrop(
                "armor_pack", 1, default);
            AuthoritativeEconomyCommand pickup = Command(
                1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                entityId: dropId);

            simulation.Step(Array.Empty<PlayerInputCommand>(), new[] { pickup });
            AuthoritativeTickResult duplicate = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[] { pickup });

            Assert.That(Quantity(duplicate.Snapshot, 1, "armor_pack"),
                Is.EqualTo(1));
            Assert.That(duplicate.EconomyCommands.Single().Accepted, Is.False);
            Assert.That(duplicate.EconomyCommands.Single().Rejection,
                Is.EqualTo(AuthoritativeEconomyRejection.InvalidSequence));
            Assert.That(duplicate.Snapshot.Economy.WorldDrops.Count,
                Is.EqualTo(1));
        }

        [Test]
        public void StaleInventoryRevisionCannotMoveNewerSlotState()
        {
            AuthoritativeCoopSimulation simulation = Simulation();
            int dropId = simulation.SpawnServerWorldDrop(
                "medical_kit", 2, default);
            simulation.Step(Array.Empty<PlayerInputCommand>(), new[]
            {
                Command(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: dropId, expectedInventoryRevision: 1)
            });

            AuthoritativeTickResult stale = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    Command(1, 2, 102,
                        AuthoritativeEconomyCommandKind.Transfer,
                        sourceSlot: 0, destinationSlot: 1,
                        expectedInventoryRevision: 1)
                });

            Assert.That(stale.EconomyCommands.Single().Rejection,
                Is.EqualTo(AuthoritativeEconomyRejection.StaleInventory));
            Assert.That(stale.Snapshot.Economy.InventorySlots.Single(value =>
                value.PlayerId == 1 && value.SlotIndex == 0).Quantity,
                Is.EqualTo(2));
        }

        [Test]
        public void ServerUseConsumesOnceAndRestoresAuthoritativeHealth()
        {
            AuthoritativeCoopSimulation simulation = Simulation();
            int dropId = simulation.SpawnServerWorldDrop(
                "medical_kit", 2, default);
            simulation.Step(Array.Empty<PlayerInputCommand>(), new[]
            {
                Command(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: dropId)
            });
            simulation.ApplyServerDamageToPlayer(1, 60d);
            AuthoritativeEconomyCommand use = Command(
                1, 2, 102, AuthoritativeEconomyCommandKind.Use,
                sourceSlot: 0);

            AuthoritativeTickResult used = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[] { use });
            AuthoritativeTickResult duplicate = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[] { use });

            Assert.That(used.Snapshot.Player(1).Health, Is.EqualTo(75d));
            Assert.That(Quantity(used.Snapshot, 1, "medical_kit"),
                Is.EqualTo(1));
            Assert.That(duplicate.Snapshot.Player(1).Health, Is.EqualTo(75d));
            Assert.That(Quantity(duplicate.Snapshot, 1, "medical_kit"),
                Is.EqualTo(1));
        }

        [Test]
        public void ArmorAndAmmoConsumablesMutateAuthoritativePlayerState()
        {
            AuthoritativeCoopSimulation armorSimulation = Simulation();
            int armorDrop = armorSimulation.SpawnServerWorldDrop(
                "armor_pack", 1, default);
            armorSimulation.Step(Array.Empty<PlayerInputCommand>(), new[]
            {
                Command(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: armorDrop)
            });
            AuthoritativeWorldSnapshot armored = armorSimulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    Command(1, 2, 102, AuthoritativeEconomyCommandKind.Use,
                        sourceSlot: 0)
                }).Snapshot;
            Assert.That(armored.Player(1).Armor, Is.EqualTo(35d));
            Assert.That(Quantity(armored, 1, "armor_pack"), Is.Zero);

            AuthoritativeCoopSimulation ammoSimulation = Simulation();
            int ammoDrop = ammoSimulation.SpawnServerWorldDrop(
                "rifle_ammo", 1, default);
            int before = ammoSimulation.CaptureSnapshot().Player(1)
                .ReserveAmmo;
            ammoSimulation.Step(Array.Empty<PlayerInputCommand>(), new[]
            {
                Command(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: ammoDrop)
            });
            AuthoritativeWorldSnapshot replenished = ammoSimulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    Command(1, 2, 102, AuthoritativeEconomyCommandKind.Use,
                        sourceSlot: 0)
                }).Snapshot;
            Assert.That(replenished.Player(1).ReserveAmmo,
                Is.EqualTo(before + 60));
        }

        [Test]
        public void ConfirmedKillGrantsExperienceAndCreatesServerDrop()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 100d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d, 10d,
                        "medical_kit", moveSpeed: 0d, attackDamage: 0d,
                        rewardExperience: 100)
                });
            AuthoritativeTickResult killed = simulation.Step(new[]
            {
                new PlayerInputCommand(1, 1, 101, 1,
                    0d, 0d, 0d, 0d, true, default,
                    false, false, false, "weapon.rifle", default)
            });

            Assert.That(killed.Snapshot.Economy.Player(1).Level,
                Is.EqualTo(2));
            Assert.That(killed.Snapshot.Economy.WorldDrops.Count,
                Is.EqualTo(1));
            Assert.That(killed.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.ExperienceGranted));
            Assert.That(killed.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.WorldDropSpawned));
        }

        [Test]
        public void EliteKillPublishesEveryRolledLootStack()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 100d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d, 10d,
                        role: AuthoritativeEnemyRole.Elite,
                        lootDrops: new[]
                        {
                            new AuthoritativeLootStack("medical_kit", 2),
                            new AuthoritativeLootStack("armor_pack", 1)
                        })
                });

            AuthoritativeTickResult result = simulation.Step(new[]
            {
                new PlayerInputCommand(1, 1, 101, 1,
                    0d, 0d, 0d, 0d, true, default,
                    false, false, false, "weapon.rifle", default)
            });

            Assert.That(result.Snapshot.Economy.WorldDrops.Select(value =>
                    value.ItemId),
                Is.EquivalentTo(new[] { "medical_kit", "armor_pack" }));
            Assert.That(result.Events.Count(value =>
                value.Kind == AuthoritativeEventKind.WorldDropSpawned),
                Is.EqualTo(2));
        }

        [Test]
        public void WaveAndFinalRewardsAreGrantedOnceAtTheirBoundaries()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 100d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d, 10d,
                        waveIndex: 1),
                    new CoopTargetSpawn(2,
                        new NetVector3(0d, 0d, 10d), 0.5d, 10d,
                        waveIndex: 2)
                },
                configuredWaves: new[]
                {
                    new AuthoritativeWaveDefinition(1, 1, 0, 0,
                        waveRewards: new[]
                        {
                            new AuthoritativeLootStack("medical_kit", 1)
                        }),
                    new AuthoritativeWaveDefinition(2, 1, 0, 0,
                        finalRewards: new[]
                        {
                            new AuthoritativeLootStack("armor_pack", 1)
                        })
                });

            for (uint sequence = 1; sequence <= 2; sequence++)
            {
                simulation.Step(new[]
                {
                    new PlayerInputCommand(1, sequence,
                        100UL + sequence, sequence,
                        0d, 0d, 0d, 0d, true, default,
                        false, false, false, "weapon.rifle", default)
                });
            }

            Assert.That(simulation.CaptureSnapshot().Economy.WorldDrops
                    .Select(value => value.ItemId),
                Is.EquivalentTo(new[] { "medical_kit", "armor_pack" }));
            simulation.Step(Array.Empty<PlayerInputCommand>());
            Assert.That(simulation.CaptureSnapshot().Economy.WorldDrops.Count,
                Is.EqualTo(2));
        }

        [Test]
        public void MovementModifierUsesTheSameRuleForServerAndPrediction()
        {
            var rules = new CoopServerRules(tickRate: 60,
                maximumAcceleration: 1000d);
            var start = new PlayerMovementState(default, default,
                0d, 0d, PlayerStance.Standing, true, long.MinValue, 0d);
            var command = new PlayerInputCommand(1, 1, 101, 1,
                0d, 1d, 0d, 0d, false, default);
            PlayerMovementState baseMovement =
                CoopGameplayRules.IntegrateMovement(
                    start, command, 60, rules);
            PlayerMovementState boostedMovement =
                CoopGameplayRules.IntegrateMovement(
                    start, command, 60, rules,
                    movementSpeedMultiplier: 1.1d);

            Assert.That(boostedMovement.Position.Z,
                Is.GreaterThan(baseMovement.Position.Z));
            var prediction = new LocalPredictionBuffer(rules, 1, default)
            {
                MovementSpeedMultiplier = 1.1d
            };
            Assert.That(prediction.Predict(command).Z,
                Is.EqualTo(CoopGameplayRules.IntegrateMovement(
                    start, command, 1, rules,
                    movementSpeedMultiplier: 1.1d).Position.Z)
                    .Within(0.000001d));
        }

        [Test]
        public void ExperienceOverflowOffersCardsAndSelectionIsIdempotent()
        {
            AuthoritativeCoopSimulation simulation = Simulation();
            simulation.ApplyServerDamageToPlayer(1, 20d);
            int levels = simulation.GrantServerExperience(1, 260);
            AuthoritativeProgressionState offered = simulation.CaptureSnapshot()
                .Economy.Player(1);

            Assert.That(levels, Is.EqualTo(2));
            Assert.That(offered.Level, Is.EqualTo(3));
            Assert.That(offered.CurrentExperience, Is.EqualTo(10));
            Assert.That(offered.PendingUpgradeChoices, Is.EqualTo(2));
            Assert.That(offered.CandidateIds.Count, Is.EqualTo(3));

            AuthoritativeEconomyCommand select = Command(
                1, 1, 101, AuthoritativeEconomyCommandKind.SelectUpgrade,
                candidateIndex: 0, choiceGeneration: offered.ChoiceGeneration);
            AuthoritativeTickResult applied = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[] { select });
            AuthoritativeTickResult duplicate = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[] { select });

            Assert.That(applied.EconomyCommands.Single().Accepted, Is.True);
            Assert.That(applied.Snapshot.Economy.Upgrades.Sum(value =>
                value.Level), Is.EqualTo(1));
            Assert.That(duplicate.Snapshot.Economy.Upgrades.Sum(value =>
                value.Level), Is.EqualTo(1));
            Assert.That(duplicate.Snapshot.Economy.Player(1)
                .PendingUpgradeChoices, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotRebuildContainsSlotsDropsExperienceAndBuildTags()
        {
            var upgrades = new[]
            {
                new AuthoritativeUpgradeDefinition("test.damage", 3,
                    AuthoritativeUpgradeEffect.WeaponDamage, 0.25d,
                    "build.damage")
            };
            AuthoritativeCoopSimulation simulation = Simulation(
                configuredUpgrades: upgrades);
            int picked = simulation.SpawnServerWorldDrop(
                "rifle_ammo", 2, default);
            int pending = simulation.SpawnServerWorldDrop(
                "armor_pack", 1, new NetVector3(2d, 0d, 0d));
            simulation.Step(Array.Empty<PlayerInputCommand>(), new[]
            {
                Command(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: picked)
            });
            simulation.ApplyServerDamageToPlayer(1, 1d);
            simulation.GrantServerExperience(1, 100);
            AuthoritativeProgressionState offer = simulation.CaptureSnapshot()
                .Economy.Player(1);
            AuthoritativeWorldSnapshot snapshot = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    Command(1, 2, 102,
                        AuthoritativeEconomyCommandKind.SelectUpgrade,
                        candidateIndex: 0,
                        choiceGeneration: offer.ChoiceGeneration)
                }).Snapshot;

            Assert.That(snapshot.Economy.InventorySlots.Count(value =>
                value.PlayerId == 1), Is.EqualTo(12));
            Assert.That(snapshot.Economy.Drop(pending).Available, Is.True);
            Assert.That(snapshot.Economy.Player(1).Level, Is.EqualTo(2));
            Assert.That(snapshot.Economy.Player(1).BuildTags,
                Does.Contain("build.damage"));
            Assert.That(snapshot.Economy.Upgrades.Single().UpgradeId,
                Is.EqualTo("test.damage"));
        }

        [Test]
        public void EconomyWirePayloadsRoundTripWithoutLosingRevisions()
        {
            var command = new NetcodeEconomyCommand
            {
                PlayerId = 2, Sequence = 9, Nonce = 99,
                Kind = AuthoritativeEconomyCommandKind.Split,
                SourceSlot = 1, DestinationSlot = 4, Quantity = 2,
                ExpectedInventoryRevision = 7,
                ExpectedDropRevision = 3,
                ChoiceGeneration = 5,
                ExpectedItemId = "medical_kit"
            };
            Assert.That(RoundTrip(command), Is.EqualTo(command));

            var progression = new NetcodeProgressionState
            {
                PlayerId = 2, Level = 4, CurrentExperience = 22,
                ExperienceToNextLevel = 325, TotalExperience = 497,
                PendingUpgradeChoices = 1,
                Candidate0 = "test.damage", BuildTags = "build.damage",
                AcknowledgedEconomySequence = 9,
                InventoryRevision = 7, ChoiceGeneration = 5,
                NextConsumableUseTick = 150
            };
            Assert.That(RoundTrip(progression), Is.EqualTo(progression));
        }

        private static T RoundTrip<T>(T source)
            where T : unmanaged, INetworkSerializable
        {
            using var writer = new FastBufferWriter(2048, Allocator.Temp);
            writer.WriteNetworkSerializable(source);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out T copy);
            return copy;
        }

        private static AuthoritativeCoopSimulation Simulation(
            bool twoPlayers = false,
            AuthoritativeUpgradeDefinition[] configuredUpgrades = null)
        {
            CoopPlayerSpawn[] players = twoPlayers
                ? new[]
                {
                    new CoopPlayerSpawn(1, default),
                    new CoopPlayerSpawn(2, default)
                }
                : new[] { new CoopPlayerSpawn(1, default) };
            return new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 25d),
                players,
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 30d), 0.5d, 100d,
                        moveSpeed: 0d, attackDamage: 0d)
                },
                configuredUpgrades: configuredUpgrades);
        }

        private static AuthoritativeEconomyCommand Command(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeEconomyCommandKind kind,
            int entityId = 0,
            int sourceSlot = -1,
            int destinationSlot = -1,
            int quantity = 0,
            int candidateIndex = -1,
            int expectedInventoryRevision = 0,
            int expectedDropRevision = 0,
            int choiceGeneration = 0) => new(
                playerId, sequence, nonce, kind, entityId, sourceSlot,
                destinationSlot, quantity, candidateIndex,
                expectedInventoryRevision, expectedDropRevision,
                choiceGeneration);

        private static int Quantity(
            AuthoritativeWorldSnapshot snapshot,
            int playerId,
            string itemId) => snapshot.Economy.InventorySlots
            .Where(value => value.PlayerId == playerId &&
                value.ItemId == itemId)
            .Sum(value => value.Quantity);
    }
}
