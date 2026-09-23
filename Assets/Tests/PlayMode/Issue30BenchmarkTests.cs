using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue30BenchmarkTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator BenchmarkCanPrewarmOneHundredEnemies()
        {
            LogAssert.ignoreFailingMessages = true;

            try
            {
                yield return SceneManager.LoadSceneAsync(
                    CityNewScene,
                    LoadSceneMode.Single);
                Type poolType = RuntimeTypeResolver.GetType(
                    "PooledEnemyFactory",
                    true);
                Type directorType = RuntimeTypeResolver.GetType(
                    "WaveDirector",
                    true);
                Type stopReasonType = RuntimeTypeResolver.GetType(
                    "WaveStopReason",
                    true);
                float deadline = Time.realtimeSinceStartup + 25f;
                Component pool = null;
                Component director = null;
                EnemyController activeEnemy = null;

                while (Time.realtimeSinceStartup < deadline)
                {
                    pool = UnityEngine.Object.FindFirstObjectByType(
                        poolType) as Component;
                    director = UnityEngine.Object.FindFirstObjectByType(
                        directorType) as Component;
                    activeEnemy = UnityEngine.Object
                        .FindFirstObjectByType<EnemyController>();

                    if (pool != null && director != null &&
                        Get<int>(pool, "PooledObjectCount") >= 4 &&
                        activeEnemy != null)
                    {
                        break;
                    }

                    yield return null;
                }

                Assert.That(pool, Is.Not.Null);
                Assert.That(director, Is.Not.Null);
                var typedPool = (PooledEnemyFactory)pool;
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                Assert.That(activeEnemy, Is.Not.Null);
                Assert.That(player, Is.Not.Null);
                Vector3 spawnPosition = activeEnemy.transform.position;
                Quaternion spawnRotation = activeEnemy.transform.rotation;
                directorType.GetMethod("StopRun").Invoke(
                    director,
                    new[]
                    {
                        Enum.Parse(stopReasonType, "Disabled")
                    });
                poolType.GetMethod("FlushPendingReleases")
                    .Invoke(pool, null);
                Assert.That(Get<int>(pool, "ActiveCount"), Is.Zero);

                poolType.GetMethod("EnsureCapacity")
                    .Invoke(pool, new object[] { 100 });

                Assert.That(
                    Get<int>(pool, "PooledObjectCount"),
                    Is.EqualTo(100));
                Assert.That(
                    Get<int>(pool, "AvailableCount"),
                    Is.EqualTo(100));
                Assert.That(
                    Get<int>(pool, "InstantiateCount"),
                    Is.InRange(99, 100),
                    "场景模板是否计入实例数不应影响 100 个敌人预热。 ");

                bool spawned = typedPool.TrySpawn(
                    new EnemySpawnRequest(
                        300001,
                        1,
                        null,
                        spawnPosition,
                        spawnRotation,
                        player.transform),
                    (_, _) => { },
                    out EnemySpawnHandle handle);

                Assert.That(
                    spawned,
                    Is.True,
                    "由休眠模板扩容出的敌人必须完成初始化并可实际租借。 ");
                Assert.That(handle.Controller, Is.Not.Null);
                Assert.That(handle.Controller.gameObject.activeInHierarchy,
                    Is.True);
                typedPool.Release(handle);
                typedPool.FlushPendingReleases();
                Assert.That(typedPool.AvailableCount, Is.EqualTo(100));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static T Get<T>(Component component, string property)
        {
            return (T)component.GetType().GetProperty(
                property,
                BindingFlags.Instance | BindingFlags.Public)
                .GetValue(component);
        }
    }
}
