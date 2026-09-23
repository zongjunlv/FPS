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
    public sealed class Issue20SurvivalUpgradeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void HealthRecoveryAndCapacityChangesPreserveMissingAmount()
        {
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            var target = new GameObject("Survival Target");

            try
            {
                Component health = target.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[]
                {
                    typeof(float), typeof(float)
                }).Invoke(health, new object[] { 100f, 50f });
                object damage = Activator.CreateInstance(
                    damageType,
                    40f,
                    Vector3.zero,
                    Vector3.forward,
                    null);
                healthType.GetMethod("ApplyDamage").Invoke(
                    health,
                    new[] { damage });
                Assert.That(ReadFloat(healthType, health, "CurrentArmor"),
                    Is.EqualTo(10f));

                Assert.That(
                    healthType.GetMethod("SetMaximumArmor").Invoke(
                        health,
                        new object[] { 75f }),
                    Is.EqualTo(true));
                Assert.That(ReadFloat(healthType, health, "MaxArmor"),
                    Is.EqualTo(75f));
                Assert.That(ReadFloat(healthType, health, "CurrentArmor"),
                    Is.EqualTo(35f),
                    "Capacity growth adds only the maximum delta.");
                Assert.That(
                    healthType.GetMethod("RestoreArmor").Invoke(
                        health,
                        new object[] { 30f }),
                    Is.EqualTo(30f));
                Assert.That(ReadFloat(healthType, health, "CurrentArmor"),
                    Is.EqualTo(65f));
                Assert.That(
                    healthType.GetMethod("RestoreArmor").Invoke(
                        health,
                        new object[] { 30f }),
                    Is.EqualTo(10f));
                Assert.That(
                    healthType.GetMethod("RestoreArmor").Invoke(
                        health,
                        new object[] { 1f }),
                    Is.EqualTo(0f),
                    "Recovery at full capacity must fail explicitly.");

                object healthDamage = Activator.CreateInstance(
                    damageType,
                    100f,
                    Vector3.zero,
                    Vector3.forward,
                    null);
                healthType.GetMethod("ApplyDamage").Invoke(
                    health,
                    new[] { healthDamage });
                Assert.That(ReadFloat(healthType, health, "CurrentHealth"),
                    Is.EqualTo(75f));
                Assert.That(
                    healthType.GetMethod("SetMaximumHealth").Invoke(
                        health,
                        new object[] { 125f }),
                    Is.EqualTo(true));
                Assert.That(ReadFloat(healthType, health, "CurrentHealth"),
                    Is.EqualTo(100f));
                Assert.That(
                    healthType.GetMethod("RestoreHealth").Invoke(
                        health,
                        new object[] { 50f }),
                    Is.EqualTo(25f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void LegacySurvivalModifiersExcludeGameplayEffectAttributes()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type stateType = RuntimeTypeResolver.GetType(
                "RunUpgradeState");
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            ScriptableObject healthUpgrade = ScriptableObject.CreateInstance(
                definitionType);
            ScriptableObject armorUpgrade = ScriptableObject.CreateInstance(
                definitionType);
            ScriptableObject moveUpgrade = ScriptableObject.CreateInstance(
                definitionType);
            GameObject player = new GameObject("Runtime Survival Stats");

            try
            {
                Configure(definitionType, rarityType, effectType,
                    healthUpgrade, "health", "MaximumHealth", 3, 0.2f);
                Configure(definitionType, rarityType, effectType,
                    armorUpgrade, "armor", "MaximumArmor", 3, 0.2f);
                Configure(definitionType, rarityType, effectType,
                    moveUpgrade, "move", "MovementSpeed", 3, 0.1f);
                object state = Activator.CreateInstance(stateType);
                MethodInfo apply = stateType.GetMethod("TryApply");
                apply.Invoke(state, new object[] { healthUpgrade });
                apply.Invoke(state, new object[] { healthUpgrade });
                apply.Invoke(state, new object[] { armorUpgrade });
                apply.Invoke(state, new object[] { moveUpgrade });
                apply.Invoke(state, new object[] { moveUpgrade });
                object modifiers = stateType.GetProperty("SurvivalModifiers")
                    .GetValue(state);
                Assert.That(ReadModifier(modifiers, "MaximumHealthMultiplier"),
                    Is.EqualTo(1f).Within(0.0001f),
                    "最大生命现由 Gameplay Effect 聚合，不应重复进入旧修正器。 ");
                Assert.That(ReadModifier(modifiers, "MaximumArmorMultiplier"),
                    Is.EqualTo(1.2f).Within(0.0001f));
                Assert.That(ReadModifier(modifiers, "MovementSpeedMultiplier"),
                    Is.EqualTo(1.2f).Within(0.0001f));

                Component stats = player.AddComponent(statsType);
                statsType.GetMethod("SetSurvivalModifiers").Invoke(
                    stats,
                    new[] { modifiers });
                float walk = (float)statsType
                    .GetMethod("ApplyMovementSpeed").Invoke(
                        stats,
                        new object[] { 2f });
                float sprint = (float)statsType
                    .GetMethod("ApplyMovementSpeed").Invoke(
                        stats,
                        new object[] { 5f });
                Assert.That(walk,
                    Is.EqualTo(2.4f).Within(0.0001f));
                Assert.That(sprint,
                    Is.EqualTo(6f).Within(0.0001f));
                Assert.That(sprint / walk,
                    Is.EqualTo(2.5f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(healthUpgrade);
                UnityEngine.Object.DestroyImmediate(armorUpgrade);
                UnityEngine.Object.DestroyImmediate(moveUpgrade);
            }
        }

        [UnityTest]
        public IEnumerator DefaultCatalogContainsEverySurvivalCategory()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            IEnumerable catalog = (IEnumerable)controllerType
                .GetProperty("AvailableUpgrades").GetValue(controller);
            var effects = new HashSet<string>(StringComparer.Ordinal);

            foreach (object definition in catalog)
            {
                effects.Add(definition.GetType().GetProperty("EffectType")
                    .GetValue(definition).ToString());
            }

            Assert.That(effects, Is.SupersetOf(new[]
            {
                "MaximumHealth",
                "MaximumArmor",
                "HealthRestore",
                "ArmorRestore",
                "MovementSpeed"
            }));
        }

        [UnityTest]
        public IEnumerator FullRecoveryDoesNotConsumeChoiceOrCloseModal()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            ScriptableObject definition = CreateDefinition(
                "full_restore", "HealthRestore", 30f);

            try
            {
                ConfigureRun(controllerType, controller, definition);
                controllerType.GetMethod("QueueUpgradeChoices").Invoke(
                    controller, new object[] { 1 });
                yield return null;
                Assert.That(controllerType.GetMethod("TrySelect").Invoke(
                    controller, new object[] { 0 }), Is.EqualTo(false));
                Assert.That(controllerType.GetProperty("PendingChoiceCount")
                    .GetValue(controller), Is.EqualTo(1));
                Assert.That(controllerType.GetProperty("SelectedUpgradeCount")
                    .GetValue(controller), Is.EqualTo(0));
                Assert.That(controllerType.GetProperty("IsChoiceOpen")
                    .GetValue(controller), Is.EqualTo(true));
                Assert.That(Time.timeScale, Is.EqualTo(0f));
            }
            finally
            {
                controller.gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [UnityTest]
        public IEnumerator MaximumHealthUpgradePreservesDamageAndRefreshesHud()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            Type hudType = RuntimeTypeResolver.GetType("UnifiedGameHud");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component health = controller.GetComponent(healthType);
            Component hud = (Component)UnityEngine.Object
                .FindAnyObjectByType(hudType);
            ScriptableObject definition = CreateDefinition(
                "max_health", "MaximumHealth", 0.2f);

            try
            {
                object damage = Activator.CreateInstance(
                    damageType, 150f, Vector3.zero, Vector3.forward, null);
                healthType.GetMethod("ApplyDamage").Invoke(
                    health, new[] { damage });
                int refreshBefore = (int)hudType
                    .GetProperty("VitalsRefreshCount").GetValue(hud);
                ConfigureRun(controllerType, controller, definition);
                controllerType.GetMethod("QueueUpgradeChoices").Invoke(
                    controller, new object[] { 1 });
                yield return null;
                Assert.That(controllerType.GetMethod("TrySelect").Invoke(
                    controller, new object[] { 0 }), Is.EqualTo(true));
                yield return null;

                Assert.That(ReadFloat(healthType, health, "MaxHealth"),
                    Is.EqualTo(120f).Within(0.001f));
                Assert.That(ReadFloat(healthType, health, "CurrentHealth"),
                    Is.EqualTo(70f).Within(0.001f),
                    "Upgrading capacity must preserve the 50 missing health.");
                Assert.That(hudType.GetProperty("HealthText").GetValue(hud),
                    Does.Contain("070 / 120"));
                Assert.That(hudType.GetProperty("VitalsRefreshCount")
                    .GetValue(hud), Is.GreaterThan(refreshBefore));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [UnityTest]
        public IEnumerator MovementUpgradeAffectsGameplayHudAndSceneReset()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Type playerType = RuntimeTypeResolver.GetType("PlayerController");
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            Type hudType = RuntimeTypeResolver.GetType("UnifiedGameHud");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component player = controller.GetComponent(playerType);
            Component hud = (Component)UnityEngine.Object
                .FindAnyObjectByType(hudType);
            ScriptableObject definition = CreateDefinition(
                "move", "MovementSpeed", 0.2f);

            try
            {
                ConfigureRun(controllerType, controller, definition);
                controllerType.GetMethod("QueueUpgradeChoices").Invoke(
                    controller, new object[] { 1 });
                yield return null;
                Assert.That(controllerType.GetMethod("TrySelect").Invoke(
                    controller, new object[] { 0 }), Is.EqualTo(true));
                yield return null;
                Vector3 walk = (Vector3)playerType
                    .GetMethod("CalculateHorizontalVelocity").Invoke(
                        player, new object[] { Vector2.up, false });
                Vector3 sprint = (Vector3)playerType
                    .GetMethod("CalculateHorizontalVelocity").Invoke(
                        player, new object[] { Vector2.up, true });
                float baseWalk = (float)playerType.GetField("walkSpeed",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(player);
                float baseSprint = (float)playerType.GetField("sprintSpeed",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(player);
                Assert.That(walk.magnitude,
                    Is.EqualTo(baseWalk * 1.2f).Within(0.001f));
                Assert.That(sprint.magnitude,
                    Is.EqualTo(baseSprint * 1.2f).Within(0.001f));
                Assert.That(hudType.GetProperty("MovementText").GetValue(hud),
                    Is.EqualTo("MOVE  120%"));

                yield return LoadCityNew();
                Component freshStats = (Component)UnityEngine.Object
                    .FindAnyObjectByType(statsType);
                object survival = statsType.GetProperty("SurvivalModifiers")
                    .GetValue(freshStats);
                Assert.That(ReadModifier(
                    survival, "MovementSpeedMultiplier"), Is.EqualTo(1f));
                Component freshHud = (Component)UnityEngine.Object
                    .FindAnyObjectByType(hudType);
                Assert.That(hudType.GetProperty("MovementText")
                    .GetValue(freshHud), Is.EqualTo("MOVE  100%"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static void Configure(
            Type definitionType,
            Type rarityType,
            Type effectType,
            ScriptableObject definition,
            string id,
            string effect,
            int maxLevel,
            float amount)
        {
            definitionType.GetMethod("Configure").Invoke(
                definition,
                new object[]
                {
                    id, id, id, null,
                    Enum.Parse(rarityType, "Common"),
                    maxLevel,
                    Enum.Parse(effectType, effect),
                    amount
                });
        }

        private static ScriptableObject CreateDefinition(
            string id,
            string effect,
            float amount)
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);
            Configure(definitionType, rarityType, effectType,
                definition, id, effect, 3, amount);
            return definition;
        }

        private static void ConfigureRun(
            Type controllerType,
            Component controller,
            ScriptableObject definition)
        {
            Type definitionType = definition.GetType();
            IList custom = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(definitionType));
            custom.Add(definition);
            controllerType.GetMethod("ConfigureRun").Invoke(
                controller, new object[] { 20020, custom });
        }

        private static IEnumerator LoadCityNew()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene, LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }
        }

        private static float ReadModifier(object modifiers, string property)
        {
            return (float)modifiers.GetType().GetProperty(property)
                .GetValue(modifiers);
        }

        private static float ReadFloat(Type type, object target, string name)
        {
            return (float)type.GetProperty(name).GetValue(target);
        }
    }
}
