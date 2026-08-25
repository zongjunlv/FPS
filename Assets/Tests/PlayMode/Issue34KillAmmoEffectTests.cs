using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue34KillAmmoEffectTests
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
        public IEnumerator ValidKillsRefillOnceClampAndStopAfterRemoval()
        {
            yield return LoadPlayer();
            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            PlayerCombatController combat = root.GetComponent<PlayerCombatController>();
            PlayerCombatEventRouter router = root.CombatEvents;
            PlayerKillAmmoEffectController effect = root.KillAmmoEffect;
            WeaponController weapon = combat.EquippedWeapon;
            GameObject enemyObject = new("Issue34 Enemy");
            GameObject foreignSource = new("Foreign Source");

            try
            {
                EnemyController enemy = enemyObject.AddComponent<EnemyController>();
                ConsumeRounds(weapon, 7);
                int before = weapon.CurrentAmmo;
                int ammoEvents = 0;
                weapon.AmmoChanged += CountAmmoEvent;

                EnemyDeathEvent foreignDeath = CreateDeath(
                    enemy,
                    1,
                    foreignSource);
                Assert.That(router.TryPublishEnemyDeath(foreignDeath), Is.False);
                Assert.That(weapon.CurrentAmmo, Is.EqualTo(before));

                EnemyDeathEvent playerDeath = CreateDeath(
                    enemy,
                    2,
                    root.gameObject);
                Assert.That(router.TryPublishEnemyDeath(playerDeath), Is.True);
                Assert.That(weapon.CurrentAmmo,
                    Is.EqualTo(before + effect.RefillAmount));
                Assert.That(ammoEvents, Is.EqualTo(1),
                    "A successful grant must notify bound HUD presenters once.");

                Assert.That(router.TryPublishEnemyDeath(playerDeath), Is.False);
                Assert.That(weapon.CurrentAmmo,
                    Is.EqualTo(before + effect.RefillAmount));
                Assert.That(ammoEvents, Is.EqualTo(1));

                weapon.AddMagazineAmmo(999);
                ammoEvents = 0;
                int triggersAtFull = effect.TriggerCount;
                Assert.That(router.TryPublishEnemyDeath(
                    CreateDeath(enemy, 3, root.gameObject)), Is.True);
                Assert.That(weapon.CurrentAmmo, Is.EqualTo(weapon.MagazineCapacity));
                Assert.That(effect.TriggerCount, Is.EqualTo(triggersAtFull));
                Assert.That(ammoEvents, Is.Zero,
                    "A full magazine must not emit a false HUD refresh.");

                Assert.That(effect.RemoveEffect(), Is.True);
                ConsumeRounds(weapon, 1);
                int afterRemoval = weapon.CurrentAmmo;
                Assert.That(router.TryPublishEnemyDeath(
                    CreateDeath(enemy, 4, root.gameObject)), Is.True);
                Assert.That(weapon.CurrentAmmo, Is.EqualTo(afterRemoval));
                weapon.AmmoChanged -= CountAmmoEvent;

                void CountAmmoEvent() => ammoEvents++;
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
                Object.DestroyImmediate(foreignSource);
            }
        }

        [UnityTest]
        public IEnumerator SwitchingIgnoresKillThenCompletedWeaponReceivesNext()
        {
            yield return LoadPlayer();
            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            PlayerCombatController combat = root.GetComponent<PlayerCombatController>();
            PlayerCombatEventRouter router = root.CombatEvents;
            PlayerKillAmmoEffectController effect = root.KillAmmoEffect;
            WeaponController original = combat.EquippedWeapon;
            int targetIndex = (combat.EquippedWeaponIndex + 1) % combat.WeaponCount;
            WeaponController target = combat.GetWeapon(targetIndex);
            GameObject enemyObject = new("Issue34 Switch Enemy");

            try
            {
                EnemyController enemy = enemyObject.AddComponent<EnemyController>();
                ConsumeRounds(original, 5);
                int originalBefore = original.CurrentAmmo;
                Assert.That(combat.TrySelectWeapon(targetIndex), Is.True);
                Assert.That(combat.IsSwitching, Is.True);
                Assert.That(router.TryPublishEnemyDeath(
                    CreateDeath(enemy, 10, root.gameObject)), Is.True);
                Assert.That(original.CurrentAmmo, Is.EqualTo(originalBefore));

                yield return new WaitUntil(() => !combat.IsSwitching);
                Assert.That(combat.EquippedWeapon, Is.SameAs(target));
                ConsumeRounds(target, 5);
                int targetBefore = target.CurrentAmmo;
                Assert.That(router.TryPublishEnemyDeath(
                    CreateDeath(enemy, 11, root.gameObject)), Is.True);
                Assert.That(target.CurrentAmmo,
                    Is.EqualTo(targetBefore + effect.RefillAmount));
                Assert.That(original.CurrentAmmo, Is.EqualTo(originalBefore));
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
            }
        }

        private static IEnumerator LoadPlayer()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.CombatEvents, Is.Not.Null);
            Assert.That(root.KillAmmoEffect, Is.Not.Null);
            Assert.That(root.KillAmmoEffect.IsEffectActive, Is.True);
        }

        private static EnemyDeathEvent CreateDeath(
            EnemyController enemy,
            int spawnId,
            GameObject source)
        {
            return new EnemyDeathEvent(
                enemy,
                spawnId,
                1,
                new DamageInfo(
                    10f,
                    enemy.transform.position,
                    Vector3.forward,
                    source,
                    DamageType.Hitscan),
                10);
        }

        private static void ConsumeRounds(WeaponController weapon, int amount)
        {
            FieldInfo stateField = typeof(WeaponController).GetField(
                "ammoState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var state = (WeaponAmmoState)stateField.GetValue(weapon);

            for (int index = 0; index < amount; index++)
            {
                Assert.That(state.TryConsumeRound(), Is.True);
            }
        }
    }
}
