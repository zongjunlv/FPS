using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue48EnemyResourceTests
    {
        private bool oldIgnore;
        private EnemyArchetypeDefinition archetype;

        [SetUp]
        public void SetUp()
        {
            oldIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (archetype != null) Object.Destroy(archetype);
            Time.timeScale = 1;
            yield return Unload();
            LogAssert.ignoreFailingMessages = oldIgnore;
        }

        [UnityTest]
        public IEnumerator SceneReopenLeavesNoLeasesAndConsumerCancellationIsIsolated()
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                yield return LoadCity();
                var service = SharedAssetLeaseService.Default;
                Assert.That(service.GetReferenceCount<GameObject>("enemy/spider"), Is.EqualTo(1));
                var extra = service.Acquire<GameObject>("enemy/spider");
                Assert.That(service.ActiveHandleCount, Is.EqualTo(1));
                Assert.That(service.GetReferenceCount<GameObject>("enemy/spider"), Is.EqualTo(2));
                extra.Cancel();
                extra.Dispose();
                Assert.That(service.GetReferenceCount<GameObject>("enemy/spider"), Is.EqualTo(1));
                Assert.That(Object.FindAnyObjectByType<AddressableEnemyFactory>().PreparationState,
                    Is.EqualTo(EnemyFactoryPreparationState.Ready));
                yield return Unload();
                Assert.That(service.ActiveHandleCount, Is.Zero);
                Assert.That(service.ReferenceCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator MissingEnemyUsesCombatReadyFallbackAndReleasesFailedLoad()
        {
            yield return LoadCity();
            var bootstrap = Object.FindAnyObjectByType<CityNewWaveBootstrap>();
            Vector3 position = bootstrap.Director.ActiveEnemies.Values.First().Controller.transform.position;
            bootstrap.Director.StopRun(WaveStopReason.Reconfigured);
            EnemyController backup = Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).First(enemy => enemy.name == "SPIDER_BOT");
            var factory = new GameObject("Fallback Test").AddComponent<AddressableEnemyFactory>();
            var failed = new FailedLoad();
            archetype = ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();
            archetype.ConfigureTemplateAddress("enemy/missing");
            factory.Configure(new[] { archetype }, 1, 4, failed, backup);
            yield return factory.PrepareAsync();
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Ready));
            Assert.That(factory.FallbackTemplateCount, Is.EqualTo(1));
            Assert.That(failed.Releases, Is.EqualTo(1));
            var request = new EnemySpawnRequest(48001, new WaveEnemyEntry(archetype), position,
                Quaternion.identity, GameObject.FindGameObjectWithTag("Player").transform);
            Assert.That(factory.TrySpawn(request, null, out var handle), Is.True);
            Assert.That(handle.Controller.GetComponentsInChildren<Collider>().Any(c => c.enabled), Is.True);
            Health health = handle.Controller.GetComponent<Health>();
            health.ApplyDamage(new DamageInfo(100000, position, Vector3.forward, null));
            Assert.That(health.IsDead, Is.True);
            factory.Release(handle);
            factory.DisposeFactory();
            Assert.That(failed.Releases, Is.EqualTo(1));
        }

        private static IEnumerator LoadCity()
        {
            yield return SceneManager.LoadSceneAsync("Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
            float end = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < end)
            {
                var bootstrap = Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                if (bootstrap != null && bootstrap.Director.ActiveEnemies.Count > 0) yield break;
                yield return null;
            }
            Assert.Fail("CityNew did not start spawning after resource preparation.");
        }

        private static IEnumerator Unload()
        {
            var empty = SceneManager.CreateScene("Issue48Cleanup-" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene != empty) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        private sealed class FailedLoad : IEnemyTemplateLoader, IEnemyTemplateLoadOperation
        {
            public int Releases;
            public IEnemyTemplateLoadOperation Load(string address) => this;
            public bool IsDone => true;
            public bool Succeeded => false;
            public float Progress => 1;
            public GameObject Template => null;
            public string Error => "Missing test template";
            public void Dispose() => Releases++;
        }
    }
}
