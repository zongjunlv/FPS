using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class Issue39SuppressorLifecycleTests
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
    public IEnumerator TacticalLeaseRetreatsHoldsRelocatesAndResets()
    {
        GameObject enemyObject = new("Suppressor Lease Test");
        GameObject targetObject = new("Target");
        targetObject.transform.position = Vector3.forward * 2f;
        enemyObject.AddComponent<Health>();
        EnemyNavigationController navigation =
            enemyObject.AddComponent<EnemyNavigationController>();
        enemyObject.AddComponent<EnemyBurnEffectController>();
        EnemyAbilityController abilities =
            enemyObject.AddComponent<EnemyAbilityController>();
        EnemyAbilitySetDefinition set = CreateAbilitySet(
            out SuppressorRangedAbilityDefinition suppressor);
        yield return null;

        try
        {
            Assert.That(
                abilities.ApplyAbilitySet(set, targetObject.transform),
                Is.True);
            EnemyMovementDirective retreat = abilities.ResolveMovement(
                targetObject.transform,
                targetObject.transform.position,
                true,
                2f,
                0.016f);
            Assert.That(retreat.Kind,
                Is.EqualTo(EnemyMovementDirectiveKind.Move));
            Assert.That(abilities.SuppressorPhase,
                Is.EqualTo(SuppressorTacticsPhase.Retreating));
            Vector3 awayFromPlayer =
                enemyObject.transform.position -
                targetObject.transform.position;
            Vector3 retreatDirection =
                retreat.Destination - targetObject.transform.position;
            Assert.That(
                Vector3.Dot(
                    awayFromPlayer.normalized,
                    retreatDirection.normalized),
                Is.GreaterThan(0.9f),
                "玩家近身时目的地必须位于远离玩家的一侧。");

            navigation.SetDestination(retreat.Destination);
            enemyObject.transform.position = retreat.Destination;
            float tacticalDistance = Vector3.Distance(
                enemyObject.transform.position,
                targetObject.transform.position);
            EnemyMovementDirective hold = abilities.ResolveMovement(
                targetObject.transform,
                targetObject.transform.position,
                true,
                tacticalDistance,
                0.016f);
            Assert.That(hold.Kind,
                Is.EqualTo(EnemyMovementDirectiveKind.None),
                "进入战术射程后应停止换位并允许射击。");

            EnemyMovementDirective relocate = abilities.ResolveMovement(
                targetObject.transform,
                targetObject.transform.position,
                false,
                tacticalDistance,
                0.016f);
            Assert.That(relocate.Kind,
                Is.EqualTo(EnemyMovementDirectiveKind.Move));
            Assert.That(abilities.SuppressorPhase,
                Is.EqualTo(SuppressorTacticsPhase.Relocating));

            abilities.ClearForPool();
            Assert.That(abilities.IsActive, Is.False);
            Assert.That(abilities.SuppressorPhase,
                Is.EqualTo(SuppressorTacticsPhase.Disabled));
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
            Object.Destroy(suppressor);
            Object.Destroy(targetObject);
            Object.Destroy(enemyObject);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator RangedAttackUsesHitscanAttributionAndPooledTracer()
    {
        GameObject enemyObject = new("Suppressor Combat Test");
        GameObject targetObject = new("Target");
        targetObject.transform.position = Vector3.forward * 10f;
        Health targetHealth = targetObject.AddComponent<Health>();
        targetHealth.Initialize(100f);
        BoxCollider targetCollider =
            targetObject.AddComponent<BoxCollider>();
        targetCollider.center = Vector3.up * 0.7f;
        EnemyController enemy =
            enemyObject.AddComponent<EnemyController>();
        EnemyAbilitySetDefinition set = CreateAbilitySet(
            out SuppressorRangedAbilityDefinition suppressor,
            0.05f,
            0.4f);
        EnemyPerceptionController perception =
            enemyObject.GetComponent<EnemyPerceptionController>();
        perception.SetTarget(targetObject.transform);
        perception.Configure(20f, 180f, 100f, 0.5f, 4f);
        Assert.That(
            enemy.ApplyAbilitySet(set, targetObject.transform),
            Is.True);

        try
        {
            float deadline = Time.realtimeSinceStartup + 2f;

            while (!targetHealth.HasLastAppliedDamage &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(targetHealth.CurrentHealth, Is.LessThan(100f));
            Assert.That(targetHealth.LastAppliedDamage.Source,
                Is.SameAs(enemyObject));
            Assert.That(targetHealth.LastAppliedDamage.Type,
                Is.EqualTo(DamageType.Hitscan));
            Assert.That(
                Object.FindObjectsByType<ProjectileController>(
                    FindObjectsSortMode.None),
                Is.Empty,
                "压制型命中判定不应创建旧实体弹丸。");
            ShotTracerPool tracerPool =
                Object.FindAnyObjectByType<ShotTracerPool>();
            Assert.That(tracerPool, Is.Not.Null);
            Assert.That(tracerPool.Capacity, Is.EqualTo(16));
        }
        finally
        {
            Object.Destroy(set);
            Object.Destroy(suppressor);
            Object.Destroy(targetObject);
            Object.Destroy(enemyObject);
            ShotTracerPool tracerPool =
                Object.FindAnyObjectByType<ShotTracerPool>();

            if (tracerPool != null)
            {
                Object.Destroy(tracerPool.gameObject);
            }
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator CityNewSpawnsSuppressorFromExistingPrewarmedPool()
    {
        yield return SceneManager.LoadSceneAsync(
            CityNewScene,
            LoadSceneMode.Single);
        float deadline = Time.realtimeSinceStartup + 25f;
        EnemySpawnHandle suppressorHandle = default;

        while (Time.realtimeSinceStartup < deadline)
        {
            WaveDirector director = WaveDirector.Active;

            if (director != null)
            {
                foreach (EnemySpawnHandle handle in
                         director.ActiveEnemies.Values)
                {
                    if (handle.EnemyTypeId == "spider_suppressor")
                    {
                        suppressorHandle = handle;
                        break;
                    }
                }
            }

            if (suppressorHandle.IsValid)
            {
                break;
            }

            yield return null;
        }

        Assert.That(suppressorHandle.IsValid, Is.True,
            "第一波应生成可识别的压制型敌人。");
        PooledEnemyFactory pool =
            Object.FindAnyObjectByType<PooledEnemyFactory>();
        Assert.That(pool, Is.Not.Null);
        int expectedPrewarm = CityNewContentCatalog.LoadDefault().EnemyArchetypes
            .Select(archetype => archetype.TemplateAddress).Distinct().Count() * 4;
        Assert.That(pool.PooledObjectCount, Is.EqualTo(expectedPrewarm));
        Assert.That(pool.ExpansionCount, Is.Zero);
        EnemyAbilityController abilities =
            suppressorHandle.Controller.AbilityController;
        Assert.That(abilities.IsSuppressor, Is.True);
        Assert.That(abilities.AttackMode,
            Is.EqualTo(EnemyAttackMode.Hitscan));
        Assert.That(abilities.ActiveSet.StableId,
            Is.EqualTo("enemy.role.spider_suppressor"));
        Assert.That(
            suppressorHandle.Controller
                .GetComponent<EnemyBurnEffectController>()
                .StatusText,
            Does.Contain("SUPPRESSOR"));
    }

    private static EnemyAbilitySetDefinition CreateAbilitySet(
        out SuppressorRangedAbilityDefinition suppressor,
        float windup = 0.1f,
        float cooldown = 0.8f)
    {
        suppressor = ScriptableObject.CreateInstance<
            SuppressorRangedAbilityDefinition>();
        suppressor.Configure(
            "enemy.ability.test_suppressor",
            6f, 10f, 14f, 4f, 2f, 1.1f,
            1f, 0.25f, windup, cooldown, 0.65f, 280f);
        EnemyAbilitySetDefinition set =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();
        set.Configure(
            "enemy.role.test_suppressor",
            "SUPPRESSOR",
            new Color(1f, 0.28f, 0.08f, 1f),
            new EnemyAbilityDefinition[] { suppressor });
        return set;
    }
}
