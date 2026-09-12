using System.Collections;
using System.Linq;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class Issue40SupportLifecycleTests
{
    private const string CityNewScene =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

    [SetUp]
    public void IgnoreHeadlessTmpImporterErrors()
    {
        LogAssert.ignoreFailingMessages = true;
    }

    [TearDown]
    public void RestoreLogState()
    {
        LogAssert.ignoreFailingMessages = false;
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator SupportPulseExcludesSelfAndRevokesOnDeath()
    {
        GameObject sourceObject = new("Support Source");
        GameObject targetObject = new("Support Target");
        targetObject.transform.position = Vector3.forward * 2f;
        EnemyController source =
            sourceObject.AddComponent<EnemyController>();
        EnemyController target =
            targetObject.AddComponent<EnemyController>();
        EnemyAbilitySetDefinition set = CreateAbilitySet(
            out EnemySupportAuraAbilityDefinition aura,
            out GameplayEffectDefinition effect);
        yield return null;

        try
        {
            Assert.That(source.ApplyAbilitySet(set, null), Is.True);
            Assert.That(source.AbilityController.PulseSupportNow(),
                Is.EqualTo(1));
            Assert.That(source.SupportEffects.ActiveSourceCount, Is.Zero);
            Assert.That(target.SupportEffects.ActiveSourceCount,
                Is.EqualTo(1));
            Assert.That(target.AttackDamage,
                Is.EqualTo(25f).Within(0.001f));

            source.GetComponent<Health>().ApplyDamage(
                new DamageInfo(
                    999f,
                    sourceObject.transform.position,
                    Vector3.zero,
                    targetObject));
            Assert.That(target.SupportEffects.ActiveSourceCount, Is.Zero);
            Assert.That(target.AttackDamage,
                Is.EqualTo(20f).Within(0.001f));
        }
        finally
        {
            Object.Destroy(set);
            Object.Destroy(aura);
            Object.Destroy(effect);
            Object.Destroy(targetObject);
            Object.Destroy(sourceObject);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator CityNewSpawnsSupportFromExistingPool()
    {
        yield return SceneManager.LoadSceneAsync(
            CityNewScene,
            LoadSceneMode.Single);
        float deadline = Time.realtimeSinceStartup + 25f;
        EnemySpawnHandle supportHandle = default;

        while (Time.realtimeSinceStartup < deadline)
        {
            WaveDirector director = WaveDirector.Active;

            if (director != null)
            {
                foreach (EnemySpawnHandle handle in
                         director.ActiveEnemies.Values)
                {
                    if (handle.EnemyTypeId == "spider_support")
                    {
                        supportHandle = handle;
                        break;
                    }
                }
            }

            if (supportHandle.IsValid)
            {
                break;
            }

            yield return null;
        }

        Assert.That(supportHandle.IsValid, Is.True,
            "第一波应生成可识别的支援型敌人。");
        PooledEnemyFactory pool =
            Object.FindAnyObjectByType<PooledEnemyFactory>();
        Assert.That(pool, Is.Not.Null);
        int expectedPrewarm = CityNewContentCatalog.LoadDefault().EnemyArchetypes
            .Select(archetype => archetype.TemplateAddress).Distinct().Count() * 4;
        Assert.That(pool.PooledObjectCount, Is.EqualTo(expectedPrewarm));
        Assert.That(pool.ExpansionCount, Is.Zero);
        Assert.That(
            supportHandle.Controller.AbilityController.IsSupport,
            Is.True);
        Assert.That(
            supportHandle.Controller.AbilityController.ActiveSet.StableId,
            Is.EqualTo("enemy.role.spider_support"));
        Assert.That(
            supportHandle.Controller
                .GetComponent<EnemyBurnEffectController>()
                .StatusText,
            Does.Contain("SUPPORT"));
    }

    private static EnemyAbilitySetDefinition CreateAbilitySet(
        out EnemySupportAuraAbilityDefinition aura,
        out GameplayEffectDefinition effect)
    {
        effect = ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        effect.Configure(
            "enemy.buff.test_support",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyAttackDamage,
                GameplayModifierOperation.Multiply,
                0.25f));
        aura = ScriptableObject.CreateInstance<
            EnemySupportAuraAbilityDefinition>();
        aura.Configure(
            "enemy.ability.test_support",
            10f,
            2,
            3f,
            30f,
            EnemySupportTargetPriority.LowestHealthRatio,
            "enemy",
            effect);
        EnemyAbilitySetDefinition set =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();
        set.Configure(
            "enemy.role.test_support",
            "SUPPORT",
            Color.green,
            new EnemyAbilityDefinition[] { aura });
        return set;
    }
}
