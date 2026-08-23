using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue19WeaponUpgradeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreTimeScale()
        {
            Time.timeScale = 1f;
        }

        [Test]
        public void WeaponEffectsAggregateInAStableOrderIndependentSnapshot()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type stateType = RuntimeTypeResolver.GetType(
                "RunUpgradeState");
            string[] effects =
            {
                "WeaponDamage",
                "WeaponFireRate",
                "WeaponMagazineCapacity",
                "WeaponReloadSpeed",
                "WeaponRecoilControl",
                "WeaponAccuracy"
            };
            var definitions = new List<ScriptableObject>();

            try
            {
                for (int index = 0; index < effects.Length; index++)
                {
                    ScriptableObject definition = ScriptableObject
                        .CreateInstance(definitionType);
                    definitionType.GetMethod("Configure").Invoke(
                        definition,
                        new object[]
                        {
                            $"effect_{index}",
                            effects[index],
                            "Runtime weapon modifier",
                            null,
                            Enum.Parse(rarityType, "Common"),
                            3,
                            Enum.Parse(effectType, effects[index]),
                            0.2f
                        });
                    definitions.Add(definition);
                }

                object forward = Activator.CreateInstance(stateType);
                object reverse = Activator.CreateInstance(stateType);

                foreach (ScriptableObject definition in definitions)
                {
                    Assert.That(
                        stateType.GetMethod("TryApply").Invoke(
                            forward,
                            new object[] { definition }),
                        Is.EqualTo(true));
                }

                for (int index = definitions.Count - 1; index >= 0; index--)
                {
                    stateType.GetMethod("TryApply").Invoke(
                        reverse,
                        new object[] { definitions[index] });
                }

                object forwardModifiers = stateType
                    .GetProperty("WeaponModifiers").GetValue(forward);
                object reverseModifiers = stateType
                    .GetProperty("WeaponModifiers").GetValue(reverse);
                string[] multipliers =
                {
                    "DamageMultiplier",
                    "FireRateMultiplier",
                    "MagazineCapacityMultiplier",
                    "ReloadSpeedMultiplier",
                    "RecoilControlMultiplier",
                    "AccuracyMultiplier"
                };

                foreach (string multiplier in multipliers)
                {
                    float left = (float)forwardModifiers.GetType()
                        .GetProperty(multiplier).GetValue(forwardModifiers);
                    float right = (float)reverseModifiers.GetType()
                        .GetProperty(multiplier).GetValue(reverseModifiers);
                    Assert.That(left, Is.EqualTo(1.2f).Within(0.0001f));
                    Assert.That(right, Is.EqualTo(left).Within(0.0001f));
                }
            }
            finally
            {
                foreach (ScriptableObject definition in definitions)
                {
                    UnityEngine.Object.DestroyImmediate(definition);
                }
            }
        }

        [Test]
        public void SameAndDifferentUpgradesStackUntilTheirOwnMaximumLevels()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type stateType = RuntimeTypeResolver.GetType(
                "RunUpgradeState");
            ScriptableObject rapid = ScriptableObject.CreateInstance(
                definitionType);
            ScriptableObject tuned = ScriptableObject.CreateInstance(
                definitionType);

            try
            {
                ConfigureUpgrade(
                    definitionType,
                    rarityType,
                    effectType,
                    rapid,
                    "rapid",
                    "WeaponFireRate",
                    2,
                    0.15f);
                ConfigureUpgrade(
                    definitionType,
                    rarityType,
                    effectType,
                    tuned,
                    "tuned",
                    "WeaponFireRate",
                    1,
                    0.1f);
                object state = Activator.CreateInstance(stateType);
                var apply = stateType.GetMethod("TryApply");
                Assert.That(apply.Invoke(state, new object[] { rapid }),
                    Is.EqualTo(true));
                Assert.That(apply.Invoke(state, new object[] { rapid }),
                    Is.EqualTo(true));
                Assert.That(apply.Invoke(state, new object[] { rapid }),
                    Is.EqualTo(false));
                Assert.That(apply.Invoke(state, new object[] { tuned }),
                    Is.EqualTo(true));
                Assert.That(
                    stateType.GetMethod("GetLevel").Invoke(
                        state,
                        new object[] { "rapid" }),
                    Is.EqualTo(2));
                object modifiers = stateType.GetProperty("WeaponModifiers")
                    .GetValue(state);
                Assert.That(
                    modifiers.GetType().GetProperty("FireRateMultiplier")
                        .GetValue(modifiers),
                    Is.EqualTo(1.4f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rapid);
                UnityEngine.Object.DestroyImmediate(tuned);
            }
        }

        [Test]
        public void RuntimeStatsApplyTheSameFormulaToEveryWeaponBaseValue()
        {
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            Type modifiersType = RuntimeTypeResolver.GetType(
                "WeaponRuntimeModifiers");
            var player = new GameObject("Runtime Stats");

            try
            {
                Component stats = player.AddComponent(statsType);
                object modifiers = Activator.CreateInstance(
                    modifiersType,
                    1.5f,
                    1.25f,
                    1.2f,
                    1.5f,
                    1.25f,
                    2f);
                statsType.GetMethod("SetWeaponModifiers").Invoke(
                    stats,
                    new[] { modifiers });

                Assert.That(
                    InvokeFloat(statsType, stats, "ApplyWeaponDamage", 20f),
                    Is.EqualTo(30f).Within(0.0001f));
                Assert.That(
                    InvokeFloat(statsType, stats, "ApplyFireInterval", 0.1f),
                    Is.EqualTo(0.08f).Within(0.0001f));
                Assert.That(
                    statsType.GetMethod("ApplyMagazineCapacity").Invoke(
                        stats,
                        new object[] { 12 }),
                    Is.EqualTo(14));
                Assert.That(
                    InvokeFloat(statsType, stats, "ApplyReloadDuration", 2.4f),
                    Is.EqualTo(1.6f).Within(0.0001f));
                Assert.That(
                    InvokeFloat(statsType, stats, "ApplyRecoil", 5f),
                    Is.EqualTo(4f).Within(0.0001f));
                Assert.That(
                    InvokeFloat(statsType, stats, "ApplySpread", 1f),
                    Is.EqualTo(0.5f).Within(0.0001f));

                statsType.GetMethod("ResetRuntimeModifiers").Invoke(stats, null);
                object reset = statsType.GetProperty("WeaponModifiers")
                    .GetValue(stats);

                foreach (string property in new[]
                {
                    "DamageMultiplier",
                    "FireRateMultiplier",
                    "MagazineCapacityMultiplier",
                    "ReloadSpeedMultiplier",
                    "RecoilControlMultiplier",
                    "AccuracyMultiplier"
                })
                {
                    Assert.That(
                        reset.GetType().GetProperty(property).GetValue(reset),
                        Is.EqualTo(1f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void DefaultModifierInputIsNormalizedToFiniteValues()
        {
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            Type modifiersType = RuntimeTypeResolver.GetType(
                "WeaponRuntimeModifiers");
            var player = new GameObject("Normalized Runtime Stats");

            try
            {
                Component stats = player.AddComponent(statsType);
                object defaultModifiers = Activator.CreateInstance(
                    modifiersType);
                statsType.GetMethod("SetWeaponModifiers").Invoke(
                    stats,
                    new[] { defaultModifiers });

                foreach (string method in new[]
                {
                    "ApplyFireInterval",
                    "ApplyReloadDuration",
                    "ApplyRecoil",
                    "ApplySpread"
                })
                {
                    float value = InvokeFloat(
                        statsType,
                        stats,
                        method,
                        1f);
                    Assert.That(float.IsNaN(value), Is.False);
                    Assert.That(float.IsInfinity(value), Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void MagazineGrowthPreservesSpentRoundsAndActiveReloadProgress()
        {
            Type ammoType = RuntimeTypeResolver.GetType(
                "WeaponAmmoState");
            object ammo = Activator.CreateInstance(ammoType, 30, 90);
            var consume = ammoType.GetMethod("TryConsumeRound");

            for (int index = 0; index < 10; index++)
            {
                Assert.That(consume.Invoke(ammo, null), Is.EqualTo(true));
            }

            Assert.That(
                ammoType.GetMethod("TryBeginReload").Invoke(ammo, null),
                Is.EqualTo(true));
            Assert.That(
                ammoType.GetMethod("AdvanceReload").Invoke(
                    ammo,
                    new object[] { 0.8f, 2f }),
                Is.EqualTo(false));
            Assert.That(
                ammoType.GetMethod("SetMagazineCapacity").Invoke(
                    ammo,
                    new object[] { 36 }),
                Is.EqualTo(true));
            Assert.That(
                ammoType.GetProperty("MagazineCapacity").GetValue(ammo),
                Is.EqualTo(36));
            Assert.That(
                ammoType.GetProperty("CurrentAmmo").GetValue(ammo),
                Is.EqualTo(26));
            Assert.That(
                ammoType.GetProperty("ReserveAmmo").GetValue(ammo),
                Is.EqualTo(90));
            Assert.That(
                ammoType.GetProperty("IsReloading").GetValue(ammo),
                Is.EqualTo(true));
            Assert.That(
                ammoType.GetMethod("SetMagazineCapacity").Invoke(
                    ammo,
                    new object[] { 36 }),
                Is.EqualTo(false));
            Assert.That(
                ammoType.GetProperty("CurrentAmmo").GetValue(ammo),
                Is.EqualTo(26));
            Assert.That(
                ammoType.GetMethod("AdvanceReload").Invoke(
                    ammo,
                    new object[] { 0.8f, 1.6f }),
                Is.EqualTo(true));
            Assert.That(
                ammoType.GetProperty("CurrentAmmo").GetValue(ammo),
                Is.EqualTo(36));
            Assert.That(
                ammoType.GetProperty("ReserveAmmo").GetValue(ammo),
                Is.EqualTo(80));
        }

        [UnityTest]
        public IEnumerator RuntimeModifiersReachBothArAndPistolGameplayValues()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            Type modifiersType = RuntimeTypeResolver.GetType(
                "WeaponRuntimeModifiers");
            Type weaponType = RuntimeTypeResolver.GetType(
                "WeaponController");
            Component combat = (Component)UnityEngine.Object
                .FindAnyObjectByType(combatType);
            Component stats = combat.GetComponent(statsType);
            Component[] weapons = combat.GetComponentsInChildren(
                weaponType,
                true);
            Assert.That(weapons, Has.Length.EqualTo(2));
            var baselines = new Dictionary<string, float[]>();

            foreach (Component weapon in weapons)
            {
                string name = (string)weaponType.GetProperty("WeaponName")
                    .GetValue(weapon);
                baselines.Add(name, new[]
                {
                    (float)weaponType.GetProperty("Damage").GetValue(weapon),
                    (float)weaponType.GetProperty("FireInterval")
                        .GetValue(weapon),
                    Convert.ToSingle(weaponType.GetProperty("MagazineCapacity")
                        .GetValue(weapon)),
                    (float)weaponType.GetProperty("ReloadDuration")
                        .GetValue(weapon),
                    (float)weaponType.GetProperty("CurrentVerticalRecoil")
                        .GetValue(weapon),
                    (float)weaponType.GetProperty("CurrentSpreadDegrees")
                        .GetValue(weapon)
                });
            }

            object modifiers = Activator.CreateInstance(
                modifiersType,
                1.5f,
                1.25f,
                1.2f,
                1.5f,
                1.25f,
                2f);
            statsType.GetMethod("SetWeaponModifiers").Invoke(
                stats,
                new[] { modifiers });
            yield return null;

            foreach (Component weapon in weapons)
            {
                string name = (string)weaponType.GetProperty("WeaponName")
                    .GetValue(weapon);
                float[] baseline = baselines[name];
                Assert.That(
                    weaponType.GetProperty("Damage").GetValue(weapon),
                    Is.EqualTo(baseline[0] * 1.5f).Within(0.0001f));
                Assert.That(
                    weaponType.GetProperty("FireInterval").GetValue(weapon),
                    Is.EqualTo(baseline[1] / 1.25f).Within(0.0001f));
                Assert.That(
                    weaponType.GetProperty("MagazineCapacity").GetValue(weapon),
                    Is.EqualTo(Mathf.RoundToInt(baseline[2] * 1.2f)));
                Assert.That(
                    weaponType.GetProperty("ReloadDuration").GetValue(weapon),
                    Is.EqualTo(baseline[3] / 1.5f).Within(0.0001f));
                Assert.That(
                    weaponType.GetProperty("CurrentVerticalRecoil")
                        .GetValue(weapon),
                    Is.EqualTo(baseline[4] / 1.25f).Within(0.0001f));
                Assert.That(
                    weaponType.GetProperty("CurrentSpreadDegrees")
                        .GetValue(weapon),
                    Is.EqualTo(baseline[5] / 2f).Within(0.0001f));
            }

            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator DefaultCatalogContainsEveryWeaponUpgradeCategory()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            IEnumerable catalog = (IEnumerable)controllerType
                .GetProperty("AvailableUpgrades").GetValue(controller);
            var effectNames = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;

            foreach (object definition in catalog)
            {
                Type type = definition.GetType();
                effectNames.Add(type.GetProperty("EffectType")
                    .GetValue(definition).ToString());
                Assert.That(
                    (float)type.GetProperty("EffectAmount")
                        .GetValue(definition),
                    Is.GreaterThan(0f));
                Assert.That(
                    (int)type.GetProperty("MaximumLevel")
                        .GetValue(definition),
                    Is.GreaterThan(0));
                Assert.That(type.GetProperty("Rarity").GetValue(definition),
                    Is.Not.Null);
                count++;
            }

            Assert.That(count, Is.GreaterThanOrEqualTo(6));
            Assert.That(effectNames, Is.SupersetOf(new[]
            {
                "WeaponDamage",
                "WeaponFireRate",
                "WeaponMagazineCapacity",
                "WeaponReloadSpeed",
                "WeaponRecoilControl",
                "WeaponAccuracy"
            }));
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator UpgradeCardShowsExactEffectRarityAndMaximumLevel()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);

            try
            {
                definitionType.GetMethod("Configure").Invoke(
                    definition,
                    new object[]
                    {
                        "ui_fire_rate",
                        "RAPID CYCLING",
                        "Faster follow-up shots.",
                        null,
                        Enum.Parse(rarityType, "Rare"),
                        1,
                        Enum.Parse(effectType, "WeaponFireRate"),
                        0.15f
                    });
                IList custom = (IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(definitionType));
                custom.Add(definition);
                controllerType.GetMethod("ConfigureRun").Invoke(
                    controller,
                    new object[] { 19, custom });
                controllerType.GetMethod("QueueUpgradeChoices").Invoke(
                    controller,
                    new object[] { 1 });
                yield return null;

                Transform card = GameObject.Find("UpgradeCard1").transform;
                Assert.That(ReadText(card, "Rarity"), Is.EqualTo("稀有"));
                Assert.That(
                    ReadText(card, "Effect"),
                    Is.EqualTo("射击速度 +15%"));
                Assert.That(
                    ReadText(card, "Stack"),
                    Does.Contain("满级"));
                Assert.That(
                    ReadText(card, "Stack"),
                    Does.Contain("1 / 1"));
                Type textType = RuntimeTypeResolver.GetType(
                    "TMPro.TMP_Text, Unity.TextMeshPro");
                Component effectText = card.Find("Effect")
                    .GetComponent(textType);
                object cardFont = textType.GetProperty("font")
                    .GetValue(effectText);
                bool supportsChinese = (bool)cardFont.GetType()
                    .GetMethod(
                        "HasCharacter",
                        new[]
                        {
                            typeof(char), typeof(bool), typeof(bool)
                        })
                    .Invoke(cardFont, new object[] { '中', false, true });
                Assert.That(supportsChinese, Is.True,
                    "升级卡字体必须包含中文字形，不能显示方框乱码。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [UnityTest]
        public IEnumerator MagazineUpgradeDoesNotCancelAnActiveReload()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component combat = controller.GetComponent(combatType);
            Component weapon = (Component)combatType
                .GetProperty("EquippedWeapon").GetValue(combat);
            Type weaponType = weapon.GetType();
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);

            try
            {
                definitionType.GetMethod("Configure").Invoke(
                    definition,
                    new object[]
                    {
                        "reload_magazine",
                        "EXTENDED MAGAZINE",
                        "Magazine capacity +20%.",
                        null,
                        Enum.Parse(rarityType, "Rare"),
                        1,
                        Enum.Parse(effectType, "WeaponMagazineCapacity"),
                        0.2f
                    });
                IList custom = (IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(definitionType));
                custom.Add(definition);
                controllerType.GetMethod("ConfigureRun").Invoke(
                    controller,
                    new object[] { 1901, custom });
                Assert.That(
                    weaponType.GetMethod("TryFire").Invoke(weapon, null),
                    Is.EqualTo(true));
                int baseCapacity = (int)weaponType
                    .GetProperty("MagazineCapacity").GetValue(weapon);
                Assert.That(
                    weaponType.GetMethod("TryStartReload").Invoke(weapon, null),
                    Is.EqualTo(true));
                controllerType.GetMethod("QueueUpgradeChoices").Invoke(
                    controller,
                    new object[] { 1 });
                yield return null;

                Assert.That(
                    weaponType.GetProperty("IsReloading").GetValue(weapon),
                    Is.EqualTo(true),
                    "Opening an upgrade choice must preserve reload progress.");
                Assert.That(
                    controllerType.GetMethod("TrySelect").Invoke(
                        controller,
                        new object[] { 0 }),
                    Is.EqualTo(true));
                Assert.That(
                    weaponType.GetProperty("IsReloading").GetValue(weapon),
                    Is.EqualTo(true));
                Assert.That(
                    weaponType.GetProperty("MagazineCapacity").GetValue(weapon),
                    Is.EqualTo(Mathf.RoundToInt(baseCapacity * 1.2f)));
                int current = (int)weaponType.GetProperty("CurrentAmmo")
                    .GetValue(weapon);
                int capacity = (int)weaponType.GetProperty("MagazineCapacity")
                    .GetValue(weapon);
                Assert.That(current, Is.InRange(0, capacity));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [UnityTest]
        public IEnumerator NonUpgradeNestedLockCancelsPreservedReloadState()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type upgradeType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            Type coordinatorType = RuntimeTypeResolver.GetType(
                "GameplayLockCoordinator");
            Type reasonType = RuntimeTypeResolver.GetType(
                "GameplayLockReason");
            Component upgrade = (Component)UnityEngine.Object
                .FindAnyObjectByType(upgradeType);
            Component combat = upgrade.GetComponent(combatType);
            Component coordinator = upgrade.GetComponent(coordinatorType);
            Component weapon = (Component)combatType
                .GetProperty("EquippedWeapon").GetValue(combat);
            Type weaponType = weapon.GetType();
            Assert.That(weaponType.GetMethod("TryFire").Invoke(weapon, null),
                Is.EqualTo(true));
            Assert.That(
                weaponType.GetMethod("TryStartReload").Invoke(weapon, null),
                Is.EqualTo(true));
            upgradeType.GetMethod("QueueUpgradeChoices").Invoke(
                upgrade,
                new object[] { 1 });
            yield return null;
            Assert.That(
                weaponType.GetProperty("IsReloading").GetValue(weapon),
                Is.EqualTo(true));

            IDisposable defeat = (IDisposable)coordinatorType
                .GetMethod("Acquire").Invoke(
                    coordinator,
                    new[] { Enum.Parse(reasonType, "Defeat") });

            try
            {
                Assert.That(
                    weaponType.GetProperty("IsReloading").GetValue(weapon),
                    Is.EqualTo(false));
            }
            finally
            {
                defeat.Dispose();
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [UnityTest]
        public IEnumerator FireRateModifierChangesActualSuccessfulShotCadence()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            Type modifiersType = RuntimeTypeResolver.GetType(
                "WeaponRuntimeModifiers");
            Component combat = (Component)UnityEngine.Object
                .FindAnyObjectByType(combatType);
            Component stats = combat.GetComponent(statsType);
            Component weapon = (Component)combatType
                .GetProperty("EquippedWeapon").GetValue(combat);
            Type weaponType = weapon.GetType();
            float baseInterval = (float)weaponType.GetProperty("FireInterval")
                .GetValue(weapon);
            object modifiers = Activator.CreateInstance(
                modifiersType,
                1f,
                4f,
                1f,
                1f,
                1f,
                1f);
            statsType.GetMethod("SetWeaponModifiers").Invoke(
                stats,
                new[] { modifiers });
            float firstShotTime = Time.time;
            Assert.That(
                weaponType.GetMethod("TryFire").Invoke(weapon, null),
                Is.EqualTo(true));
            yield return new WaitForSeconds(baseInterval * 0.25f + 0.01f);

            Assert.That(
                Time.time - firstShotTime,
                Is.LessThan(baseInterval));
            Assert.That(
                weaponType.GetMethod("TryFire").Invoke(weapon, null),
                Is.EqualTo(true),
                "The upgraded cadence must reach TryFire, not only the HUD.");
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator SceneReloadResetsEveryRuntimeWeaponModifier()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            Type modifiersType = RuntimeTypeResolver.GetType(
                "WeaponRuntimeModifiers");
            Component stats = (Component)UnityEngine.Object
                .FindAnyObjectByType(statsType);
            object modified = Activator.CreateInstance(
                modifiersType,
                2f,
                2f,
                2f,
                2f,
                2f,
                2f);
            statsType.GetMethod("SetWeaponModifiers").Invoke(
                stats,
                new[] { modified });

            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Component freshStats = (Component)UnityEngine.Object
                .FindAnyObjectByType(statsType);
            object reset = statsType.GetProperty("WeaponModifiers")
                .GetValue(freshStats);

            foreach (string property in new[]
            {
                "DamageMultiplier",
                "FireRateMultiplier",
                "MagazineCapacityMultiplier",
                "ReloadSpeedMultiplier",
                "RecoilControlMultiplier",
                "AccuracyMultiplier"
            })
            {
                Assert.That(
                    reset.GetType().GetProperty(property).GetValue(reset),
                    Is.EqualTo(1f));
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private static float InvokeFloat(
            Type type,
            object target,
            string method,
            float value)
        {
            return (float)type.GetMethod(method).Invoke(
                target,
                new object[] { value });
        }

        private static string ReadText(Transform parent, string childName)
        {
            Type textType = RuntimeTypeResolver.GetType(
                "TMPro.TMP_Text, Unity.TextMeshPro");
            Component text = parent.Find(childName).GetComponent(textType);
            return (string)textType.GetProperty("text").GetValue(text);
        }

        private static void ConfigureUpgrade(
            Type definitionType,
            Type rarityType,
            Type effectType,
            ScriptableObject definition,
            string stableId,
            string effect,
            int maximumLevel,
            float amount)
        {
            definitionType.GetMethod("Configure").Invoke(
                definition,
                new object[]
                {
                    stableId,
                    stableId,
                    "Weapon modifier",
                    null,
                    Enum.Parse(rarityType, "Common"),
                    maximumLevel,
                    Enum.Parse(effectType, effect),
                    amount
                });
        }
    }
}
