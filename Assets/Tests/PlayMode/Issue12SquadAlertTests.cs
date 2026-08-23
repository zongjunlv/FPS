using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue12SquadAlertTests
    {
        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
        }

        [Test]
        public void ReceiverAcceptsOnlyFreshInRangeNewIntel()
        {
            Type alertType = RuntimeTypeResolver.GetType(
                "EnemySquadAlert");
            Type memoryType = RuntimeTypeResolver.GetType(
                "EnemyAlertMemory");
            Assert.That(alertType, Is.Not.Null);
            Assert.That(memoryType, Is.Not.Null);
            GameObject source = new GameObject("Alert Source");
            object memory = Activator.CreateInstance(memoryType);
            memoryType.GetMethod("Configure")
                .Invoke(memory, new object[] { 3f });

            try
            {
                object fresh = CreateAlert(
                    alertType,
                    source,
                    Vector3.zero,
                    new Vector3(8f, 0f, 4f),
                    10f,
                    0.9f,
                    12f,
                    1);
                Assert.That(
                    memoryType.GetMethod("TryAccept")
                        .Invoke(
                            memory,
                            new object[]
                            {
                                fresh,
                                Vector3.right * 5f,
                                11f
                            }),
                    Is.EqualTo(true));
                Assert.That(
                    memoryType.GetProperty("AcceptedCount")
                        .GetValue(memory),
                    Is.EqualTo(1));

                Assert.That(
                    memoryType.GetMethod("TryAccept")
                        .Invoke(
                            memory,
                            new object[]
                            {
                                fresh,
                                Vector3.right * 5f,
                                11.1f
                            }),
                    Is.EqualTo(false),
                    "同一序号的重复警报必须被忽略。");

                object stale = CreateAlert(
                    alertType,
                    source,
                    Vector3.zero,
                    Vector3.back * 20f,
                    9f,
                    1f,
                    12f,
                    2);
                Assert.That(
                    memoryType.GetMethod("TryAccept")
                        .Invoke(
                            memory,
                            new object[]
                            {
                                stale,
                                Vector3.right * 5f,
                                11.2f
                            }),
                    Is.EqualTo(false),
                    "旧时间戳不得覆盖更新的最后目击位置。");

                object expired = CreateAlert(
                    alertType,
                    source,
                    Vector3.zero,
                    Vector3.forward * 30f,
                    5f,
                    1f,
                    12f,
                    3);
                Assert.That(
                    memoryType.GetMethod("TryAccept")
                        .Invoke(
                            memory,
                            new object[]
                            {
                                expired,
                                Vector3.right * 5f,
                                11.2f
                            }),
                    Is.EqualTo(false));

                object outOfRange = CreateAlert(
                    alertType,
                    source,
                    Vector3.zero,
                    Vector3.forward * 40f,
                    12f,
                    1f,
                    4f,
                    4);
                Assert.That(
                    memoryType.GetMethod("TryAccept")
                        .Invoke(
                            memory,
                            new object[]
                            {
                                outOfRange,
                                Vector3.right * 5f,
                                12f
                            }),
                    Is.EqualTo(false));
                Assert.That(
                    alertType.GetProperty("Source").GetValue(fresh),
                    Is.SameAs(source));
                Assert.That(
                    alertType.GetProperty("LastKnownPosition")
                        .GetValue(fresh),
                    Is.EqualTo(new Vector3(8f, 0f, 4f)));
                Assert.That(
                    alertType.GetProperty("Timestamp").GetValue(fresh),
                    Is.EqualTo(10f));
                Assert.That(
                    alertType.GetProperty("Confidence").GetValue(fresh),
                    Is.EqualTo(0.9f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CoordinatorFiltersRangeAndAppliesSourceCooldown()
        {
            Type coordinatorType = RuntimeTypeResolver.GetType(
                "EnemySquadCoordinator");
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Assert.That(coordinatorType, Is.Not.Null);
            GameObject coordinatorObject =
                new GameObject("Test Squad Coordinator");
            GameObject sourceObject =
                new GameObject("Squad Source");
            GameObject nearObject =
                new GameObject("Near Receiver");
            GameObject farObject =
                new GameObject("Far Receiver");

            try
            {
                Component coordinator =
                    coordinatorObject.AddComponent(coordinatorType);
                Component source =
                    sourceObject.AddComponent(perceptionType);
                Component near =
                    nearObject.AddComponent(perceptionType);
                Component far =
                    farObject.AddComponent(perceptionType);
                sourceObject.transform.position = Vector3.zero;
                nearObject.transform.position = Vector3.right * 5f;
                farObject.transform.position = Vector3.right * 15f;
                coordinatorType.GetMethod("Configure")
                    .Invoke(
                        coordinator,
                        new object[] { 10f, 1f, 3f, 4f });
                coordinatorType.GetMethod("Register")
                    .Invoke(coordinator, new[] { source });
                coordinatorType.GetMethod("Register")
                    .Invoke(coordinator, new[] { near });
                coordinatorType.GetMethod("Register")
                    .Invoke(coordinator, new[] { far });

                Vector3 firstPosition =
                    new Vector3(2f, 0f, 8f);
                Assert.That(
                    coordinatorType.GetMethod("TryBroadcast")
                        .Invoke(
                            coordinator,
                            new object[]
                            {
                                source,
                                firstPosition,
                                0.9f,
                                10f
                            }),
                    Is.EqualTo(true));
                Assert.That(
                    perceptionType.GetProperty(
                            "ReceivedSquadAlertCount")
                        .GetValue(near),
                    Is.EqualTo(1));
                Assert.That(
                    perceptionType.GetProperty(
                            "ReceivedSquadAlertCount")
                        .GetValue(far),
                    Is.EqualTo(0),
                    "传播范围外的敌人不得收到警报。");
                Assert.That(
                    perceptionType.GetProperty("State")
                        .GetValue(near)
                        .ToString(),
                    Is.EqualTo("Alert"));

                Vector3 newerPosition =
                    new Vector3(6f, 0f, 10f);
                Assert.That(
                    coordinatorType.GetMethod("TryBroadcast")
                        .Invoke(
                            coordinator,
                            new object[]
                            {
                                source,
                                newerPosition,
                                1f,
                                10.5f
                            }),
                    Is.EqualTo(false),
                    "来源冷却期间不得重复广播。");
                Assert.That(
                    coordinatorType.GetProperty("BroadcastCount")
                        .GetValue(coordinator),
                    Is.EqualTo(1));

                Assert.That(
                    coordinatorType.GetMethod("TryBroadcast")
                        .Invoke(
                            coordinator,
                            new object[]
                            {
                                source,
                                newerPosition,
                                1f,
                                11.1f
                            }),
                    Is.EqualTo(true));
                Assert.That(
                    perceptionType.GetProperty("LastKnownPosition")
                        .GetValue(near),
                    Is.EqualTo(newerPosition),
                    "重新看到玩家后必须广播最新位置。");
                Assert.That(
                    coordinatorType.GetProperty("BroadcastCount")
                        .GetValue(coordinator),
                    Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(farObject);
                UnityEngine.Object.DestroyImmediate(nearObject);
                UnityEngine.Object.DestroyImmediate(sourceObject);
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
            }
        }

        [Test]
        public void SharedIntelConfidenceSelectsSearchOrAlertState()
        {
            Type awarenessType = RuntimeTypeResolver.GetType(
                "EnemyAwarenessStateMachine");
            Assert.That(awarenessType, Is.Not.Null);
            object lowConfidenceReceiver =
                Activator.CreateInstance(awarenessType);
            object highConfidenceReceiver =
                Activator.CreateInstance(awarenessType);
            Vector3 position = new Vector3(4f, 0f, 7f);

            awarenessType.GetMethod("ApplySharedAlert")
                .Invoke(
                    lowConfidenceReceiver,
                    new object[] { position, 0.55f });
            awarenessType.GetMethod("ApplySharedAlert")
                .Invoke(
                    highConfidenceReceiver,
                    new object[] { position, 0.9f });

            Assert.That(
                awarenessType.GetProperty("State")
                    .GetValue(lowConfidenceReceiver)
                    .ToString(),
                Is.EqualTo("Search"),
                "低置信度情报应让接收者搜索，不应直接锁定玩家。");
            Assert.That(
                awarenessType.GetProperty("State")
                    .GetValue(highConfidenceReceiver)
                    .ToString(),
                Is.EqualTo("Alert"),
                "高置信度情报应让接收者进入警戒追踪。");
            Assert.That(
                awarenessType.GetProperty("LastKnownPosition")
                    .GetValue(highConfidenceReceiver),
                Is.EqualTo(position));
        }

        [Test]
        public void BroadcastKeepsDebugDataWithoutWorldVisuals()
        {
            Type coordinatorType = RuntimeTypeResolver.GetType(
                "EnemySquadCoordinator");
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Type debugViewType = RuntimeTypeResolver.GetType(
                "EnemySquadAlertDebugView");
            GameObject coordinatorObject =
                new GameObject("Debug Coordinator");
            GameObject sourceObject =
                new GameObject("Debug Source");
            GameObject receiverObject =
                new GameObject("Debug Receiver");

            try
            {
                Component coordinator =
                    coordinatorObject.AddComponent(coordinatorType);
                Component source =
                    sourceObject.AddComponent(perceptionType);
                Component receiver =
                    receiverObject.AddComponent(perceptionType);
                receiverObject.transform.position = Vector3.right * 3f;
                coordinatorType.GetMethod("Configure")
                    .Invoke(
                        coordinator,
                        new object[] { 12f, 1f, 3f, 4f });
                coordinatorType.GetMethod("Register")
                    .Invoke(coordinator, new[] { source });
                coordinatorType.GetMethod("Register")
                    .Invoke(coordinator, new[] { receiver });
                coordinatorType.GetMethod("TryBroadcast")
                    .Invoke(
                        coordinator,
                        new object[]
                        {
                            source,
                            Vector3.forward * 6f,
                            1f,
                            10f
                        });

                Assert.That(debugViewType, Is.Not.Null);
                Assert.That(
                    coordinatorObject.GetComponent(debugViewType),
                    Is.Not.Null);
                Assert.That(
                    coordinatorType.GetProperty(
                            "DebugVisualizationActive")
                        .GetValue(coordinator),
                    Is.EqualTo(true));
                Assert.That(
                    coordinatorType.GetProperty("LastDebugRadius")
                        .GetValue(coordinator),
                    Is.EqualTo(12f));
                Assert.That(
                    coordinatorType.GetProperty("DebugRelationCount")
                        .GetValue(coordinator),
                    Is.EqualTo(1));
                Assert.That(
                    coordinatorObject
                        .GetComponentsInChildren<LineRenderer>(true)
                        .Length,
                    Is.EqualTo(0),
                    "警报调试数据不得生成连接线或侦查范围圈。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(receiverObject);
                UnityEngine.Object.DestroyImmediate(sourceObject);
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
            }
        }

        [UnityTest]
        public IEnumerator CitySquadUsesDistinctReachableSearchPoints()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;

            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Type navigationType = RuntimeTypeResolver.GetType(
                "EnemyNavigationController");
            Type coordinatorType = RuntimeTypeResolver.GetType(
                "EnemySquadCoordinator");
            Component[] perceptions = Array.Empty<Component>();
            float spawnDeadline = Time.realtimeSinceStartup + 15f;

            while (Time.realtimeSinceStartup < spawnDeadline)
            {
                perceptions = FindComponents(perceptionType);

                foreach (Component perception in perceptions)
                {
                    perceptionType.GetMethod("SetTarget")
                        .Invoke(perception, new object[] { null });
                }

                if (perceptions.Length >= 3 &&
                    AllUseNavMesh(perceptions, navigationType))
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                perceptions.Length,
                Is.GreaterThanOrEqualTo(3),
                "CityNew 波次应生成可验证协同的三名敌人。");

            foreach (Component perception in perceptions)
            {
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[] { null });
            }

            yield return null;
            Component coordinator =
                FindComponents(coordinatorType)[0];
            coordinatorType.GetMethod("Configure")
                .Invoke(
                    coordinator,
                    new object[] { 50f, 1f, 3f, 5f });

            foreach (Component perception in perceptions)
            {
                coordinatorType.GetMethod("Register")
                    .Invoke(coordinator, new[] { perception });
            }

            Vector3 lastKnown = new Vector3(50f, 0f, 60f);
            float broadcastTime = Time.time;
            Assert.That(
                coordinatorType.GetMethod("TryBroadcast")
                    .Invoke(
                        coordinator,
                        new object[]
                        {
                            perceptions[0],
                            lastKnown,
                            1f,
                            broadcastTime
                        }),
                Is.EqualTo(true));
            Assert.That(
                coordinatorType.GetProperty("LastRecipientCount")
                    .GetValue(coordinator),
                Is.EqualTo(perceptions.Length - 1));
            Assert.That(
                coordinatorType.GetProperty("DebugRelationCount")
                    .GetValue(coordinator),
                Is.EqualTo(perceptions.Length - 1));

            for (int first = 1;
                 first < perceptions.Length;
                 first++)
            {
                Vector3 destination = (Vector3)perceptionType
                    .GetProperty("SquadSearchDestination")
                    .GetValue(perceptions[first]);
                Component navigation =
                    perceptions[first].GetComponent(navigationType);
                NavMeshAgent agent =
                    perceptions[first].GetComponent<NavMeshAgent>();
                var path = new NavMeshPath();

                Assert.That(
                    NavMesh.CalculatePath(
                        perceptions[first].transform.position,
                        destination,
                        agent.areaMask,
                        path),
                    Is.True);
                Assert.That(
                    path.status,
                    Is.EqualTo(NavMeshPathStatus.PathComplete));
                Assert.That(
                    navigationType.GetProperty("UsesNavMesh")
                        .GetValue(navigation),
                    Is.EqualTo(true));

                for (int second = first + 1;
                     second < perceptions.Length;
                     second++)
                {
                    Vector3 otherDestination =
                        (Vector3)perceptionType
                            .GetProperty("SquadSearchDestination")
                            .GetValue(perceptions[second]);
                    Assert.That(
                        Vector3.Distance(
                            destination,
                            otherDestination),
                        Is.GreaterThanOrEqualTo(1.5f),
                        "小队成员必须分散到不同搜索点。");
                }
            }
        }

        private static bool AllUseNavMesh(
            Component[] perceptions,
            Type navigationType)
        {
            foreach (Component perception in perceptions)
            {
                Component navigation =
                    perception.GetComponent(navigationType);

                if (navigation == null ||
                    !(bool)navigationType.GetProperty("UsesNavMesh")
                        .GetValue(navigation))
                {
                    return false;
                }
            }

            return perceptions.Length > 0;
        }

        private static Component[] FindComponents(Type type)
        {
            MonoBehaviour[] behaviours =
                UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            var matches = new System.Collections.Generic.List<Component>();

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != null && type.IsInstanceOfType(behaviour))
                {
                    matches.Add(behaviour);
                }
            }

            return matches.ToArray();
        }

        private static object CreateAlert(
            Type alertType,
            GameObject source,
            Vector3 sourcePosition,
            Vector3 lastKnownPosition,
            float timestamp,
            float confidence,
            float radius,
            int sequence)
        {
            return Activator.CreateInstance(
                alertType,
                new object[]
                {
                    source,
                    sourcePosition,
                    lastKnownPosition,
                    timestamp,
                    confidence,
                    radius,
                    sequence
                });
        }
    }
}
