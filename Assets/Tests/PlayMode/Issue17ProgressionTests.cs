using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue17ProgressionTests
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
        public void LargeGrantCrossesMultipleLevelsAndKeepsOverflow()
        {
            (Type type, object state) = CreateState(100, 150, 225);

            int gained = (int)type.GetMethod("GrantExperience")
                .Invoke(state, new object[] { 300 });

            Assert.That(gained, Is.EqualTo(2));
            Assert.That(GetInt(type, state, "Level"), Is.EqualTo(3));
            Assert.That(
                GetInt(type, state, "CurrentExperience"),
                Is.EqualTo(50));
            Assert.That(
                GetInt(type, state, "TotalExperience"),
                Is.EqualTo(300));
            Assert.That(
                GetInt(type, state, "LevelUpCount"),
                Is.EqualTo(2));
        }

        [Test]
        public void FixedSequenceProducesDeterministicProgression()
        {
            (Type type, object first) = CreateState(100, 150, 225);
            (Type _, object second) = CreateState(100, 150, 225);
            int[] sequence = { 35, 65, 25, 200, 50 };

            foreach (int amount in sequence)
            {
                type.GetMethod("GrantExperience")
                    .Invoke(first, new object[] { amount });
                type.GetMethod("GrantExperience")
                    .Invoke(second, new object[] { amount });
            }

            Assert.That(
                GetInt(type, first, "Level"),
                Is.EqualTo(GetInt(type, second, "Level")));
            Assert.That(
                GetInt(type, first, "CurrentExperience"),
                Is.EqualTo(GetInt(
                    type,
                    second,
                    "CurrentExperience")));
            Assert.That(GetInt(type, first, "Level"), Is.EqualTo(3));
            Assert.That(
                GetInt(type, first, "CurrentExperience"),
                Is.EqualTo(125));
            Assert.That(
                GetInt(type, first, "LevelUpCount"),
                Is.EqualTo(2));
        }

        [Test]
        public void NonPositiveExperienceDoesNotChangeState()
        {
            (Type type, object state) = CreateState(100, 150);

            Assert.That(
                type.GetMethod("GrantExperience")
                    .Invoke(state, new object[] { 0 }),
                Is.EqualTo(0));
            Assert.That(
                type.GetMethod("GrantExperience")
                    .Invoke(state, new object[] { -50 }),
                Is.EqualTo(0));
            Assert.That(GetInt(type, state, "Level"), Is.EqualTo(1));
            Assert.That(
                GetInt(type, state, "TotalExperience"),
                Is.EqualTo(0));
        }

        [Test]
        public void HealthKeepsLastAcceptedDamageContext()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            Type kindType = Type.GetType("DamageType, Assembly-CSharp");
            var target = new GameObject("Target");
            var source = new GameObject("Player Source");

            try
            {
                Component health = target.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[] { typeof(float) })
                    .Invoke(health, new object[] { 20f });
                object damage = Activator.CreateInstance(
                    damageType,
                    30f,
                    Vector3.one,
                    Vector3.forward,
                    source,
                    Enum.Parse(kindType, "Hitscan"));
                healthType.GetMethod("ApplyDamage")
                    .Invoke(health, new[] { damage });
                object last = healthType.GetProperty("LastAppliedDamage")
                    .GetValue(health);

                Assert.That(
                    healthType.GetProperty("IsDead").GetValue(health),
                    Is.EqualTo(true));
                Assert.That(
                    damageType.GetProperty("Source").GetValue(last),
                    Is.SameAs(source));
                Assert.That(
                    damageType.GetProperty("Type").GetValue(last).ToString(),
                    Is.EqualTo("Hitscan"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void PlayerOwnershipAndSpawnIdentityGateRewards()
        {
            Type progressionType = Type.GetType(
                "PlayerRunProgression, Assembly-CSharp");
            Type enemyType = Type.GetType(
                "EnemyController, Assembly-CSharp");
            Type deathType = Type.GetType(
                "EnemyDeathEvent, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            var player = new GameObject("Player");
            var childSource = new GameObject("Weapon Source");
            var outsider = new GameObject("Environment Source");
            var enemyObject = new GameObject("Enemy");
            player.SetActive(false);
            enemyObject.SetActive(false);

            try
            {
                childSource.transform.SetParent(player.transform);
                Component progression = player.AddComponent(progressionType);
                progressionType.GetMethod("ConfigureThresholds")
                    .Invoke(
                        progression,
                        new object[] { new[] { 100, 150 } });
                Component enemy = enemyObject.AddComponent(enemyType);
                object playerDamage = Activator.CreateInstance(
                    damageType,
                    100f,
                    Vector3.zero,
                    Vector3.forward,
                    childSource);
                object outsiderDamage = Activator.CreateInstance(
                    damageType,
                    100f,
                    Vector3.zero,
                    Vector3.forward,
                    outsider);
                object ownedDeath = Activator.CreateInstance(
                    deathType,
                    enemy,
                    7,
                    1,
                    playerDamage,
                    60);
                object otherDeath = Activator.CreateInstance(
                    deathType,
                    enemy,
                    8,
                    1,
                    outsiderDamage,
                    60);
                MethodInfo apply = progressionType.GetMethod(
                    "TryApplyEnemyDeath");

                Assert.That(
                    apply.Invoke(progression, new[] { ownedDeath }),
                    Is.EqualTo(true));
                Assert.That(
                    apply.Invoke(progression, new[] { ownedDeath }),
                    Is.EqualTo(false),
                    "同一出生实例只能奖励一次。");
                Assert.That(
                    apply.Invoke(progression, new[] { otherDeath }),
                    Is.EqualTo(false),
                    "非玩家归属伤害不得奖励经验。");
                object snapshot = progressionType
                    .GetProperty("CurrentProgress")
                    .GetValue(progression);
                Assert.That(
                    snapshot.GetType().GetProperty("CurrentExperience")
                        .GetValue(snapshot),
                    Is.EqualTo(60));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemyObject);
                UnityEngine.Object.DestroyImmediate(outsider);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [UnityTest]
        public IEnumerator PlayerKillPublishesContextAndRefreshesHud()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = Type.GetType(
                "WaveDirector, Assembly-CSharp");
            Type progressionType = Type.GetType(
                "PlayerRunProgression, Assembly-CSharp");
            Type hudType = Type.GetType(
                "UnifiedGameHud, Assembly-CSharp");
            Component director = null;
            Component progression = null;
            Component hud = null;
            GameObject player = null;
            float deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                director = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                hud = (Component)UnityEngine.Object
                    .FindAnyObjectByType(hudType);
                player = GameObject.FindGameObjectWithTag("Player");
                progression = player != null
                    ? player.GetComponent(progressionType)
                    : null;

                if (director != null && progression != null && hud != null &&
                    GetActiveEnemyCount(directorType, director) > 0)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(director, Is.Not.Null);
            Assert.That(progression, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            Component enemy = GetFirstActiveEnemy(directorType, director);
            Assert.That(enemy, Is.Not.Null);
            Component health = enemy.GetComponent(
                Type.GetType("Health, Assembly-CSharp"));
            Type healthType = health.GetType();
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            Type kindType = Type.GetType("DamageType, Assembly-CSharp");
            float maxHealth = (float)healthType.GetProperty("MaxHealth")
                .GetValue(health);
            object lethal = Activator.CreateInstance(
                damageType,
                maxHealth + 100f,
                enemy.transform.position,
                Vector3.forward,
                player,
                Enum.Parse(kindType, "Hitscan"));
            int hudRefreshBefore = (int)hudType
                .GetProperty("ProgressionRefreshCount")
                .GetValue(hud);
            healthType.GetMethod("ApplyDamage")
                .Invoke(health, new[] { lethal });
            yield return null;

            Assert.That(
                directorType.GetProperty("EnemyDeathEventCount")
                    .GetValue(director),
                Is.EqualTo(1));
            object death = directorType.GetProperty("LastEnemyDeath")
                .GetValue(director);
            Assert.That(
                death.GetType().GetProperty("DamageSource").GetValue(death),
                Is.SameAs(player));
            Assert.That(
                death.GetType().GetProperty("DamageType").GetValue(death)
                    .ToString(),
                Is.EqualTo("Hitscan"));
            Assert.That(
                death.GetType().GetProperty("WaveNumber").GetValue(death),
                Is.EqualTo(1));
            Assert.That(
                progressionType.GetProperty("RewardedKillCount")
                    .GetValue(progression),
                Is.EqualTo(1));
            Assert.That(
                hudType.GetProperty("ProgressionRefreshCount")
                    .GetValue(hud),
                Is.EqualTo(hudRefreshBefore + 1));
            Assert.That(
                hudType.GetProperty("LevelText").GetValue(hud),
                Is.EqualTo("LV 01"));
            Assert.That(
                hudType.GetProperty("ExperienceText").GetValue(hud),
                Is.EqualTo("40 / 100 XP"));
            Assert.That(
                (float)hudType.GetProperty("ExperienceNormalized")
                    .GetValue(hud),
                Is.EqualTo(0.4f).Within(0.001f));

            healthType.GetMethod("ApplyDamage")
                .Invoke(health, new[] { lethal });
            yield return null;
            Assert.That(
                directorType.GetProperty("EnemyDeathEventCount")
                    .GetValue(director),
                Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RestartCreatesFreshProgressionAndSubscriptions()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = Type.GetType(
                "WaveDirector, Assembly-CSharp");
            Type progressionType = Type.GetType(
                "PlayerRunProgression, Assembly-CSharp");
            Type missionType = Type.GetType(
                "CityNewMissionController, Assembly-CSharp");
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            Component oldProgression = null;
            Component mission = null;
            Component director = null;
            GameObject player = null;
            float deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                director = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                player = GameObject.FindGameObjectWithTag("Player");
                oldProgression = player != null
                    ? player.GetComponent(progressionType)
                    : null;
                mission = player != null
                    ? player.GetComponent(missionType)
                    : null;

                if (director != null && oldProgression != null &&
                    mission != null &&
                    GetActiveEnemyCount(directorType, director) > 0)
                {
                    break;
                }

                yield return null;
            }

            Component enemy = GetFirstActiveEnemy(directorType, director);
            Component enemyHealth = enemy.GetComponent(healthType);
            float maxHealth = (float)healthType.GetProperty("MaxHealth")
                .GetValue(enemyHealth);
            object lethal = Activator.CreateInstance(
                damageType,
                maxHealth + 100f,
                enemy.transform.position,
                Vector3.forward,
                player);
            healthType.GetMethod("ApplyDamage")
                .Invoke(enemyHealth, new[] { lethal });
            yield return null;
            Assert.That(
                progressionType.GetProperty("RewardedKillCount")
                    .GetValue(oldProgression),
                Is.EqualTo(1));

            var oldProgressionId = oldProgression.GetEntityId();
            Component playerHealth = player.GetComponent(healthType);
            float playerMaxHealth = (float)healthType
                .GetProperty("MaxHealth")
                .GetValue(playerHealth);
            object playerLethal = Activator.CreateInstance(
                damageType,
                playerMaxHealth + 100f,
                player.transform.position,
                Vector3.back,
                enemy.gameObject);
            healthType.GetMethod("ApplyDamage")
                .Invoke(playerHealth, new[] { playerLethal });
            yield return null;
            Assert.That(
                missionType.GetProperty("State").GetValue(mission)
                    .ToString(),
                Is.EqualTo("Defeat"));
            Assert.That(
                missionType.GetMethod("RestartLevel")
                    .Invoke(mission, null),
                Is.EqualTo(true));
            Component newProgression = null;
            deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                GameObject nextPlayer =
                    GameObject.FindGameObjectWithTag("Player");
                newProgression = nextPlayer != null
                    ? nextPlayer.GetComponent(progressionType)
                    : null;

                if (newProgression != null &&
                    newProgression.GetEntityId() != oldProgressionId)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(newProgression, Is.Not.Null);
            Assert.That(
                newProgression.GetEntityId(),
                Is.Not.EqualTo(oldProgressionId));
            object fresh = progressionType.GetProperty("CurrentProgress")
                .GetValue(newProgression);
            Assert.That(
                fresh.GetType().GetProperty("Level").GetValue(fresh),
                Is.EqualTo(1));
            Assert.That(
                fresh.GetType().GetProperty("TotalExperience")
                    .GetValue(fresh),
                Is.EqualTo(0));
            Assert.That(
                progressionType.GetProperty("RewardedKillCount")
                    .GetValue(newProgression),
                Is.EqualTo(0));
        }

        private static (Type type, object state) CreateState(
            params int[] thresholds)
        {
            Type type = Type.GetType(
                "RunExperienceState, Assembly-CSharp");
            object state = Activator.CreateInstance(
                type,
                new object[] { thresholds });
            return (type, state);
        }

        private static int GetInt(Type type, object target, string property)
        {
            return (int)type.GetProperty(property).GetValue(target);
        }

        private static int GetActiveEnemyCount(
            Type directorType,
            Component director)
        {
            object active = directorType.GetProperty("ActiveEnemies")
                .GetValue(director);
            return (int)active.GetType().GetProperty("Count")
                .GetValue(active);
        }

        private static Component GetFirstActiveEnemy(
            Type directorType,
            Component director)
        {
            IEnumerable active = (IEnumerable)directorType
                .GetProperty("ActiveEnemies")
                .GetValue(director);

            foreach (object pair in active)
            {
                object handle = pair.GetType().GetProperty("Value")
                    .GetValue(pair);
                return (Component)handle.GetType()
                    .GetProperty("Controller")
                    .GetValue(handle);
            }

            return null;
        }
    }
}
