using FPS.Simulation;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue63SharedCombatRulesTests
{
    [Test]
    public void ArmorAbsorbsDamageBeforeHealth()
    {
        CombatDamageResolution resolution = CombatDamageRules.Apply(
            100f,
            25f,
            40f);

        Assert.That(resolution.RemainingArmor, Is.EqualTo(0f));
        Assert.That(resolution.RemainingHealth, Is.EqualTo(85f));
        Assert.That(resolution.AppliedDamage, Is.EqualTo(40f));
        Assert.That(resolution.WasKilled, Is.False);
    }

    [Test]
    public void RuntimeHealthUsesAuthoritativeDamageRule()
    {
        var gameObject = new GameObject("Issue63 Shared Damage Rule");
        try
        {
            Health health = gameObject.AddComponent<Health>();
            health.Initialize(100f, 25f);
            DamageResult result = health.ApplyDamage(new DamageInfo(
                40f,
                Vector3.zero,
                Vector3.forward,
                null));

            Assert.That(result.AppliedAmount, Is.EqualTo(40f));
            Assert.That(health.CurrentArmor, Is.EqualTo(0f));
            Assert.That(health.CurrentHealth, Is.EqualTo(85f));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }
}
