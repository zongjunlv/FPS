using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;

namespace FPS.Tests.Architecture
{
    public sealed class Issue95AuthoritativeCombatTests
    {
        [Test]
        public void ProjectWeaponProfilesMatchRifleAndPistolContent()
        {
            var rules = new CoopServerRules(tickRate: 60,
                fireCooldownTicks: 6, shotDamage: 10d);
            AuthoritativeWeaponDefinition[] weapons =
                AuthoritativeWeaponDefinition.CreateProjectDefaults(rules)
                    .ToArray();

            AuthoritativeWeaponDefinition rifle = weapons.Single(value =>
                value.WeaponId == "weapon.rifle");
            AuthoritativeWeaponDefinition pistol = weapons.Single(value =>
                value.WeaponId == "weapon.pistol");
            Assert.That((rifle.MagazineCapacity, rifle.InitialReserveAmmo,
                    rifle.FireIntervalTicks, rifle.BaseDamage),
                Is.EqualTo((50, 150, 6, 10d)));
            Assert.That((pistol.MagazineCapacity, pistol.InitialReserveAmmo,
                    pistol.FireIntervalTicks, pistol.BaseDamage),
                Is.EqualTo((12, 60, 17, 20d)));
            Assert.That(rifle.ReloadDurationTicks, Is.EqualTo(144));
            Assert.That(pistol.ReloadDurationTicks, Is.EqualTo(132));
        }

        [Test]
        public void ServerOwnsAmmoAndRejectsEmptyAndOverRateShots()
        {
            AuthoritativeWeaponDefinition weapon = Weapon(
                magazine: 1, reserve: 0, fireTicks: 3);
            AuthoritativeCoopSimulation simulation = Simulation(
                new[] { weapon });

            CommandResolution accepted = simulation.Step(new[]
            {
                Command(1, 1, 1, fire: true)
            }).Commands.Single();
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(simulation.CaptureSnapshot().Player(1).MagazineAmmo,
                Is.Zero);

            CommandResolution empty = simulation.Step(new[]
            {
                Command(2, 2, 2, fire: true)
            }).Commands.Single();
            Assert.That(empty.RejectionReason,
                Is.EqualTo(CommandRejectionReason.OutOfAmmo));

            simulation = Simulation(new[] { Weapon(2, 0, 3) });
            Assert.That(simulation.Step(new[]
            {
                Command(1, 1, 1, fire: true)
            }).Commands.Single().Accepted, Is.True);
            Assert.That(simulation.Step(new[]
            {
                Command(2, 2, 2, fire: true)
            }).Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.FireRateExceeded));
        }

        [Test]
        public void ReloadTransfersOnlyMissingRoundsAfterServerDuration()
        {
            AuthoritativeCoopSimulation simulation = Simulation(new[]
            {
                Weapon(5, 9, 1, reloadTicks: 3)
            });
            simulation.Step(new[] { Command(1, 1, 1, fire: true) });

            WeaponActionResolution reload = simulation.ApplyWeaponAction(
                1, AuthoritativeWeaponAction.Reload, "weapon.rifle");
            Assert.That(reload.Accepted, Is.True);
            Assert.That(simulation.CaptureSnapshot().Player(1).Reloading,
                Is.True);
            Assert.That(simulation.Step(new[]
            {
                Command(2, 2, 2, fire: true)
            }).Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.Reloading));
            simulation.Step(Array.Empty<PlayerInputCommand>());
            AuthoritativeTickResult completed = simulation.Step(
                Array.Empty<PlayerInputCommand>());

            AuthoritativePlayerState state = completed.Snapshot.Player(1);
            Assert.That(state.Reloading, Is.False);
            Assert.That(state.MagazineAmmo, Is.EqualTo(5));
            Assert.That(state.ReserveAmmo, Is.EqualTo(8));
            Assert.That(completed.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.ReloadCompleted));
        }

        [Test]
        public void SwitchBlocksFireThenRestoresEachWeaponsIndependentAmmo()
        {
            AuthoritativeCoopSimulation simulation = Simulation(
                AuthoritativeWeaponDefinition.CreateProjectDefaults(
                    new CoopServerRules(tickRate: 10,
                        fireCooldownTicks: 1, shotDamage: 10d)));
            simulation.Step(new[] { Command(1, 1, 1, fire: true) });
            WeaponActionResolution switching = simulation.ApplyWeaponAction(
                1, AuthoritativeWeaponAction.SwitchWeapon, "weapon.pistol");
            Assert.That(switching.Accepted, Is.True);

            Assert.That(simulation.Step(new[]
            {
                Command(2, 2, 2, fire: true, weapon: "weapon.pistol")
            }).Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.WeaponMismatch));
            for (int index = 0; index < 5; index++)
                simulation.Step(Array.Empty<PlayerInputCommand>());
            AuthoritativePlayerState state = simulation.CaptureSnapshot()
                .Player(1);
            Assert.That(state.EquippedWeaponId, Is.EqualTo("weapon.pistol"));
            Assert.That(state.MagazineAmmo, Is.EqualTo(12));
            Assert.That(state.Weapons.Single(value =>
                value.WeaponId == "weapon.rifle").MagazineAmmo,
                Is.EqualTo(49));
        }

        [Test]
        public void RewoundHeadAndBodyHitsUseServerGeometryAndDamage()
        {
            AuthoritativeCoopSimulation body = Simulation(
                AuthoritativeWeaponDefinition.CreateProjectDefaults(
                    new CoopServerRules(fireCooldownTicks: 1,
                        shotDamage: 10d)),
                new CoopTargetSpawn(1, new NetVector3(0d, 0d, 10d),
                    0.45d, 100d, headOffset: new NetVector3(0d, 1d, 0d),
                    headRadius: 0.3d));
            ShotResolution bodyShot = body.Step(new[]
            {
                Command(1, 1, 1, fire: true)
            }).Commands.Single().Shot;
            Assert.That(bodyShot.HitRegion,
                Is.EqualTo(AuthoritativeHitRegion.Body));
            Assert.That(bodyShot.AppliedDamage, Is.EqualTo(10d));

            AuthoritativeCoopSimulation head = Simulation(
                AuthoritativeWeaponDefinition.CreateProjectDefaults(
                    new CoopServerRules(fireCooldownTicks: 1,
                        shotDamage: 10d)),
                new CoopTargetSpawn(1, new NetVector3(0d, 0d, 10d),
                    0.45d, 100d, headOffset: new NetVector3(0d, 1d, 0d),
                    headRadius: 0.3d));
            ShotResolution headShot = head.Step(new[]
            {
                Command(1, 1, 1, fire: true,
                    origin: new NetVector3(0d, 1d, 0d))
            }).Commands.Single().Shot;
            Assert.That(headShot.HitRegion,
                Is.EqualTo(AuthoritativeHitRegion.Head));
            Assert.That(headShot.AppliedDamage, Is.EqualTo(20d));
            Assert.That(headShot.EndPoint.Z, Is.InRange(9.69d, 9.71d));
        }

        [Test]
        public void ServerLineOfSightCanBlockHistoricalTargetDamage()
        {
            AuthoritativeCoopSimulation simulation = Simulation();
            simulation.SetShotObstructionResolver((_, _, _) =>
                AuthoritativeShotObstruction.At(
                    new NetVector3(0d, 0d, 4d),
                    new NetVector3(0d, 0d, -1d),
                    AuthoritativeSurface.Metal));

            ShotResolution shot = simulation.Step(new[]
            {
                Command(1, 1, 1, fire: true)
            }).Commands.Single().Shot;

            Assert.That(shot.Kind, Is.EqualTo(ShotResolutionKind.Blocked));
            Assert.That(shot.Surface,
                Is.EqualTo(AuthoritativeSurface.Metal));
            Assert.That(shot.EndPoint, Is.EqualTo(
                new NetVector3(0d, 0d, 4d)));
            Assert.That(shot.Normal, Is.EqualTo(
                new NetVector3(0d, 0d, -1d)));
            Assert.That(simulation.CaptureSnapshot().Target(1).Health,
                Is.EqualTo(100d));
        }

        [Test]
        public void UnknownMismatchedAndImpossibleOriginShotsAreRejected()
        {
            AuthoritativeCoopSimulation unknown = Simulation();
            Assert.That(unknown.Step(new[]
            {
                Command(1, 1, 1, true, "weapon.hacked")
            }).Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.UnknownWeapon));

            AuthoritativeCoopSimulation origin = Simulation();
            Assert.That(origin.Step(new[]
            {
                Command(1, 1, 1, true, "weapon.rifle",
                    new NetVector3(100d, 0d, 0d))
            }).Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.InvalidShotOrigin));
        }

        [Test]
        public void ShotFeedbackWirePayloadKeepsAuthoritativeOutcome()
        {
            var source = new NetcodeShotFeedbackEvent
            {
                ServerTick = 90,
                Sequence = 12,
                ShooterPlayerId = 2,
                ShotCommandSequence = 44,
                WeaponId = "weapon.pistol",
                Kind = ShotResolutionKind.Killed,
                TargetId = 17,
                Origin = new UnityEngine.Vector3(1f, 2f, 3f),
                EndPoint = new UnityEngine.Vector3(4f, 5f, 6f),
                Normal = UnityEngine.Vector3.back,
                HitRegion = AuthoritativeHitRegion.Head,
                Surface = AuthoritativeSurface.Flesh,
                AppliedDamage = 20f
            };
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteNetworkSerializable(source);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetcodeShotFeedbackEvent copy);

            Assert.That(copy, Is.EqualTo(source));
            Assert.That(copy.DidHit, Is.True);
        }

        [Test]
        public void ConfirmedWeaponVisualsUseBoundedPrewarmedPools()
        {
            var host = new UnityEngine.GameObject(
                "Issue95 Weapon Feedback Pool");
            try
            {
                NetworkWeaponFeedbackPool pool =
                    host.AddComponent<NetworkWeaponFeedbackPool>();
                pool.PlayMuzzle(default, UnityEngine.Vector3.forward);
                pool.EjectCasing(default, UnityEngine.Vector3.forward);
                Assert.That(pool.MuzzleFlashCapacity, Is.EqualTo(16));
                Assert.That(pool.CasingCapacityValue, Is.EqualTo(32));

                for (int index = 1; index < 20; index++)
                    pool.PlayMuzzle(default, UnityEngine.Vector3.forward);
                for (int index = 1; index < 40; index++)
                    pool.EjectCasing(default, UnityEngine.Vector3.forward);

                Assert.That(pool.ActiveMuzzleFlashCount, Is.EqualTo(16));
                Assert.That(pool.ActiveCasingCount, Is.EqualTo(32));
                pool.ReturnAll();
                Assert.That(pool.ActiveMuzzleFlashCount, Is.Zero);
                Assert.That(pool.ActiveCasingCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static AuthoritativeCoopSimulation Simulation(
            IEnumerable<AuthoritativeWeaponDefinition> weapons = null,
            CoopTargetSpawn? target = null)
        {
            return new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60,
                    fireCooldownTicks: 1, shotDamage: 10d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    target ?? new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d, 100d)
                },
                configuredWeapons: weapons);
        }

        private static AuthoritativeWeaponDefinition Weapon(
            int magazine,
            int reserve,
            int fireTicks,
            int reloadTicks = 3)
        {
            return new AuthoritativeWeaponDefinition(
                "weapon.rifle", magazine, reserve, fireTicks,
                reloadTicks, 3, 120d, 10d, 2d, true);
        }

        private static PlayerInputCommand Command(
            uint sequence,
            ulong nonce,
            long tick,
            bool fire = false,
            string weapon = "weapon.rifle",
            NetVector3 origin = default)
        {
            return new PlayerInputCommand(1, sequence, nonce, tick,
                0d, 0d, 0d, 0d, fire, default,
                false, false, false, weapon, origin);
        }
    }
}
