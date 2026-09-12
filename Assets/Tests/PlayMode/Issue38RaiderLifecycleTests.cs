using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class Issue38RaiderLifecycleTests
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
    public IEnumerator AbilityLeaseSelectsFlankAndPoolResetClearsState()
    {
        GameObject enemyObject = new("Raider Lease Test");
        GameObject targetObject = new("Target");
        targetObject.transform.position = Vector3.forward * 12f;
        enemyObject.AddComponent<Health>();
        EnemyNavigationController navigation =
            enemyObject.AddComponent<EnemyNavigationController>();
        enemyObject.AddComponent<EnemyBurnEffectController>();
        EnemyAbilityController abilities =
            enemyObject.AddComponent<EnemyAbilityController>();
        EnemyAbilitySetDefinition set = CreateAbilitySet(
            out RaiderApproachAbilityDefinition raider);
        yield return null;

        try
        {
            Assert.That(
                abilities.ApplyAbilitySet(set, targetObject.transform),
                Is.True);
            Assert.That(abilities.TryResolveChaseDestination(
                targetObject.transform,
                true,
                12f,
                0.016f,
                out Vector3 flank),
                Is.True);
            Assert.That(abilities.Phase,
                Is.EqualTo(RaiderTacticsPhase.Flanking));
            Assert.That(abilities.HasFlankDestination, Is.True);
            Assert.That(Vector3.Distance(flank, targetObject.transform.position),
                Is.GreaterThan(2f));
            Vector3 targetToFlank =
                flank - targetObject.transform.position;
            Assert.That(
                Vector3.Dot(targetToFlank, targetObject.transform.forward),
                Is.LessThan(-0.5f),
                "侧翼点必须位于玩家侧后方，而不是侧前方。");
            Assert.That(
                Mathf.Abs(Vector3.Dot(
                    targetToFlank,
                    targetObject.transform.right)),
                Is.GreaterThan(2f));

            enemyObject.transform.position =
                targetObject.transform.position -
                targetObject.transform.forward * 3f;
            Assert.That(abilities.TryResolveChaseDestination(
                targetObject.transform,
                true,
                3f,
                0.016f,
                out Vector3 committedFlank),
                Is.True);
            Assert.That(abilities.Phase,
                Is.EqualTo(RaiderTacticsPhase.Flanking),
                "尚未抵达侧翼点时不能因接近玩家而提前转入冲锋。");
            Assert.That(committedFlank, Is.EqualTo(flank));

            navigation.SetDestination(flank);
            enemyObject.transform.position = flank;
            Assert.That(abilities.TryResolveChaseDestination(
                targetObject.transform,
                true,
                Vector3.Distance(flank, targetObject.transform.position),
                0.016f,
                out Vector3 chargeDestination),
                Is.True);
            Assert.That(abilities.Phase,
                Is.EqualTo(RaiderTacticsPhase.Charging));
            Assert.That(chargeDestination,
                Is.EqualTo(targetObject.transform.position));

            Assert.That(abilities.TryResolveChaseDestination(
                targetObject.transform,
                false,
                Vector3.Distance(flank, targetObject.transform.position),
                0.016f,
                out Vector3 committedChargeDestination),
                Is.True,
                "已开始的冲锋不能因转向时短暂丢失视野而中断。");
            Assert.That(committedChargeDestination,
                Is.EqualTo(targetObject.transform.position));
            Assert.That(navigation.SpeedMultiplier, Is.GreaterThan(1f));
            Assert.That(
                enemyObject.GetComponent<EnemyBurnEffectController>()
                    .HasRoleStatus,
                Is.True);

            abilities.ClearForPool();

            Assert.That(abilities.IsActive, Is.False);
            Assert.That(abilities.Phase,
                Is.EqualTo(RaiderTacticsPhase.Disabled));
            Assert.That(abilities.HasFlankDestination, Is.False);
            Assert.That(navigation.SpeedMultiplier, Is.EqualTo(1f));
            Assert.That(
                enemyObject.GetComponent<EnemyBurnEffectController>()
                    .HasRoleStatus,
                Is.False);
        }
        finally
        {
            Object.Destroy(set);
            Object.Destroy(raider);
            Object.Destroy(targetObject);
            Object.Destroy(enemyObject);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator NoProgressFallsBackInsteadOfStallingInFlank()
    {
        GameObject enemyObject = new("Blocked Raider Test");
        GameObject targetObject = new("Target");
        targetObject.transform.position = Vector3.forward * 12f;
        enemyObject.AddComponent<Health>();
        enemyObject.AddComponent<EnemyNavigationController>();
        enemyObject.AddComponent<EnemyBurnEffectController>();
        EnemyAbilityController abilities =
            enemyObject.AddComponent<EnemyAbilityController>();
        EnemyAbilitySetDefinition set = CreateAbilitySet(
            out RaiderApproachAbilityDefinition raider,
            0.1f);
        yield return null;

        try
        {
            abilities.ApplyAbilitySet(set, targetObject.transform);
            Assert.That(abilities.TryResolveChaseDestination(
                targetObject.transform, true, 12f, 0f, out _), Is.True);

            Assert.That(abilities.TryResolveChaseDestination(
                targetObject.transform, true, 12f, 0.11f, out _), Is.False);
            Assert.That(abilities.Phase,
                Is.EqualTo(RaiderTacticsPhase.Regrouping));
            Assert.That(abilities.PathFailureCount, Is.EqualTo(1));
        }
        finally
        {
            Object.Destroy(set);
            Object.Destroy(raider);
            Object.Destroy(targetObject);
            Object.Destroy(enemyObject);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator CityNewSpawnsRaiderFromExistingPrewarmedPool()
    {
        yield return SceneManager.LoadSceneAsync(
            CityNewScene,
            LoadSceneMode.Single);
        float deadline = Time.realtimeSinceStartup + 25f;
        EnemySpawnHandle raiderHandle = default;

        while (Time.realtimeSinceStartup < deadline)
        {
            WaveDirector director = WaveDirector.Active;

            if (director != null)
            {
                foreach (EnemySpawnHandle handle in
                         director.ActiveEnemies.Values)
                {
                    if (handle.EnemyTypeId == "spider_raider")
                    {
                        raiderHandle = handle;
                        break;
                    }
                }
            }

            if (raiderHandle.IsValid)
            {
                break;
            }

            yield return null;
        }

        Assert.That(raiderHandle.IsValid, Is.True,
            "第一波应生成可识别的突袭型敌人。");
        PooledEnemyFactory pool =
            Object.FindAnyObjectByType<PooledEnemyFactory>();
        Assert.That(pool, Is.Not.Null);
        int expectedPrewarm = CityNewContentCatalog.LoadDefault().EnemyArchetypes
            .Select(archetype => archetype.TemplateAddress).Distinct().Count() * 4;
        Assert.That(pool.PooledObjectCount, Is.EqualTo(expectedPrewarm));
        Assert.That(pool.ExpansionCount, Is.Zero,
            "突袭职责应复用同一预热池，不应首次生成时临时实例化。");
        EnemyAbilityController abilities =
            raiderHandle.Controller.AbilityController;
        Assert.That(abilities.IsActive, Is.True);
        Assert.That(abilities.ActiveSet.StableId,
            Is.EqualTo("enemy.role.spider_raider"));
        Assert.That(
            raiderHandle.Controller
                .GetComponent<EnemyBurnEffectController>()
                .StatusText,
            Does.Contain("RAIDER"));
    }

    private static EnemyAbilitySetDefinition CreateAbilitySet(
        out RaiderApproachAbilityDefinition raider,
        float noProgressTimeout = 1f)
    {
        raider = ScriptableObject.CreateInstance<
            RaiderApproachAbilityDefinition>();
        raider.Configure(
            "enemy.ability.test_raider",
            4f, 1f, 2f, 5f, 1.25f, 1.75f,
            noProgressTimeout, 0.5f,
            2f, 0.2f, 0.8f, 0.75f);
        EnemyAbilitySetDefinition set =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();
        set.Configure(
            "enemy.role.test_raider",
            "RAIDER",
            Color.cyan,
            new EnemyAbilityDefinition[] { raider });
        return set;
    }
}
