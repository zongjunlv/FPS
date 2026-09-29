using System;
using System.Linq;
using FPS.Networking.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class ShotDirectionValidationTests
    {
        [TestCase(-1d)]
        [TestCase(1d)]
        public void DistantTargetCannotBeHitOutsideThePlayersAimAndWeaponSpread(double side)
        {
            var targetPosition = new NetVector3(side * 30d, 1.5d, 40d);
            var simulation = new AuthoritativeCoopSimulation(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, targetPosition, 0.25d, 100d,
                    bodyHalfExtents: new NetVector3(0.25d, 0.25d, 0.25d)) });
            var command = new PlayerInputCommand(1, 1, 1, 1,
                0d, 0d, 0d, 0d, true, default, false, false, false,
                "weapon.rifle", new NetVector3(0d, 1.5d, 0d),
                shotViewTick: 0, shotDirection: new NetVector3(side * 0.6d, 0d, 0.8d));

            AuthoritativeTickResult result = simulation.Step(new[] { command });

            Assert.That(result.Commands[0].RejectionReason,
                Is.EqualTo(CommandRejectionReason.InvalidAim),
                "固定角色瞄准角不能允许客户端射线指向50米处横向偏离30米的目标。");
            Assert.That(result.Snapshot.Target(1).Health, Is.EqualTo(100d));
        }

        [TestCase(1.05d)]
        [TestCase(1.25d)]
        [TestCase(1.6d)]
        [TestCase(1.65d)]
        public void NearbyCameraTargetAllowsNormalOffsetMuzzleConvergence(double cameraHeight)
        {
            var target = new NetVector3(0d, cameraHeight, 1.25d);
            var origin = new NetVector3(0.5d, cameraHeight - 0.15d, 0d);
            var simulation = Simulation(target);

            CommandResolution result = Fire(simulation, "weapon.rifle", origin,
                (target - origin).Normalized);

            Assert.That(result.Accepted, Is.True,
                "近距准星目标与偏移枪口之间的正常视差不能被远距散布上限误拒绝。");
            Assert.That(result.Shot.DidHit, Is.True);
        }

        [TestCase("weapon.rifle", "Assets/DefineAssets/Weapon/AR.asset")]
        [TestCase("weapon.pistol", "Assets/DefineAssets/Weapon/Pistol.asset")]
        public void ProjectMaximumSpreadMatchesAssetsAndCanStillHitAtRange(string weaponId, string assetPath)
        {
            var serialized = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(assetPath));
            double sourceMaximum = Math.Max(
                    serialized.FindProperty("HipSpreadDegrees").floatValue,
                    serialized.FindProperty("AdsSpreadDegrees").floatValue) +
                serialized.FindProperty("MovementSpreadBonus").floatValue +
                serialized.FindProperty("SprintSpreadBonus").floatValue +
                serialized.FindProperty("MaxShotSpread").floatValue;
            var definition = AuthoritativeWeaponDefinition.CreateProjectDefaults(new CoopServerRules())
                .Single(value => value.WeaponId == weaponId);
            Assert.That(definition.MaxSpreadDegrees, Is.EqualTo(sourceMaximum).Within(0.000001d),
                "权威上限必须覆盖正式资产的移动、疾跑与最大连续开火散布。");

            NetVector3 direction = DirectionAtYaw(sourceMaximum);
            var origin = new NetVector3(0d, 1.5d, 0d);
            var simulation = Simulation(origin + direction * 50d);
            CommandResolution result = Fire(simulation, weaponId, origin, direction);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Shot.DidHit, Is.True);
        }

        [TestCase("weapon.rifle", 8.2d)]
        [TestCase("weapon.pistol", 8.6d)]
        public void DirectionWithinSpreadAndCameraRecoilBudgetCanHitDistantTarget(
            string weaponId, double angle)
        {
            NetVector3 direction = DirectionAtYaw(angle);
            var origin = new NetVector3(0d, 1.5d, 0d);
            var simulation = Simulation(origin + direction * 50d);

            CommandResolution result = Fire(simulation, weaponId, origin, direction);

            Assert.That(result.Accepted, Is.True,
                "超过单独散布的方向仍可能来自合法相机后坐力，不能按单独散布上限拒绝。");
            Assert.That(result.Shot.DidHit, Is.True);
        }

        [Test]
        public void CameraRecoilBudgetCoversTheRealPlayerRigAndRemainsSeparateFromSpread()
        {
            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Player/PlayerGameplayRig.prefab");
            Assert.That(rig, Is.Not.Null);
            Component recoil = rig.GetComponentsInChildren<Component>(true)
                .Single(value => value != null && value.GetType().Name == "PlayerRecoilController");
            var serialized = new SerializedObject(recoil);
            double vertical = serialized.FindProperty("maxVerticalRecoil").floatValue;
            double horizontal = serialized.FindProperty("maxHorizontalRecoil").floatValue;
            Assert.That(vertical, Is.InRange(0d, 10d));
            Assert.That(horizontal, Is.InRange(0d, 3d));
            double combined = Math.Acos(Math.Cos(vertical * Math.PI / 180d) *
                Math.Cos(horizontal * Math.PI / 180d)) * 180d / Math.PI;
            double budget = AuthoritativeWeaponDefinition.MaxCameraRecoilDeviationDegrees;
            Assert.That(budget, Is.EqualTo(11d));
            Assert.That(combined, Is.LessThan(budget),
                "权威独立相机后坐力预算必须覆盖正式PlayerGameplayRig的真实旋转上限。");
            var definitions = AuthoritativeWeaponDefinition.CreateProjectDefaults(new CoopServerRules());
            Assert.That(definitions.Single(value => value.WeaponId == "weapon.rifle").MaxSpreadDegrees,
                Is.EqualTo(5.2d).Within(0.000001d));
            Assert.That(definitions.Single(value => value.WeaponId == "weapon.pistol").MaxSpreadDegrees,
                Is.EqualTo(5.6d).Within(0.000001d));
        }

        [TestCase("weapon.rifle", -3f)]
        [TestCase("weapon.rifle", 3f)]
        [TestCase("weapon.pistol", -3f)]
        [TestCase("weapon.pistol", 3f)]
        public void SourceBoundedCameraRecoilAndMaximumWeaponSpreadCanHitAtRange(
            string weaponId, float horizontalRecoil)
        {
            var definition = AuthoritativeWeaponDefinition.CreateProjectDefaults(new CoopServerRules())
                .Single(value => value.WeaponId == weaponId);
            // RecoilPivot uses exactly this rotation in PlayerRecoilController.
            Quaternion cameraRotation = Quaternion.Euler(-10f, horizontalRecoil, 0f);
            Type spread = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("WeaponSpreadState"))
                .First(type => type != null);
            Vector3 actualDirection = (Vector3)spread.GetMethod("ApplySpread").Invoke(null, new object[]
            {
                cameraRotation * Vector3.forward, cameraRotation * Vector3.right,
                cameraRotation * Vector3.up, (float)definition.MaxSpreadDegrees, Vector2.up
            });
            var direction = new NetVector3(actualDirection.x, actualDirection.y, actualDirection.z);
            var origin = new NetVector3(0d, 1.5d, 0d);
            var simulation = Simulation(origin + direction * 50d);

            CommandResolution result = Fire(simulation, weaponId, origin, direction);

            Assert.That(result.Accepted, Is.True,
                "真实相机后坐力旋转叠加真实WeaponSpreadState散布后，合法连射不能被基础movement aim误拒绝。");
            Assert.That(result.Shot.DidHit, Is.True);
        }

        [TestCase("weapon.rifle")]
        [TestCase("weapon.pistol")]
        public void FullSpreadAndCameraRecoilBudgetBoundaryCanHitAtRange(string weaponId)
        {
            var definition = AuthoritativeWeaponDefinition.CreateProjectDefaults(new CoopServerRules())
                .Single(value => value.WeaponId == weaponId);
            NetVector3 direction = DirectionAtYaw(definition.MaxSpreadDegrees +
                AuthoritativeWeaponDefinition.MaxCameraRecoilDeviationDegrees);
            var origin = new NetVector3(0d, 1.5d, 0d);
            var simulation = Simulation(origin + direction * 50d);

            CommandResolution result = Fire(simulation, weaponId, origin, direction);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Shot.DidHit, Is.True);
        }

        [TestCase("weapon.rifle", 19.2d)]
        [TestCase("weapon.pistol", 19.6d)]
        public void DirectionBeyondMaximumSpreadPlusBoundedCameraRecoilCannotDamageDistantTarget(
            string weaponId, double angle)
        {
            // 5.2/5.6-degree source spread + conservative 11-degree bounded
            // source camera recoil + 3 degrees beyond both legitimate budgets.
            NetVector3 direction = DirectionAtYaw(angle);
            var origin = new NetVector3(0d, 1.5d, 0d);
            var simulation = Simulation(origin + direction * 50d);

            CommandResolution result = Fire(simulation, weaponId, origin, direction);

            Assert.That(result.RejectionReason, Is.EqualTo(CommandRejectionReason.InvalidAim));
            Assert.That(simulation.CaptureSnapshot().Target(1).Health, Is.EqualTo(100d));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonFiniteShotDirectionIsRejectedBeforeDamage(double invalid)
        {
            var simulation = Simulation(new NetVector3(0d, 1.5d, 10d));
            CommandResolution result = Fire(simulation, "weapon.rifle",
                new NetVector3(0d, 1.5d, 0d), new NetVector3(invalid, 0d, 1d));

            Assert.That(result.RejectionReason, Is.EqualTo(CommandRejectionReason.InvalidAim));
            Assert.That(simulation.CaptureSnapshot().Target(1).Health, Is.EqualTo(100d));
        }

        [Test]
        public void NonUnitShotDirectionCannotBypassValidation()
        {
            var simulation = Simulation(new NetVector3(0d, 1.5d, 10d));
            CommandResolution result = Fire(simulation, "weapon.rifle",
                new NetVector3(0d, 1.5d, 0d), new NetVector3(0d, 0d, 2d));

            Assert.That(result.RejectionReason, Is.EqualTo(CommandRejectionReason.InvalidAim));
        }

        [Test]
        public void NoDamageCandidateKeepsTheCoarseConvergenceBoundForMisses()
        {
            var simulation = Simulation(new NetVector3(0d, 1.5d, 10d));
            CommandResolution result = Fire(simulation, "weapon.rifle",
                new NetVector3(0d, 1.5d, 0d), new NetVector3(0.6d, 0d, 0.8d));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Shot.DidHit, Is.False,
                "近墙视差产生的大角度空枪可以保留，但不能造成目标伤害。");
            Assert.That(simulation.CaptureSnapshot().Target(1).Health, Is.EqualTo(100d));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1d)]
        [TestCase(34d)]
        [TestCase(44d)]
        [TestCase(45d)]
        public void WeaponSpreadPlusRecoilBudgetMustBeFiniteAndBelowTheCoarseDirectionBound(double invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AuthoritativeWeaponDefinition(
                "weapon.rifle", 30, 90, 1, 1, 1, 100d, 10d, 2d, true,
                maxSpreadDegrees: invalid));
        }

        private static AuthoritativeCoopSimulation Simulation(NetVector3 target) =>
            new(new CoopServerRules(), new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, target, 0.25d, 100d,
                    bodyHalfExtents: new NetVector3(0.25d, 0.25d, 0.25d)) });

        private static CommandResolution Fire(AuthoritativeCoopSimulation simulation,
            string weaponId, NetVector3 origin, NetVector3 direction)
        {
            if (weaponId != "weapon.rifle")
            {
                Assert.That(simulation.ApplyWeaponAction(1,
                    AuthoritativeWeaponAction.SwitchWeapon, weaponId).Accepted, Is.True);
                int switchTicks = AuthoritativeWeaponDefinition.CreateProjectDefaults(simulation.Rules)
                    .Single(value => value.WeaponId == weaponId).SwitchDurationTicks;
                for (int index = 0; index < switchTicks; index++)
                    simulation.Step(Array.Empty<PlayerInputCommand>());
            }
            long tick = simulation.CaptureSnapshot().Tick + 1;
            var command = new PlayerInputCommand(1, 1, 1, tick,
                0d, 0d, 0d, 0d, true, default, false, false, false,
                weaponId, origin, shotViewTick: tick - 1, shotDirection: direction);
            return simulation.Step(new[] { command }).Commands.Single();
        }

        private static NetVector3 DirectionAtYaw(double angle)
        {
            double radians = angle * Math.PI / 180d;
            return new NetVector3(Math.Sin(radians), 0d, Math.Cos(radians));
        }
    }
}
