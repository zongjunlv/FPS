using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public class Issue6DamageTests
    {
        [Test]
        public void DamageContractUsesPlainDataAndCorrectInterface()
        {
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            Type damageableType =
                Type.GetType("IDamageable, Assembly-CSharp");

            Assert.That(damageInfoType, Is.Not.Null);
            Assert.That(damageInfoType.IsValueType, Is.True);
            Assert.That(damageableType, Is.Not.Null);
            Assert.That(damageableType.IsInterface, Is.True);
            Assert.That(
                Type.GetType("IDamagable, Assembly-CSharp"),
                Is.Null,
                "The misspelled legacy damage type must be removed.");
        }

        [Test]
        public void HealthAppliesOrdinaryDamage()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            GameObject target = new GameObject("Ordinary Damage Target");

            try
            {
                Component health = target.AddComponent(healthType);
                healthType.GetMethod("Initialize")
                    .Invoke(health, new object[] { 100f });
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        25f,
                        Vector3.one,
                        Vector3.forward,
                        null
                    });

                bool accepted =
                    (bool)healthType.GetMethod("ApplyDamage")
                        .Invoke(health, new[] { damage });

                Assert.That(accepted, Is.True);
                Assert.That(
                    (float)healthType
                        .GetProperty("CurrentHealth")
                        .GetValue(health),
                    Is.EqualTo(75f));
                Assert.That(
                    (bool)healthType.GetProperty("IsDead")
                        .GetValue(health),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void HealthClampsOverkillAndRejectsDamageAfterDeath()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");

            Assert.That(healthType, Is.Not.Null);
            Assert.That(damageInfoType, Is.Not.Null);

            GameObject target = new GameObject("Damage Target");

            try
            {
                Component health = target.AddComponent(healthType);
                MethodInfo initialize =
                    healthType.GetMethod("Initialize");
                MethodInfo applyDamage =
                    healthType.GetMethod("ApplyDamage");
                PropertyInfo currentHealth =
                    healthType.GetProperty("CurrentHealth");
                PropertyInfo maxHealth =
                    healthType.GetProperty("MaxHealth");
                PropertyInfo isDead =
                    healthType.GetProperty("IsDead");

                Assert.That(initialize, Is.Not.Null);
                Assert.That(applyDamage, Is.Not.Null);

                initialize.Invoke(health, new object[] { 50f });
                object lethalDamage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        75f,
                        Vector3.zero,
                        Vector3.forward,
                        null
                    });

                bool accepted =
                    (bool)applyDamage.Invoke(
                        health,
                        new[] { lethalDamage });
                bool acceptedAfterDeath =
                    (bool)applyDamage.Invoke(
                        health,
                        new[] { lethalDamage });

                Assert.That(accepted, Is.True);
                Assert.That(acceptedAfterDeath, Is.False);
                Assert.That(
                    (float)maxHealth.GetValue(health),
                    Is.EqualTo(50f));
                Assert.That(
                    (float)currentHealth.GetValue(health),
                    Is.EqualTo(0f));
                Assert.That((bool)isDead.GetValue(health), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void HealthRaisesDeathEventOnlyOnce()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            GameObject target = new GameObject("Death Event Target");

            try
            {
                Component health = target.AddComponent(healthType);
                healthType.GetMethod("Initialize")
                    .Invoke(health, new object[] { 10f });
                int deathCount = 0;
                Action onDeath = () => deathCount++;
                EventInfo died = healthType.GetEvent("Died");
                died.AddEventHandler(health, onDeath);
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        100f,
                        Vector3.zero,
                        Vector3.forward,
                        null
                    });
                MethodInfo applyDamage =
                    healthType.GetMethod("ApplyDamage");

                applyDamage.Invoke(health, new[] { damage });
                applyDamage.Invoke(health, new[] { damage });
                applyDamage.Invoke(health, new[] { damage });

                Assert.That(deathCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void DamageHitboxAppliesConfiguredHeadMultiplier()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            Type hitboxType =
                Type.GetType("DamageHitbox, Assembly-CSharp");

            Assert.That(
                hitboxType,
                Is.Not.Null,
                "A configurable damage hitbox module is required.");

            GameObject target = new GameObject("Damage Target");
            GameObject head = new GameObject("Head Hitbox");
            head.transform.SetParent(target.transform);

            try
            {
                Component health = target.AddComponent(healthType);
                healthType.GetMethod("Initialize")
                    .Invoke(health, new object[] { 100f });
                Component hitbox = head.AddComponent(hitboxType);
                hitboxType.GetMethod("Configure")
                    .Invoke(hitbox, new object[] { health, 2f });
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        10f,
                        Vector3.zero,
                        Vector3.forward,
                        null
                    });

                bool accepted =
                    (bool)hitboxType.GetMethod("ApplyDamage")
                        .Invoke(hitbox, new[] { damage });
                float remaining =
                    (float)healthType.GetProperty("CurrentHealth")
                        .GetValue(health);

                Assert.That(accepted, Is.True);
                Assert.That(remaining, Is.EqualTo(80f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator CompletedTracerIsReusedByFixedPool()
        {
            Type poolType =
                Type.GetType("ShotTracerPool, Assembly-CSharp");
            Type tracerType =
                Type.GetType(
                    "ShotTracerController, Assembly-CSharp");

            Assert.That(
                poolType,
                Is.Not.Null,
                "A fixed-capacity tracer pool is required.");

            GameObject poolObject =
                new GameObject("Tracer Pool Test");

            try
            {
                Component pool = poolObject.AddComponent(poolType);
                MethodInfo play = poolType.GetMethod("Play");
                PropertyInfo capacity =
                    poolType.GetProperty("Capacity");
                Component first =
                    (Component)play.Invoke(
                        pool,
                        new object[]
                        {
                            Vector3.zero,
                            Vector3.forward,
                            300f
                        });
                int warmedCount =
                    poolObject.GetComponentsInChildren(
                        tracerType,
                        true).Length;

                yield return new WaitForSeconds(0.15f);

                Assert.That(first, Is.Not.Null);
                Assert.That(first.gameObject.activeSelf, Is.False);

                Component second =
                    (Component)play.Invoke(
                        pool,
                        new object[]
                        {
                            Vector3.zero,
                            Vector3.right,
                            300f
                        });

                Assert.That(
                    second,
                    Is.SameAs(first));
                Assert.That(
                    warmedCount,
                    Is.EqualTo((int)capacity.GetValue(pool)));
                Assert.That(
                    poolObject.GetComponentsInChildren(
                        tracerType,
                        true).Length,
                    Is.EqualTo(warmedCount),
                    "Playing a tracer must not grow the prewarmed pool.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(poolObject);
            }
        }
    }
}
