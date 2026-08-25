using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue42AiLodLifecycleTests
{
    [SetUp]
    public void SetUp()
    {
        LogAssert.ignoreFailingMessages = true;
        EnemyAiLodController.SetGlobalEnabled(true);
    }

    [TearDown]
    public void TearDown()
    {
        LogAssert.ignoreFailingMessages = false;
        EnemyAiLodController.SetGlobalEnabled(true);
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator FarEnemySuspendsNavigationAndDamagePromotesImmediately()
    {
        GameObject target = new("LOD Target");
        GameObject enemyObject = new("LOD Enemy");
        target.transform.position = Vector3.forward * 80f;
        EnemyController enemy = enemyObject.AddComponent<EnemyController>();
        enemy.SetFactoryManaged(true);
        yield return null;

        try
        {
            EnemyPerceptionController perception =
                enemyObject.GetComponent<EnemyPerceptionController>();
            EnemyAiLodController lod =
                enemyObject.GetComponent<EnemyAiLodController>();
            EnemyNavigationController navigation =
                enemyObject.GetComponent<EnemyNavigationController>();
            perception.SetTarget(target.transform);
            lod.ResetForSpawn(target.transform);
            lod.Configure(12f, 30f, 2f);
            yield return null;

            Assert.That(lod.CurrentTier, Is.EqualTo(EnemyAiLodTier.Far));
            Assert.That(navigation.IsLodSuspended, Is.True);

            Health health = enemyObject.GetComponent<Health>();
            health.ApplyDamage(new DamageInfo(
                1f,
                enemyObject.transform.position,
                Vector3.forward,
                target));

            Assert.That(lod.CurrentTier, Is.EqualTo(EnemyAiLodTier.Near));
            Assert.That(navigation.IsLodSuspended, Is.False);

            health.ApplyDamage(new DamageInfo(
                9999f,
                enemyObject.transform.position,
                Vector3.forward,
                target));
            Assert.That(health.IsDead, Is.True,
                "远距离 LOD 不得跳过同步死亡结算。");
        }
        finally
        {
            Object.Destroy(enemyObject);
            Object.Destroy(target);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator PoolResetClearsTierHistoryAndOldTarget()
    {
        GameObject firstTarget = new("First Target");
        GameObject secondTarget = new("Second Target");
        GameObject enemyObject = new("Reusable LOD Enemy");
        firstTarget.transform.position = Vector3.forward * 80f;
        secondTarget.transform.position = Vector3.forward * 4f;
        EnemyController enemy = enemyObject.AddComponent<EnemyController>();
        yield return null;

        try
        {
            EnemyAiLodController lod =
                enemyObject.GetComponent<EnemyAiLodController>();
            lod.ResetForSpawn(firstTarget.transform);
            yield return null;
            Assert.That(lod.CurrentTier, Is.EqualTo(EnemyAiLodTier.Far));

            enemy.PrepareForPool();
            enemy.ResetForSpawn(secondTarget.transform);
            yield return null;

            Assert.That(lod.CurrentTier, Is.EqualTo(EnemyAiLodTier.Near));
            Assert.That(lod.MaximumDecisionLatencyFrames, Is.Zero);
        }
        finally
        {
            Object.Destroy(enemyObject);
            Object.Destroy(secondTarget);
            Object.Destroy(firstTarget);
        }

        yield return null;
    }
}
