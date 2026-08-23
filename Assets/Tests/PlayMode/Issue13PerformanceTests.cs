using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue13PerformanceTests
    {
        [Test]
        public void FixedPoolPrewarmsAndNeverReclaimsBorrowedObjects()
        {
            Type poolType = RuntimeTypeResolver.GetType(
                "RuntimeGameObjectPool");
            Assert.That(
                poolType,
                Is.Not.Null,
                "Issue 13 需要提供固定容量的运行时 GameObject 对象池。");

            var createdObjects = new List<GameObject>();
            Func<int, GameObject> factory = index =>
            {
                var instance = new GameObject($"Pooled Object {index}");
                createdObjects.Add(instance);
                return instance;
            };
            object pool = Activator.CreateInstance(
                poolType,
                new object[] { 3, factory });
            MethodInfo rent = poolType.GetMethod("Rent");
            MethodInfo returnToPool = poolType.GetMethod("Return");
            var foreignObject = new GameObject("Foreign Object");

            try
            {
                Assert.That(createdObjects, Has.Count.EqualTo(3));
                Assert.That(
                    createdObjects,
                    Has.All.Matches<GameObject>(
                        instance => !instance.activeSelf));
                AssertCounts(poolType, pool, 3, 3, 0);

                var first = (GameObject)rent.Invoke(pool, null);
                var second = (GameObject)rent.Invoke(pool, null);
                var third = (GameObject)rent.Invoke(pool, null);

                Assert.That(
                    new[] { first, second, third },
                    Is.Unique);
                AssertCounts(poolType, pool, 3, 0, 3);

                Assert.That(rent.Invoke(pool, null), Is.Null);
                Assert.That(
                    createdObjects,
                    Has.Count.EqualTo(3),
                    "借空后不得临时扩容或再次调用工厂。");
                Assert.That(
                    new[] { first, second, third },
                    Has.All.Matches<GameObject>(
                        instance => instance.activeSelf),
                    "借空时不得停用或抢占仍在使用的对象。");
                AssertCounts(poolType, pool, 3, 0, 3);

                Assert.That(
                    returnToPool.Invoke(pool, new object[] { second }),
                    Is.EqualTo(true));
                Assert.That(second.activeSelf, Is.False);
                AssertCounts(poolType, pool, 3, 1, 2);

                Assert.That(rent.Invoke(pool, null), Is.SameAs(second));
                Assert.That(second.activeSelf, Is.True);
                AssertCounts(poolType, pool, 3, 0, 3);

                Assert.That(
                    returnToPool.Invoke(pool, new object[] { second }),
                    Is.EqualTo(true));
                Assert.That(
                    returnToPool.Invoke(pool, new object[] { second }),
                    Is.EqualTo(false),
                    "同一对象重复归还必须被拒绝。");
                Assert.That(
                    returnToPool.Invoke(
                        pool,
                        new object[] { foreignObject }),
                    Is.EqualTo(false),
                    "池外对象不得改变对象池状态。");
                Assert.That(foreignObject.activeSelf, Is.True);

                Assert.That(
                    returnToPool.Invoke(pool, new object[] { first }),
                    Is.EqualTo(true));
                Assert.That(
                    returnToPool.Invoke(pool, new object[] { third }),
                    Is.EqualTo(true));
                AssertCounts(poolType, pool, 3, 3, 0);
            }
            finally
            {
                foreach (GameObject instance in createdObjects)
                {
                    if (instance != null)
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }

                UnityEngine.Object.DestroyImmediate(foreignObject);
            }
        }

        [Test]
        public void CombatImpactPoolHasHardCapacityAndReusesMetalEffects()
        {
            Type poolType = RuntimeTypeResolver.GetType(
                "CombatEffectPool");
            Type shotResultType = RuntimeTypeResolver.GetType(
                "ShotResult");
            Type damageResultType = RuntimeTypeResolver.GetType(
                "DamageResult");
            Type surfaceType = RuntimeTypeResolver.GetType(
                "SurfaceType");
            Type metalControllerType = RuntimeTypeResolver.GetType(
                "MetalImpactVisualController");
            Assert.That(
                poolType,
                Is.Not.Null,
                "高频命中反馈必须由固定容量池统一管理。");

            GameObject host = new GameObject("Combat Effect Pool Test");

            try
            {
                Component pool = host.AddComponent(poolType);
                poolType.GetMethod("Configure")
                    .Invoke(pool, new object[] { null });
                object none = damageResultType.GetProperty("None")
                    .GetValue(null);
                object metalResult = Activator.CreateInstance(
                    shotResultType,
                    new object[]
                    {
                        true,
                        Vector3.zero,
                        Vector3.forward,
                        Enum.Parse(surfaceType, "Metal"),
                        none
                    });
                MethodInfo present =
                    poolType.GetMethod("PresentImpact");

                for (int shot = 0; shot < 64; shot++)
                {
                    present.Invoke(pool, new[] { metalResult });
                }

                int capacity = (int)poolType
                    .GetProperty("MetalCapacity")
                    .GetValue(pool);
                Assert.That(capacity, Is.EqualTo(48));
                Assert.That(
                    poolType.GetProperty("MetalActiveCount")
                        .GetValue(pool),
                    Is.EqualTo(capacity),
                    "容量耗尽时不得继续创建命中特效。");
                Assert.That(
                    host.GetComponentsInChildren(
                        metalControllerType,
                        true).Length,
                    Is.EqualTo(capacity));

                poolType.GetMethod("ReturnAll").Invoke(pool, null);
                present.Invoke(pool, new[] { metalResult });

                Assert.That(
                    poolType.GetProperty("MetalActiveCount")
                        .GetValue(pool),
                    Is.EqualTo(1));
                Assert.That(
                    host.GetComponentsInChildren(
                        metalControllerType,
                        true).Length,
                    Is.EqualTo(capacity),
                    "回收后再次命中必须复用预热对象。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void StablePooledImpactLoopDoesNotAllocateManagedMemory()
        {
            Type poolType = RuntimeTypeResolver.GetType(
                "CombatEffectPool");
            Type shotResultType = RuntimeTypeResolver.GetType(
                "ShotResult");
            Type damageResultType = RuntimeTypeResolver.GetType(
                "DamageResult");
            Type surfaceType = RuntimeTypeResolver.GetType(
                "SurfaceType");
            GameObject host = new GameObject("Pool Allocation Test");

            try
            {
                Component pool = host.AddComponent(poolType);
                poolType.GetMethod("Configure")
                    .Invoke(pool, new object[] { null });
                object none = damageResultType.GetProperty("None")
                    .GetValue(null);
                object result = Activator.CreateInstance(
                    shotResultType,
                    new object[]
                    {
                        true,
                        Vector3.zero,
                        Vector3.forward,
                        Enum.Parse(surfaceType, "Metal"),
                        none
                    });
                MethodInfo measure = poolType.GetMethod(
                    "MeasureSteadyStateManagedAllocation");

                Assert.That(
                    measure,
                    Is.Not.Null,
                    "对象池必须提供稳定阶段的可重复分配探针。");
                long allocated = (long)measure.Invoke(
                    pool,
                    new[] { result, (object)128 });
                Assert.That(
                    allocated,
                    Is.LessThanOrEqualTo(256L),
                    "预热后的命中特效借出、播放和归还不应持续产生托管分配。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void AssertCounts(
            Type poolType,
            object pool,
            int capacity,
            int available,
            int active)
        {
            Assert.That(
                poolType.GetProperty("Capacity").GetValue(pool),
                Is.EqualTo(capacity));
            Assert.That(
                poolType.GetProperty("AvailableCount").GetValue(pool),
                Is.EqualTo(available));
            Assert.That(
                poolType.GetProperty("ActiveCount").GetValue(pool),
                Is.EqualTo(active));
        }

    }
}
