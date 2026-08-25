using System.Collections;
using System.Collections.Generic;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue36EliteAffixLifecycleTests
{
    private readonly List<Object> cleanup = new();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
            {
                Object.Destroy(cleanup[index]);
            }
        }

        cleanup.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ApplyAndPoolResetKeepEliteStateLeaseScoped()
    {
        GameObject enemyObject = Track(new GameObject("Elite Enemy"));
        enemyObject.AddComponent<Health>();
        EnemyController enemy = enemyObject.AddComponent<EnemyController>();
        EnemyAffixDefinition affix = CreateAffix();
        yield return null;

        enemy.ResetForSpawn(null);
        Assert.That(enemy.ApplyAffix(affix), Is.True);

        Health health = enemyObject.GetComponent<Health>();
        EnemyBurnEffectController overhead =
            enemyObject.GetComponent<EnemyBurnEffectController>();
        Assert.That(health.MaxArmor, Is.EqualTo(60f));
        Assert.That(health.CurrentArmor, Is.EqualTo(60f));
        Assert.That(enemy.AttackDamage, Is.EqualTo(30f));
        Assert.That(enemy.RewardExperience, Is.EqualTo(80));
        Assert.That(enemy.LootQuantityMultiplier, Is.EqualTo(1.5f));
        Assert.That(enemy.AffixController.ActiveEffectCount, Is.EqualTo(1));
        Assert.That(overhead.StatusText, Does.Contain("ELITE ARMOR"));

        health.ApplyDamage(new DamageInfo(
            20f,
            enemyObject.transform.position,
            Vector3.forward,
            null,
            DamageType.Projectile));
        Assert.That(health.CurrentArmor, Is.EqualTo(40f));
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));

        enemy.PrepareForPool();
        Assert.That(enemy.AffixController.HasAffix, Is.False);
        Assert.That(enemy.AffixController.ActiveEffectCount, Is.Zero);
        Assert.That(overhead.HasAffixStatus, Is.False);
        Assert.That(enemy.AttackDamage, Is.EqualTo(20f));
        Assert.That(enemy.RewardExperience, Is.EqualTo(40));
        Assert.That(enemy.LootQuantityMultiplier, Is.EqualTo(1f));

        enemy.ResetForSpawn(null);
        Assert.That(health.MaxArmor, Is.Zero);
        Assert.That(health.CurrentArmor, Is.Zero);
    }

    private EnemyAffixDefinition CreateAffix()
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
        EnemyAffixDefinition affix = Track(
            ScriptableObject.CreateInstance<EnemyAffixDefinition>());
        affix.Configure(
            "armored_elite",
            "ELITE ARMOR",
            new Color(1f, 0.72f, 0.12f, 1f),
            effect);
        return affix;
    }

    private T Track<T>(T instance) where T : Object
    {
        cleanup.Add(instance);
        return instance;
    }
}
