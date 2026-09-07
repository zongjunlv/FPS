using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue44BatchedSightTests
{
    private GameObject schedulerObject;

    [SetUp]
    public void SetUp()
    {
        LogAssert.ignoreFailingMessages = true;
        EnemyAiLodController.SetGlobalEnabled(true);
        EnemyPerceptionScheduler.SetBatchEnabled(true);

        EnemyPerceptionScheduler[] existingSchedulers =
            Object.FindObjectsByType<EnemyPerceptionScheduler>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (EnemyPerceptionScheduler scheduler in existingSchedulers)
        {
            Object.DestroyImmediate(scheduler.gameObject);
        }

        schedulerObject = new GameObject("Issue 44 Test Scheduler");
        schedulerObject.AddComponent<EnemyPerceptionScheduler>();
    }

    [TearDown]
    public void TearDown()
    {
        EnemyPerceptionScheduler.SetBatchEnabled(true);
        EnemyAiLodController.SetGlobalEnabled(true);

        if (schedulerObject != null)
        {
            Object.DestroyImmediate(schedulerObject);
        }

        LogAssert.ignoreFailingMessages = false;
    }

    [UnityTest]
    public IEnumerator BatchResolvesVisibleAndOccludedTargets()
    {
        GameObject visibleEnemy = new("Visible Batch Enemy");
        GameObject blockedEnemy = new("Blocked Batch Enemy");
        GameObject visibleTarget = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);
        GameObject blockedTarget = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);

        try
        {
            visibleEnemy.transform.position = Vector3.left * 2f;
            blockedEnemy.transform.position = Vector3.right * 2f;
            visibleTarget.transform.position =
                visibleEnemy.transform.position + Vector3.forward * 6f;
            blockedTarget.transform.position =
                blockedEnemy.transform.position + Vector3.forward * 6f;
            wall.transform.position =
                blockedEnemy.transform.position +
                new Vector3(0f, 0.75f, 3f);
            wall.transform.localScale = new Vector3(2f, 2f, 0.5f);

            EnemyPerceptionController visible =
                visibleEnemy.AddComponent<EnemyPerceptionController>();
            EnemyPerceptionController blocked =
                blockedEnemy.AddComponent<EnemyPerceptionController>();
            visible.Configure(10f, 90f, 1f, 0.5f, 4f);
            blocked.Configure(10f, 90f, 1f, 0.5f, 4f);
            visible.SetTarget(visibleTarget.transform);
            blocked.SetTarget(blockedTarget.transform);
            EnemyPerceptionScheduler.Instance.Configure(2);
            Physics.SyncTransforms();

            yield return null;
            yield return null;

            Assert.That(visible.HasVisualContact, Is.True);
            Assert.That(blocked.HasVisualContact, Is.False);
            Assert.That(
                EnemyPerceptionScheduler.Instance.CompletedBatchCount,
                Is.GreaterThan(0));
        }
        finally
        {
            Object.Destroy(visibleEnemy);
            Object.Destroy(blockedEnemy);
            Object.Destroy(visibleTarget);
            Object.Destroy(blockedTarget);
            Object.Destroy(wall);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator PoolReuseRejectsPendingOldGenerationResult()
    {
        GameObject enemy = new("Reusable Batch Enemy");
        GameObject firstTarget = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);
        GameObject reusedTarget = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);

        try
        {
            firstTarget.transform.position = Vector3.forward * 5f;
            reusedTarget.transform.position = Vector3.forward * 6f;
            wall.transform.position = new Vector3(0f, 0.75f, 3f);
            wall.transform.localScale = new Vector3(2f, 2f, 0.5f);
            wall.SetActive(false);
            EnemyPerceptionController perception =
                enemy.AddComponent<EnemyPerceptionController>();
            perception.Configure(10f, 90f, 1f, 0.5f, 4f);
            perception.SetTarget(firstTarget.transform);
            EnemyPerceptionScheduler scheduler =
                EnemyPerceptionScheduler.Instance;
            scheduler.Configure(1);
            Physics.SyncTransforms();

            yield return null;
            Assert.That(scheduler.PendingBatchCount, Is.EqualTo(1));

            perception.PrepareForPool();
            perception.ResetForSpawn(reusedTarget.transform);
            wall.SetActive(true);
            Physics.SyncTransforms();
            yield return null;

            Assert.That(perception.SightCheckCount, Is.Zero);
            Assert.That(perception.HasVisualContact, Is.False);
            Assert.That(scheduler.DiscardedStaleResultCount,
                Is.GreaterThan(0));

            yield return null;
            Assert.That(perception.SightCheckCount, Is.EqualTo(1));
            Assert.That(perception.HasVisualContact, Is.False,
                "复用后的新代际应采用墙后目标的新查询结果。");
        }
        finally
        {
            Object.Destroy(enemy);
            Object.Destroy(firstTarget);
            Object.Destroy(reusedTarget);
            Object.Destroy(wall);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator DeathRejectsPendingSightResult()
    {
        GameObject enemy = new("Dying Batch Enemy");
        GameObject target = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);

        try
        {
            target.transform.position = Vector3.forward * 5f;
            Health health = enemy.AddComponent<Health>();
            health.Initialize(10f);
            EnemyPerceptionController perception =
                enemy.AddComponent<EnemyPerceptionController>();
            perception.SetTarget(target.transform);
            EnemyPerceptionScheduler scheduler =
                EnemyPerceptionScheduler.Instance;
            scheduler.Configure(1);
            Physics.SyncTransforms();

            yield return null;
            Assert.That(scheduler.PendingBatchCount, Is.EqualTo(1));

            health.ApplyDamage(new DamageInfo(
                20f,
                enemy.transform.position,
                Vector3.forward,
                target));
            yield return null;

            Assert.That(perception.SightCheckCount, Is.Zero);
            Assert.That(perception.HasVisualContact, Is.False);
            Assert.That(scheduler.DiscardedStaleResultCount,
                Is.GreaterThan(0));
        }
        finally
        {
            Object.Destroy(enemy);
            Object.Destroy(target);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator BatchIgnoresEnemyOwnedHitboxes()
    {
        GameObject enemy = new("Batch Enemy With Hitbox");
        GameObject ownHitbox = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        GameObject target = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);

        try
        {
            ownHitbox.transform.SetParent(enemy.transform, false);
            ownHitbox.transform.localPosition =
                new Vector3(0f, 0.75f, 0.45f);
            target.transform.position = Vector3.forward * 5f;
            EnemyPerceptionController perception =
                enemy.AddComponent<EnemyPerceptionController>();
            perception.Configure(10f, 90f, 1f, 0.5f, 4f);
            perception.SetTarget(target.transform);
            EnemyPerceptionScheduler.Instance.Configure(1);
            Physics.SyncTransforms();

            yield return null;
            yield return null;

            Assert.That(perception.HasVisualContact, Is.True);
        }
        finally
        {
            Object.Destroy(enemy);
            Object.Destroy(target);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator NearEnemyReceivesBatchResultWithinTwoFrames()
    {
        GameObject enemyObject = new("Near Batch Enemy");
        GameObject target = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);

        try
        {
            target.transform.position = Vector3.forward * 4f;
            EnemyController enemy =
                enemyObject.AddComponent<EnemyController>();
            EnemyPerceptionController perception =
                enemyObject.GetComponent<EnemyPerceptionController>();
            perception.SetTarget(target.transform);
            enemy.GetComponent<EnemyAiLodController>()
                .ResetForSpawn(target.transform);
            EnemyPerceptionScheduler.Instance.Configure(1);
            Physics.SyncTransforms();

            for (int frame = 0; frame < 8; frame++)
            {
                yield return null;
            }

            Assert.That(perception.SightCheckCount, Is.GreaterThan(2));
            Assert.That(perception.MaximumSightCheckLatencyFrames,
                Is.LessThanOrEqualTo(2));
            Assert.That(perception.MaximumSightResultDelayFrames,
                Is.LessThanOrEqualTo(1));
        }
        finally
        {
            Object.Destroy(enemyObject);
            Object.Destroy(target);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator WarmBatchSchedulingAllocatesNoManagedMemory()
    {
        GameObject enemy = new("Allocation Batch Enemy");
        GameObject target = GameObject.CreatePrimitive(
            PrimitiveType.Capsule);
        var members = new List<EnemyPerceptionController>(1);

        try
        {
            target.transform.position = Vector3.forward * 5f;
            EnemyPerceptionController perception =
                enemy.AddComponent<EnemyPerceptionController>();
            perception.SetTarget(target.transform);
            members.Add(perception);
            Physics.SyncTransforms();
            yield return null;

            using var processor = new EnemySightBatchProcessor(1);

            for (int warmup = 0; warmup < 16; warmup++)
            {
                processor.Schedule(members, warmup);
                processor.CompleteAndApply(out _, out _, out _);
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();

            for (int sample = 0; sample < 128; sample++)
            {
                processor.Schedule(members, sample);
                processor.CompleteAndApply(out _, out _, out _);
            }

            long allocated =
                System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
        finally
        {
            Object.Destroy(enemy);
            Object.Destroy(target);
        }

        yield return null;
    }
}
