using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue26EnemyPoolTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [SetUp]
        public void IgnoreHeadlessTmpImporterErrors()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void RestoreLogAndTimeState()
        {
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator ConsecutiveWavesReusePrewarmedEnemiesWithoutExpansion()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            Component pool = Find(RuntimeType("PooledEnemyFactory"));
            Component director = Find(RuntimeType("WaveDirector"));
            Assert.That(pool, Is.Not.Null);
            int expectedPrewarm = CityNewContentCatalog.LoadDefault()
                .EnemyArchetypes
                .Select(archetype => archetype.TemplateAddress)
                .Distinct()
                .Count() * 4;
            Assert.That(Get<int>(pool, "PooledObjectCount"), Is.EqualTo(expectedPrewarm));
            int instantiatedBefore = Get<int>(pool, "InstantiateCount");
            int reusedBefore = Get<int>(pool, "ReuseCount");
            List<object> firstWaveHandles = AllActiveHandles(director);
            HashSet<string> firstWaveInstances = InstanceIds(firstWaveHandles);

            KillAllActive(director);
            yield return WaitForWave(director, 2, 12f);

            Assert.That(Get<int>(pool, "InstantiateCount"),
                Is.EqualTo(instantiatedBefore),
                "预热容量覆盖稳定波次后不应继续创建敌人。 ");
            Assert.That(Get<int>(pool, "ExpansionCount"), Is.Zero);
            Assert.That(Get<int>(pool, "ReuseCount"), Is.GreaterThan(reusedBefore));
            HashSet<string> secondWaveInstances = InstanceIds(director);
            Assert.That(secondWaveInstances.Overlaps(firstWaveInstances), Is.True,
                "第二波应复用第一波的物理实例。 ");
            object staleHandle = firstWaveHandles.Find(handle =>
                secondWaveInstances.Contains(
                    ((Component)handle.GetType().GetProperty("Controller")
                        .GetValue(handle)).GetEntityId().ToString()));
            Assert.That(staleHandle, Is.Not.Null);
            int activeBeforeStaleRelease = Get<int>(pool, "ActiveCount");
            pool.GetType().GetMethod("Release").Invoke(
                pool,
                new[] { staleHandle });
            Assert.That(Get<int>(pool, "ActiveCount"),
                Is.EqualTo(activeBeforeStaleRelease),
                "上一代 handle 不得回收已经复用的新一代敌人。 ");

            foreach (Component enemy in ActiveEnemies(director))
            {
                Component health = enemy.GetComponent(RuntimeType("Health"));
                Assert.That(Get<bool>(health, "IsDead"), Is.False);
                Assert.That(Get<float>(health, "CurrentHealth"),
                    Is.EqualTo(Get<float>(health, "MaxHealth")).Within(0.001f));
                Assert.That(Get<int>(enemy, "SpawnResetCount"), Is.GreaterThan(1));
            }
        }

        [UnityTest]
        public IEnumerator EveryPooledEnemyOwnsExactlyOneOverheadHealthBar()
        {
            yield return LoadRuntime();
            Component director = Find(RuntimeType("WaveDirector"));
            Component pool = Find(RuntimeType("PooledEnemyFactory"));
            List<Component> enemies = ActiveEnemies(director);
            Component[] availableClones = pool.GetComponentsInChildren(
                RuntimeType("EnemyController"), true);
            enemies.AddRange(availableClones);
            Assert.That(enemies.Count, Is.GreaterThan(2),
                "需要同时检查活动敌人和池内休眠克隆体。");

            foreach (Component enemy in enemies)
            {
                int overheadCount = 0;

                foreach (Transform item in enemy.GetComponentsInChildren<
                             Transform>(true))
                {
                    if (item.name == "Enemy Overhead Information")
                    {
                        overheadCount++;
                    }
                }

                Assert.That(overheadCount, Is.EqualTo(1),
                    $"{enemy.name} 应且仅应拥有一条头顶血条。");
            }
        }

        [UnityTest]
        public IEnumerator PlayerFailureReturnsEveryLeaseAndClearsRegistrations()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component pool = Find(RuntimeType("PooledEnemyFactory"));
            Component playerHealth = player.GetComponent(RuntimeType("Health"));
            ApplyLethalDamage(playerHealth);
            yield return null;
            yield return null;

            Assert.That(Get<int>(pool, "ActiveCount"), Is.Zero);
            Assert.That(Get<int>(pool, "PendingReleaseCount"), Is.Zero);
            Assert.That(Get<int>(pool, "AvailableCount"),
                Is.EqualTo(Get<int>(pool, "PooledObjectCount")));
            Component scheduler = Find(RuntimeType("EnemyPerceptionScheduler"));
            Assert.That(Get<int>(scheduler, "RegisteredCount"), Is.Zero,
                "回收后感知调度器不应残留僵尸成员。 ");
        }

        private static IEnumerator LoadRuntime()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                Component director = Find(RuntimeType("WaveDirector"));
                Component pool = Find(RuntimeType("PooledEnemyFactory"));

                if (director != null && pool != null &&
                    ActiveEnemies(director).Count > 0 &&
                    AllActiveHandles(director).Count >
                    ActiveHandles(director).Count)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("CityNew 对象池波次未在期限内就绪。 ");
        }

        private static IEnumerator WaitForWave(
            Component director,
            int wave,
            float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                object progress = director.GetType()
                    .GetProperty("CurrentProgress").GetValue(director);
                int currentWave = (int)progress.GetType()
                    .GetProperty("CurrentWave").GetValue(progress);

                if (currentWave == wave && ActiveEnemies(director).Count > 0)
                {
                    yield break;
                }

                if (currentWave < wave)
                {
                    KillAllActive(director);
                }

                yield return null;
            }

            Assert.Fail($"未在期限内进入第 {wave} 波。 ");
        }

        private static void KillAllActive(Component director)
        {
            foreach (object handle in AllActiveHandles(director))
            {
                Component enemy = (Component)handle.GetType()
                    .GetProperty("Controller").GetValue(handle);
                ApplyLethalDamage(enemy.GetComponent(RuntimeType("Health")));
            }
        }

        private static void ApplyLethalDamage(Component health)
        {
            Type damageType = RuntimeType("DamageInfo");
            object damage = Activator.CreateInstance(
                damageType,
                100000f,
                health.transform.position,
                Vector3.forward,
                null);
            health.GetType().GetMethod("ApplyDamage").Invoke(
                health,
                new[] { damage });
        }

        private static List<Component> ActiveEnemies(Component director)
        {
            var result = new List<Component>();

            foreach (object handle in ActiveHandles(director))
            {
                object controller = handle.GetType().GetProperty("Controller")
                    .GetValue(handle);
                result.Add((Component)controller);
            }

            return result;
        }

        private static List<object> ActiveHandles(Component director)
        {
            object value = director.GetType().GetProperty("ActiveEnemies")
                .GetValue(director);
            var result = new List<object>();

            foreach (object item in (IEnumerable)value)
            {
                result.Add(item.GetType().GetProperty("Value").GetValue(item));
            }

            return result;
        }

        private static List<object> AllActiveHandles(Component director)
        {
            List<object> result = ActiveHandles(director);
            object encounters = director.GetType()
                .GetProperty("EncounterEnemies").GetValue(director);
            foreach (object item in (IEnumerable)encounters)
                result.Add(item.GetType().GetProperty("Value").GetValue(item));
            return result;
        }

        private static HashSet<string> InstanceIds(Component director)
        {
            var ids = new HashSet<string>();

            foreach (Component enemy in ActiveEnemies(director))
            {
                ids.Add(enemy.GetEntityId().ToString());
            }

            return ids;
        }

        private static HashSet<string> InstanceIds(IEnumerable<object> handles)
        {
            var ids = new HashSet<string>();
            foreach (object handle in handles)
            {
                Component enemy = (Component)handle.GetType()
                    .GetProperty("Controller").GetValue(handle);
                ids.Add(enemy.GetEntityId().ToString());
            }
            return ids;
        }

        private static Type RuntimeType(string name)
        {
            return RuntimeTypeResolver.GetType(name, true);
        }

        private static Component Find(Type type)
        {
            return UnityEngine.Object.FindFirstObjectByType(type) as Component;
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
