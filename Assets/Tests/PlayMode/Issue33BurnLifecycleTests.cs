using System.Collections;
using System.Reflection;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue33BurnLifecycleTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [SetUp]
        public void IgnoreHeadlessPresentationWarnings()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void RestoreLogState()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void BurnTicksKeepSourcesAssignKillAndClearPresentation()
        {
            GameObject enemy = new("Burn Enemy");
            GameObject sourceA = new("Source A");
            GameObject sourceB = new("Source B");

            try
            {
                Health health = enemy.AddComponent<Health>();
                health.Initialize(10f, 0f);
                EnemyBurnEffectController burn =
                    enemy.AddComponent<EnemyBurnEffectController>();
                burn.ConfigureBurn(
                    6f,
                    1f,
                    3f,
                    2,
                    GameplayEffectStackRefreshPolicy.RefreshAllDurations);
                DamageInfo killingDamage = default;
                health.Killed += damage => killingDamage = damage;

                Assert.That(burn.ApplyBurn(sourceA), Is.True);
                Assert.That(burn.ApplyBurn(sourceB), Is.True);
                Assert.That(burn.StackCount, Is.EqualTo(2));
                Assert.That(burn.PresentationVisible, Is.True);
                Assert.That(burn.StackText, Does.Contain("×2"));

                burn.Advance(1f);

                Assert.That(health.IsDead, Is.True);
                Assert.That(killingDamage.Source, Is.SameAs(sourceB));
                Assert.That(killingDamage.Type,
                    Is.EqualTo(DamageType.StatusEffect));
                Assert.That(health.LastAppliedDamage.Source,
                    Is.SameAs(sourceB));
                Assert.That(burn.IsBurning, Is.False);
                Assert.That(burn.PresentationVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(sourceA);
                Object.DestroyImmediate(sourceB);
            }
        }

        [Test]
        public void PoolPreparationAndSpawnResetRemoveBurnState()
        {
            GameObject enemyObject = new("Pooled Burn Enemy");
            GameObject source = new("Burn Source");

            try
            {
                enemyObject.AddComponent<BoxCollider>();
                EnemyController enemy =
                    enemyObject.AddComponent<EnemyController>();
                EnemyBurnEffectController burn =
                    enemyObject.GetComponent<EnemyBurnEffectController>();
                Assert.That(burn, Is.Not.Null);
                Assert.That(burn.ApplyBurn(source), Is.True);
                Assert.That(burn.IsBurning, Is.True);

                enemy.PrepareForPool();
                Assert.That(burn.IsBurning, Is.False);
                Assert.That(burn.PresentationVisible, Is.False);

                enemy.ResetForSpawn(null);
                Assert.That(burn.IsBurning, Is.False);
                Assert.That(burn.HealthBarVisible, Is.True,
                    "A rented living enemy should show a fresh health bar.");
                Assert.That(burn.StatusVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void OverheadHealthBarTracksVitalsAndSeparatesStatusRow()
        {
            GameObject enemy = new("Overhead Info Enemy");
            GameObject source = new("Burn Source");

            try
            {
                Health health = enemy.AddComponent<Health>();
                health.Initialize(100f, 0f);
                EnemyBurnEffectController burn =
                    enemy.AddComponent<EnemyBurnEffectController>();
                burn.ConfigureBurn(
                    4f,
                    5f,
                    1f,
                    3,
                    GameplayEffectStackRefreshPolicy.RefreshAllDurations);

                Assert.That(burn.HealthBarVisible, Is.True);
                Assert.That(burn.StatusVisible, Is.False);
                Assert.That(burn.PresentationWorldScale,
                    Is.EqualTo(0.0065f).Within(0.0001f));
                health.ApplyDamage(new DamageInfo(
                    40f,
                    Vector3.zero,
                    Vector3.forward,
                    source));
                Assert.That(burn.HealthFillNormalized,
                    Is.EqualTo(0.6f).Within(0.001f));

                Assert.That(burn.ApplyBurn(source), Is.True);
                Assert.That(burn.StatusVisible, Is.True);
                Assert.That(burn.StatusText, Does.Contain("BURN ×1"));
                burn.Advance(1f);

                Assert.That(burn.IsBurning, Is.False);
                Assert.That(burn.HealthBarVisible, Is.True,
                    "Expiring a status must not hide the living enemy health bar.");
                Assert.That(burn.StatusVisible, Is.False);
                Assert.That(burn.HealthFillNormalized,
                    Is.EqualTo(0.6f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void PerceptionRuntimeNavigationOverlayIsRemoved()
        {
            MethodInfo overlay = typeof(EnemyPerceptionController).GetMethod(
                "OnGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo legacyToggle = typeof(EnemyPerceptionController).GetField(
                "showDebugOverlay",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(overlay, Is.Null,
                "Runtime AI/path text should not compete with overhead info.");
            Assert.That(legacyToggle, Is.Null);
        }

        [UnityTest]
        public IEnumerator WeaponHitAppliesFirstBurnStack()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            WeaponController weapon =
                Object.FindFirstObjectByType<WeaponController>();
            Assert.That(weapon, Is.Not.Null);
            GameObject target = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            target.name = "Issue33 Weapon Burn Target";
            target.transform.position = new Vector3(1000f, 1000f, 1000f);
            Health health = target.AddComponent<Health>();
            health.Initialize(1000f, 0f);
            EnemyBurnEffectController burn =
                target.AddComponent<EnemyBurnEffectController>();
            Physics.SyncTransforms();
            var ray = new Ray(
                target.transform.position - Vector3.forward * 5f,
                Vector3.forward);
            Assert.That(Physics.Raycast(ray, out RaycastHit hit, 10f), Is.True);
            MethodInfo applyHit = typeof(WeaponController).GetMethod(
                "ApplyHit",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                applyHit.Invoke(weapon, new object[] { hit, Vector3.forward });
                Assert.That(burn.StackCount, Is.EqualTo(1));
                Assert.That(burn.PresentationVisible, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
