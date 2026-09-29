using System.Collections;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Unity.Collections;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue96NetworkEnemyIntegrationTests
    {
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) Object.Destroy(created[index]);
            created.Clear();
            yield return null;
            yield return null;
        }

        [Test]
        public void TwoClientPresentersReceiveSameRoleTargetAndTransform()
        {
            CoopNetworkWorldPresenter first = Presenter("Issue96 Client A");
            CoopNetworkWorldPresenter second = Presenter("Issue96 Client B");
            NetcodeTargetState state = State(
                new Vector3(4f, 0f, 8f),
                AuthoritativeEnemyRole.Raider,
                AuthoritativeEnemyBehavior.Pursue);

            first.PresentForTests(new[] { state }, 1f / 60f);
            second.PresentForTests(new[] { state }, 1f / 60f);

            Assert.That(first.TryGetTargetView(1, out GameObject left),
                Is.True);
            Assert.That(second.TryGetTargetView(1, out GameObject right),
                Is.True);
            Assert.That(left.transform.position,
                Is.EqualTo(right.transform.position));
            Assert.That(left.transform.rotation,
                Is.EqualTo(right.transform.rotation));
            Assert.That(left.activeSelf, Is.EqualTo(right.activeSelf));
            Assert.That(first.TryGetTargetStatus(1,
                out float health,
                out float maximumHealth,
                out AuthoritativeEnemyBehavior behavior), Is.True);
            Assert.That(health, Is.EqualTo(100f));
            Assert.That(maximumHealth, Is.EqualTo(100f));
            Assert.That(behavior,
                Is.EqualTo(AuthoritativeEnemyBehavior.Pursue));
        }

        [UnityTest]
        public IEnumerator DestroyingClientViewRecreatesItWithoutServerDamage()
        {
            GameObject authorityHost = Track(new GameObject(
                "Issue96 Authority"));
            NetworkCoopSessionAuthority authority = authorityHost.AddComponent<
                NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(tickRate: 60),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 10d), 0.5d, 80d,
                        role: AuthoritativeEnemyRole.Elite,
                        moveSpeed: 0d)
                });
            NetcodeTargetState state = NetcodeTargetState.FromDomain(
                authority.LastAuthoritativeSnapshot.Target(1));
            CoopNetworkWorldPresenter presenter = Presenter(
                "Issue96 Disposable Client View");
            presenter.PresentForTests(new[] { state }, 1f / 60f);
            presenter.TryGetTargetView(1, out GameObject original);

            Object.Destroy(original);
            yield return null;
            presenter.PresentForTests(new[] { state }, 1f / 60f);

            Assert.That(presenter.TryGetTargetView(1,
                out GameObject recreated), Is.True);
            Assert.That(recreated, Is.Not.SameAs(original));
            Assert.That(presenter.RecreatedViewCount, Is.EqualTo(1));
            Assert.That(authority.LastAuthoritativeSnapshot.Target(1).Health,
                Is.EqualTo(80d));
            Assert.That(authority.LastAuthoritativeSnapshot.Target(1).IsAlive,
                Is.True);
        }

        [Test]
        public void RemoteEnemyUsesInterpolationAndGenerationCanSnap()
        {
            CoopNetworkWorldPresenter presenter = Presenter(
                "Issue96 Interpolation Client");
            NetcodeTargetState initial = State(Vector3.zero,
                AuthoritativeEnemyRole.Assault,
                AuthoritativeEnemyBehavior.Pursue);
            presenter.PresentForTests(new[] { initial }, 1f / 60f);
            presenter.TryGetTargetView(1, out GameObject view);

            NetcodeTargetState moved = initial;
            moved.Position = Vector3.right * 10f;
            presenter.PresentForTests(new[] { moved }, 0.05f);
            Assert.That(view.transform.position.x,
                Is.GreaterThan(0f).And.LessThan(10f));

            moved.SpawnGeneration = 2;
            moved.Position = Vector3.right * 20f;
            presenter.PresentForTests(new[] { moved }, 0.01f);
            Assert.That(view.transform.position.x,
                Is.EqualTo(20f).Within(0.0001f));
        }

        [Test]
        public void SameGenerationSnapsToAuthoritativePositionAfterPresentationResumes()
        {
            CoopNetworkWorldPresenter presenter = Presenter(
                "Issue96 Resumed Enemy Client");
            NetcodeTargetState initial = State(Vector3.right * 50f,
                AuthoritativeEnemyRole.Assault,
                AuthoritativeEnemyBehavior.Pursue);
            presenter.PresentForTests(new[] { initial }, 1f / 60f);
            Assert.That(presenter.TryGetTargetView(1,
                out GameObject originalView), Is.True);

            // A reconnect can leave the presenter and pooled view alive while
            // no world frames are presented. The server may advance hundreds
            // of ticks before the first complete replacement snapshot arrives.
            presenter.enabled = false;
            presenter.enabled = true;
            NetcodeTargetState resumed = initial;
            resumed.Position = Vector3.right * 63.5f;
            presenter.PresentForTests(new[] { resumed }, 1f / 60f);

            Assert.That(presenter.TryGetTargetView(1,
                out GameObject resumedView), Is.True);
            Assert.That(resumedView, Is.SameAs(originalView),
                "重连后无需重建同代敌人表现对象。");
            Assert.That(resumedView.transform.position.x,
                Is.EqualTo(63.5f).Within(0.0001f),
                "恢复展示时不能从断线前的位置快速滑行到新快照。");
        }

        [Test]
        public void SameGenerationContinuousUpdatesStillInterpolate()
        {
            CoopNetworkWorldPresenter presenter = Presenter(
                "Issue96 Continuous Enemy Client");
            NetcodeTargetState initial = State(Vector3.right * 50f,
                AuthoritativeEnemyRole.Assault,
                AuthoritativeEnemyBehavior.Pursue);
            presenter.PresentForTests(new[] { initial }, 1f / 60f,
                serverTick: 507);
            presenter.TryGetTargetView(1, out GameObject view);

            NetcodeTargetState nextFrame = initial;
            nextFrame.Position = Vector3.right * 50.3f;
            presenter.PresentForTests(new[] { nextFrame }, 1f / 60f,
                serverTick: 508);

            Assert.That(view.transform.position.x,
                Is.GreaterThan(50f).And.LessThan(50.3f),
                "正常连续快照仍应使用插值，而不是每帧瞬移。");
        }

        [Test]
        public void SameGenerationSnapsAfterLargeServerTickGap()
        {
            CoopNetworkWorldPresenter presenter = Presenter(
                "Issue96 Tick Gap Enemy Client");
            NetcodeTargetState initial = State(Vector3.right * 50f,
                AuthoritativeEnemyRole.Assault,
                AuthoritativeEnemyBehavior.Pursue);
            presenter.PresentForTests(new[] { initial }, 1f / 60f,
                serverTick: 507);
            presenter.TryGetTargetView(1, out GameObject view);

            NetcodeTargetState resumed = initial;
            resumed.Position = Vector3.right * 63.5f;
            presenter.PresentForTests(new[] { resumed }, 1f / 60f,
                serverTick: 1138);

            Assert.That(view.transform.position.x,
                Is.EqualTo(63.5f).Within(0.0001f),
                "服务端在断线期间已前进数百 Tick，首帧应对齐权威位置。");
        }

        [UnityTest]
        public IEnumerator RuntimeStateLoadsOfficialAddressablePresentation()
        {
            CoopNetworkWorldPresenter presenter = Presenter(
                "Official Addressable Enemy Presenter");
            NetcodeTargetState state = State(
                Vector3.forward * 8f,
                AuthoritativeEnemyRole.Assault,
                AuthoritativeEnemyBehavior.Pursue);
            state.ArchetypeId = new FixedString64Bytes(
                "enemy.archetype.spider_assault");
            state.PresentationAddress = new FixedString64Bytes(
                "enemy/trilobite-assault");
            presenter.PresentForTests(new[] { state }, 1f / 60f);
            presenter.TryGetTargetView(1, out GameObject view);

            float timeout = Time.realtimeSinceStartup + 10f;
            while (view != null &&
                   view.GetComponentInChildren<Renderer>(true) == null &&
                   Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.That(view, Is.Not.Null);
            Assert.That(view.GetComponentInChildren<Renderer>(true),
                Is.Not.Null,
                "正式敌人 Addressable 应在联机表现根节点下完成加载。");
            Assert.That(view.name, Does.Contain("Coop Enemy View"));
            Assert.That(view.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            NavMeshAgent[] agents =
                view.GetComponentsInChildren<NavMeshAgent>(true);
            Assert.That(agents, Is.Empty,
                "纯表现资源不得实例化单机 NavMeshAgent，即使暂时 disabled 也不允许。");
            var model = view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true);
            Assert.That(model, Is.Not.Null);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(model.transform.localPosition.sqrMagnitude,
                Is.LessThan(0.0001f),
                "展示一段时间后，模型根节点仍应保持预制体的局部原点。");
        }

        [Test]
        public void DeadAndMissingEnemiesRemainInBoundedPresentationPool()
        {
            CoopNetworkWorldPresenter presenter = Presenter(
                "Issue96 Bounded Visual Pool");
            NetcodeTargetState living = State(Vector3.forward * 8f,
                AuthoritativeEnemyRole.Support,
                AuthoritativeEnemyBehavior.Attack);
            presenter.PresentForTests(new[] { living }, 1f / 60f);
            NetcodeTargetState dead = living;
            dead.Active = false;
            dead.Health = 0f;
            dead.Behavior = AuthoritativeEnemyBehavior.Dead;
            presenter.PresentForTests(new[] { dead }, 1f / 60f);

            Assert.That(presenter.ViewCount, Is.EqualTo(1));
            Assert.That(presenter.CreatedViewCount, Is.EqualTo(1));
            presenter.TryGetTargetView(1, out GameObject view);
            Assert.That(view.activeSelf, Is.False);

            presenter.PresentForTests(new NetcodeTargetState[0],
                1f / 60f);
            Assert.That(presenter.ViewCount, Is.EqualTo(1));
            Assert.That(presenter.CreatedViewCount, Is.EqualTo(1));
        }

        private CoopNetworkWorldPresenter Presenter(string name)
        {
            return Track(new GameObject(name)).AddComponent<
                CoopNetworkWorldPresenter>();
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        private static NetcodeTargetState State(
            Vector3 position,
            AuthoritativeEnemyRole role,
            AuthoritativeEnemyBehavior behavior)
        {
            return new NetcodeTargetState
            {
                TargetId = 1,
                Position = position,
                Radius = 1f,
                Health = 100f,
                MaximumHealth = 100f,
                Active = true,
                YawDegrees = 35f,
                Role = role,
                Behavior = behavior,
                TargetPlayerId = 1,
                SpawnGeneration = 1
            };
        }
    }
}
