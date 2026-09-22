using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue11MissionFlowTests
    {
        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [Test]
        public void ExtractionRequiresTerminalAndAllTargets()
        {
            Type flowType = RuntimeTypeResolver.GetType(
                "MissionFlowStateMachine");
            Assert.That(
                flowType,
                Is.Not.Null,
                "Issue 11 需要独立的任务流程状态机。");
            object flow = Activator.CreateInstance(flowType);
            flowType.GetMethod("Configure")
                .Invoke(flow, new object[] { 1 });

            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("EliminateTargets"));
            Assert.That(
                flowType.GetMethod("TryExtract").Invoke(flow, null),
                Is.EqualTo(false),
                "终端和清除目标未完成时不得撤离。");

            Assert.That(
                flowType.GetMethod("CompleteTerminal").Invoke(flow, null),
                Is.EqualTo(false),
                "清怪完成前终端阶段不得提前完成。");
            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("EliminateTargets"));
            Assert.That(
                flowType.GetMethod("TryExtract").Invoke(flow, null),
                Is.EqualTo(false),
                "仍有指定目标存活时不得撤离。");

            flowType.GetMethod("RegisterTargetEliminated")
                .Invoke(flow, null);
            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("ActivateTerminal"));
            Assert.That(
                flowType.GetMethod("CompleteTerminal").Invoke(flow, null),
                Is.EqualTo(true));
            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("ExtractionAvailable"));
            Assert.That(
                flowType.GetMethod("TryExtract").Invoke(flow, null),
                Is.EqualTo(true));
            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("Victory"));
        }

        [Test]
        public void EarlyTargetEliminationDoesNotSoftLockMission()
        {
            Type flowType = RuntimeTypeResolver.GetType(
                "MissionFlowStateMachine");
            object flow = Activator.CreateInstance(flowType);
            flowType.GetMethod("Configure")
                .Invoke(flow, new object[] { 1 });

            Assert.That(
                flowType.GetMethod("RegisterTargetEliminated")
                    .Invoke(flow, null),
                Is.EqualTo(true));
            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("ActivateTerminal"));
            Assert.That(
                flowType.GetMethod("CompleteTerminal")
                    .Invoke(flow, null),
                Is.EqualTo(true));
            Assert.That(
                flowType.GetProperty("State").GetValue(flow).ToString(),
                Is.EqualTo("ExtractionAvailable"));
            Assert.That(
                flowType.GetMethod("RegisterTargetEliminated")
                    .Invoke(flow, null),
                Is.EqualTo(false),
                "重复死亡事件不得重复累计目标。");
        }

        [Test]
        public void TerminalStaysLockedUntilEnemyObjectiveIsComplete()
        {
            Type terminalType = RuntimeTypeResolver.GetType(
                "TerminalInteractable");
            GameObject terminalObject = new GameObject("Locked Terminal");
            GameObject actor = new GameObject("Terminal Actor");

            try
            {
                Component terminal =
                    terminalObject.AddComponent(terminalType);
                terminalType.GetMethod("Configure").Invoke(
                    terminal,
                    new object[]
                    {
                        0.05f,
                        Enum.Parse(
                            RuntimeTypeResolver.GetType(
                                "TerminalInterruptionProgressMode"),
                            "Reset"),
                        Enum.Parse(
                            RuntimeTypeResolver.GetType(
                                "TerminalCompletionMode"),
                            "Silent"),
                        20f,
                        1f
                    });

                terminalType.GetMethod("SetMissionAvailable")
                    .Invoke(terminal, new object[] { false });
                object lockedView = terminalType.GetProperty("View")
                    .GetValue(terminal);
                Assert.That(
                    lockedView.GetType().GetProperty("Prompt")
                        .GetValue(lockedView),
                    Is.EqualTo("先清除怪物"));
                Assert.That(
                    terminalType.GetMethod("TryBegin")
                        .Invoke(terminal, new object[] { actor }),
                    Is.EqualTo(false));

                terminalType.GetMethod("SetMissionAvailable")
                    .Invoke(terminal, new object[] { true });
                Assert.That(
                    terminalType.GetMethod("TryBegin")
                        .Invoke(terminal, new object[] { actor }),
                    Is.EqualTo(true));
                Assert.That(
                    terminalType.GetMethod("Advance")
                        .Invoke(terminal, new object[] { actor, 0.05f }),
                    Is.EqualTo(true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
                UnityEngine.Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void StatisticsCountCombatActionsWithCorrectSemantics()
        {
            Type statisticsType = RuntimeTypeResolver.GetType(
                "MissionRunStatistics");
            Type shotType = RuntimeTypeResolver.GetType(
                "ShotResult");
            Type damageType = RuntimeTypeResolver.GetType(
                "DamageResult");
            Type regionType = RuntimeTypeResolver.GetType(
                "HitRegion");
            Type surfaceType = RuntimeTypeResolver.GetType(
                "SurfaceType");
            Type enemyType = RuntimeTypeResolver.GetType(
                "EnemyController");

            Assert.That(statisticsType, Is.Not.Null);
            object statistics = Activator.CreateInstance(statisticsType);
            object genericRegion = Enum.Parse(regionType, "Generic");
            object concrete = Enum.Parse(surfaceType, "Concrete");
            object noDamage = Activator.CreateInstance(
                damageType,
                new object[] { false, false, 0f, genericRegion });
            object killDamage = Activator.CreateInstance(
                damageType,
                new object[] { true, true, 50f, genericRegion });
            GameObject wall = new GameObject("Wall");
            GameObject enemy = new GameObject("Mission Enemy");

            try
            {
                enemy.AddComponent(enemyType);
                object miss = shotType.GetProperty("Miss").GetValue(null);
                object wallHit = Activator.CreateInstance(
                    shotType,
                    new object[]
                    {
                        true,
                        Vector3.zero,
                        Vector3.up,
                        concrete,
                        noDamage,
                        wall
                    });
                object enemyKill = Activator.CreateInstance(
                    shotType,
                    new object[]
                    {
                        true,
                        Vector3.zero,
                        Vector3.up,
                        concrete,
                        killDamage,
                        enemy
                    });

                statisticsType.GetMethod("RegisterShot")
                    .Invoke(statistics, new[] { miss });
                statisticsType.GetMethod("RegisterShot")
                    .Invoke(statistics, new[] { wallHit });
                statisticsType.GetMethod("RegisterShot")
                    .Invoke(statistics, new[] { enemyKill });
                statisticsType.GetMethod("RegisterDamageTaken")
                    .Invoke(statistics, null);
                statisticsType.GetMethod("AdvanceTime")
                    .Invoke(statistics, new object[] { 12.5f });

                Assert.That(
                    statisticsType.GetProperty("ShotsFired")
                        .GetValue(statistics),
                    Is.EqualTo(3));
                Assert.That(
                    statisticsType.GetProperty("Hits")
                        .GetValue(statistics),
                    Is.EqualTo(1),
                    "墙体碰撞不应计为命中。");
                Assert.That(
                    statisticsType.GetProperty("Kills")
                        .GetValue(statistics),
                    Is.EqualTo(1));
                Assert.That(
                    statisticsType.GetProperty("DamageTakenCount")
                        .GetValue(statistics),
                    Is.EqualTo(1));
                Assert.That(
                    (float)statisticsType.GetProperty("Accuracy")
                        .GetValue(statistics),
                    Is.EqualTo(1f / 3f).Within(0.001f));
                Assert.That(
                    (float)statisticsType.GetProperty("ElapsedSeconds")
                        .GetValue(statistics),
                    Is.EqualTo(12.5f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [UnityTest]
        public IEnumerator CityNewConnectsTerminalTargetAndExtraction()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type missionType = RuntimeTypeResolver.GetType(
                "CityNewMissionController");
            Type terminalType = RuntimeTypeResolver.GetType(
                "TerminalInteractable");
            Type healthType = RuntimeTypeResolver.GetType(
                "Health");
            Type damageInfoType = RuntimeTypeResolver.GetType(
                "DamageInfo");
            Component playerController =
                FindFirstComponent("PlayerController");
            GameObject player = playerController.gameObject;
            Component mission = player.GetComponent(missionType);

            Assert.That(mission, Is.Not.Null);
            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("EliminateTargets"));
            Assert.That(
                missionType.GetProperty("ExtractionAvailable")
                    .GetValue(mission),
                Is.EqualTo(false));

            Component terminal = (Component)missionType
                .GetProperty("Terminal")
                .GetValue(mission);
            terminalType.GetMethod("Configure")
                .Invoke(terminal, new object[]
                {
                    0.05f,
                    Enum.Parse(
                        RuntimeTypeResolver.GetType(
                            "TerminalInterruptionProgressMode"),
                        "Reset"),
                    Enum.Parse(
                        RuntimeTypeResolver.GetType(
                            "TerminalCompletionMode"),
                        "Silent"),
                    32f,
                    1f
                });
            Assert.That(
                terminalType.GetProperty("MissionAvailable")
                    .GetValue(terminal),
                Is.EqualTo(false));
            Assert.That(
                terminalType.GetMethod("TryBegin")
                    .Invoke(terminal, new object[] { player }),
                Is.EqualTo(false),
                "清怪完成前终端必须锁定。");

            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("EliminateTargets"));

            Type directorType = RuntimeTypeResolver.GetType(
                "WaveDirector");
            Component director = (Component)UnityEngine.Object
                .FindAnyObjectByType(directorType);
            Type upgradeType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Component upgrades = player.GetComponent(upgradeType);
            Assert.That(director, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30f;

            while (!(bool)directorType.GetProperty("IsCompleted")
                       .GetValue(director) &&
                   Time.realtimeSinceStartup < deadline)
            {
                object activeEnemies = directorType
                    .GetProperty("ActiveEnemies").GetValue(director);
                var controllers = new ArrayList();

                foreach (object pair in (IEnumerable)activeEnemies)
                {
                    object handle = pair.GetType().GetProperty("Value")
                        .GetValue(pair);
                    Component controller = (Component)handle.GetType()
                        .GetProperty("Controller").GetValue(handle);

                    if (controller != null)
                    {
                        controllers.Add(controller);
                    }
                }

                object encounterEnemies = directorType
                    .GetProperty("EncounterEnemies").GetValue(director);
                foreach (object pair in (IEnumerable)encounterEnemies)
                {
                    object handle = pair.GetType().GetProperty("Value")
                        .GetValue(pair);
                    Component controller = (Component)handle.GetType()
                        .GetProperty("Controller").GetValue(handle);
                    if (controller != null) controllers.Add(controller);
                }

                foreach (Component controller in controllers)
                {
                    Component targetHealth =
                        controller.GetComponent(healthType);

                    if ((bool)healthType.GetProperty("IsDead")
                        .GetValue(targetHealth))
                    {
                        continue;
                    }

                    object lethalDamage = Activator.CreateInstance(
                        damageInfoType,
                        new object[]
                        {
                            999f,
                            targetHealth.transform.position,
                            Vector3.forward,
                            player
                        });
                    healthType.GetMethod("ApplyDamage")
                        .Invoke(targetHealth, new[] { lethalDamage });
                }

                if (upgrades != null &&
                    (bool)upgradeType.GetProperty("IsChoiceOpen")
                        .GetValue(upgrades))
                {
                    for (int candidate = 0; candidate < 3; candidate++)
                    {
                        if ((bool)upgradeType.GetMethod("TrySelect")
                            .Invoke(upgrades, new object[] { candidate }))
                        {
                            break;
                        }
                    }
                }

                yield return null;
                yield return null;
            }

            Assert.That(
                directorType.GetProperty("IsCompleted").GetValue(director),
                Is.True,
                "任务撤离必须等待整波敌人清除完成。");

            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("ActivateTerminal"));
            Assert.That(
                terminalType.GetProperty("MissionAvailable")
                    .GetValue(terminal),
                Is.EqualTo(true));
            Assert.That(
                terminalType.GetMethod("TryBegin")
                    .Invoke(terminal, new object[] { player }),
                Is.EqualTo(true));
            Assert.That(
                terminalType.GetMethod("Advance")
                    .Invoke(terminal, new object[] { player, 0.05f }),
                Is.EqualTo(true));
            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("ExtractionAvailable"));
            Assert.That(
                missionType.GetProperty("ExtractionAvailable")
                    .GetValue(mission),
                Is.EqualTo(true));
            Assert.That(
                missionType.GetMethod("TryEnterExtraction")
                    .Invoke(mission, new object[] { player }),
                Is.EqualTo(true));
            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("Victory"));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(
                playerController.GetType()
                    .GetProperty("GameplayInputEnabled")
                    .GetValue(playerController),
                Is.EqualTo(false));
        }

        [UnityTest]
        public IEnumerator PauseActionsAndDefeatUseUnifiedMissionFlow()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type missionType = RuntimeTypeResolver.GetType(
                "CityNewMissionController");
            Component playerController =
                FindFirstComponent("PlayerController");
            Component mission =
                playerController.GetComponent(missionType);
            Component failure =
                playerController.GetComponent(
                    "PlayerFailureFlowController");
            Component health =
                playerController.GetComponent("Health");

            playerController.GetType().GetMethod("SetPaused")
                .Invoke(playerController, new object[] { true });
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(
                missionType.GetMethod("ResumeGame")
                    .Invoke(mission, null),
                Is.EqualTo(true));
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            Type damageInfoType = RuntimeTypeResolver.GetType(
                "DamageInfo");
            object lethalDamage = Activator.CreateInstance(
                damageInfoType,
                new object[]
                {
                    999f,
                    playerController.transform.position,
                    Vector3.forward,
                    null
                });
            health.GetType().GetMethod("ApplyDamage")
                .Invoke(health, new[] { lethalDamage });
            yield return null;

            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("Defeat"));
            Assert.That(
                failure.GetType().GetProperty("IsFailed")
                    .GetValue(failure),
                Is.EqualTo(true));
            Assert.That(
                failure.GetType()
                    .GetProperty("ExternalPresentationEnabled")
                    .GetValue(failure),
                Is.EqualTo(true),
                "失败界面应由统一结算流程接管，避免双层 UI。");
            Assert.That(Time.timeScale, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator RestartCreatesFreshMissionAndStatistics()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type missionType = RuntimeTypeResolver.GetType(
                "CityNewMissionController");
            Component playerController =
                FindFirstComponent("PlayerController");
            Component mission =
                playerController.GetComponent(missionType);
            object statistics = missionType.GetProperty("Statistics")
                .GetValue(mission);
            statistics.GetType().GetMethod("AdvanceTime")
                .Invoke(statistics, new object[] { 30f });
            playerController.GetType().GetMethod("SetPaused")
                .Invoke(playerController, new object[] { true });

            Assert.That(
                missionType.GetMethod("RestartLevel")
                    .Invoke(mission, null),
                Is.EqualTo(true));
            yield return null;
            yield return null;
            yield return null;

            Component restoredPlayer =
                FindFirstComponent("PlayerController");
            Component restoredMission =
                restoredPlayer.GetComponent(missionType);
            object restoredStatistics = missionType
                .GetProperty("Statistics")
                .GetValue(restoredMission);

            Assert.That(restoredMission, Is.Not.SameAs(mission));
            Assert.That(
                missionType.GetProperty("State")
                    .GetValue(restoredMission)
                    .ToString(),
                Is.EqualTo("EliminateTargets"));
            Assert.That(
                restoredStatistics.GetType()
                    .GetProperty("ShotsFired")
                    .GetValue(restoredStatistics),
                Is.EqualTo(0));
            Assert.That(
                (float)restoredStatistics.GetType()
                    .GetProperty("ElapsedSeconds")
                    .GetValue(restoredStatistics),
                Is.LessThan(1f));
            Assert.That(
                missionType.GetProperty("ExtractionAvailable")
                    .GetValue(restoredMission),
                Is.EqualTo(false));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        private static Component FindFirstComponent(string typeName)
        {
            foreach (MonoBehaviour behaviour in
                UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                if (behaviour != null &&
                    behaviour.GetType().Name == typeName)
                {
                    return behaviour;
                }
            }

            return null;
        }
    }
}
