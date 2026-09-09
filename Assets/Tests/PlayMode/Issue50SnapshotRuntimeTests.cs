using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue50SnapshotRuntimeTests
    {
        private bool oldIgnoreLogs;

        [SetUp]
        public void SetUp()
        {
            oldIgnoreLogs = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = oldIgnoreLogs;
        }

        [UnityTest]
        public IEnumerator SceneSnapshotRestoresAllWeaponsUpgradesAndRejectsInvalidWithoutChanges()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
            yield return WaitReady();
            var root = Object.FindAnyObjectByType<PlayerCombatCompositionRoot>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.TryInitialize(), Is.True);
            var upgrades = root.GetComponent<PlayerUpgradeController>();
            var health = root.GetComponent<Health>();
            var loadout = root.GetComponent<WeaponLoadoutController>();
            var adapter = root.GetComponent<RunSnapshotRuntimeAdapter>() ??
                root.gameObject.AddComponent<RunSnapshotRuntimeAdapter>();
            RunSnapshot initial = adapter.Capture();
            UpgradeDefinition maxHealth = upgrades.AvailableUpgrades.First(
                value => value.EffectType == UpgradeEffectType.MaximumHealth);
            UpgradeDefinition magazine = upgrades.AvailableUpgrades.First(
                value => value.EffectType == UpgradeEffectType.WeaponMagazineCapacity);
            UpgradeDefinition instantHeal = upgrades.AvailableUpgrades.First(
                value => value.EffectType == UpgradeEffectType.HealthRestore);
            initial.Seed = 50777;
            initial.Health = 37f;
            initial.Armor = 13f;
            initial.CurrentWeaponId = initial.Weapons[1].WeaponId;
            initial.UpgradeSelectionHistory = new List<string>
                { maxHealth.StableId, magazine.StableId, instantHeal.StableId };
            initial.Upgrades = new List<UpgradeLevelSnapshot>
            {
                new() { UpgradeId = maxHealth.StableId, Level = 1 },
                new() { UpgradeId = magazine.StableId, Level = 1 },
                new() { UpgradeId = instantHeal.StableId, Level = 1 }
            };
            initial.PlayerEffects = new List<GameplayEffectSnapshot>
            {
                SavedPersistentEffect(maxHealth)
            };
            foreach (WeaponAmmoSnapshot ammo in initial.Weapons)
            {
                ammo.Magazine = 3;
                ammo.Reserve = 7;
            }
            int deathEvents = 0;
            int damageEvents = 0;
            int choiceEvents = 0;
            health.Died += () => deathEvents++;
            health.DamageApplied += _ => damageEvents++;
            root.GetComponent<PlayerRunProgression>().LevelsGained += _ => choiceEvents++;
            Assert.That(adapter.TryRestore(initial, out string error), Is.True, error);
            Assert.That(health.CurrentHealth, Is.EqualTo(37f));
            Assert.That(health.CurrentArmor, Is.EqualTo(13f));
            float restoredMaxHealth = health.MaxHealth;
            Assert.That(restoredMaxHealth, Is.GreaterThan(100f));
            Assert.That(upgrades.RunSeed, Is.EqualTo(50777));
            Assert.That(upgrades.PendingChoiceCount, Is.Zero);
            Assert.That(upgrades.SelectedUpgradeCount, Is.EqualTo(3));
            Assert.That(loadout.CurrentWeapon.StableId, Is.EqualTo(initial.CurrentWeaponId));
            for (int i = 0; i < loadout.WeaponCount; i++)
            {
                Assert.That(loadout.GetWeapon(i).CurrentAmmo, Is.EqualTo(3));
                Assert.That(loadout.GetWeapon(i).ReserveAmmo, Is.EqualTo(7));
            }
            Assert.That(adapter.TryRestore(initial, out error), Is.True, error);
            Assert.That(health.MaxHealth, Is.EqualTo(restoredMaxHealth));
            Assert.That(health.CurrentHealth, Is.EqualTo(37f));
            Assert.That(upgrades.SelectedUpgradeCount, Is.EqualTo(3));
            Assert.That(deathEvents + damageEvents + choiceEvents, Is.Zero);
            RunSnapshot invalid = adapter.Capture();
            invalid.Health = 99f;
            invalid.Weapons[1].Reserve = int.MaxValue;
            Assert.That(adapter.TryRestore(invalid, out error), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(37f));
            Assert.That(loadout.GetWeapon(0).ReserveAmmo, Is.EqualTo(7));
            invalid = adapter.Capture();
            invalid.UpgradeSelectionHistory[0] = "missing-upgrade";
            Assert.That(adapter.TryRestore(invalid, out error), Is.False);
            Assert.That(upgrades.SelectedUpgradeCount, Is.EqualTo(3));
        }

        private static GameplayEffectSnapshot SavedPersistentEffect(
            UpgradeDefinition upgrade)
        {
            return new GameplayEffectSnapshot
            {
                EffectId = upgrade.GameplayEffect.StableId,
                SourceId = upgrade.StableId,
                SourceKey = "upgrade:" + upgrade.StableId,
                DurationPolicy = 0
            };
        }

        private static IEnumerator WaitReady()
        {
            float timeout = Time.realtimeSinceStartup + 35f;
            while (Time.realtimeSinceStartup < timeout)
            {
                CityNewWaveBootstrap wave =
                    Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                CityNewMissionController mission =
                    Object.FindAnyObjectByType<CityNewMissionController>();
                if (wave != null && wave.Director != null &&
                    wave.Director.IsRunning && mission != null &&
                    mission.Terminal != null)
                {
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("波次或任务未在时限内准备完成。");
        }
    }
}
