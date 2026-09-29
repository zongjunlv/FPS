using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class CoopHitGeometryParityTests
    {
        [Test]
        public void FullMatchAutomationAimsAtCalibratedBodyRatherThanFeet()
        {
            var driver = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("FPS.Networking.Acceptance.Issue100ClientScenarioDriver"))
                .First(type => type != null);
            var target = new NetcodeTargetState
            {
                Position = Vector3.forward * 5f,
                Radius = 0.8f,
                BodyOffset = Vector3.up * 0.92f,
                BodyHalfExtents = new Vector3(0.4f, 0.29f, 0.33f),
                YawDegrees = 90f
            };
            var point = (Vector3)driver.GetMethod("AimPoint", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { target });
            Assert.That(point.y, Is.EqualTo(0.92f).Within(0.001f),
                "正式对局验收必须瞄准和画面一致的校准身体，不能仍然瞄准已取消的脚底球。");
        }

        [Test]
        public void ShotAtVisibleBodyAboveGroundHitsAuthoredVolume()
        {
            var target = new CoopTargetSpawn(1, new NetVector3(0, 0, 5), 0.1d, 100d,
                bodyOffset: new NetVector3(0, 1, 0), bodyHalfExtents: new NetVector3(0.25, 0.25, 0.25));
            var simulation = new AuthoritativeCoopSimulation(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) }, new[] { target });
            var shot = new PlayerInputCommand(1, 1, 1, 1, 0, 0, 0, 0, true,
                default, false, false, false, "weapon.rifle", new NetVector3(0, 1, 0));
            CommandResolution result = simulation.Step(new[] { shot }).Commands[0];
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Shot.DidHit, Is.True,
                "脚底根节点不是身体中心；瞄准可见甲壳应命中校准体。");
        }

        [Test]
        public void MuzzleRayUsesCameraConvergedDirectionNotMovementYaw()
        {
            var simulation = new AuthoritativeCoopSimulation(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0, 0, 5), 0.1d, 100d) });
            var origin = new NetVector3(0.5, 0, 0);
            var direction = (new NetVector3(0, 0, 5) - origin).Normalized;
            var shot = new PlayerInputCommand(1, 1, 1, 1, 0, 0, 0, 0, true,
                default, false, false, false, "weapon.rifle", origin,
                shotViewTick: 0, shotDirection: direction);
            Assert.That(simulation.Step(new[] { shot }).Commands[0].Shot.DidHit, Is.True,
                "服务器必须使用经过校验的枪口至准星目标方向，不能替换为角色前向。");
        }

        [Test]
        public void HistoricalViewCannotHitAnEnemyThatWasNotSpawnedThen()
        {
            var simulation = new AuthoritativeCoopSimulation(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0, 0, 5), 0.5d, 100d, spawnTick: 2) });
            simulation.Step(System.Array.Empty<PlayerInputCommand>());
            var shot = new PlayerInputCommand(1, 1, 1, 2, 0, 0, 0, 0, true,
                default, false, false, false, "weapon.rifle", default,
                shotViewTick: 1, shotDirection: new NetVector3(0, 0, 1));
            var result = simulation.Step(new[] { shot });
            Assert.That(result.Commands[0].Accepted, Is.True);
            Assert.That(result.Commands[0].Shot.DidHit, Is.False);
            Assert.That(result.Snapshot.Target(1).IsAlive, Is.True);
        }

        [TestCase(100, CommandRejectionReason.TimestampInFuture)]
        [TestCase(0, CommandRejectionReason.TimestampTooOld)]
        public void ViewTickOutsideTheBoundedRewindWindowIsRejected(long viewTick, CommandRejectionReason rejection)
        {
            var simulation = new AuthoritativeCoopSimulation(new CoopServerRules(maximumPastCommandTicks: 5),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0, 0, 5), 0.5d, 100d) });
            for (int index = 0; index < 30; index++) simulation.Step(System.Array.Empty<PlayerInputCommand>());
            var shot = new PlayerInputCommand(1, 1, 1, 31, 0, 0, 0, 0, true,
                default, false, false, false, "weapon.rifle", default,
                shotViewTick: viewTick, shotDirection: new NetVector3(0, 0, 1));
            Assert.That(simulation.Step(new[] { shot }).Commands[0].RejectionReason, Is.EqualTo(rejection));
        }

        [Test]
        public void ForgedShotDirectionCannotFireBehindThePlayersAim()
        {
            var simulation = new AuthoritativeCoopSimulation(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0, 0, -5), 0.5d, 100d) });
            var shot = new PlayerInputCommand(1, 1, 1, 1, 0, 0, 0, 0, true,
                default, false, false, false, "weapon.rifle", default,
                shotViewTick: 0, shotDirection: new NetVector3(0, 0, -1));
            Assert.That(simulation.Step(new[] { shot }).Commands[0].RejectionReason, Is.EqualTo(CommandRejectionReason.InvalidAim));
        }
    }
}
