using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue8AwarenessTests
    {
        [Test]
        public void SustainedVisionRaisesAlertAndLosingSightStartsSearch()
        {
            Type stateMachineType = RuntimeTypeResolver.GetType(
                "EnemyAwarenessStateMachine");
            Assert.That(stateMachineType, Is.Not.Null);

            object stateMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(stateMachine, new object[]
                {
                    1f,
                    0.5f,
                    4f
                });
            MethodInfo observe = stateMachineType.GetMethod("Observe");
            MethodInfo tick = stateMachineType.GetMethod("Tick");
            Vector3 lastSeen = new Vector3(4f, 0f, 2f);

            observe.Invoke(
                stateMachine,
                new object[] { lastSeen, 0.35f });
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Suspicious"));

            observe.Invoke(
                stateMachine,
                new object[] { lastSeen, 0.7f });
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Alert"));
            Assert.That(
                stateMachineType.GetProperty("Awareness")
                    .GetValue(stateMachine),
                Is.EqualTo(1f));

            tick.Invoke(stateMachine, new object[] { 0.2f });
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Alert"),
                "短暂遮挡或近距离转向不应立即丢失 Alert。");

            tick.Invoke(stateMachine, new object[] { 0.8f });
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Search"));
            Assert.That(
                stateMachineType.GetProperty("LastKnownPosition")
                    .GetValue(stateMachine),
                Is.EqualTo(lastSeen));
        }

        [Test]
        public void HearingGunshotStartsInvestigationAtSoundPosition()
        {
            Type stateMachineType = RuntimeTypeResolver.GetType(
                "EnemyAwarenessStateMachine");
            object stateMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(stateMachine, new object[]
                {
                    1f,
                    0.5f,
                    4f
                });
            Vector3 soundPosition = new Vector3(8f, 0f, -3f);

            stateMachineType.GetMethod("Hear")
                .Invoke(stateMachine, new object[]
                {
                    soundPosition,
                    0.65f
                });

            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Suspicious"));
            Assert.That(
                stateMachineType.GetProperty("Awareness")
                    .GetValue(stateMachine),
                Is.EqualTo(0.65f));
            Assert.That(
                stateMachineType.GetProperty("LastKnownPosition")
                    .GetValue(stateMachine),
                Is.EqualTo(soundPosition));
        }

        [Test]
        public void SearchTimeoutStartsOnlyAfterDestinationIsReached()
        {
            Type stateMachineType = RuntimeTypeResolver.GetType(
                "EnemyAwarenessStateMachine");
            object stateMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(stateMachine, new object[]
                {
                    1f,
                    0.5f,
                    4f
                });
            stateMachineType.GetMethod("Observe")
                .Invoke(stateMachine, new object[]
                {
                    Vector3.forward * 6f,
                    1f
                });
            stateMachineType.GetMethod("Tick")
                .Invoke(stateMachine, new object[] { 0.8f });
            MethodInfo advanceSearch =
                stateMachineType.GetMethod("AdvanceSearch");

            advanceSearch.Invoke(
                stateMachine,
                new object[] { 2f, false });
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Search"));
            Assert.That(
                stateMachineType.GetProperty("SearchTimeRemaining")
                    .GetValue(stateMachine),
                Is.EqualTo(4f));

            advanceSearch.Invoke(
                stateMachine,
                new object[] { 4f, true });
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Patrol"));
            Assert.That(
                stateMachineType.GetProperty("Awareness")
                    .GetValue(stateMachine),
                Is.EqualTo(0f));
        }

        [Test]
        public void UnreachableSearchDestinationEventuallyReturnsToPatrol()
        {
            Type stateMachineType = RuntimeTypeResolver.GetType(
                "EnemyAwarenessStateMachine");
            object stateMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(stateMachine, new object[]
                {
                    1f,
                    0.5f,
                    1f
                });
            stateMachineType.GetMethod("Observe")
                .Invoke(stateMachine, new object[]
                {
                    new Vector3(5f, 0f, 5f),
                    1f
                });
            stateMachineType.GetMethod("Tick")
                .Invoke(stateMachine, new object[] { 0.8f });

            stateMachineType.GetMethod("AdvanceSearch")
                .Invoke(stateMachine, new object[]
                {
                    10f,
                    false
                });

            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Patrol"),
                "搜索点不可达时也必须在有限时间内退出 Search。");
        }

        [Test]
        public void PatrolAdvancesAndWrapsConfiguredWaypoints()
        {
            Type navigationType = RuntimeTypeResolver.GetType(
                "EnemyNavigationController");
            GameObject enemy = new GameObject("Patrol Enemy");

            try
            {
                Component navigation =
                    enemy.AddComponent(navigationType);
                Vector3 first = new Vector3(2f, 0f, 0f);
                Vector3 second = new Vector3(4f, 0f, 0f);
                navigationType.GetMethod("ConfigurePatrolPoints")
                    .Invoke(navigation, new object[]
                    {
                        new[] { first, second }
                    });
                MethodInfo tick =
                    navigationType.GetMethod("TickPatrol");
                PropertyInfo destination =
                    navigationType.GetProperty("Destination");

                tick.Invoke(navigation, null);
                Assert.That(
                    destination.GetValue(navigation),
                    Is.EqualTo(first));

                enemy.transform.position = first;
                tick.Invoke(navigation, null);
                Assert.That(
                    destination.GetValue(navigation),
                    Is.EqualTo(second));

                enemy.transform.position = second;
                tick.Invoke(navigation, null);
                Assert.That(
                    destination.GetValue(navigation),
                    Is.EqualTo(first));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void ReachingSoundSourceIgnoresSourceHeight()
        {
            Type navigationType = RuntimeTypeResolver.GetType(
                "EnemyNavigationController");
            GameObject enemy = new GameObject(
                "Sound Investigation Enemy");

            try
            {
                Component navigation =
                    enemy.AddComponent(navigationType);
                enemy.transform.position = Vector3.zero;
                navigationType.GetMethod("SetDestination")
                    .Invoke(navigation, new object[]
                    {
                        new Vector3(0f, 1.6f, 0f)
                    });

                Assert.That(
                    navigationType
                        .GetProperty("HasReachedDestination")
                        .GetValue(navigation),
                    Is.EqualTo(true),
                    "枪口高度不应让已到达声源水平位置的敌人" +
                    "永远停留在 Suspicious。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void VisionHonorsRangeFovAndPhysicalOcclusion()
        {
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            GameObject enemy = new GameObject("Vision Enemy");
            GameObject target = new GameObject("Vision Target");
            GameObject wall = null;

            try
            {
                Component perception =
                    enemy.AddComponent(perceptionType);
                perceptionType.GetMethod("Configure")
                    .Invoke(perception, new object[]
                    {
                        10f,
                        90f,
                        1f,
                        0.5f,
                        4f
                    });
                target.transform.position =
                    new Vector3(7f, -0.15f, 7f);
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[]
                    {
                        target.transform
                    });
                Physics.SyncTransforms();

                Assert.That(
                    perceptionType.GetMethod("CanSeeTarget")
                        .Invoke(perception, null),
                    Is.EqualTo(true));

                wall = GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
                wall.transform.position =
                    new Vector3(3.535f, 0.75f, 3.535f);
                wall.transform.localScale =
                    new Vector3(1f, 2f, 1f);
                Physics.SyncTransforms();

                Assert.That(
                    perceptionType.GetMethod("CanSeeTarget")
                        .Invoke(perception, null),
                    Is.EqualTo(false));
            }
            finally
            {
                if (wall != null)
                {
                    UnityEngine.Object.DestroyImmediate(wall);
                }

                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void VisionIgnoresEnemyOwnHitboxes()
        {
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            GameObject enemy = new GameObject(
                "Enemy With Hitboxes");
            GameObject target = GameObject.CreatePrimitive(
                PrimitiveType.Capsule);
            GameObject ownHitbox = GameObject.CreatePrimitive(
                PrimitiveType.Cube);

            try
            {
                ownHitbox.name = "Body Hitbox";
                ownHitbox.transform.SetParent(
                    enemy.transform,
                    false);
                ownHitbox.transform.localPosition =
                    new Vector3(0f, 0.75f, 0.45f);
                ownHitbox.transform.localScale =
                    new Vector3(0.7f, 0.8f, 0.35f);
                target.transform.position =
                    new Vector3(0f, 0f, 5f);

                Component perception =
                    enemy.AddComponent(perceptionType);
                perceptionType.GetMethod("Configure")
                    .Invoke(perception, new object[]
                    {
                        10f,
                        90f,
                        1f,
                        0.5f,
                        4f
                    });
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[]
                    {
                        target.transform
                    });
                Physics.SyncTransforms();

                Assert.That(
                    perceptionType.GetMethod("CanSeeTarget")
                        .Invoke(perception, null),
                    Is.EqualTo(true),
                    "敌人自身的 Body/Head Hitbox 不应遮挡视线。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ownHitbox);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [UnityTest]
        public IEnumerator HearingThenSeeingPlayerTracksPlayerNotSound()
        {
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Type combatType = RuntimeTypeResolver.GetType(
                "EnemyCombatController");
            Type channelType = RuntimeTypeResolver.GetType(
                "CombatSoundEventChannel");
            Type stimulusType = RuntimeTypeResolver.GetType(
                "SoundStimulus");
            GameObject enemy = new GameObject(
                "Sound Alert Enemy");
            GameObject target = GameObject.CreatePrimitive(
                PrimitiveType.Capsule);

            try
            {
                enemy.transform.rotation = Quaternion.identity;
                target.transform.position =
                    new Vector3(0f, 0f, 8f);
                Component perception =
                    enemy.AddComponent(perceptionType);
                enemy.AddComponent(combatType);
                perceptionType.GetMethod("Configure")
                    .Invoke(perception, new object[]
                    {
                        20f,
                        120f,
                        100f,
                        0.5f,
                        4f
                    });
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[]
                    {
                        target.transform
                    });

                UnityEngine.Object channel = Resources.Load(
                    "CombatSoundEvents",
                    channelType);
                object stimulus = Activator.CreateInstance(
                    stimulusType,
                    new object[]
                    {
                        new Vector3(10f, 1.6f, 0f),
                        30f,
                        1f,
                        null
                    });
                channelType.GetMethod("Publish")
                    .Invoke(channel, new[] { stimulus });
                Physics.SyncTransforms();

                yield return new WaitForSeconds(0.05f);

                Assert.That(
                    perceptionType.GetProperty("State")
                        .GetValue(perception)
                        .ToString(),
                    Is.EqualTo("Alert"));
                Vector3 destination = (Vector3)perceptionType
                    .GetProperty("Destination")
                    .GetValue(perception);
                Assert.That(
                    Vector3.Distance(
                        new Vector3(
                            destination.x,
                            0f,
                            destination.z),
                        target.transform.position),
                    Is.LessThan(0.1f),
                    "看见玩家后应追踪玩家当前位置，而不是旧枪声位置。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [UnityTest]
        public IEnumerator AlertTracksLastSeenPositionBehindOcclusion()
        {
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Type combatType = RuntimeTypeResolver.GetType(
                "EnemyCombatController");
            GameObject enemy = new GameObject(
                "Occlusion Tracking Enemy");
            GameObject target = GameObject.CreatePrimitive(
                PrimitiveType.Capsule);
            GameObject wall = null;

            try
            {
                enemy.transform.rotation = Quaternion.identity;
                target.transform.position =
                    new Vector3(0f, 0f, 8f);
                Component perception =
                    enemy.AddComponent(perceptionType);
                enemy.AddComponent(combatType);
                perceptionType.GetMethod("Configure")
                    .Invoke(perception, new object[]
                    {
                        20f,
                        120f,
                        100f,
                        0.5f,
                        4f
                    });
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[]
                    {
                        target.transform
                    });
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.05f);

                Assert.That(
                    perceptionType.GetProperty("State")
                        .GetValue(perception)
                        .ToString(),
                    Is.EqualTo("Alert"));
                Vector3 lastSeen = (Vector3)perceptionType
                    .GetProperty("LastKnownPosition")
                    .GetValue(perception);

                wall = GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
                wall.transform.position =
                    new Vector3(0f, 1f, 4f);
                wall.transform.localScale =
                    new Vector3(20f, 3f, 1f);
                target.transform.position =
                    new Vector3(4f, 0f, 8f);
                Physics.SyncTransforms();
                yield return null;
                yield return null;

                Vector3 destination = (Vector3)perceptionType
                    .GetProperty("Destination")
                    .GetValue(perception);
                Assert.That(
                    Vector3.Distance(destination, lastSeen),
                    Is.LessThan(0.1f),
                    "玩家进入遮挡后只能追踪最后目击位置，" +
                    "不能穿墙读取玩家实时位置。");
            }
            finally
            {
                if (wall != null)
                {
                    UnityEngine.Object.DestroyImmediate(wall);
                }

                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [UnityTest]
        public IEnumerator SoundInvestigationSearchesThenReturnsToPatrol()
        {
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Type channelType = RuntimeTypeResolver.GetType(
                "CombatSoundEventChannel");
            Type stimulusType = RuntimeTypeResolver.GetType(
                "SoundStimulus");
            GameObject enemy = new GameObject(
                "Completed Sound Investigation Enemy");

            try
            {
                Component perception =
                    enemy.AddComponent(perceptionType);
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[] { null });
                perceptionType.GetMethod("Configure")
                    .Invoke(perception, new object[]
                    {
                        20f,
                        120f,
                        1f,
                        0.5f,
                        0.1f
                    });

                UnityEngine.Object channel = Resources.Load(
                    "CombatSoundEvents",
                    channelType);
                object stimulus = Activator.CreateInstance(
                    stimulusType,
                    new object[]
                    {
                        new Vector3(0f, 1.6f, 0f),
                        30f,
                        1f,
                        null
                    });
                channelType.GetMethod("Publish")
                    .Invoke(channel, new[] { stimulus });

                yield return null;
                Assert.That(
                    perceptionType.GetProperty("State")
                        .GetValue(perception)
                        .ToString(),
                    Is.EqualTo("Search"));

                yield return new WaitForSeconds(0.15f);
                Assert.That(
                    perceptionType.GetProperty("State")
                        .GetValue(perception)
                        .ToString(),
                    Is.EqualTo("Patrol"),
                    "完成声源搜索后应恢复 Patrol。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [UnityTest]
        public IEnumerator SuccessfulShotPublishesGunshotStimulus()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;

            Type channelType = RuntimeTypeResolver.GetType(
                "CombatSoundEventChannel");
            UnityEngine.Object channel = Resources.Load(
                "CombatSoundEvents",
                channelType);
            PropertyInfo publishCount =
                channelType.GetProperty("PublishCount");
            int before = (int)publishCount.GetValue(channel);
            Component weapon = FindFirstComponent("WeaponController");

            Assert.That(weapon, Is.Not.Null);
            bool fired = (bool)weapon.GetType()
                .GetMethod("TryFire")
                .Invoke(weapon, null);

            Assert.That(fired, Is.True);
            Assert.That(
                publishCount.GetValue(channel),
                Is.EqualTo(before + 1));
            object stimulus = channelType
                .GetProperty("LastStimulus")
                .GetValue(channel);
            Assert.That(
                (float)stimulus.GetType()
                    .GetProperty("Radius")
                    .GetValue(stimulus),
                Is.GreaterThan(1f));
            Assert.That(
                (float)stimulus.GetType()
                    .GetProperty("Intensity")
                    .GetValue(stimulus),
                Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator CityEnemyBuildsNavMeshAndMovesOnPatrol()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;

            Component perception = null;
            Component navigation = null;
            float navigationDeadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < navigationDeadline)
            {
                if (TryFindNavigatingEnemy(out perception, out navigation))
                {
                    break;
                }
                yield return null;
            }
            Assert.That(perception, Is.Not.Null);
            Assert.That(navigation, Is.Not.Null);
            perception.GetType().GetMethod("SetTarget")
                .Invoke(perception, new object[] { null });
            perception.GetType().GetMethod("Configure")
                .Invoke(perception, new object[]
                {
                    22f,
                    110f,
                    0.75f,
                    10f,
                    5f
                });
            PropertyInfo usesNavMesh =
                navigation.GetType().GetProperty("UsesNavMesh");

            Assert.That(
                usesNavMesh.GetValue(navigation),
                Is.EqualTo(true));
            Assert.That(
                perception.GetType()
                    .GetProperty("PatrolPointCount")
                    .GetValue(perception),
                Is.GreaterThanOrEqualTo(2));
            Vector3 start = perception.transform.position;

            yield return new WaitForSeconds(1.2f);

            NavMeshAgent agent =
                perception.GetComponent<NavMeshAgent>();
            Vector3 destination = (Vector3)navigation.GetType()
                .GetProperty("Destination")
                .GetValue(navigation);
            Assert.That(
                Vector3.Distance(
                    start,
                    perception.transform.position),
                Is.GreaterThan(0.15f),
                $"start={start}, now={perception.transform.position}, " +
                $"destination={destination}, hasPath={agent.hasPath}, " +
                $"pathStatus={agent.pathStatus}, " +
                $"remaining={agent.remainingDistance}");
        }

        private static Component FindFirstComponent(string typeName)
        {
            MonoBehaviour[] behaviours =
                UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != null &&
                    behaviour.GetType().Name == typeName)
                {
                    return behaviour;
                }
            }

            return null;
        }

        private static bool TryFindNavigatingEnemy(
            out Component perception,
            out Component navigation)
        {
            MonoBehaviour[] behaviours =
                UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null ||
                    behaviour.GetType().Name != "EnemyPerceptionController")
                {
                    continue;
                }
                Component candidate =
                    behaviour.GetComponent("EnemyNavigationController");
                if (candidate != null &&
                    (bool)candidate.GetType().GetProperty("UsesNavMesh")
                        .GetValue(candidate))
                {
                    perception = behaviour;
                    navigation = candidate;
                    return true;
                }
            }
            perception = null;
            navigation = null;
            return false;
        }
    }
}
