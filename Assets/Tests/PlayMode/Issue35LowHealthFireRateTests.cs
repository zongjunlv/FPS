using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue35LowHealthFireRateTests
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

        [UnityTest]
        public IEnumerator DamageHealingAndRemovalKeepCadenceAnimationAndHudSynced()
        {
            yield return LoadScene();
            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            Health health = root.PlayerHealth;
            PlayerRuntimeCombatStats stats = root.RuntimeStats;
            PlayerLowHealthFireRateEffectController effect =
                root.LowHealthFireRateEffect;
            PlayerCombatController combat =
                root.GetComponent<PlayerCombatController>();
            WeaponController weapon = combat.EquippedWeapon;
            UnifiedGameHud hud = Object.FindFirstObjectByType<UnifiedGameHud>();
            float baseInterval = weapon.FireInterval;

            Assert.That(effect.IsEffectInstalled, Is.True);
            Assert.That(effect.IsConditionActive, Is.False);
            Assert.That(stats.FireRateMultiplier, Is.EqualTo(1f));

            health.ApplyDamage(new DamageInfo(
                170f,
                Vector3.zero,
                Vector3.forward,
                root.gameObject,
                DamageType.Environment));

            Assert.That(health.CurrentHealth, Is.EqualTo(30f));
            Assert.That(effect.IsConditionActive, Is.True);
            Assert.That(effect.ActivationCount, Is.EqualTo(1));
            Assert.That(stats.ActiveGameplayEffects, Has.Count.EqualTo(1));
            Assert.That(stats.FireRateMultiplier,
                Is.EqualTo(1.35f).Within(0.0001f));
            Assert.That(weapon.FireInterval,
                Is.EqualTo(baseInterval / 1.35f).Within(0.0001f));
            Assert.That(weapon.FireAnimationSpeed,
                Is.EqualTo(1.35f).Within(0.0001f));
            Assert.That(weapon.WeaponAnimatorPlaybackSpeed,
                Is.EqualTo(1.35f).Within(0.0001f));
            Assert.That(hud.FireModeText, Does.Contain("135%"));
            Assert.That(hud.WeaponStatusText, Does.Contain("射速 +35%"));

            health.ApplyDamage(new DamageInfo(
                1f,
                Vector3.zero,
                Vector3.forward,
                root.gameObject));
            Assert.That(effect.ActivationCount, Is.EqualTo(1),
                "Repeated vitals events below the threshold must not stack.");
            Assert.That(stats.ActiveGameplayEffects, Has.Count.EqualTo(1));

            Assert.That(health.RestoreHealth(6f), Is.EqualTo(6f));
            Assert.That(health.CurrentHealth, Is.EqualTo(35f));
            Assert.That(effect.IsConditionActive, Is.False);
            Assert.That(stats.FireRateMultiplier, Is.EqualTo(1f));
            Assert.That(weapon.FireInterval,
                Is.EqualTo(baseInterval).Within(0.0001f));
            Assert.That(hud.FireModeText, Does.Contain("100%"));
            Assert.That(hud.WeaponStatusText, Is.Empty);

            health.ApplyDamage(new DamageInfo(
                1f,
                Vector3.zero,
                Vector3.forward,
                root.gameObject));
            Assert.That(effect.ActivationCount, Is.EqualTo(2));
            health.Initialize(100f, 100f);
            Assert.That(effect.IsConditionActive, Is.False,
                "Run reinitialization must remove the conditional instance.");
            Assert.That(stats.FireRateMultiplier, Is.EqualTo(1f));

            Assert.That(effect.RemoveEffect(), Is.True);
            health.ApplyDamage(new DamageInfo(
                170f,
                Vector3.zero,
                Vector3.forward,
                root.gameObject));
            Assert.That(effect.IsConditionActive, Is.False);
            Assert.That(stats.FireRateMultiplier, Is.EqualTo(1f));
            Assert.That(effect.ApplyEffect(), Is.True);
            Assert.That(effect.IsConditionActive, Is.True);
            Assert.That(effect.RemoveEffect(), Is.True);
            Assert.That(stats.FireRateMultiplier, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator SceneReloadStartsHealthyWithoutStaleConditionalEffect()
        {
            yield return LoadScene();
            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            root.PlayerHealth.ApplyDamage(new DamageInfo(
                170f,
                Vector3.zero,
                Vector3.forward,
                root.gameObject));
            Assert.That(root.LowHealthFireRateEffect.IsConditionActive, Is.True);

            yield return LoadScene();
            root = Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            Assert.That(root.PlayerHealth.CurrentHealth,
                Is.EqualTo(root.PlayerHealth.MaxHealth));
            Assert.That(root.LowHealthFireRateEffect.IsConditionActive, Is.False);
            Assert.That(root.RuntimeStats.FireRateMultiplier, Is.EqualTo(1f));
        }

        private static IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.LowHealthFireRateEffect, Is.Not.Null);
            yield return new WaitUntil(() => root.HudBootstrap.IsInitialized);
            Assert.That(root.HudBootstrap.Hud, Is.Not.Null);
        }
    }
}
