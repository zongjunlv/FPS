using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue18UpgradeChoiceTests
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
        public void SameSeedAndHistoryProduceSameCandidateSequence()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type stateType = RuntimeTypeResolver.GetType(
                "RunUpgradeState");
            Type generatorType = RuntimeTypeResolver.GetType(
                "UpgradeCandidateGenerator");
            IList definitions = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(definitionType));

            try
            {
                for (int index = 0; index < 5; index++)
                {
                    ScriptableObject definition = ScriptableObject
                        .CreateInstance(definitionType);
                    definitionType.GetMethod("Configure").Invoke(
                        definition,
                        new object[]
                        {
                            $"upgrade_{index}",
                            $"Upgrade {index}",
                            "Damage boost",
                            null,
                            Enum.Parse(rarityType, "Common"),
                            3,
                            Enum.Parse(effectType, "WeaponDamage"),
                            0.1f
                        });
                    definitions.Add(definition);
                }

                object leftState = Activator.CreateInstance(stateType);
                object rightState = Activator.CreateInstance(stateType);
                object leftGenerator = Activator.CreateInstance(
                    generatorType,
                    9182);
                object rightGenerator = Activator.CreateInstance(
                    generatorType,
                    9182);

                for (int round = 0; round < 3; round++)
                {
                    object left = Generate(
                        generatorType,
                        leftGenerator,
                        definitions,
                        leftState);
                    object right = Generate(
                        generatorType,
                        rightGenerator,
                        definitions,
                        rightState);
                    IList leftCandidates = GetCandidates(left);
                    IList rightCandidates = GetCandidates(right);
                    Assert.That(
                        CandidateIds(leftCandidates),
                        Is.EqualTo(CandidateIds(rightCandidates)));
                    stateType.GetMethod("TryApply").Invoke(
                        leftState,
                        new[] { leftCandidates[0] });
                    stateType.GetMethod("TryApply").Invoke(
                        rightState,
                        new[] { rightCandidates[0] });
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
        public void FullLevelsAreExcludedAndShortageIsExplicit()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type stateType = RuntimeTypeResolver.GetType(
                "RunUpgradeState");
            Type generatorType = RuntimeTypeResolver.GetType(
                "UpgradeCandidateGenerator");
            ScriptableObject full = ScriptableObject.CreateInstance(
                definitionType);
            ScriptableObject remaining = ScriptableObject.CreateInstance(
                definitionType);

            try
            {
                ConfigureDefinition(
                    definitionType,
                    rarityType,
                    effectType,
                    full,
                    "full",
                    1);
                ConfigureDefinition(
                    definitionType,
                    rarityType,
                    effectType,
                    remaining,
                    "remaining",
                    1);
                object state = Activator.CreateInstance(stateType);
                object generator = Activator.CreateInstance(
                    generatorType,
                    4);
                stateType.GetMethod("TryApply").Invoke(
                    state,
                    new object[] { full });
                object result = Generate(
                    generatorType,
                    generator,
                    CreateDefinitionList(definitionType, full, remaining),
                    state);

                Assert.That(GetCandidates(result), Has.Count.EqualTo(1));
                Assert.That(
                    result.GetType().GetProperty("Status").GetValue(result)
                        .ToString(),
                    Is.EqualTo("Reduced"));
                stateType.GetMethod("TryApply").Invoke(
                    state,
                    new object[] { remaining });
                result = Generate(
                    generatorType,
                    generator,
                    CreateDefinitionList(definitionType, full, remaining),
                    state);
                Assert.That(GetCandidates(result), Is.Empty);
                Assert.That(
                    result.GetType().GetProperty("Status").GetValue(result)
                        .ToString(),
                    Is.EqualTo("NoEligibleUpgrade"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(full);
                UnityEngine.Object.DestroyImmediate(remaining);
            }
        }

        [Test]
        public void CandidateGenerationDoesNotTouchUnityRandomState()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            Type stateType = RuntimeTypeResolver.GetType(
                "RunUpgradeState");
            Type generatorType = RuntimeTypeResolver.GetType(
                "UpgradeCandidateGenerator");
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);

            try
            {
                ConfigureDefinition(
                    definitionType,
                    rarityType,
                    effectType,
                    definition,
                    "isolated_random",
                    1);
                object state = Activator.CreateInstance(stateType);
                object generator = Activator.CreateInstance(
                    generatorType,
                    42);
                UnityEngine.Random.State before = UnityEngine.Random.state;
                Generate(
                    generatorType,
                    generator,
                    CreateDefinitionList(definitionType, definition),
                    state);
                UnityEngine.Random.State after = UnityEngine.Random.state;
                Assert.That(after, Is.EqualTo(before));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void DamageUpgradeChangesRuntimeValueWithoutMutatingAsset()
        {
            Type weaponDefinitionType = RuntimeTypeResolver.GetType(
                "WeaponDefinition");
            Type statsType = RuntimeTypeResolver.GetType(
                "PlayerRuntimeCombatStats");
            ScriptableObject weapon = ScriptableObject.CreateInstance(
                weaponDefinitionType);
            var player = new GameObject("Player");

            try
            {
                FieldInfo damageField = weaponDefinitionType.GetField(
                    "Damage");
                damageField.SetValue(weapon, 10f);
                Component stats = player.AddComponent(statsType);
                statsType.GetMethod("SetWeaponDamageMultiplier").Invoke(
                    stats,
                    new object[] { 1.25f });

                Assert.That(
                    statsType.GetMethod("ApplyWeaponDamage").Invoke(
                        stats,
                        new object[] { 10f }),
                    Is.EqualTo(12.5f));
                Assert.That(damageField.GetValue(weapon), Is.EqualTo(10f));
                statsType.GetMethod("ResetRuntimeModifiers")
                    .Invoke(stats, null);
                Assert.That(
                    statsType.GetProperty("WeaponDamageMultiplier")
                        .GetValue(stats),
                    Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(weapon);
            }
        }

        [UnityTest]
        public IEnumerator LevelUpBuildsThreeCardsAndLocksGameplay()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            RuntimeContext context = FindRuntimeContext();
            Assert.That(context.UpgradeController, Is.Not.Null);
            Type progressionType = RuntimeTypeResolver.GetType(
                "PlayerRunProgression");
            Component progression = context.UpgradeController
                .GetComponent(progressionType);
            progressionType.GetMethod("ConfigureThresholds").Invoke(
                progression,
                new object[] { new[] { 1, 100 } });
            Type directorType = RuntimeTypeResolver.GetType(
                "WaveDirector");
            Component director = (Component)UnityEngine.Object
                .FindAnyObjectByType(directorType);
            Component enemy = null;
            float deadline = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < deadline && enemy == null)
            {
                director ??= (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                enemy = director != null
                    ? GetFirstActiveEnemy(directorType, director)
                    : null;

                if (enemy == null)
                {
                    yield return null;
                }
            }

            Assert.That(enemy, Is.Not.Null);
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Component health = enemy.GetComponent(healthType);
            float maxHealth = (float)healthType.GetProperty("MaxHealth")
                .GetValue(health);
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            object lethal = Activator.CreateInstance(
                damageType,
                maxHealth + 100f,
                enemy.transform.position,
                Vector3.forward,
                context.UpgradeController.gameObject);
            healthType.GetMethod("ApplyDamage")
                .Invoke(health, new[] { lethal });
            yield return null;

            Assert.That(
                context.UpgradeType.GetProperty("IsChoiceOpen")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(true));
            Transform cards = context.Hud.transform.Find(
                "ModalLayer/UpgradeChoicePanel/UpgradeCards");
            Assert.That(cards.childCount, Is.EqualTo(3));
            Transform firstCard = cards.Find("UpgradeCard1");
            Assert.That(firstCard.Find("Icon"), Is.Not.Null);
            Assert.That(firstCard.Find("Rarity"), Is.Not.Null);
            Assert.That(firstCard.Find("Description"), Is.Not.Null);
            Assert.That(firstCard.Find("Stack"), Is.Not.Null);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(
                context.CoordinatorType.GetProperty("IsLocked")
                    .GetValue(context.Coordinator),
                Is.EqualTo(true));
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator SceneReloadClearsSelectionsAndRuntimeDamage()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            RuntimeContext context = FindRuntimeContext();
            context.UpgradeType.GetMethod("QueueUpgradeChoices").Invoke(
                context.UpgradeController,
                new object[] { 1 });
            yield return null;
            ExecuteEvents.Execute(
                EventSystem.current.currentSelectedGameObject,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;
            Assert.That(
                context.UpgradeType.GetProperty("SelectedUpgradeCount")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(1));

            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            RuntimeContext fresh = FindRuntimeContext();
            Assert.That(
                fresh.UpgradeType.GetProperty("SelectedUpgradeCount")
                    .GetValue(fresh.UpgradeController),
                Is.EqualTo(0));
            Assert.That(
                fresh.UpgradeType.GetProperty("WeaponDamageMultiplier")
                    .GetValue(fresh.UpgradeController),
                Is.EqualTo(1f));
            Assert.That(
                fresh.WeaponType.GetProperty("Damage").GetValue(fresh.Weapon),
                Is.EqualTo(fresh.WeaponType.GetProperty("BaseDamage")
                    .GetValue(fresh.Weapon)));
            Assert.That(
                fresh.UpgradeType.GetProperty("IsChoiceOpen")
                    .GetValue(fresh.UpgradeController),
                Is.EqualTo(false));
        }

        [UnityTest]
        public IEnumerator SubmitAppliesDamageOnceAndRestoresGameplay()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            RuntimeContext context = FindRuntimeContext();
            Type definitionType = RuntimeTypeResolver.GetType(
                "UpgradeDefinition");
            Type rarityType = RuntimeTypeResolver.GetType(
                "UpgradeRarity");
            Type effectType = RuntimeTypeResolver.GetType(
                "UpgradeEffectType");
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);
            ConfigureDefinition(
                definitionType,
                rarityType,
                effectType,
                definition,
                "submit_damage_once",
                1);
            context.UpgradeType.GetMethod("ConfigureRun").Invoke(
                context.UpgradeController,
                new object[]
                {
                    18018,
                    CreateDefinitionList(definitionType, definition)
                });
            float assetDamage = (float)context.WeaponType
                .GetProperty("BaseDamage")
                .GetValue(context.Weapon);
            context.UpgradeType.GetMethod("QueueUpgradeChoices").Invoke(
                context.UpgradeController,
                new object[] { 1 });
            yield return null;
            GameObject selected = EventSystem.current
                .currentSelectedGameObject;
            ExecuteEvents.Execute(
                selected,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;

            Assert.That(
                context.UpgradeType.GetProperty("SelectedUpgradeCount")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(1));
            Assert.That(
                context.WeaponType.GetProperty("Damage")
                    .GetValue(context.Weapon),
                Is.GreaterThan(assetDamage));
            Assert.That(
                context.WeaponType.GetProperty("BaseDamage")
                    .GetValue(context.Weapon),
                Is.EqualTo(assetDamage));
            Assert.That(
                context.UpgradeType.GetProperty("IsChoiceOpen")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(false));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));

            ExecuteEvents.Execute(
                selected,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            Assert.That(
                context.UpgradeType.GetProperty("SelectedUpgradeCount")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(definition);
        }

        [UnityTest]
        public IEnumerator OuterLockSurvivesUpgradeSelection()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            RuntimeContext context = FindRuntimeContext();
            Type reasonType = RuntimeTypeResolver.GetType(
                "GameplayLockReason");
            IDisposable outer = (IDisposable)context.CoordinatorType
                .GetMethod("Acquire")
                .Invoke(
                    context.Coordinator,
                    new[] { Enum.Parse(reasonType, "Inventory") });
            context.UpgradeType.GetMethod("QueueUpgradeChoices").Invoke(
                context.UpgradeController,
                new object[] { 1 });
            yield return null;
            ExecuteEvents.Execute(
                EventSystem.current.currentSelectedGameObject,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;

            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(
                context.CoordinatorType.GetProperty("IsLocked")
                    .GetValue(context.Coordinator),
                Is.EqualTo(true));
            outer.Dispose();
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator MultipleLevelsQueueOneChoicePerLevel()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadRuntime();
            RuntimeContext context = FindRuntimeContext();
            context.UpgradeType.GetMethod("QueueUpgradeChoices").Invoke(
                context.UpgradeController,
                new object[] { 2 });
            yield return null;
            ExecuteEvents.Execute(
                EventSystem.current.currentSelectedGameObject,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;

            Assert.That(
                context.UpgradeType.GetProperty("IsChoiceOpen")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(true));
            Assert.That(Time.timeScale, Is.Zero);
            ExecuteEvents.Execute(
                EventSystem.current.currentSelectedGameObject,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;
            Assert.That(
                context.UpgradeType.GetProperty("SelectedUpgradeCount")
                    .GetValue(context.UpgradeController),
                Is.EqualTo(2));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
        }

        private static IEnumerator LoadRuntime()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }
        }

        private static RuntimeContext FindRuntimeContext()
        {
            Type upgradeType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Type coordinatorType = RuntimeTypeResolver.GetType(
                "GameplayLockCoordinator");
            Type hudType = RuntimeTypeResolver.GetType(
                "UnifiedGameHud");
            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            Type weaponType = RuntimeTypeResolver.GetType(
                "WeaponController");
            Component upgrade = (Component)UnityEngine.Object
                .FindAnyObjectByType(upgradeType);
            Component coordinator = upgrade.GetComponent(coordinatorType);
            Component hud = (Component)UnityEngine.Object
                .FindAnyObjectByType(hudType);
            Component combat = upgrade.GetComponent(combatType);
            Component weapon = (Component)combatType
                .GetProperty("EquippedWeapon")
                .GetValue(combat);
            return new RuntimeContext(
                upgradeType,
                upgrade,
                coordinatorType,
                coordinator,
                hud,
                weaponType,
                weapon);
        }

        private static object Generate(
            Type generatorType,
            object generator,
            object definitions,
            object state)
        {
            return generatorType.GetMethod("Generate").Invoke(
                generator,
                new[] { definitions, state, 3 });
        }

        private static IList GetCandidates(object result)
        {
            return (IList)result.GetType().GetProperty("Candidates")
                .GetValue(result);
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

        private static string[] CandidateIds(IList candidates)
        {
            var ids = new string[candidates.Count];

            for (int index = 0; index < candidates.Count; index++)
            {
                ids[index] = (string)candidates[index].GetType()
                    .GetProperty("StableId")
                    .GetValue(candidates[index]);
            }

            return ids;
        }

        private static void ConfigureDefinition(
            Type definitionType,
            Type rarityType,
            Type effectType,
            ScriptableObject definition,
            string id,
            int maxLevel)
        {
            definitionType.GetMethod("Configure").Invoke(
                definition,
                new object[]
                {
                    id,
                    id,
                    "Damage boost",
                    null,
                    Enum.Parse(rarityType, "Common"),
                    maxLevel,
                    Enum.Parse(effectType, "WeaponDamage"),
                    0.25f
                });
        }

        private static object CreateDefinitionList(
            Type definitionType,
            params ScriptableObject[] definitions)
        {
            IList list = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(definitionType));

            foreach (ScriptableObject definition in definitions)
            {
                list.Add(definition);
            }

            return list;
        }

        private readonly struct RuntimeContext
        {
            public RuntimeContext(
                Type upgradeType,
                Component upgradeController,
                Type coordinatorType,
                Component coordinator,
                Component hud,
                Type weaponType,
                Component weapon)
            {
                UpgradeType = upgradeType;
                UpgradeController = upgradeController;
                CoordinatorType = coordinatorType;
                Coordinator = coordinator;
                Hud = hud;
                WeaponType = weaponType;
                Weapon = weapon;
            }

            public Type UpgradeType { get; }
            public Component UpgradeController { get; }
            public Type CoordinatorType { get; }
            public Component Coordinator { get; }
            public Component Hud { get; }
            public Type WeaponType { get; }
            public Component Weapon { get; }
        }
    }
}
