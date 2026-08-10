using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue15WaveTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
        }

        [Test]
        public void WaveNeverSpawnsAboveConcurrentLimit()
        {
            Type stateType = Type.GetType(
                "SingleWaveState, Assembly-CSharp");
            Assert.That(stateType, Is.Not.Null);
            object state = Activator.CreateInstance(
                stateType,
                new object[] { 5, 2 });

            Assert.That(Register(stateType, state, 1), Is.True);
            Assert.That(Register(stateType, state, 2), Is.True);
            Assert.That(Register(stateType, state, 3), Is.False,
                "达到同时存活上限后不得继续登记生成。");
            Assert.That(
                stateType.GetProperty("AliveCount").GetValue(state),
                Is.EqualTo(2));

            Assert.That(Settle(stateType, state, 1), Is.True);
            Assert.That(Register(stateType, state, 3), Is.True);
            Assert.That(
                stateType.GetProperty("AliveCount").GetValue(state),
                Is.EqualTo(2));
        }

        [Test]
        public void LastEnemyCompletesOnceAcrossDeathAndDisableSignals()
        {
            Type stateType = Type.GetType(
                "SingleWaveState, Assembly-CSharp");
            object state = Activator.CreateInstance(
                stateType,
                new object[] { 1, 1 });
            int completedCount = 0;
            Action completed = () => completedCount++;
            stateType.GetEvent("Completed").AddEventHandler(
                state,
                completed);

            Assert.That(Register(stateType, state, 21), Is.True);
            Assert.That(Settle(stateType, state, 21), Is.True);
            Assert.That(Settle(stateType, state, 21), Is.False,
                "同一敌人的死亡与禁用不得重复结算。");
            Assert.That(
                stateType.GetProperty("IsComplete").GetValue(state),
                Is.True);
            Assert.That(completedCount, Is.EqualTo(1));
        }

        [Test]
        public void WaveRegistersExactlyConfiguredTotal()
        {
            Type stateType = Type.GetType(
                "SingleWaveState, Assembly-CSharp");
            object state = Activator.CreateInstance(
                stateType,
                new object[] { 5, 2 });
            var activeIds = new Queue<int>();
            int nextId = 1;
            int peakAlive = 0;

            while (!(bool)stateType.GetProperty("IsComplete")
                       .GetValue(state))
            {
                while (Register(stateType, state, nextId))
                {
                    activeIds.Enqueue(nextId++);
                    peakAlive = Math.Max(
                        peakAlive,
                        (int)stateType.GetProperty("AliveCount")
                            .GetValue(state));
                }

                Assert.That(activeIds.Count, Is.GreaterThan(0));
                Assert.That(
                    Settle(stateType, state, activeIds.Dequeue()),
                    Is.True);
            }

            Assert.That(
                stateType.GetProperty("SpawnedCount").GetValue(state),
                Is.EqualTo(5));
            Assert.That(
                stateType.GetProperty("SettledCount").GetValue(state),
                Is.EqualTo(5));
            Assert.That(peakAlive, Is.LessThanOrEqualTo(2));
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void InvalidWaveCountsAreRejected(
            int totalCount,
            int maximumAlive)
        {
            Type stateType = Type.GetType(
                "SingleWaveState, Assembly-CSharp");

            TargetInvocationException error = Assert.Throws<
                TargetInvocationException>(() =>
                    Activator.CreateInstance(
                        stateType,
                        new object[] { totalCount, maximumAlive }));
            Assert.That(
                error.InnerException,
                Is.TypeOf<ArgumentOutOfRangeException>());
        }

        [UnityTest]
        public IEnumerator CityNewRunsOneExactFactoryWaveStage()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                LogAssert.Expect(
                    LogType.Error,
                    "No graphic device is available to initialize the view.");
                LogAssert.Expect(
                    LogType.Error,
                    "No graphic device is available to show the window.");
                LogAssert.Expect(
                    LogType.Error,
                    "No graphic device is available to initialize the view.");
            }

            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = Type.GetType(
                "WaveDirector, Assembly-CSharp");
            Type hudType = Type.GetType(
                "UnifiedGameHud, Assembly-CSharp");
            Component director = null;
            float deadline = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < deadline)
            {
                director = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);

                if (director != null &&
                    GetProgressCount(
                        directorType,
                        director,
                        "SpawnedCount") >= 3)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(director, Is.Not.Null,
                "CityNew 必须启用 WaveDirector。");
            Assert.That(
                GetProgressCount(
                    directorType,
                    director,
                    "SpawnedCount"),
                Is.EqualTo(3),
                "首批敌人必须精确停在同时存活上限3。");
            Assert.That(
                GetProgressCount(
                    directorType,
                    director,
                    "AliveCount"),
                Is.EqualTo(3));
            Assert.That(
                directorType.GetProperty("PeakAliveCount")
                    .GetValue(director),
                Is.EqualTo(3));

            ValidateActiveSpawnPoints(directorType, director);
            Assert.That(
                directorType.GetProperty(
                        "MinimumSpawnSafetyDistanceObserved")
                    .GetValue(director),
                Is.GreaterThanOrEqualTo(10f),
                "每个实际采用的出生点必须位于玩家安全半径之外。");
            Assert.That(
                directorType.GetProperty(
                        "MinimumSpawnEnemySpacingObserved")
                    .GetValue(director),
                Is.GreaterThanOrEqualTo(3f),
                "生成时敌人之间必须保持最小间距。");
            Assert.That(
                GameObject.Find("SPIDER_BOT SUPPORT 1"),
                Is.Null,
                "波次模式不得触发旧三人小队补齐逻辑。");

            Component hud = (Component)UnityEngine.Object
                .FindAnyObjectByType(hudType);
            Assert.That(hud, Is.Not.Null);
            Assert.That(
                hudType.GetProperty("IsWaveBound").GetValue(hud),
                Is.True);
            Assert.That(
                hudType.GetProperty("WaveText").GetValue(hud),
                Is.EqualTo("WAVE 1/3"));
            Assert.That(
                hudType.GetProperty("SpawnedText").GetValue(hud),
                Is.EqualTo("SPAWNED 3/4"));
            Assert.That(
                hudType.GetProperty("RemainingEnemiesText")
                    .GetValue(hud),
                Is.EqualTo("REMAINING 4"));

            deadline = Time.realtimeSinceStartup + 30f;
            Type damageInfoType = Type.GetType(
                "DamageInfo, Assembly-CSharp");

            while ((int)directorType.GetProperty("CompletedWaveCount")
                       .GetValue(director) < 1 &&
                   Time.realtimeSinceStartup < deadline)
            {
                foreach (Component enemy in
                         GetActiveEnemyControllers(
                             directorType,
                             director))
                {
                    Component health = enemy.GetComponent("Health");

                    if (health != null &&
                        !(bool)health.GetType().GetProperty("IsDead")
                            .GetValue(health))
                    {
                        object damage = Activator.CreateInstance(
                            damageInfoType,
                            new object[]
                            {
                                100000f,
                                enemy.transform.position,
                                Vector3.forward,
                                null
                            });
                        health.GetType().GetMethod("ApplyDamage")
                            .Invoke(health, new[] { damage });
                    }
                }

                yield return null;
                yield return null;
            }

            Assert.That(
                directorType.GetProperty("CompletedWaveCount")
                    .GetValue(director),
                Is.EqualTo(1),
                "第一波必须在超时前完整生成并结算。");
            Assert.That(
                GetProgressCount(
                    directorType,
                    director,
                    "SpawnedCount"),
                Is.EqualTo(4));
            Assert.That(
                GetProgressCount(
                    directorType,
                    director,
                    "AliveCount"),
                Is.Zero);
            Assert.That(
                GetProgressCount(
                    directorType,
                    director,
                    "RemainingCount"),
                Is.Zero);
            Assert.That(
                directorType.GetProperty("CompletionEventCount")
                    .GetValue(director),
                Is.Zero,
                "普通波结束不得发布整局完成事件。");
            Assert.That(
                hudType.GetProperty("SpawnedText").GetValue(hud),
                Is.EqualTo("SPAWNED 4/4"));
            Assert.That(
                hudType.GetProperty("RemainingEnemiesText")
                    .GetValue(hud),
                Is.EqualTo("REMAINING 0"));
        }

        private static bool Register(
            Type stateType,
            object state,
            int spawnId)
        {
            return (bool)stateType.GetMethod("TryRegisterSpawn")
                .Invoke(state, new object[] { spawnId });
        }

        private static bool Settle(
            Type stateType,
            object state,
            int spawnId)
        {
            return (bool)stateType.GetMethod("TrySettle")
                .Invoke(state, new object[] { spawnId });
        }

        private static int GetProgressCount(
            Type directorType,
            object director,
            string propertyName)
        {
            object progress = directorType.GetProperty("CurrentProgress")
                .GetValue(director);
            return (int)progress.GetType().GetProperty(propertyName)
                .GetValue(progress);
        }

        private static List<Component> GetActiveEnemyControllers(
            Type directorType,
            object director)
        {
            object dictionary = directorType.GetProperty("ActiveEnemies")
                .GetValue(director);
            var result = new List<Component>();

            foreach (object pair in (IEnumerable)dictionary)
            {
                object handle = pair.GetType().GetProperty("Value")
                    .GetValue(pair);
                Component controller = (Component)handle.GetType()
                    .GetProperty("Controller").GetValue(handle);

                if (controller != null)
                {
                    result.Add(controller);
                }
            }

            return result;
        }

        private static void ValidateActiveSpawnPoints(
            Type directorType,
            object director)
        {
            List<Component> enemies = GetActiveEnemyControllers(
                directorType,
                director);
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Assert.That(player, Is.Not.Null);
            Assert.That(
                NavMesh.SamplePosition(
                    player.transform.position,
                    out NavMeshHit playerHit,
                    4f,
                    NavMesh.AllAreas),
                Is.True);

            for (int first = 0; first < enemies.Count; first++)
            {
                Component enemy = enemies[first];
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                Assert.That(agent, Is.Not.Null);
                Assert.That(agent.isOnNavMesh, Is.True);
                var path = new NavMeshPath();
                Assert.That(
                    NavMesh.CalculatePath(
                        playerHit.position,
                        enemy.transform.position,
                        NavMesh.AllAreas,
                        path),
                    Is.True);
                Assert.That(
                    path.status,
                    Is.EqualTo(NavMeshPathStatus.PathComplete));
            }
        }
    }
}
