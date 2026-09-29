using System.Collections;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopEnemyPresentationParityTests
    {
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (GameObject value in created)
                if (value != null) Object.Destroy(value);
            created.Clear();
            yield return null;
            yield return null;
        }

        [Test]
        public void FormalEnemyKeepsWorldSizeAtEveryLodDistance()
        {
            var camera = Track(new GameObject("Parity camera"))
                .AddComponent<Camera>();
            var presenter = Track(new GameObject("Parity presenter"))
                .AddComponent<CoopNetworkWorldPresenter>();
            NetcodeTargetState state = State(false);
            state.Position = Vector3.forward * 70f;
            presenter.PresentForTests(new[] { state }, 1f / 60f, camera);
            Assert.That(presenter.TryGetTargetView(1, out GameObject view), Is.True);
            Assert.That(view.transform.localScale, Is.EqualTo(Vector3.one),
                "距离LOD不得缩小正式模型，否则可见体积与服务器命中体分离。");
        }

        [UnityTest]
        public IEnumerator InactivePreloadThenSpawnCannotInstallLocalEnemyLogic()
        {
            var presenter = Track(new GameObject("Delayed spawn presenter"))
                .AddComponent<CoopNetworkWorldPresenter>();
            NetcodeTargetState state = State(false);
            presenter.PresentForTests(new[] { state }, 1f / 60f);
            presenter.TryGetTargetView(1, out GameObject view);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (view.GetComponentInChildren<Renderer>(true) == null &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(view.GetComponentInChildren<Renderer>(true), Is.Not.Null);

            state.Active = true;
            state.SpawnGeneration = 1;
            presenter.PresentForTests(new[] { state }, 1f / 60f);
            yield return null;
            yield return null;
            Assert.That(view.GetComponentsInChildren<NavMeshAgent>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<EnemyController>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<EnemyNavigationController>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<EnemyCombatController>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Health>(true), Is.Empty,
                "客户端纯表现不得残留本地生命/伤害结算。");
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        [Test]
        public void BufferedPublishTicksMoveIndependentlyAndNeverExtrapolateForever()
        {
            var presenter = Track(new GameObject("Buffered world presenter"))
                .AddComponent<CoopNetworkWorldPresenter>();
            NetcodeTargetState state = State(true);
            state.PresentationAddress = default;
            state.RunGeneration = 1;
            state.SpawnGeneration = 1;
            state.Position = Vector3.zero;
            state.SnapshotTick = 10;
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 10);
            presenter.TryGetTargetView(1, out var view);
            state.Position = Vector3.right * 2f;
            state.SnapshotTick = 12;
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 11);
            Assert.That(view.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 30);
            Assert.That(view.transform.position.x, Is.EqualTo(2f).Within(0.001f));
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 2000);
            Assert.That(view.transform.position.x, Is.EqualTo(2f).Within(0.001f),
                "无新快照只能停在最后可信位置，不得持续外推飞走。");
            state.Position = Vector3.right * 50f;
            state.SpawnGeneration = 2;
            state.SnapshotTick = 13;
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 11);
            Assert.That(view.transform.position.x, Is.EqualTo(50f).Within(0.001f),
                "新一代敌人不能连接上一代的插值轨迹。");
        }

        [UnityTest]
        public IEnumerator AllFiveFormalModelsRenderWithoutAnyLocalGameplay()
        {
            GameObject[] prefabs = Resources.LoadAll<GameObject>("CoopPresentation/EnemyModels");
            Assert.That(prefabs.Length, Is.EqualTo(5));
            var presenter = Track(new GameObject("All model presenter"))
                .AddComponent<CoopNetworkWorldPresenter>();
            var states = new List<NetcodeTargetState>();
            for (int index = 0; index < prefabs.Length; index++)
            {
                var definition = prefabs[index].GetComponent<CoopEnemyPresentationDefinition>();
                NetcodeTargetState state = State(true);
                state.TargetId = index + 1;
                state.SpawnGeneration = 1;
                state.Position = new Vector3(index * 4f, 0f, 10f);
                state.PresentationAddress = definition.SourceAddress;
                states.Add(state);
            }
            presenter.PresentForTests(states, 1f / 60f);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (presenter.GetComponentsInChildren<CoopEnemyPresentationDefinition>(true).Length < 5 &&
                Time.realtimeSinceStartup < deadline) yield return null;
            presenter.PresentForTests(states, 1f / 60f);
            Assert.That(presenter.VisibleViewCount, Is.EqualTo(5));
            Assert.That(presenter.GetComponentsInChildren<Health>(true), Is.Empty);
            Assert.That(presenter.GetComponentsInChildren<NavMeshAgent>(true), Is.Empty);
            Assert.That(presenter.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (Animator animator in presenter.GetComponentsInChildren<Animator>(true))
            {
                Assert.That(animator.applyRootMotion, Is.False);
                Assert.That(animator.fireEvents, Is.False);
                Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            }
        }

        [UnityTest]
        public IEnumerator AddressReplacementInvalidatesAnOutstandingLoad()
        {
            var presenter = Track(new GameObject("Replacement presenter"))
                .AddComponent<CoopNetworkWorldPresenter>();
            NetcodeTargetState state = State(true);
            presenter.PresentForTests(new[] { state }, 1f / 60f);
            state.PresentationAddress = "enemy/quad-shell-elite";
            state.SpawnGeneration = 2;
            presenter.PresentForTests(new[] { state }, 1f / 60f);
            presenter.TryGetTargetView(1, out var current);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (current.GetComponentInChildren<CoopEnemyPresentationDefinition>(true) == null &&
                Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            var model = current.GetComponentInChildren<CoopEnemyPresentationDefinition>(true);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.SourceAddress, Is.EqualTo("enemy/quad-shell-elite"));
            Assert.That(presenter.GetComponentsInChildren<CoopEnemyPresentationDefinition>(true).Length, Is.EqualTo(1));
        }

        private static NetcodeTargetState State(bool active) => new()
        {
            TargetId = 1,
            Position = Vector3.forward * 8f,
            Radius = 0.8f,
            Health = 100f,
            MaximumHealth = 100f,
            Active = active,
            Role = AuthoritativeEnemyRole.Assault,
            Behavior = AuthoritativeEnemyBehavior.Pursue,
            ArchetypeId = new FixedString64Bytes("enemy.archetype.spider_assault"),
            PresentationAddress = new FixedString64Bytes("enemy/trilobite-assault")
        };
    }
}
