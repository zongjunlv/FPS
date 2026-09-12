using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue47AddressableSceneTests
    {
        private bool previousIgnoreLogs;

        [SetUp]
        public void SetUp()
        {
            previousIgnoreLogs = LogAssert.ignoreFailingMessages;
            // CityNew imports emit unrelated audio/shader diagnostics in headless mode.
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            var empty = SceneManager.CreateScene("Issue47Cleanup");
            SceneManager.SetActiveScene(empty);
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene != empty) yield return SceneManager.UnloadSceneAsync(scene);
            }
            LogAssert.ignoreFailingMessages = previousIgnoreLogs;
        }

        [UnityTest]
        public IEnumerator CityNewLoadsCatalogAddressesAndReusesEnemiesAfterDeath()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
            float deadline = Time.realtimeSinceStartup + 35f;
            CityNewWaveBootstrap bootstrap = null;
            AddressableEnemyFactory factory = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                bootstrap = Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                factory = Object.FindAnyObjectByType<AddressableEnemyFactory>();
                if (bootstrap != null && factory != null &&
                    bootstrap.Director.ActiveEnemies.Count > 0) break;
                yield return null;
            }
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.ConfigurationError, Is.Empty);
            Assert.That(factory, Is.Not.Null);
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Ready));
            int expectedTemplateCount = bootstrap.ContentCatalog.EnemyArchetypes
                .Select(archetype => archetype.TemplateAddress)
                .Distinct()
                .Count();
            Assert.That(factory.LoadedTemplateCount, Is.EqualTo(expectedTemplateCount));
            Assert.That(factory.Pool.PooledObjectCount, Is.GreaterThanOrEqualTo(expectedTemplateCount));
            var victim = bootstrap.Director.ActiveEnemies.Values.First();
            Vector3 point = victim.Controller.transform.position;
            int deaths = bootstrap.Director.EnemyDeathEventCount;
            victim.Controller.GetComponent<Health>().ApplyDamage(
                new DamageInfo(100000, point, Vector3.forward, null));
            Assert.That(bootstrap.Director.EnemyDeathEventCount, Is.EqualTo(deaths + 1));
            bootstrap.Director.StopRun(WaveStopReason.Reconfigured);
            yield return null;
            yield return null;
            int instantiated = factory.Pool.InstantiateCount;
            int reused = factory.Pool.ReuseCount;
            yield return factory.PrepareAsync();
            Assert.That(factory.LoadedTemplateCount, Is.EqualTo(expectedTemplateCount));
            var entry = new WaveEnemyEntry(bootstrap.ContentCatalog.EnemyArchetypes[0]);
            var request = new EnemySpawnRequest(47001, entry, point, Quaternion.identity,
                GameObject.FindGameObjectWithTag("Player").transform);
            Assert.That(factory.TrySpawn(request, null, out var handle), Is.True);
            Assert.That(handle.Controller.GetComponent<Health>().IsDead, Is.False);
            Assert.That(factory.Pool.InstantiateCount, Is.EqualTo(instantiated));
            Assert.That(factory.Pool.ReuseCount, Is.GreaterThan(reused));
            factory.Release(handle);
            factory.DisposeFactory();
            Assert.That(factory.LoadedTemplateCount, Is.Zero);
        }
    }
}
