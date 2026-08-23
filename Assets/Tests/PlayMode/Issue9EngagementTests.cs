using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue9EngagementTests
    {
        [TearDown]
        public void RestoreTimeScale()
        {
            Time.timeScale = 1f;
        }

        [Test]
        public void AttackRequiresAimTimeAndRespectsCooldown()
        {
            Type attackType = RuntimeTypeResolver.GetType(
                "EnemyAttackStateMachine");
            Assert.That(attackType, Is.Not.Null);
            object attack = Activator.CreateInstance(attackType);
            attackType.GetMethod("Configure")
                .Invoke(attack, new object[]
                {
                    2f,
                    0.5f,
                    1f
                });

            AssertDecision(attackType, attack, 1.5f, true, 0.3f, "Aim");
            AssertDecision(attackType, attack, 1.5f, true, 0.19f, "Aim");
            AssertDecision(attackType, attack, 1.5f, true, 0.01f, "Attack");
            AssertDecision(
                attackType,
                attack,
                1.5f,
                true,
                0.9f,
                "Cooldown");
            AssertDecision(
                attackType,
                attack,
                1.5f,
                true,
                0.1f,
                "Aim");
            AssertDecision(attackType, attack, 1.5f, true, 0.5f, "Attack");
        }

        [Test]
        public void EnemyCombatPresentationUsesExistingAttackAssets()
        {
            Type profileType = RuntimeTypeResolver.GetType(
                "EnemyCombatPresentationProfile");
            UnityEngine.Object profile = Resources.Load(
                "EnemyCombatPresentation",
                profileType);

            Assert.That(profile, Is.Not.Null);
            Assert.That(
                profileType.GetField("AttackClip").GetValue(profile),
                Is.Not.Null);
            Assert.That(
                profileType.GetField("AttackImpact").GetValue(profile),
                Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator AlertEnemyDamagesPlayerOnceBeforeCooldownEnds()
        {
            Type enemyType = RuntimeTypeResolver.GetType(
                "EnemyController");
            Type perceptionType = RuntimeTypeResolver.GetType(
                "EnemyPerceptionController");
            Type combatType = RuntimeTypeResolver.GetType(
                "EnemyCombatController");
            Type healthType = RuntimeTypeResolver.GetType(
                "Health");
            GameObject enemy = new GameObject("Combat Enemy");
            GameObject player = new GameObject("Combat Player");
            player.transform.position = Vector3.forward * 1.5f;

            try
            {
                Component health = player.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[] { typeof(float) })
                    .Invoke(health, new object[] { 100f });
                enemy.AddComponent(enemyType);
                Component perception =
                    enemy.GetComponent(perceptionType);
                perceptionType.GetMethod("SetTarget")
                    .Invoke(perception, new object[]
                    {
                        player.transform
                    });
                perceptionType.GetMethod("Configure")
                    .Invoke(perception, new object[]
                    {
                        10f,
                        180f,
                        100f,
                        0.5f,
                        4f
                    });

                yield return new WaitForSeconds(0.75f);

                Component combat = enemy.GetComponent(combatType);
                float healthAfterAttack = (float)healthType
                    .GetProperty("CurrentHealth")
                    .GetValue(health);
                Assert.That(healthAfterAttack, Is.LessThan(100f));
                Assert.That(
                    combatType.GetProperty("SuccessfulAttackCount")
                        .GetValue(combat),
                    Is.EqualTo(1));

                yield return new WaitForSeconds(0.4f);

                Assert.That(
                    healthType.GetProperty("CurrentHealth")
                        .GetValue(health),
                    Is.EqualTo(healthAfterAttack));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [UnityTest]
        public IEnumerator PlayerDeathFreezesGameplayAndShowsFailure()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;

            Component player =
                FindFirstComponent("PlayerController");
            Component combat =
                player.GetComponent("PlayerCombatController");
            Component health = player.GetComponent("Health");
            Component failure =
                player.GetComponent("PlayerFailureFlowController");

            Assert.That(failure, Is.Not.Null);
            Type damageInfoType = RuntimeTypeResolver.GetType(
                "DamageInfo");
            object lethalDamage = Activator.CreateInstance(
                damageInfoType,
                new object[]
                {
                    999f,
                    player.transform.position,
                    Vector3.forward,
                    null
                });
            health.GetType().GetMethod("ApplyDamage")
                .Invoke(health, new[] { lethalDamage });
            yield return null;

            Assert.That(
                failure.GetType().GetProperty("IsFailed")
                    .GetValue(failure),
                Is.EqualTo(true));
            Assert.That(
                player.GetType()
                    .GetProperty("GameplayInputEnabled")
                    .GetValue(player),
                Is.EqualTo(false));
            Assert.That(
                combat.GetType()
                    .GetProperty("GameplayInputEnabled")
                    .GetValue(combat),
                Is.EqualTo(false));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator PlayerDamageReportsIncomingDirection()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;

            Component feedback =
                FindFirstComponent("PlayerCombatFeedbackController");
            Component health = feedback.GetComponent("Health");
            GameObject source = new GameObject("Left Damage Source");
            source.transform.position =
                feedback.transform.position -
                feedback.transform.right * 3f;

            try
            {
                Type damageInfoType = RuntimeTypeResolver.GetType(
                    "DamageInfo");
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        10f,
                        feedback.transform.position,
                        feedback.transform.right,
                        source
                    });
                health.GetType().GetMethod("ApplyDamage")
                    .Invoke(health, new[] { damage });
                yield return null;

                Assert.That(
                    feedback.GetType()
                        .GetProperty("LastDamageSide")
                        .GetValue(feedback)
                        .ToString(),
                    Is.EqualTo("Left"));
                Assert.That(
                    feedback.GetType()
                        .GetProperty("DamageFlashAlpha")
                        .GetValue(feedback),
                    Is.GreaterThan(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [UnityTest]
        public IEnumerator RestartRestoresPlayerEnemyAndWeaponState()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;

            Component player =
                FindFirstComponent("PlayerController");
            Component health = player.GetComponent("Health");
            Component failure =
                player.GetComponent("PlayerFailureFlowController");
            Type damageInfoType = RuntimeTypeResolver.GetType(
                "DamageInfo");
            object lethalDamage = Activator.CreateInstance(
                damageInfoType,
                new object[]
                {
                    999f,
                    player.transform.position,
                    Vector3.forward,
                    null
                });
            health.GetType().GetMethod("ApplyDamage")
                .Invoke(health, new[] { lethalDamage });
            yield return null;

            Assert.That(
                failure.GetType().GetMethod("RestartLevel")
                    .Invoke(failure, null),
                Is.EqualTo(true));
            yield return null;
            yield return null;

            Component restoredPlayer =
                FindFirstComponent("PlayerController");
            Component restoredHealth =
                restoredPlayer.GetComponent("Health");
            Component restoredFailure =
                restoredPlayer.GetComponent(
                    "PlayerFailureFlowController");
            Component weapon =
                FindFirstComponent("WeaponController");

            Assert.That(restoredPlayer, Is.Not.SameAs(player));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(
                restoredHealth.GetType()
                    .GetProperty("CurrentHealth")
                    .GetValue(restoredHealth),
                Is.EqualTo(100f));
            Assert.That(
                restoredHealth.GetType()
                    .GetProperty("CurrentArmor")
                    .GetValue(restoredHealth),
                Is.EqualTo(100f));
            Assert.That(
                restoredFailure.GetType()
                    .GetProperty("IsFailed")
                    .GetValue(restoredFailure),
                Is.EqualTo(false));
            Assert.That(
                weapon.GetType().GetProperty("CurrentAmmo")
                    .GetValue(weapon),
                Is.EqualTo(
                    weapon.GetType()
                        .GetProperty("MagazineCapacity")
                        .GetValue(weapon)));
            Assert.That(
                FindFirstComponent("EnemyController"),
                Is.Not.Null);
        }

        private static void AssertDecision(
            Type attackType,
            object attack,
            float distance,
            bool hasLineOfSight,
            float deltaTime,
            string expected)
        {
            object decision = attackType.GetMethod("Evaluate")
                .Invoke(attack, new object[]
                {
                    distance,
                    hasLineOfSight,
                    deltaTime
                });
            Assert.That(decision.ToString(), Is.EqualTo(expected));
        }

        private static Component FindFirstComponent(string typeName)
        {
            MonoBehaviour[] behaviours =
                UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include);

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
    }
}
