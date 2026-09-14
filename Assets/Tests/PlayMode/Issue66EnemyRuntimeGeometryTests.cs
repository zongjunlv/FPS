using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AI;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue66EnemyRuntimeGeometryTests
    {
        private readonly List<GameObject> ownedObjects = new();
        private readonly List<AsyncOperationHandle<GameObject>> loads = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = ownedObjects.Count - 1; index >= 0; index--)
            {
                if (ownedObjects[index] != null)
                {
                    Object.Destroy(ownedObjects[index]);
                }
            }

            ownedObjects.Clear();

            for (int index = 0; index < loads.Count; index++)
            {
                if (loads[index].IsValid())
                {
                    Addressables.Release(loads[index]);
                }
            }

            loads.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredAgentGeometrySurvivesAwakeAndSpawnReset()
        {
            foreach (string address in new[]
                     {
                         "enemy/quad-shell-elite",
                         "enemy/eye-drone-suppressor",
                         "enemy/eye-drone-support"
                     })
            {
                AsyncOperationHandle<GameObject> load =
                    Addressables.LoadAssetAsync<GameObject>(address);
                loads.Add(load);
                yield return load;
                Assert.That(load.Status, Is.EqualTo(AsyncOperationStatus.Succeeded), address);

                NavMeshAgent authored = load.Result.GetComponent<NavMeshAgent>();
                Assert.That(authored, Is.Not.Null, address);
                float radius = authored.radius;
                float height = authored.height;
                float baseOffset = authored.baseOffset;
                Assert.That(
                    Mathf.Approximately(radius, 0.35f) &&
                    Mathf.Approximately(height, 1.2f),
                    Is.False,
                    $"{address} 必须拥有区别于运行时默认值的 authoring 几何。");

                GameObject instance = Object.Instantiate(load.Result);
                ownedObjects.Add(instance);
                yield return null;
                AssertAgentGeometry(instance, radius, height, baseOffset, address + " Awake");

                EnemyNavigationController navigation =
                    instance.GetComponent<EnemyNavigationController>();
                Assert.That(navigation, Is.Not.Null, address);
                navigation.ResetForSpawn();
                AssertAgentGeometry(instance, radius, height, baseOffset, address + " ResetForSpawn");
            }
        }

        [Test]
        public void DynamicallyCreatedAgentReceivesDefaultGeometry()
        {
            var enemy = new GameObject("Dynamic Enemy");
            ownedObjects.Add(enemy);
            EnemyNavigationController navigation =
                enemy.AddComponent<EnemyNavigationController>();

            navigation.AttachToNavMesh();

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            Assert.That(agent, Is.Not.Null);
            Assert.That(agent.radius, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(agent.height, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(agent.baseOffset, Is.Zero.Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator PoolNamesUseDisplayNameOrTemplateIdentity()
        {
            foreach ((string Address, string ExpectedLabel) expectation in new[]
                     {
                         ("enemy/trilobite-assault", "TRILOBITE"),
                         ("enemy/eye-drone-support", "EyeDroneSupport")
                     })
            {
                AsyncOperationHandle<GameObject> load =
                    Addressables.LoadAssetAsync<GameObject>(expectation.Address);
                loads.Add(load);
                yield return load;
                Assert.That(load.Status, Is.EqualTo(AsyncOperationStatus.Succeeded), expectation.Address);

                var factoryObject = new GameObject("Issue66 Naming Pool");
                ownedObjects.Add(factoryObject);
                PooledEnemyFactory pool =
                    factoryObject.AddComponent<PooledEnemyFactory>();
                EnemyController template =
                    load.Result.GetComponent<EnemyController>();
                pool.ConfigurePrefab(template, 2);
                Assert.That(pool.PrewarmOne(template), Is.True);

                var request = new EnemySpawnRequest(
                    66,
                    null,
                    Vector3.zero,
                    Quaternion.identity,
                    null);
                bool spawned = pool.TrySpawnWithTemplate(
                    request,
                    template,
                    null,
                    out EnemySpawnHandle handle);
                EnemyController namedInstance = spawned
                    ? handle.Controller
                    : pool.GetComponentsInChildren<EnemyController>(true)
                        .FirstOrDefault(candidate =>
                            candidate.name.Contains("WAVE 066"));

                Assert.That(namedInstance, Is.Not.Null, expectation.Address);
                Assert.That(
                    namedInstance.name,
                    Is.EqualTo($"{expectation.ExpectedLabel} WAVE 066"));
                Assert.That(namedInstance.name, Does.Not.StartWith("SPIDER_BOT"));

                if (spawned)
                {
                    pool.Release(handle);
                    pool.FlushPendingReleases();
                }

                pool.DisposePool();
            }
        }

        private static void AssertAgentGeometry(
            GameObject instance,
            float radius,
            float height,
            float baseOffset,
            string context)
        {
            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            Assert.That(agent, Is.Not.Null, context);
            Assert.That(agent.radius, Is.EqualTo(radius).Within(0.0001f), context);
            Assert.That(agent.height, Is.EqualTo(height).Within(0.0001f), context);
            Assert.That(agent.baseOffset, Is.EqualTo(baseOffset).Within(0.0001f), context);
        }
    }
}
