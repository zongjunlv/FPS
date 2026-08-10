using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue16MultiWaveTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreRuntimeState()
        {
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [Test]
        public void CurrentWaveCannotCompleteWhileEnemyIsAlive()
        {
            (Type flowType, object flow) = CreateFlow(
                (2, 2, 2f),
                (1, 1, 1f),
                (1, 1, 0f));
            flowType.GetMethod("StartRun").Invoke(flow, null);
            Register(flowType, flow, 1);
            Register(flowType, flow, 2);

            Assert.That(Settle(flowType, flow, 1), Is.True);
            AssertFlow(flowType, flow, 1, "Fighting");
            Assert.That(
                flowType.GetProperty("AliveCount").GetValue(flow),
                Is.EqualTo(1));
        }

        [Test]
        public void ThreeWavesUseIntermissionAndCompleteOnlyAtTheEnd()
        {
            (Type flowType, object flow) = CreateFlow(
                (2, 2, 2f),
                (1, 1, 0f),
                (2, 1, 4f));
            flowType.GetMethod("StartRun").Invoke(flow, null);

            CompleteCurrentWave(flowType, flow, 1, 2);
            AssertFlow(flowType, flow, 1, "Intermission");
            Assert.That(
                flowType.GetMethod("Tick")
                    .Invoke(flow, new object[] { 1f }),
                Is.EqualTo(false));
            AssertFlow(flowType, flow, 1, "Intermission");
            Assert.That(
                flowType.GetMethod("Tick")
                    .Invoke(flow, new object[] { 1f }),
                Is.EqualTo(true));
            AssertFlow(flowType, flow, 2, "Spawning");

            CompleteCurrentWave(flowType, flow, 3);
            AssertFlow(flowType, flow, 3, "Spawning");
            CompleteCurrentWave(flowType, flow, 4, 5);
            AssertFlow(flowType, flow, 3, "Completed");
            Assert.That(
                flowType.GetProperty("IsCompleted").GetValue(flow),
                Is.EqualTo(true));
        }

        [Test]
        public void StoppedRunCannotResumeFromIntermissionTick()
        {
            (Type flowType, object flow) = CreateFlow(
                (1, 1, 5f),
                (1, 1, 1f),
                (1, 1, 0f));
            flowType.GetMethod("StartRun").Invoke(flow, null);
            CompleteCurrentWave(flowType, flow, 1);

            Assert.That(
                flowType.GetMethod("StopRun").Invoke(flow, null),
                Is.EqualTo(true));
            Assert.That(
                flowType.GetMethod("Tick")
                    .Invoke(flow, new object[] { 10f }),
                Is.EqualTo(false));
            AssertFlow(flowType, flow, 1, "Stopped");
        }

        [UnityTest]
        public IEnumerator CityNewRunsThreeDistinctWavesBeforeExtraction()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = Type.GetType(
                "WaveDirector, Assembly-CSharp");
            Type missionType = Type.GetType(
                "CityNewMissionController, Assembly-CSharp");
            Type hudType = Type.GetType(
                "UnifiedGameHud, Assembly-CSharp");
            Component director = null;
            Component mission = null;
            Component hud = null;
            GameObject player = null;
            float deadline = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < deadline)
            {
                director = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                player = GameObject.FindGameObjectWithTag("Player");
                mission = player != null
                    ? player.GetComponent(missionType)
                    : null;
                hud = (Component)UnityEngine.Object
                    .FindAnyObjectByType(hudType);

                if (director != null && mission != null && hud != null &&
                    GetInt(directorType, director, "WaveStartedEventCount") == 1)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(director, Is.Not.Null);
            Assert.That(mission, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            CompleteTerminal(missionType, mission, player);
            Assert.That(
                missionType.GetProperty("State").GetValue(mission).ToString(),
                Is.EqualTo("EliminateTargets"));
            Assert.That(
                hudType.GetProperty("WaveText").GetValue(hud),
                Is.EqualTo("WAVE 1/3"));
            Assert.That(
                hudType.GetProperty("WaveCueText").GetValue(hud),
                Is.EqualTo("WAVE 1 START"));

            yield return WaitForSpawned(
                directorType,
                director,
                1,
                3,
                20f);
            Assert.That(GetPhase(directorType, director), Is.EqualTo("Spawning"));
            Assert.That(GetAlive(directorType, director), Is.EqualTo(3));
            yield return KillOneActiveEnemy(directorType, director);
            yield return WaitForSpawned(
                directorType,
                director,
                1,
                4,
                10f);
            Assert.That(GetPhase(directorType, director), Is.EqualTo("Fighting"));

            yield return KillUntilWaveEnds(directorType, director, 1, 20f);
            Assert.That(GetPhase(directorType, director), Is.EqualTo("Intermission"));
            Assert.That(
                missionType.GetProperty("ExtractionAvailable")
                    .GetValue(mission),
                Is.EqualTo(false),
                "普通波结束不得开放撤离。");
            Assert.That(
                hudType.GetProperty("IsWaveCountdownVisible").GetValue(hud),
                Is.EqualTo(true));
            Assert.That(
                hudType.GetProperty("WaveCueText").GetValue(hud),
                Is.EqualTo("WAVE 1 CLEARED"));

            yield return WaitForWaveStart(directorType, director, 2, 10f);
            Assert.That(
                hudType.GetProperty("WaveText").GetValue(hud),
                Is.EqualTo("WAVE 2/3"));
            Assert.That(
                hudType.GetProperty("IsWaveCountdownVisible").GetValue(hud),
                Is.EqualTo(false));
            yield return WaitForSpawned(
                directorType,
                director,
                2,
                3,
                15f);
            Assert.That(GetAlive(directorType, director), Is.EqualTo(3));
            yield return KillUntilWaveEnds(directorType, director, 2, 30f);
            Assert.That(
                missionType.GetProperty("ExtractionAvailable")
                    .GetValue(mission),
                Is.EqualTo(false),
                "第二波结束仍不得开放撤离。");

            yield return WaitForWaveStart(directorType, director, 3, 10f);
            yield return WaitForSpawned(
                directorType,
                director,
                3,
                4,
                15f);
            Assert.That(GetAlive(directorType, director), Is.EqualTo(4));
            yield return KillUntilCompleted(directorType, director, 40f);
            Assert.That(GetPhase(directorType, director), Is.EqualTo("Completed"));
            Assert.That(
                GetInt(directorType, director, "CompletionEventCount"),
                Is.EqualTo(1));
            Assert.That(
                GetInt(directorType, director, "WaveStartedEventCount"),
                Is.EqualTo(3));
            Assert.That(
                GetInt(directorType, director, "WaveEndedEventCount"),
                Is.EqualTo(3));
            Assert.That(
                GetCompletedWaveCounts(directorType, director),
                Is.EqualTo(new[] { 4, 6, 8 }));
            Assert.That(
                GetPeakAliveCounts(directorType, director),
                Is.EqualTo(new[] { 3, 3, 4 }));
            Assert.That(
                missionType.GetProperty("ExtractionAvailable")
                    .GetValue(mission),
                Is.EqualTo(true),
                "只有最终波完成后才开放撤离。");
            Assert.That(
                hudType.GetProperty("WaveText").GetValue(hud),
                Is.EqualTo("WAVE 3/3"));
            Assert.That(
                hudType.GetProperty("RemainingEnemiesText").GetValue(hud),
                Is.EqualTo("REMAINING 0"));
            Assert.That(
                hudType.GetProperty("WaveCueText").GetValue(hud),
                Is.EqualTo("ALL WAVES CLEARED"));
        }

        [UnityTest]
        public IEnumerator PlayerDeathStopsRunAndSceneRestartResetsIt()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = Type.GetType(
                "WaveDirector, Assembly-CSharp");
            Type missionType = Type.GetType(
                "CityNewMissionController, Assembly-CSharp");
            Component oldDirector = null;
            Component mission = null;
            GameObject player = null;
            float deadline = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < deadline)
            {
                oldDirector = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                player = GameObject.FindGameObjectWithTag("Player");
                mission = player != null
                    ? player.GetComponent(missionType)
                    : null;

                if (oldDirector != null && mission != null &&
                    GetSpawned(directorType, oldDirector) > 0)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(oldDirector, Is.Not.Null);
            Assert.That(mission, Is.Not.Null);
            ApplyLethalDamage(player.GetComponent("Health"));
            yield return null;
            Assert.That(GetPhase(directorType, oldDirector), Is.EqualTo("Stopped"));
            Assert.That(
                directorType.GetProperty("StopReason")
                    .GetValue(oldDirector)
                    .ToString(),
                Is.EqualTo("PlayerDied"));
            Assert.That(GetAlive(directorType, oldDirector), Is.GreaterThan(0),
                "停止保留权威计数，活动实例则由工厂释放。");
            Assert.That(GetActiveEnemies(directorType, oldDirector).Count, Is.Zero);
            int stoppedSpawned = GetSpawned(directorType, oldDirector);
            yield return null;
            yield return null;
            Assert.That(GetSpawned(directorType, oldDirector), Is.EqualTo(stoppedSpawned));

            Assert.That(
                missionType.GetMethod("RestartLevel").Invoke(mission, null),
                Is.EqualTo(true));
            Component newDirector = null;
            deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                newDirector = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);

                if (newDirector != null && newDirector != oldDirector &&
                    GetInt(directorType, newDirector, "WaveStartedEventCount") == 1)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(newDirector, Is.Not.Null);
            Assert.That(newDirector, Is.Not.SameAs(oldDirector));
            Assert.That(GetCurrentWave(directorType, newDirector), Is.EqualTo(1));
            Assert.That(
                directorType.GetProperty("StopReason").GetValue(newDirector)
                    .ToString(),
                Is.EqualTo("None"));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        private static IEnumerator WaitForSpawned(
            Type directorType,
            Component director,
            int waveNumber,
            int spawnedCount,
            float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                DisableEnemyThreats(directorType, director);

                if (GetCurrentWave(directorType, director) == waveNumber &&
                    GetSpawned(directorType, director) >= spawnedCount)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"等待第{waveNumber}波生成{spawnedCount}只敌人超时；" +
                $"phase={GetPhase(directorType, director)}, " +
                $"spawned={GetSpawned(directorType, director)}, " +
                $"alive={GetAlive(directorType, director)}");
        }

        private static IEnumerator WaitForWaveStart(
            Type directorType,
            Component director,
            int waveNumber,
            float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (GetCurrentWave(directorType, director) == waveNumber &&
                    (GetPhase(directorType, director) == "Spawning" ||
                     GetPhase(directorType, director) == "Fighting"))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"等待第{waveNumber}波开始超时。");
        }

        private static IEnumerator KillUntilWaveEnds(
            Type directorType,
            Component director,
            int expectedCompletedWaves,
            float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;

            while (GetInt(directorType, director, "CompletedWaveCount") <
                   expectedCompletedWaves &&
                   Time.realtimeSinceStartup < deadline)
            {
                DisableEnemyThreats(directorType, director);
                KillAllActiveEnemies(directorType, director);
                yield return null;
                yield return null;
            }

            Assert.That(
                GetInt(directorType, director, "CompletedWaveCount"),
                Is.GreaterThanOrEqualTo(expectedCompletedWaves),
                $"第{expectedCompletedWaves}波未在超时前结束。");
        }

        private static IEnumerator KillUntilCompleted(
            Type directorType,
            Component director,
            float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;

            while (!(bool)directorType.GetProperty("IsCompleted")
                       .GetValue(director) &&
                   Time.realtimeSinceStartup < deadline)
            {
                DisableEnemyThreats(directorType, director);
                KillAllActiveEnemies(directorType, director);
                yield return null;
                yield return null;
            }

            Assert.That(
                directorType.GetProperty("IsCompleted").GetValue(director),
                Is.EqualTo(true),
                "三波未在超时前完成。");
        }

        private static IEnumerator KillOneActiveEnemy(
            Type directorType,
            Component director)
        {
            List<Component> active = GetActiveEnemies(directorType, director);
            Assert.That(active.Count, Is.GreaterThan(0));
            ApplyLethalDamage(active[0].GetComponent("Health"));
            yield return null;
            yield return null;
        }

        private static void KillAllActiveEnemies(
            Type directorType,
            Component director)
        {
            foreach (Component enemy in GetActiveEnemies(directorType, director))
            {
                Component health = enemy.GetComponent("Health");

                if (health != null &&
                    !(bool)health.GetType().GetProperty("IsDead")
                        .GetValue(health))
                {
                    ApplyLethalDamage(health);
                }
            }
        }

        private static void DisableEnemyThreats(
            Type directorType,
            Component director)
        {
            foreach (Component enemy in GetActiveEnemies(directorType, director))
            {
                Behaviour combat = enemy.GetComponent("EnemyCombatController")
                    as Behaviour;

                if (combat != null)
                {
                    combat.enabled = false;
                }

                Component perception =
                    enemy.GetComponent("EnemyPerceptionController");
                perception?.GetType().GetMethod("SetTarget")
                    ?.Invoke(perception, new object[] { null });
            }
        }

        private static List<Component> GetActiveEnemies(
            Type directorType,
            Component director)
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

        private static void CompleteTerminal(
            Type missionType,
            Component mission,
            GameObject player)
        {
            Component terminal = (Component)missionType
                .GetProperty("Terminal").GetValue(mission);
            Type terminalType = terminal.GetType();
            Type interruptionType = Type.GetType(
                "TerminalInterruptionProgressMode, Assembly-CSharp");
            Type completionType = Type.GetType(
                "TerminalCompletionMode, Assembly-CSharp");
            terminalType.GetMethod("Configure").Invoke(
                terminal,
                new object[]
                {
                    0.05f,
                    Enum.Parse(interruptionType, "Reset"),
                    Enum.Parse(completionType, "Silent"),
                    32f,
                    1f
                });
            terminalType.GetMethod("TryBegin")
                .Invoke(terminal, new object[] { player });
            terminalType.GetMethod("Advance")
                .Invoke(terminal, new object[] { player, 0.05f });
        }

        private static void ApplyLethalDamage(Component health)
        {
            Type damageInfoType = Type.GetType(
                "DamageInfo, Assembly-CSharp");
            object damage = Activator.CreateInstance(
                damageInfoType,
                new object[]
                {
                    100000f,
                    health.transform.position,
                    Vector3.forward,
                    null
                });
            health.GetType().GetMethod("ApplyDamage")
                .Invoke(health, new[] { damage });
        }

        private static int[] GetCompletedWaveCounts(
            Type directorType,
            Component director)
        {
            return CopyIntList(
                directorType.GetProperty("CompletedWaveSpawnCounts")
                    .GetValue(director));
        }

        private static int[] GetPeakAliveCounts(
            Type directorType,
            Component director)
        {
            return CopyIntList(
                directorType.GetProperty("PeakAliveByWave")
                    .GetValue(director));
        }

        private static int[] CopyIntList(object values)
        {
            var result = new List<int>();

            foreach (object value in (IEnumerable)values)
            {
                result.Add((int)value);
            }

            return result.ToArray();
        }

        private static int GetInt(
            Type directorType,
            Component director,
            string propertyName)
        {
            return (int)directorType.GetProperty(propertyName)
                .GetValue(director);
        }

        private static object GetProgress(
            Type directorType,
            Component director)
        {
            return directorType.GetProperty("CurrentProgress")
                .GetValue(director);
        }

        private static int GetProgressInt(
            Type directorType,
            Component director,
            string propertyName)
        {
            object progress = GetProgress(directorType, director);
            return (int)progress.GetType().GetProperty(propertyName)
                .GetValue(progress);
        }

        private static int GetCurrentWave(Type type, Component director) =>
            GetProgressInt(type, director, "CurrentWave");

        private static int GetSpawned(Type type, Component director) =>
            GetProgressInt(type, director, "SpawnedCount");

        private static int GetAlive(Type type, Component director) =>
            GetProgressInt(type, director, "AliveCount");

        private static string GetPhase(Type type, Component director) =>
            type.GetProperty("Phase").GetValue(director).ToString();

        private static (Type, object) CreateFlow(
            params (int total, int maximumAlive, float rest)[] stages)
        {
            Type flowType = Type.GetType(
                "MultiWaveFlowState, Assembly-CSharp");
            Type rulesType = Type.GetType(
                "WaveStageRules, Assembly-CSharp");
            Assert.That(flowType, Is.Not.Null);
            Assert.That(rulesType, Is.Not.Null);
            Array rules = Array.CreateInstance(rulesType, stages.Length);

            for (int index = 0; index < stages.Length; index++)
            {
                rules.SetValue(
                    Activator.CreateInstance(
                        rulesType,
                        new object[]
                        {
                            stages[index].total,
                            stages[index].maximumAlive,
                            stages[index].rest
                        }),
                    index);
            }

            object flow = Activator.CreateInstance(
                flowType,
                new object[] { rules });
            return (flowType, flow);
        }

        private static void CompleteCurrentWave(
            Type flowType,
            object flow,
            params int[] spawnIds)
        {
            foreach (int spawnId in spawnIds)
            {
                Register(flowType, flow, spawnId);
                Assert.That(Settle(flowType, flow, spawnId), Is.True);
            }
        }

        private static void Register(Type flowType, object flow, int spawnId)
        {
            Assert.That(
                flowType.GetMethod("TryRegisterSpawn")
                    .Invoke(flow, new object[] { spawnId }),
                Is.EqualTo(true));
        }

        private static bool Settle(Type flowType, object flow, int spawnId)
        {
            return (bool)flowType.GetMethod("TrySettle")
                .Invoke(flow, new object[] { spawnId });
        }

        private static void AssertFlow(
            Type flowType,
            object flow,
            int wave,
            string phase)
        {
            Assert.That(
                flowType.GetProperty("CurrentWave").GetValue(flow),
                Is.EqualTo(wave));
            Assert.That(
                flowType.GetProperty("Phase").GetValue(flow).ToString(),
                Is.EqualTo(phase));
        }
    }
}
