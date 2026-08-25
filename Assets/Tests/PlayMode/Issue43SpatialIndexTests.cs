using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue43SpatialIndexTests
{
    [UnityTest]
    public IEnumerator RadiusQueryMatchesExactDistanceAcrossCells()
    {
        var index = new EnemySpatialHash(5f);
        var results = new List<EnemyPerceptionController>(8);
        GameObject center = CreatePerception("Center", Vector3.zero);
        GameObject inside = CreatePerception(
            "Inside",
            new Vector3(5.9f, 0f, 0f));
        GameObject diagonalOutside = CreatePerception(
            "Diagonal Outside",
            new Vector3(4.5f, 0f, 4.5f));
        yield return null;

        try
        {
            EnemyPerceptionController centerPerception =
                center.GetComponent<EnemyPerceptionController>();
            EnemyPerceptionController insidePerception =
                inside.GetComponent<EnemyPerceptionController>();
            index.Register(centerPerception);
            index.Register(insidePerception);
            index.Register(diagonalOutside.GetComponent<
                EnemyPerceptionController>());

            int count = index.Query(
                Vector3.zero,
                6f,
                centerPerception,
                "*",
                int.MaxValue,
                results);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(results, Is.EquivalentTo(
                new[] { insidePerception }));
        }
        finally
        {
            Object.Destroy(center);
            Object.Destroy(inside);
            Object.Destroy(diagonalOutside);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator QueryAppliesGameplayTagAndMaximumResultCount()
    {
        var index = new EnemySpatialHash(5f);
        var results = new List<EnemyPerceptionController>(8);
        GameObject source = CreateEnemy("Source", Vector3.zero, "source");
        GameObject raiderA = CreateEnemy(
            "Raider A", Vector3.right * 2f, "spider_raider");
        GameObject raiderB = CreateEnemy(
            "Raider B", Vector3.right * 3f, "spider_raider");
        GameObject support = CreateEnemy(
            "Support", Vector3.forward * 2f, "spider_support");
        yield return null;

        try
        {
            EnemyPerceptionController sourcePerception =
                source.GetComponent<EnemyPerceptionController>();
            index.Register(sourcePerception);
            index.Register(raiderA.GetComponent<EnemyPerceptionController>());
            index.Register(raiderB.GetComponent<EnemyPerceptionController>());
            index.Register(support.GetComponent<EnemyPerceptionController>());

            int count = index.Query(
                Vector3.zero,
                10f,
                sourcePerception,
                "enemy.type.spider_raider",
                1,
                results);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(results[0].GetComponent<EnemyController>()
                .HasGameplayTag("enemy.type.spider_raider"), Is.True);
        }
        finally
        {
            Object.Destroy(source);
            Object.Destroy(raiderA);
            Object.Destroy(raiderB);
            Object.Destroy(support);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator MovingAndUnregisteringKeepMembershipConsistent()
    {
        var index = new EnemySpatialHash(5f);
        var results = new List<EnemyPerceptionController>(4);
        GameObject moving = CreatePerception(
            "Moving",
            Vector3.right * 2f);
        yield return null;

        try
        {
            EnemyPerceptionController perception =
                moving.GetComponent<EnemyPerceptionController>();
            index.Register(perception);
            Assert.That(index.Query(
                Vector3.zero, 4f, null, "*", 4, results), Is.EqualTo(1));

            moving.transform.position = Vector3.right * 22f;
            Assert.That(index.Update(perception), Is.True);
            Assert.That(index.Query(
                Vector3.zero, 4f, null, "*", 4, results), Is.Zero);
            Assert.That(index.Query(
                moving.transform.position, 4f, null, "*", 4, results),
                Is.EqualTo(1));

            Assert.That(index.Unregister(perception), Is.True);
            Assert.That(index.RegisteredCount, Is.Zero);
            Assert.That(index.Query(
                moving.transform.position, 4f, null, "*", 4, results),
                Is.Zero);
        }
        finally
        {
            Object.Destroy(moving);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator PrepareForPoolUnregistersBeforeGameObjectIsDisabled()
    {
        var results = new List<EnemyController>(4);
        GameObject sourceObject = CreateEnemy(
            "Pool Source", Vector3.zero, "spider");
        GameObject targetObject = CreateEnemy(
            "Pool Target", Vector3.right * 2f, "spider");
        yield return null;

        try
        {
            EnemyController source =
                sourceObject.GetComponent<EnemyController>();
            EnemyController target =
                targetObject.GetComponent<EnemyController>();
            EnemySpatialIndexService service =
                EnemySpatialIndexService.Instance;
            Assert.That(service, Is.Not.Null);
            Assert.That(service.CollectAliveNeighbors(
                source, 5f, "enemy", results), Is.EqualTo(1));

            target.PrepareForPool();
            Assert.That(targetObject.activeSelf, Is.True,
                "对象池会延迟到 LateUpdate 才禁用对象。此时索引必须已注销。");
            Assert.That(service.CollectAliveNeighbors(
                source, 5f, "enemy", results), Is.Zero);

            target.ResetForSpawn(null);
            Assert.That(service.CollectAliveNeighbors(
                source, 5f, "enemy", results), Is.EqualTo(1));
        }
        finally
        {
            Object.Destroy(targetObject);
            Object.Destroy(sourceObject);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator FixedSampleMatchesNaiveScanAfterCrossCellMoves()
    {
        const int count = 100;
        var index = new EnemySpatialHash(10f, 128);
        var objects = new List<GameObject>(count);
        var indexed = new List<EnemyPerceptionController>(count);
        var naive = new List<EnemyPerceptionController>(count);
        var random = new System.Random(43043);

        for (int item = 0; item < count; item++)
        {
            Vector3 position = new(
                (float)(random.NextDouble() * 100.0 - 50.0),
                0f,
                (float)(random.NextDouble() * 100.0 - 50.0));
            GameObject gameObject = CreatePerception(
                $"Fixed {item:000}", position);
            objects.Add(gameObject);
        }

        yield return null;

        try
        {
            foreach (GameObject gameObject in objects)
            {
                index.Register(gameObject.GetComponent<
                    EnemyPerceptionController>());
            }

            for (int moved = 0; moved < 20; moved++)
            {
                GameObject gameObject = objects[moved * 3];
                gameObject.transform.position += new Vector3(17f, 0f, -13f);
                index.Update(gameObject.GetComponent<
                    EnemyPerceptionController>());
            }

            Vector3 center = new(3f, 0f, -7f);
            const float radius = 18f;
            index.Query(
                center, radius, null, "*", int.MaxValue, indexed);

            float squaredRadius = radius * radius;

            foreach (GameObject gameObject in objects)
            {
                EnemyPerceptionController perception =
                    gameObject.GetComponent<EnemyPerceptionController>();
                Vector3 offset = perception.transform.position - center;
                offset.y = 0f;

                if (offset.sqrMagnitude <= squaredRadius)
                {
                    naive.Add(perception);
                }
            }

            Assert.That(indexed, Is.EquivalentTo(naive));
            Assert.That(index.LastCandidateVisitCount, Is.LessThan(count));
        }
        finally
        {
            foreach (GameObject gameObject in objects)
            {
                Object.Destroy(gameObject);
            }
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator MaximumResultsMatchNaiveRegistrationOrder()
    {
        var index = new EnemySpatialHash(5f, 16);
        var objects = new List<GameObject>(6);
        var indexed = new List<EnemyPerceptionController>(6);
        var naive = new List<EnemyPerceptionController>(6);
        Vector3[] positions =
        {
            new(6f, 0f, 0f),
            new(-2f, 0f, 0f),
            new(1f, 0f, 4f),
            new(-5f, 0f, -2f),
            new(2f, 0f, -1f),
            new(0f, 0f, 7f)
        };

        for (int item = 0; item < positions.Length; item++)
        {
            objects.Add(CreatePerception($"Ordered {item}", positions[item]));
        }

        yield return null;

        try
        {
            foreach (GameObject gameObject in objects)
            {
                index.Register(gameObject.GetComponent<
                    EnemyPerceptionController>());
            }

            foreach (int maximum in new[] { 1, 3 })
            {
                index.Query(
                    Vector3.zero, 20f, null, "*", maximum, indexed);
                index.QueryNaively(
                    Vector3.zero, 20f, null, "*", maximum, naive);
                Assert.That(indexed, Is.EqualTo(naive),
                    $"maximumResults={maximum} 时两条路径顺序应一致。");
            }
        }
        finally
        {
            foreach (GameObject gameObject in objects)
            {
                Object.Destroy(gameObject);
            }
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator QueryFindsOneCellMoveBeforeLateUpdateSynchronizes()
    {
        var results = new List<EnemyPerceptionController>(4);
        GameObject moving = CreatePerception(
            "Immediate Cross Cell", new Vector3(11f, 0f, 0f));
        yield return null;

        try
        {
            EnemyPerceptionController perception =
                moving.GetComponent<EnemyPerceptionController>();
            EnemySpatialIndexService service =
                EnemySpatialIndexService.Instance;
            Assert.That(service, Is.Not.Null);

            moving.transform.position = new Vector3(4f, 0f, 0f);
            int count = service.CollectPerceptions(
                Vector3.zero, 5f, null, "*", results, 4);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(results[0], Is.SameAs(perception));
        }
        finally
        {
            Object.Destroy(moving);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator DestroyedMemberIsRemovedWithoutLeakingRegistration()
    {
        EnemySpatialIndexService service =
            EnemySpatialIndexService.EnsureForActiveScene();
        int baseline = service.RegisteredCount;
        GameObject target = CreatePerception(
            "Destroy Cleanup", Vector3.right * 2f);
        yield return null;

        Assert.That(service.RegisteredCount, Is.EqualTo(baseline + 1));
        Object.Destroy(target);
        yield return null;

        Assert.That(service.RegisteredCount, Is.EqualTo(baseline));
    }

    [UnityTest]
    public IEnumerator WarmRadiusQueriesAllocateNoManagedMemory()
    {
        var index = new EnemySpatialHash(10f, 64);
        var objects = new List<GameObject>(32);
        var results = new List<EnemyPerceptionController>(32);

        for (int item = 0; item < 32; item++)
        {
            float angle = item * Mathf.PI * 2f / 32f;
            GameObject gameObject = CreatePerception(
                $"Allocation {item:00}",
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 12f);
            objects.Add(gameObject);
        }

        yield return null;

        try
        {
            foreach (GameObject gameObject in objects)
            {
                index.Register(gameObject.GetComponent<
                    EnemyPerceptionController>());
            }

            for (int warmup = 0; warmup < 16; warmup++)
            {
                index.Query(
                    Vector3.zero, 20f, null, "*", 32, results);
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();

            for (int sample = 0; sample < 256; sample++)
            {
                index.Query(
                    Vector3.zero, 20f, null, "*", 32, results);
            }

            long allocated =
                System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
        finally
        {
            foreach (GameObject gameObject in objects)
            {
                Object.Destroy(gameObject);
            }
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator WarmServiceQueriesAllocateNoManagedMemory()
    {
        var objects = new List<GameObject>(33);
        var results = new List<EnemyController>(32);
        GameObject sourceObject = CreateEnemy(
            "Allocation Source", Vector3.zero, "spider");
        objects.Add(sourceObject);

        for (int item = 0; item < 32; item++)
        {
            float angle = item * Mathf.PI * 2f / 32f;
            objects.Add(CreateEnemy(
                $"Service Allocation {item:00}",
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 12f,
                "spider"));
        }

        yield return null;

        try
        {
            EnemyController source =
                sourceObject.GetComponent<EnemyController>();
            EnemySpatialIndexService service =
                EnemySpatialIndexService.Instance;
            Assert.That(service, Is.Not.Null);

            for (int warmup = 0; warmup < 16; warmup++)
            {
                service.CollectAliveNeighbors(
                    source, 20f, "enemy", results);
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();

            for (int sample = 0; sample < 256; sample++)
            {
                service.CollectAliveNeighbors(
                    source, 20f, "enemy", results);
            }

            long allocated =
                System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
        finally
        {
            foreach (GameObject gameObject in objects)
            {
                Object.Destroy(gameObject);
            }
        }

        yield return null;
    }

    private static GameObject CreatePerception(
        string name,
        Vector3 position)
    {
        GameObject gameObject = new(name);
        gameObject.transform.position = position;
        gameObject.AddComponent<EnemyPerceptionController>();
        return gameObject;
    }

    private static GameObject CreateEnemy(
        string name,
        Vector3 position,
        string enemyTypeId)
    {
        GameObject gameObject = new(name);
        gameObject.transform.position = position;
        EnemyController controller =
            gameObject.AddComponent<EnemyController>();
        WaveEnemyLifecycle lifecycle =
            gameObject.AddComponent<WaveEnemyLifecycle>();
        lifecycle.Arm(
            name.Length,
            1,
            controller,
            enemyTypeId,
            LootRewardTier.Normal,
            (_, _) => { });
        return gameObject;
    }
}
