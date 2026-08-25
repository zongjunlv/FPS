using System.Collections.Generic;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue36EliteAffixTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
            {
                Object.DestroyImmediate(cleanup[index]);
            }
        }

        cleanup.Clear();
    }

    [Test]
    public void ArmoredEliteEffectAggregatesAllRiskRewardAttributes()
    {
        GameplayEffectDefinition effect = CreateEffect();
        GameObject target = Track(new GameObject("Elite Target"));
        var runtime = new GameplayEffectRuntime(target);
        runtime.Apply(
            effect,
            new GameplayEffectContext("test", effect, target));

        Assert.That(runtime.Evaluate(
            GameplayAttributeId.EnemyMaximumArmor, 0f), Is.EqualTo(60f));
        Assert.That(runtime.Evaluate(
            GameplayAttributeId.EnemyAttackDamage, 20f), Is.EqualTo(30f));
        Assert.That(runtime.Evaluate(
            GameplayAttributeId.EnemyExperienceReward, 40f), Is.EqualTo(80f));
        Assert.That(runtime.Evaluate(
            GameplayAttributeId.EnemyLootQuantity, 1f), Is.EqualTo(1.5f));
    }

    [Test]
    public void SameWaveScheduleAlwaysPlacesEliteAtSameSpawnIndexes()
    {
        EnemyAffixDefinition affix = CreateAffix();
        WaveDefinition wave = Track(
            ScriptableObject.CreateInstance<WaveDefinition>());
        wave.Configure(
            8,
            4,
            0f,
            new[]
            {
                new WaveEnemyEntry(
                    null, 3, LootRewardTier.Normal, "spider_bot"),
                new WaveEnemyEntry(
                    null, 1, LootRewardTier.Elite, "spider_bot", affix)
            });

        for (int spawnIndex = 0; spawnIndex < 12; spawnIndex++)
        {
            bool expectedElite = spawnIndex % 4 == 3;
            WaveEnemyEntry first = wave.GetEntry(spawnIndex);
            WaveEnemyEntry second = wave.GetEntry(spawnIndex);
            Assert.That(first.RewardTier == LootRewardTier.Elite,
                Is.EqualTo(expectedElite));
            Assert.That(second.Affix, Is.SameAs(first.Affix));
        }
    }

    [Test]
    public void SameSeedResolvesSameScaledEliteReward()
    {
        LootDropTableDefinition table = Track(
            ScriptableObject.CreateInstance<LootDropTableDefinition>());
        table.Configure(new[]
        {
            new LootDropRule(
                "spider_bot",
                1,
                99,
                LootRewardTier.Elite,
                2,
                2,
                new[]
                {
                    new LootDropEntry("armor_pack", 1, 2, 2, 1f)
                })
        });
        var context = new LootRewardContext(
            "spider_bot", 2, LootRewardTier.Elite, 17, 1.5f);

        IReadOnlyList<LootDropStack> first =
            new DeterministicLootResolver(18018).Resolve(table, context);
        IReadOnlyList<LootDropStack> second =
            new DeterministicLootResolver(18018).Resolve(table, context);

        Assert.That(first.Count, Is.EqualTo(1));
        Assert.That(second.Count, Is.EqualTo(first.Count));
        Assert.That(first[0].ItemStableId, Is.EqualTo(second[0].ItemStableId));
        Assert.That(first[0].Quantity, Is.EqualTo(6));
        Assert.That(second[0].Quantity, Is.EqualTo(first[0].Quantity));
    }

    [Test]
    public void DeathEventPreservesEliteLootMultiplier()
    {
        var death = new EnemyDeathEvent(
            null,
            7,
            2,
            default,
            80,
            Vector3.one,
            "spider_bot",
            LootRewardTier.Elite,
            1.5f);

        Assert.That(death.RewardExperience, Is.EqualTo(80));
        Assert.That(death.LootQuantityMultiplier, Is.EqualTo(1.5f));
    }

    private EnemyAffixDefinition CreateAffix()
    {
        GameplayEffectDefinition effect = CreateEffect();
        EnemyAffixDefinition affix = Track(
            ScriptableObject.CreateInstance<EnemyAffixDefinition>());
        affix.Configure(
            "armored_elite",
            "ELITE ARMOR",
            Color.yellow,
            effect);
        return affix;
    }

    private GameplayEffectDefinition CreateEffect()
    {
        GameplayEffectDefinition effect = Track(
            ScriptableObject.CreateInstance<GameplayEffectDefinition>());
        effect.Configure(
            "enemy.affix.armored_elite",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyMaximumArmor,
                GameplayModifierOperation.Add,
                60f),
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyAttackDamage,
                GameplayModifierOperation.Multiply,
                0.5f),
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyExperienceReward,
                GameplayModifierOperation.Multiply,
                1f),
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyLootQuantity,
                GameplayModifierOperation.Multiply,
                0.5f));
        return effect;
    }

    private T Track<T>(T instance) where T : Object
    {
        cleanup.Add(instance);
        return instance;
    }
}
