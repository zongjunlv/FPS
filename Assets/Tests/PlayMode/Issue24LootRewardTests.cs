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
    public sealed class Issue24LootRewardTests
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
        public void TableSelectsByEnemyTypeWaveAndRewardTier()
        {
            LootFixture fixture = CreateFixture();

            try
            {
                object normal = fixture.TableType.GetMethod("ResolveRule")
                    .Invoke(fixture.Table, new[]
                    {
                        "other_enemy",
                        (object)2,
                        Enum.Parse(fixture.TierType, "Normal")
                    });
                object elite = fixture.TableType.GetMethod("ResolveRule")
                    .Invoke(fixture.Table, new[]
                    {
                        "spider_bot",
                        (object)2,
                        Enum.Parse(fixture.TierType, "Elite")
                    });
                object missing = fixture.TableType.GetMethod("ResolveRule")
                    .Invoke(fixture.Table, new[]
                    {
                        "other_enemy",
                        (object)2,
                        Enum.Parse(fixture.TierType, "Elite")
                    });

                Assert.That(normal, Is.Not.Null);
                Assert.That(elite, Is.Not.Null);
                Assert.That(elite.GetType().GetProperty("EnemyTypeId")
                    .GetValue(elite), Is.EqualTo("spider_bot"));
                Assert.That(missing, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Table);
            }
        }

        [Test]
        public void SameSeedAndContextAreStableAndDoNotTouchUnityRandom()
        {
            LootFixture fixture = CreateFixture();

            try
            {
                Type resolverType = RuntimeTypeResolver.GetType(
                    "DeterministicLootResolver");
                Type contextType = RuntimeTypeResolver.GetType(
                    "LootRewardContext");
                object first = Activator.CreateInstance(resolverType, 24024);
                object second = Activator.CreateInstance(resolverType, 24024);
                UnityEngine.Random.InitState(9917);
                string randomBefore = JsonUtility.ToJson(
                    UnityEngine.Random.state);
                var firstResults = new List<string>();
                var secondResults = new List<string>();

                for (int spawnId = 1; spawnId <= 24; spawnId++)
                {
                    object context = Activator.CreateInstance(
                        contextType,
                        "spider_bot",
                        2,
                        Enum.Parse(fixture.TierType, "Normal"),
                        spawnId);
                    firstResults.Add(DescribeDrops(
                        resolverType.GetMethod("Resolve").Invoke(
                            first,
                            new[] { fixture.Table, context })));
                }

                for (int spawnId = 24; spawnId >= 1; spawnId--)
                {
                    object context = Activator.CreateInstance(
                        contextType,
                        "spider_bot",
                        2,
                        Enum.Parse(fixture.TierType, "Normal"),
                        spawnId);
                    secondResults.Insert(0, DescribeDrops(
                        resolverType.GetMethod("Resolve").Invoke(
                            second,
                            new[] { fixture.Table, context })));
                }

                Assert.That(secondResults, Is.EqualTo(firstResults),
                    "Per-spawn loot must not depend on death order.");
                Assert.That(
                    JsonUtility.ToJson(UnityEngine.Random.state),
                    Is.EqualTo(randomBefore),
                    "Loot resolution must use its own random stream.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Table);
            }
        }

        [Test]
        public void DuplicateDeathAndFinalWaveTimingAreIdempotent()
        {
            LootFixture fixture = CreateFixture();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerLootRewardController");
            Type deathType = RuntimeTypeResolver.GetType(
                "EnemyDeathEvent");
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            Type inventoryType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type definitionType = RuntimeTypeResolver.GetType(
                "ItemDefinition");
            GameObject player = new("Loot Reward Player");
            ScriptableObject medicalKit = CreateItem(
                definitionType, "medical_kit", "医疗包");

            try
            {
                Component inventory = player.AddComponent(inventoryType);
                inventoryType.GetMethod("RegisterItem")
                    .Invoke(inventory, new object[] { medicalKit });
                Component controller = player.AddComponent(controllerType);
                controllerType.GetMethod("ConfigureForTesting").Invoke(
                    controller,
                    new object[] { fixture.Table, 24024, 3 });
                object death = Activator.CreateInstance(
                    deathType,
                    null,
                    77,
                    3,
                    Activator.CreateInstance(damageType),
                    40,
                    new Vector3(10000f, 500f, 10000f),
                    "spider_bot",
                    Enum.Parse(fixture.TierType, "Elite"));

                Assert.That(controllerType.GetMethod("ProcessEnemyDeath")
                    .Invoke(controller, new[] { death }), Is.EqualTo(true));
                Assert.That(controllerType.GetMethod("ProcessEnemyDeath")
                    .Invoke(controller, new[] { death }), Is.EqualTo(false));
                Assert.That(controllerType.GetProperty("EnemySettlementCount")
                    .GetValue(controller), Is.EqualTo(1));
                Assert.That(controllerType.GetProperty("FinalRewardCount")
                    .GetValue(controller), Is.EqualTo(0),
                    "A death in the final wave must not grant completion loot.");
                Assert.That(controllerType.GetMethod("ProcessWaveEnded")
                    .Invoke(controller, new object[] { 3 }), Is.EqualTo(false));
                Assert.That(controllerType.GetProperty("FinalRewardCount")
                    .GetValue(controller), Is.EqualTo(0));
                Assert.That(controllerType.GetMethod("ProcessRunCompleted")
                    .Invoke(controller, null), Is.EqualTo(true));
                Assert.That(controllerType.GetMethod("ProcessRunCompleted")
                    .Invoke(controller, null), Is.EqualTo(false));
                Assert.That(controllerType.GetProperty("FinalRewardCount")
                    .GetValue(controller), Is.EqualTo(0),
                    "An unreachable reward must remain pending instead of being reported as granted.");
                Assert.That(controllerType.GetProperty("PendingRewardCount")
                    .GetValue(controller), Is.GreaterThan(0));
                controllerType.GetMethod("RetryPendingRewards")
                    .Invoke(controller, null);
                Assert.That(controllerType.GetProperty("FinalRewardCount")
                    .GetValue(controller), Is.EqualTo(0),
                    "Retrying an invalid location must not duplicate or falsely settle the reward.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(medicalKit);
                UnityEngine.Object.DestroyImmediate(fixture.Table);
            }
        }

        [Test]
        public void FullInventoryLeavesWorldItemAndShowsClearFeedback()
        {
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type definitionType = RuntimeTypeResolver.GetType(
                "ItemDefinition");
            Type pickupType = RuntimeTypeResolver.GetType(
                "WorldItemPickup");
            ScriptableObject occupied = CreateItem(
                definitionType, "occupied", "占位物品");
            ScriptableObject reward = CreateItem(
                definitionType, "reward", "奖励物资");
            GameObject player = new("Full Inventory Player");
            GameObject worldItem = new("Reward Pickup");

            try
            {
                Component controller = player.AddComponent(controllerType);
                controllerType.GetMethod("ConfigureInventory").Invoke(
                    controller, new object[] { 1 });
                Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                    controller, new object[] { occupied, 1 }), Is.EqualTo(true));
                Component pickup = worldItem.AddComponent(pickupType);
                pickupType.GetMethod("Configure").Invoke(
                    pickup, new object[] { reward, 2 });
                Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                    pickup, new object[] { player }), Is.EqualTo(true));
                pickupType.GetMethod("Advance").Invoke(
                    pickup, new object[] { player, 0.1f });
                object view = pickupType.GetProperty("View").GetValue(pickup);

                Assert.That(pickupType.GetProperty("RemainingQuantity")
                    .GetValue(pickup), Is.EqualTo(2));
                Assert.That(pickupType.GetProperty("IsClaimed")
                    .GetValue(pickup), Is.EqualTo(false));
                Assert.That(view.GetType().GetProperty("Prompt")
                    .GetValue(view).ToString(), Does.Contain("背包空间不足"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldItem);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(occupied);
                UnityEngine.Object.DestroyImmediate(reward);
            }
        }

        [UnityTest]
        public IEnumerator CityNewConfiguresEliteDropsAndRewardHud()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = RuntimeTypeResolver.GetType(
                "WaveDirector");
            Type rewardsType = RuntimeTypeResolver.GetType(
                "PlayerLootRewardController");
            Type hudType = RuntimeTypeResolver.GetType(
                "UnifiedGameHud");
            Component director = null;
            Component rewards = null;
            Component hud = null;
            float deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                director = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                rewards = (Component)UnityEngine.Object
                    .FindAnyObjectByType(rewardsType);
                hud = (Component)UnityEngine.Object
                    .FindAnyObjectByType(hudType);

                if (director != null && rewards != null && hud != null &&
                    GetActiveEnemyCount(directorType, director) > 0)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(rewards, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            Assert.That(rewardsType.GetProperty("RunSeed").GetValue(rewards),
                Is.EqualTo(18018));
            Assert.That(rewardsType.GetProperty("EnemySettlementCount")
                .GetValue(rewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("WaveRewardCount")
                .GetValue(rewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("FinalRewardCount")
                .GetValue(rewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("SpawnedStackCount")
                .GetValue(rewards), Is.EqualTo(0),
                "A fresh scene must not retain rewards from the previous run.");
        }

        [UnityTest]
        public IEnumerator FirstWaveProducesEnemyAndWaveRewardsOnReachableGround()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type directorType = RuntimeTypeResolver.GetType(
                "WaveDirector");
            Type rewardsType = RuntimeTypeResolver.GetType(
                "PlayerLootRewardController");
            Type factoryType = RuntimeTypeResolver.GetType(
                "WorldItemFactory");
            Type pickupType = RuntimeTypeResolver.GetType(
                "WorldItemPickup");
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            Type hudType = RuntimeTypeResolver.GetType(
                "UnifiedGameHud");
            Type upgradeType = RuntimeTypeResolver.GetType(
                "PlayerUpgradeController");
            Component director = null;
            Component rewards = null;
            Component hud = null;
            GameObject player = null;
            float deadline = Time.realtimeSinceStartup + 30f;

            while (Time.realtimeSinceStartup < deadline)
            {
                director = (Component)UnityEngine.Object
                    .FindAnyObjectByType(directorType);
                rewards = (Component)UnityEngine.Object
                    .FindAnyObjectByType(rewardsType);
                hud = (Component)UnityEngine.Object
                    .FindAnyObjectByType(hudType);
                player = GameObject.FindGameObjectWithTag("Player");

                if (director != null && rewards != null && hud != null &&
                    player != null &&
                    GetActiveEnemyCount(directorType, director) > 0)
                {
                    break;
                }

                yield return null;
            }

            Behaviour upgrades = player.GetComponent(upgradeType) as Behaviour;

            if (upgrades != null)
            {
                upgrades.enabled = false;
            }

            while (Time.realtimeSinceStartup < deadline &&
                   (int)rewardsType.GetProperty("WaveRewardCount")
                       .GetValue(rewards) < 1)
            {
                var activeEnemies = new List<Component>();
                IEnumerable active = (IEnumerable)directorType
                    .GetProperty("ActiveEnemies").GetValue(director);

                foreach (object pair in active)
                {
                    object handle = pair.GetType().GetProperty("Value")
                        .GetValue(pair);
                    Component enemy = (Component)handle.GetType()
                        .GetProperty("Controller").GetValue(handle);

                    if (enemy != null)
                    {
                        activeEnemies.Add(enemy);
                    }
                }

                for (int index = 0; index < activeEnemies.Count; index++)
                {
                    Component health = activeEnemies[index]
                        .GetComponent(healthType);
                    float maximum = (float)healthType.GetProperty("MaxHealth")
                        .GetValue(health);
                    object lethal = Activator.CreateInstance(
                        damageType,
                        maximum + 100f,
                        activeEnemies[index].transform.position,
                        Vector3.forward,
                        player);
                    healthType.GetMethod("ApplyDamage").Invoke(
                        health,
                        new[] { lethal });
                }

                yield return null;
            }

            Component factory = player.GetComponent(factoryType);
            Component lastPickup = (Component)factoryType
                .GetProperty("LastSpawnedPickup").GetValue(factory);
            Assert.That(rewardsType.GetProperty("EnemySettlementCount")
                .GetValue(rewards), Is.EqualTo(4));
            Assert.That(rewardsType.GetProperty("WaveRewardCount")
                .GetValue(rewards), Is.EqualTo(1));
            Assert.That(rewardsType.GetProperty("FinalRewardCount")
                .GetValue(rewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("SpawnedStackCount")
                .GetValue(rewards), Is.GreaterThan(0));
            Assert.That(lastPickup, Is.Not.Null);
            Assert.That(pickupType.GetProperty("Source").GetValue(lastPickup)
                .ToString(), Is.EqualTo("EnemyDrop"));
            Assert.That(hudType.GetProperty("RewardCueCount").GetValue(hud),
                Is.GreaterThanOrEqualTo(2),
                "Elite and wave clear rewards must both publish HUD feedback.");
            Assert.That(hudType.GetProperty("RewardCueText").GetValue(hud)
                .ToString(), Does.Contain("精英奖励"),
                "The elite notice must remain visible while the wave notice waits in queue.");
        }

        [UnityTest]
        public IEnumerator MissionRestartClearsRewardStateAndWorldDrops()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            Type rewardsType = RuntimeTypeResolver.GetType(
                "PlayerLootRewardController");
            Type factoryType = RuntimeTypeResolver.GetType(
                "WorldItemFactory");
            Type missionType = RuntimeTypeResolver.GetType(
                "CityNewMissionController");
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            Component rewards = null;
            Component mission = null;
            GameObject player = null;
            float deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                rewards = (Component)UnityEngine.Object
                    .FindAnyObjectByType(rewardsType);
                mission = (Component)UnityEngine.Object
                    .FindAnyObjectByType(missionType);
                player = GameObject.FindGameObjectWithTag("Player");

                if (rewards != null && mission != null && player != null)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(rewards, Is.Not.Null);
            Assert.That(mission, Is.Not.Null);
            Assert.That(rewardsType.GetMethod("ProcessWaveEnded")
                .Invoke(rewards, new object[] { 1 }), Is.EqualTo(true));

            while (Time.realtimeSinceStartup < deadline &&
                   (int)rewardsType.GetProperty("WaveRewardCount")
                       .GetValue(rewards) == 0)
            {
                yield return null;
            }

            Component oldFactory = player.GetComponent(factoryType);
            Assert.That(rewardsType.GetProperty("WaveRewardCount")
                .GetValue(rewards), Is.EqualTo(1));
            Assert.That(factoryType.GetProperty("ActivePickupCount")
                .GetValue(oldFactory), Is.GreaterThan(0));
            var oldRewardsId = rewards.GetEntityId();
            Component playerHealth = player.GetComponent(healthType);
            float maxHealth = (float)healthType.GetProperty("MaxHealth")
                .GetValue(playerHealth);
            object lethal = Activator.CreateInstance(
                damageType,
                maxHealth + 100f,
                player.transform.position,
                Vector3.back,
                null);
            healthType.GetMethod("ApplyDamage")
                .Invoke(playerHealth, new[] { lethal });
            yield return null;
            Assert.That(missionType.GetMethod("RestartLevel")
                .Invoke(mission, null), Is.EqualTo(true));

            Component freshRewards = null;
            Component freshFactory = null;
            deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                GameObject freshPlayer =
                    GameObject.FindGameObjectWithTag("Player");
                freshRewards = (Component)UnityEngine.Object
                    .FindAnyObjectByType(rewardsType);
                freshFactory = freshPlayer != null
                    ? freshPlayer.GetComponent(factoryType)
                    : null;

                if (freshRewards != null && freshFactory != null &&
                    freshRewards.GetEntityId() != oldRewardsId)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(freshRewards, Is.Not.Null);
            Assert.That(freshRewards.GetEntityId(),
                Is.Not.EqualTo(oldRewardsId));
            Assert.That(rewardsType.GetProperty("EnemySettlementCount")
                .GetValue(freshRewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("WaveRewardCount")
                .GetValue(freshRewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("FinalRewardCount")
                .GetValue(freshRewards), Is.EqualTo(0));
            Assert.That(rewardsType.GetProperty("PendingRewardCount")
                .GetValue(freshRewards), Is.EqualTo(0));
            Assert.That(factoryType.GetProperty("ActivePickupCount")
                .GetValue(freshFactory), Is.EqualTo(0));
        }

        private static LootFixture CreateFixture()
        {
            Type tableType = RuntimeTypeResolver.GetType(
                "LootDropTableDefinition");
            Type ruleType = RuntimeTypeResolver.GetType(
                "LootDropRule");
            Type entryType = RuntimeTypeResolver.GetType(
                "LootDropEntry");
            Type tierType = RuntimeTypeResolver.GetType(
                "LootRewardTier");
            ScriptableObject table = ScriptableObject.CreateInstance(tableType);
            Array normalEntries = Array.CreateInstance(entryType, 2);
            normalEntries.SetValue(Activator.CreateInstance(
                entryType, "medical_kit", 2, 1, 2, 0.65f), 0);
            normalEntries.SetValue(Activator.CreateInstance(
                entryType, "rifle_ammo", 4, 1, 3, 1f), 1);
            Array eliteEntries = Array.CreateInstance(entryType, 1);
            eliteEntries.SetValue(Activator.CreateInstance(
                entryType, "armor_pack", 1, 2, 2, 1f), 0);
            Array finalEntries = Array.CreateInstance(entryType, 1);
            finalEntries.SetValue(Activator.CreateInstance(
                entryType, "medical_kit", 1, 3, 3, 1f), 0);
            Array rules = Array.CreateInstance(ruleType, 3);
            rules.SetValue(Activator.CreateInstance(
                ruleType,
                "*",
                1,
                3,
                Enum.Parse(tierType, "Normal"),
                4,
                6,
                normalEntries), 0);
            rules.SetValue(Activator.CreateInstance(
                ruleType,
                "spider_bot",
                2,
                3,
                Enum.Parse(tierType, "Elite"),
                1,
                1,
                eliteEntries), 1);
            rules.SetValue(Activator.CreateInstance(
                ruleType,
                "*",
                3,
                3,
                Enum.Parse(tierType, "FinalWave"),
                1,
                1,
                finalEntries), 2);
            tableType.GetMethod("Configure").Invoke(table, new object[] { rules });
            return new LootFixture(tableType, tierType, table);
        }

        private static ScriptableObject CreateItem(
            Type definitionType,
            string stableId,
            string displayName)
        {
            Type itemType = RuntimeTypeResolver.GetType("ItemType");
            Type effectType = RuntimeTypeResolver.GetType(
                "ItemEffectType");
            ScriptableObject item = ScriptableObject.CreateInstance(
                definitionType);
            definitionType.GetMethod("Configure").Invoke(
                item,
                new object[]
                {
                    stableId,
                    displayName,
                    "测试物品",
                    null,
                    Enum.Parse(itemType, "Consumable"),
                    1,
                    Enum.Parse(effectType, "RestoreHealth"),
                    10f
                });
            return item;
        }

        private static string DescribeDrops(object drops)
        {
            var descriptions = new List<string>();

            foreach (object drop in (IEnumerable)drops)
            {
                Type type = drop.GetType();
                descriptions.Add(
                    type.GetProperty("ItemStableId").GetValue(drop) + ":" +
                    type.GetProperty("Quantity").GetValue(drop));
            }

            return string.Join("|", descriptions);
        }

        private static int GetActiveEnemyCount(Type directorType, object director)
        {
            object active = directorType.GetProperty("ActiveEnemies")
                .GetValue(director);
            return (int)active.GetType().GetProperty("Count").GetValue(active);
        }

        private readonly struct LootFixture
        {
            public LootFixture(
                Type tableType,
                Type tierType,
                ScriptableObject table)
            {
                TableType = tableType;
                TierType = tierType;
                Table = table;
            }

            public Type TableType { get; }
            public Type TierType { get; }
            public ScriptableObject Table { get; }
        }
    }
}
