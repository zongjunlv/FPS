using System;
using System.Collections;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopEnemyNavigationSafetyTests
    {
        private readonly List<GameObject> createdObjects = new();
        private readonly List<NavMeshData> createdNavigation = new();
        private readonly List<NavMeshDataInstance> navigationInstances = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Never remove navigation installed by the currently loaded map
            // or another fixture. This fixture owns only these instances.
            foreach (NavMeshDataInstance instance in navigationInstances)
                if (instance.valid) instance.Remove();
            navigationInstances.Clear();
            foreach (NavMeshData navigation in createdNavigation)
                if (navigation != null) Object.Destroy(navigation);
            createdNavigation.Clear();
            foreach (GameObject value in createdObjects)
                if (value != null) Object.Destroy(value);
            createdObjects.Clear();
            yield return null;
            yield return null;
        }

        [Test]
        public void MissingGroundNavigationDoesNotSnapEnemyOntoOverheadRoof()
        {
            Vector3 origin = new Vector3(9000f, 0f, 9000f);
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.name = "Ground Below Unconnected Navigation Roof";
            ground.transform.position = origin + Vector3.down * 0.1f;
            ground.transform.localScale = new Vector3(24f, 0.2f, 24f);
            GameObject roofGeometry = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            roofGeometry.name = "Unconnected Roof Above Ground Enemy";
            roofGeometry.transform.position = origin + Vector3.up * 1.4f;
            roofGeometry.transform.localScale = new Vector3(24f, 0.2f, 24f);
            Physics.SyncTransforms();

            // Reproduce a ground-nav gap underneath a separate walkable
            // rooftop. No stairs, ramp or off-mesh link connects the two.
            BuildBoxNavigation(origin + Vector3.up * 1.4f,
                Quaternion.identity, new Vector3(24f, 0.2f, 24f));
            Assert.That(NavMesh.SamplePosition(origin, out _, 0.25f,
                NavMesh.AllAreas), Is.False,
                "夹具必须确实缺少脚下地面NavMesh，而不是正常连通坡道。");
            Assert.That(NavMesh.SamplePosition(origin, out NavMeshHit roof,
                2f, NavMesh.AllAreas), Is.True);
            Assert.That(roof.position.y, Is.GreaterThan(1f),
                "旧版2米起点采样必须可以选中独立上层屋顶。");

            NetworkCoopSessionAuthority authority = Authority(origin,
                origin + Vector3.forward * 6f, AuthoritativeEnemyRole.Assault);
            AuthoritativeTargetState before = authority.LastAuthoritativeSnapshot.Target(1);
            AuthoritativeTargetState after = authority.ServerStep().Snapshot.Target(1);

            Assert.That(after.Behavior, Is.EqualTo(AuthoritativeEnemyBehavior.Pursue));
            Assert.That(after.Position.Y, Is.EqualTo(before.Position.Y).Within(0.001d),
                "地面导航缺失时不得把敌人穿过头顶障碍吸附到无连接的屋顶；这是跨层瞬移，不是爬坡。");
        }

        [TestCase(AuthoritativeEnemyRole.Assault)]
        [TestCase(AuthoritativeEnemyRole.Raider)]
        [TestCase(AuthoritativeEnemyRole.Support)]
        [TestCase(AuthoritativeEnemyRole.Suppressor)]
        [TestCase(AuthoritativeEnemyRole.Elite)]
        public void NoNavigationFallbackDoesNotFollowJumpingPlayerVertically(
            AuthoritativeEnemyRole role)
        {
            Vector3 origin = new Vector3(8000f, 0f, 8000f);
            Assert.That(NavMesh.SamplePosition(origin, out _, 4f,
                NavMesh.AllAreas), Is.False,
                "此对照必须经过正式服务器的无NavMesh回退路径。");
            NetworkCoopSessionAuthority authority = Authority(origin,
                origin + Vector3.forward * 25f, role);
            authority.RegisterPlayerClient(10UL, 1);
            AuthoritativePlayerState beforePlayer = authority.LastAuthoritativeSnapshot.Player(1);
            var jump = new PlayerInputCommand(1, 1, 101, 1,
                0d, 0d, 0d, 0d, false, beforePlayer.Position,
                jumpPressed: true, sprintHeld: false, crouchRequested: false);
            PlayerMovementState predictedJump = CoopGameplayRules.IntegrateMovement(
                beforePlayer.Movement, jump, 1, authority.Rules);
            NetcodePlayerCommand jumpCommand = NetcodePlayerCommand.FromDomain(jump);
            jumpCommand.ClaimedPosition = new Vector3((float)predictedJump.Position.X,
                (float)predictedJump.Position.Y, (float)predictedJump.Position.Z);
            Assert.That(authority.TryQueueCommand(10UL,
                jumpCommand, false), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();

            Assert.That(result.Commands[0].Accepted, Is.True,
                result.Commands[0].RejectionReason.ToString());
            Assert.That(result.Snapshot.Player(1).Position.Y, Is.GreaterThan(0d),
                "客户端跳跃输入必须先真正进入服务器演算，不能仅改表现对象。");
            AuthoritativeTargetState enemy = result.Snapshot.Target(1);
            Assert.That(enemy.Behavior, Is.EqualTo(AuthoritativeEnemyBehavior.Pursue));
            Assert.That(enemy.Position.Y, Is.EqualTo(0d).Within(0.001d),
                "全部正式角色的Domain目标均为平面追踪，玩家跳跃不能让回退路径飞天。");
            Assert.That((enemy.Position - ToDomain(origin)).Magnitude, Is.GreaterThan(0d));

            AuthoritativePlayerState airborne = result.Snapshot.Player(1);
            var continueAirborne = new PlayerInputCommand(1, 2, 102, 2,
                0d, 0d, 0d, 0d, false, airborne.Position,
                jumpPressed: false, sprintHeld: false, crouchRequested: false);
            PlayerMovementState predictedNext = CoopGameplayRules.IntegrateMovement(
                airborne.Movement, continueAirborne, 1, authority.Rules);
            NetcodePlayerCommand nextCommand = NetcodePlayerCommand.FromDomain(continueAirborne);
            nextCommand.ClaimedPosition = new Vector3((float)predictedNext.Position.X,
                (float)predictedNext.Position.Y, (float)predictedNext.Position.Z);
            Assert.That(authority.TryQueueCommand(10UL, nextCommand, false), Is.True);
            AuthoritativeTickResult next = authority.ServerStep();
            Assert.That(next.Commands[0].Accepted, Is.True,
                next.Commands[0].RejectionReason.ToString());
            Assert.That(next.Snapshot.Player(1).Position.Y, Is.GreaterThan(airborne.Position.Y));
            Assert.That(next.Snapshot.Target(1).Position.Y, Is.EqualTo(0d).Within(0.001d));
        }

        [Test]
        public void ConnectedWalkableSlopeStillAllowsGroundHeightChanges()
        {
            Vector3 origin = new Vector3(10000f, 0f, 10000f);
            BuildBoxNavigation(origin, Quaternion.Euler(-15f, 0f, 0f),
                new Vector3(24f, 0.2f, 32f));
            Assert.That(NavMesh.SamplePosition(origin, out NavMeshHit start,
                0.75f, NavMesh.AllAreas), Is.True);
            Vector3 desired = start.position + Vector3.forward * 8f;
            Assert.That(NavMesh.SamplePosition(desired, out NavMeshHit end,
                4f, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(start.position, end.position,
                NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete),
                "对照必须确实是连通可步行坡道。");
            Assert.That(end.position.y, Is.GreaterThan(start.position.y + 1f));

            NetworkCoopSessionAuthority authority = Authority(start.position,
                end.position, AuthoritativeEnemyRole.Assault);
            AuthoritativeTargetState before = authority.LastAuthoritativeSnapshot.Target(1);
            for (int tick = 0; tick < 60; tick++) authority.ServerStep();
            AuthoritativeTargetState after = authority.LastAuthoritativeSnapshot.Target(1);

            Assert.That(after.Position.Z, Is.GreaterThan(before.Position.Z + 0.5d));
            Assert.That(after.Position.Y, Is.GreaterThan(before.Position.Y + 0.1d),
                "修复跨层吸附不能把合法坡道/台阶移动统一压成固定Y。");
        }

        private NetworkCoopSessionAuthority Authority(Vector3 enemyPosition,
            Vector3 playerPosition, AuthoritativeEnemyRole role)
        {
            var authority = Track(new GameObject("Enemy Navigation Authority"))
                .AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(tickRate: 60),
                new[] { new CoopPlayerSpawn(1, ToDomain(playerPosition)) },
                new[] { new CoopTargetSpawn(1, ToDomain(enemyPosition),
                    0.5d, 100d, role: role, moveSpeed: 2d,
                    attackRange: 1.8d, attackDamage: 0d) });
            return authority;
        }

        private void BuildBoxNavigation(Vector3 center, Quaternion rotation,
            Vector3 size)
        {
            Assert.That(NavMesh.GetSettingsCount(), Is.GreaterThan(0));
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(center, rotation, Vector3.one),
                size = size,
                area = 0
            };
            var bounds = new Bounds(center, new Vector3(32f, 16f, 40f));
            NavMeshData navigation = NavMeshBuilder.BuildNavMeshData(settings,
                new List<NavMeshBuildSource> { source }, bounds,
                Vector3.zero, Quaternion.identity);
            Assert.That(navigation, Is.Not.Null);
            createdNavigation.Add(navigation);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(navigation);
            Assert.That(instance.valid, Is.True);
            navigationInstances.Add(instance);
        }

        private GameObject Track(GameObject value)
        {
            createdObjects.Add(value);
            return value;
        }

        private static NetVector3 ToDomain(Vector3 value) =>
            new NetVector3(value.x, value.y, value.z);
    }
}
