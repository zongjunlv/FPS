using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public class Issue7FeedbackTests
    {
        [UnityTest]
        public IEnumerator CombatFeedbackVisualProfileKeepsBuildShaders()
        {
            Type profileType = Type.GetType(
                "CombatFeedbackVisualProfile, Assembly-CSharp");
            UnityEngine.Object profile = Resources.Load(
                "CombatFeedbackVisual",
                profileType);
            Material surfaceMaterial = (Material)profileType
                .GetField("SurfaceMaterial")
                .GetValue(profile);
            Material sparkMaterial = (Material)profileType
                .GetField("MetalSparkMaterial")
                .GetValue(profile);

            Assert.That(profile, Is.Not.Null);
            Assert.That(surfaceMaterial, Is.Not.Null);
            Assert.That(sparkMaterial, Is.Not.Null);
            Assert.That(
                surfaceMaterial.shader.name,
                Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(
                sparkMaterial.shader.name,
                Is.EqualTo(
                    "Universal Render Pipeline/Particles/Unlit"));
            yield return null;
        }

        [Test]
        public void HealthReturnsObservableDamageResult()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            Type damageResultType =
                Type.GetType("DamageResult, Assembly-CSharp");

            Assert.That(
                damageResultType,
                Is.Not.Null,
                "Damage must return immutable result data for feedback.");

            GameObject target = new GameObject("Damage Result Target");

            try
            {
                Component health = target.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[] { typeof(float) })
                    .Invoke(health, new object[] { 100f });
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        25f,
                        Vector3.zero,
                        Vector3.forward,
                        null
                    });
                object result =
                    healthType.GetMethod("ApplyDamage")
                        .Invoke(health, new[] { damage });

                Assert.That(
                    (bool)damageResultType.GetProperty("WasApplied")
                        .GetValue(result),
                    Is.True);
                Assert.That(
                    (float)damageResultType.GetProperty("AppliedAmount")
                        .GetValue(result),
                    Is.EqualTo(25f));
                Assert.That(
                    (bool)damageResultType.GetProperty("WasKilled")
                        .GetValue(result),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void PlayerArmorAbsorbsDamageBeforeHealth()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            GameObject player = new GameObject("Armored Player");

            try
            {
                Component health = player.AddComponent(healthType);
                MethodInfo initializeVitals = healthType.GetMethod(
                    "Initialize",
                    new[] { typeof(float), typeof(float) });

                Assert.That(
                    initializeVitals,
                    Is.Not.Null,
                    "Health must support explicit player armor.");
                initializeVitals.Invoke(
                    health,
                    new object[] { 100f, 100f });
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        30f,
                        Vector3.zero,
                        Vector3.forward,
                        null
                    });
                healthType.GetMethod("ApplyDamage")
                    .Invoke(health, new[] { damage });

                Assert.That(
                    (float)healthType.GetProperty("CurrentArmor")
                        .GetValue(health),
                    Is.EqualTo(70f));
                Assert.That(
                    (float)healthType.GetProperty("CurrentHealth")
                        .GetValue(health),
                    Is.EqualTo(100f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void VitalsHudTracksHealthAndArmor()
        {
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type hudType =
                Type.GetType("PlayerVitalsHudPresenter, Assembly-CSharp");

            Assert.That(
                hudType,
                Is.Not.Null,
                "Player needs a visible health and armor HUD.");

            GameObject player = new GameObject("Vitals HUD Player");

            try
            {
                Component health = player.AddComponent(healthType);
                healthType.GetMethod(
                        "Initialize",
                        new[] { typeof(float), typeof(float) })
                    .Invoke(health, new object[] { 100f, 100f });
                Component hud = player.AddComponent(hudType);
                hudType.GetMethod("Bind")
                    .Invoke(hud, new[] { health });

                Assert.That(
                    (float)hudType.GetProperty("DisplayedHealth")
                        .GetValue(hud),
                    Is.EqualTo(100f));
                Assert.That(
                    (float)hudType.GetProperty("DisplayedArmor")
                        .GetValue(hud),
                    Is.EqualTo(100f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void DamageResultClassifiesBodyHeadshotAndKillFeedback()
        {
            Type damageResultType =
                Type.GetType("DamageResult, Assembly-CSharp");
            Type hitRegionType =
                Type.GetType("HitRegion, Assembly-CSharp");
            Type shotResultType =
                Type.GetType("ShotResult, Assembly-CSharp");
            Type surfaceType =
                Type.GetType("SurfaceType, Assembly-CSharp");

            object body = CreateShotResult(
                damageResultType,
                hitRegionType,
                shotResultType,
                surfaceType,
                "Body",
                false);
            object head = CreateShotResult(
                damageResultType,
                hitRegionType,
                shotResultType,
                surfaceType,
                "Head",
                false);
            object kill = CreateShotResult(
                damageResultType,
                hitRegionType,
                shotResultType,
                surfaceType,
                "Head",
                true);

            PropertyInfo feedbackKind =
                shotResultType.GetProperty("FeedbackKind");
            Assert.That(feedbackKind.GetValue(body).ToString(),
                Is.EqualTo("Normal"));
            Assert.That(feedbackKind.GetValue(head).ToString(),
                Is.EqualTo("Headshot"));
            Assert.That(feedbackKind.GetValue(kill).ToString(),
                Is.EqualTo("Kill"));
        }

        [Test]
        public void WeaponSpreadDistinguishesAdsMovementSprintAndBloom()
        {
            Type spreadType =
                Type.GetType("WeaponSpreadState, Assembly-CSharp");
            object spread = Activator.CreateInstance(spreadType);
            spreadType.GetMethod("Configure").Invoke(
                spread,
                new object[] { 0.7f, 0.1f, 0.8f, 2.5f, 0.2f, 1f, 4f });
            MethodInfo setContext = spreadType.GetMethod("SetContext");
            PropertyInfo current =
                spreadType.GetProperty("CurrentSpreadDegrees");

            setContext.Invoke(spread, new object[] { 0f, 0f, false });
            float hip = (float)current.GetValue(spread);
            setContext.Invoke(spread, new object[] { 1f, 0f, false });
            float ads = (float)current.GetValue(spread);
            setContext.Invoke(spread, new object[] { 0f, 1f, false });
            float moving = (float)current.GetValue(spread);
            setContext.Invoke(spread, new object[] { 0f, 1f, true });
            float sprint = (float)current.GetValue(spread);
            spreadType.GetMethod("RegisterShot").Invoke(spread, null);
            float fired = (float)current.GetValue(spread);
            spreadType.GetMethod("Tick").Invoke(spread, new object[] { 1f });
            float recovered = (float)current.GetValue(spread);

            Assert.That(ads, Is.LessThan(hip));
            Assert.That(moving, Is.GreaterThan(hip));
            Assert.That(sprint, Is.GreaterThan(moving));
            Assert.That(fired, Is.GreaterThan(sprint));
            Assert.That(recovered, Is.LessThan(fired));
        }

        [Test]
        public void RecoilAccumulatesAcrossContinuousFire()
        {
            Type recoilType = Type.GetType(
                "PlayerRecoilController, Assembly-CSharp");
            GameObject player = new GameObject("Recoil Accumulation");
            GameObject pivot = new GameObject("Recoil Pivot");
            pivot.transform.SetParent(player.transform);

            try
            {
                Component recoil = player.AddComponent(recoilType);
                recoilType.GetField(
                        "recoilPivot",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(recoil, pivot.transform);
                recoilType.GetMethod("SetMovementAmount")
                    .Invoke(recoil, new object[] { 1f });
                recoilType.GetMethod("AddRecoil")
                    .Invoke(recoil, new object[] { 1f, 0f });
                float first = ((Vector2)recoilType
                    .GetProperty("TargetRecoil").GetValue(recoil)).x;
                recoilType.GetMethod("AddRecoil")
                    .Invoke(recoil, new object[] { 1f, 0f });
                float second = ((Vector2)recoilType
                    .GetProperty("TargetRecoil").GetValue(recoil)).x;

                Assert.That(second, Is.GreaterThan(first * 1.5f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SurfaceDescriptorOverridesFallbackClassification()
        {
            Type descriptorType =
                Type.GetType("SurfaceDescriptor, Assembly-CSharp");
            Type surfaceType =
                Type.GetType("SurfaceType, Assembly-CSharp");
            Type resolverType =
                Type.GetType("SurfaceResolver, Assembly-CSharp");
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                Component descriptor =
                    target.AddComponent(descriptorType);
                object metal = Enum.Parse(surfaceType, "Metal");
                descriptorType.GetMethod("Configure")
                    .Invoke(descriptor, new[] { metal });
                object resolved = resolverType.GetMethod("Resolve")
                    .Invoke(null, new object[]
                    {
                        target.GetComponent<Collider>()
                    });

                Assert.That(resolved.ToString(), Is.EqualTo("Metal"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ConcreteAndMetalUseDistinctImpactStyles()
        {
            Type styleType =
                Type.GetType("SurfaceImpactStyle, Assembly-CSharp");
            Type surfaceType =
                Type.GetType("SurfaceType, Assembly-CSharp");

            Assert.That(
                styleType,
                Is.Not.Null,
                "Each surface needs a dedicated visual preset.");
            MethodInfo forSurface = styleType.GetMethod(
                "For",
                BindingFlags.Public | BindingFlags.Static);
            object concrete = forSurface.Invoke(
                null,
                new[] { Enum.Parse(surfaceType, "Concrete") });
            object metal = forSurface.Invoke(
                null,
                new[] { Enum.Parse(surfaceType, "Metal") });

            Assert.That(
                styleType.GetProperty("SparkCount").GetValue(metal),
                Is.Not.EqualTo(
                    styleType.GetProperty("SparkCount")
                        .GetValue(concrete)));
            Assert.That(
                styleType.GetProperty("Color").GetValue(metal),
                Is.Not.EqualTo(
                    styleType.GetProperty("Color")
                        .GetValue(concrete)));
            PropertyInfo markerLifetime =
                styleType.GetProperty("UniqueMarkerLifetime");
            PropertyInfo markerScale =
                styleType.GetProperty("UniqueMarkerScale");
            Assert.That(
                markerLifetime,
                Is.Not.Null,
                "Metal needs a persistent unique visual, not only a flash.");
            Assert.That(
                (float)markerLifetime.GetValue(metal),
                Is.GreaterThanOrEqualTo(2.5f));
            Assert.That(
                markerScale.GetValue(metal),
                Is.Not.EqualTo(markerScale.GetValue(concrete)));
        }

        [Test]
        public void MetalImpactDoesNotReuseConcreteVisual()
        {
            Type controllerType = Type.GetType(
                "WeaponImpactFeedbackController, Assembly-CSharp");
            Type shotResultType =
                Type.GetType("ShotResult, Assembly-CSharp");
            Type damageResultType =
                Type.GetType("DamageResult, Assembly-CSharp");
            Type surfaceType =
                Type.GetType("SurfaceType, Assembly-CSharp");
            GameObject host = new GameObject("Impact Feedback Test");

            try
            {
                Component controller = host.AddComponent(controllerType);
                controllerType.GetMethod("Configure")
                    .Invoke(controller, new object[] { null, null });
                object none = damageResultType
                    .GetProperty(
                        "None",
                        BindingFlags.Public | BindingFlags.Static)
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
                controllerType.GetMethod("Present")
                    .Invoke(controller, new[] { result });

                GameObject metal = GameObject.Find("Metal Impact(Clone)");
                GameObject sparks = GameObject.Find("Metal Spark Burst");
                Assert.That(metal, Is.Not.Null);
                Assert.That(sparks, Is.Not.Null);
                Assert.That(
                    metal.transform.Find("Silver Metal Dent"),
                    Is.Not.Null);
                Assert.That(
                    metal.transform.Find("Hot Impact Core"),
                    Is.Not.Null);
                Assert.That(
                    GameObject.Find("Concrete(Clone)"),
                    Is.Null);

                UnityEngine.Object.DestroyImmediate(metal);
                UnityEngine.Object.DestroyImmediate(sparks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CrosshairShowsDistinctFeedbackAndDynamicGap()
        {
            Type crosshairType =
                Type.GetType("PlayerCrosshairPresenter, Assembly-CSharp");
            Type feedbackType =
                Type.GetType("HitFeedbackKind, Assembly-CSharp");
            GameObject player = new GameObject("Crosshair Test");

            try
            {
                Component crosshair = player.AddComponent(crosshairType);
                crosshairType.GetMethod("SetState")
                    .Invoke(crosshair, new object[] { 0f, true });
                float hip = (float)crosshairType.GetProperty("CurrentGap")
                    .GetValue(crosshair);
                crosshairType.GetMethod("SetMotionState")
                    .Invoke(crosshair, new object[] { 1f, false });
                float moving = (float)crosshairType.GetProperty("CurrentGap")
                    .GetValue(crosshair);
                crosshairType.GetMethod("AddFireBloom")
                    .Invoke(crosshair, null);
                float fired = (float)crosshairType.GetProperty("CurrentGap")
                    .GetValue(crosshair);
                object headshot = Enum.Parse(feedbackType, "Headshot");
                crosshairType.GetMethod("ShowHitFeedback")
                    .Invoke(crosshair, new[] { headshot });

                Assert.That(moving, Is.GreaterThan(hip));
                Assert.That(fired, Is.GreaterThan(moving));
                Assert.That(
                    crosshairType.GetProperty("CurrentHitFeedback")
                        .GetValue(crosshair).ToString(),
                    Is.EqualTo("Headshot"));
                Assert.That(
                    (int)crosshairType.GetProperty("HitFeedbackCount")
                        .GetValue(crosshair),
                    Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void CombatFeedbackAudioProfileContainsRequiredClips()
        {
            Type profileType = Type.GetType(
                "CombatFeedbackAudioProfile, Assembly-CSharp");
            UnityEngine.Object profile = Resources.Load(
                "CombatFeedbackAudio",
                profileType);

            Assert.That(profile, Is.Not.Null);
            Assert.That(
                profileType.GetField("ConcreteImpact").GetValue(profile),
                Is.Not.Null);
            Assert.That(
                profileType.GetField("MetalImpact").GetValue(profile),
                Is.Not.Null);
            Assert.That(
                profileType.GetField("PlayerDamaged").GetValue(profile),
                Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator PlayerDamageTriggersDedicatedFeedback()
        {
            Type feedbackType = Type.GetType(
                "PlayerCombatFeedbackController, Assembly-CSharp");
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageInfoType =
                Type.GetType("DamageInfo, Assembly-CSharp");
            GameObject player = new GameObject("Damage Feedback Player");

            try
            {
                Component feedback = player.AddComponent(feedbackType);
                yield return null;
                Component health = player.GetComponent(healthType);
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        10f,
                        Vector3.zero,
                        Vector3.forward,
                        null
                    });
                healthType.GetMethod("ApplyDamage")
                    .Invoke(health, new[] { damage });

                Assert.That(
                    (int)feedbackType.GetProperty("DamageFeedbackCount")
                        .GetValue(feedback),
                    Is.EqualTo(1));
                Assert.That(
                    player.GetComponents<AudioSource>().Length,
                    Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        private static object CreateShotResult(
            Type damageResultType,
            Type hitRegionType,
            Type shotResultType,
            Type surfaceType,
            string regionName,
            bool killed)
        {
            object region = Enum.Parse(hitRegionType, regionName);
            object damage = Activator.CreateInstance(
                damageResultType,
                new object[] { true, killed, 20f, region });
            return Activator.CreateInstance(
                shotResultType,
                new object[]
                {
                    true,
                    Vector3.one,
                    Vector3.up,
                    Enum.Parse(surfaceType, "Concrete"),
                    damage
                });
        }
    }
}
