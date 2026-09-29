using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public class Issue7FeedbackTests
    {
        private Scene ownedFeedbackScene;

        [UnityTearDown]
        public IEnumerator TearDownOwnedFeedbackScene()
        {
            if (!ownedFeedbackScene.IsValid() || !ownedFeedbackScene.isLoaded)
            {
                ownedFeedbackScene = default;
                yield break;
            }

            // Only the CityNew scene explicitly loaded by this fixture is
            // ours. Do not delete unrelated same-named effects from a scene
            // supplied by another fixture merely to satisfy an assertion.
            Scene cleanupScene = SceneManager.CreateScene(
                "Issue7 Feedback Cleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanupScene);
            Scene sceneToUnload = ownedFeedbackScene;
            ownedFeedbackScene = default;
            yield return SceneManager.UnloadSceneAsync(sceneToUnload);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DamageTargetImpactFollowsHitObjectAndReturnsWhenDisabled()
        {
            Type poolType = RuntimeTypeResolver.GetType(
                "CombatEffectPool");
            Type shotResultType = RuntimeTypeResolver.GetType(
                "ShotResult");
            Type damageResultType = RuntimeTypeResolver.GetType(
                "DamageResult");
            Type hitRegionType = RuntimeTypeResolver.GetType(
                "HitRegion");
            Type surfaceType = RuntimeTypeResolver.GetType(
                "SurfaceType");
            GameObject host = new GameObject("Impact Pool Host");
            GameObject target = new GameObject("Moving Damage Target");
            GameObject impactPrefab = new GameObject("Impact Prefab");

            try
            {
                target.transform.position = new Vector3(2f, 1f, 3f);
                GameObject hitObject = new GameObject("Moving Hitbox");
                hitObject.transform.SetParent(target.transform, false);
                hitObject.transform.localPosition = new Vector3(0f, 1f, 0f);
                Component pool = host.AddComponent(poolType);
                poolType.GetMethod("Configure").Invoke(
                    pool,
                    new object[] { impactPrefab });
                object damage = Activator.CreateInstance(
                    damageResultType,
                    new object[]
                    {
                        true,
                        false,
                        10f,
                        Enum.Parse(hitRegionType, "Body")
                    });
                Vector3 hitPoint = hitObject.transform.TransformPoint(
                    new Vector3(0.2f, 0.1f, 0f));
                object result = Activator.CreateInstance(
                    shotResultType,
                    new object[]
                    {
                        true,
                        hitPoint,
                        Vector3.forward,
                        Enum.Parse(surfaceType, "Concrete"),
                        damage,
                        target,
                        hitObject
                    });

                poolType.GetMethod("PresentImpact").Invoke(
                    pool,
                    new[] { result });
                Assert.That(
                    (int)poolType.GetProperty("ConcreteActiveCount").GetValue(pool),
                    Is.EqualTo(1));
                GameObject impact = FindActiveEffectInPool(pool, "Concrete(Clone)");
                Vector3 relativePosition =
                    impact.transform.position - hitObject.transform.position;

                target.transform.position += new Vector3(4f, 0f, -2f);
                yield return null;

                Assert.That(
                    Vector3.Distance(
                        impact.transform.position,
                        hitObject.transform.position + relativePosition),
                    Is.LessThan(0.001f),
                    "怪物移动后，弹痕必须跟随实际受击部位。");

                target.SetActive(false);
                yield return null;

                Assert.That(impact.activeSelf, Is.False,
                    "受击目标失活后，绑定弹痕必须立即回收到对象池。");
                Assert.That(
                    (int)poolType.GetProperty("ConcreteActiveCount").GetValue(pool),
                    Is.Zero,
                    "目标失活后本次池不能仍占用该混凝土弹痕。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(impactPrefab);
            }
        }

        [UnityTest]
        public IEnumerator CombatFeedbackVisualProfileKeepsBuildShaders()
        {
            Type profileType = RuntimeTypeResolver.GetType(
                "CombatFeedbackVisualProfile");
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
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageInfoType =
                RuntimeTypeResolver.GetType("DamageInfo");
            Type damageResultType =
                RuntimeTypeResolver.GetType("DamageResult");

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
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageInfoType =
                RuntimeTypeResolver.GetType("DamageInfo");
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
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type hudType =
                RuntimeTypeResolver.GetType("PlayerVitalsHudPresenter");

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
                RuntimeTypeResolver.GetType("DamageResult");
            Type hitRegionType =
                RuntimeTypeResolver.GetType("HitRegion");
            Type shotResultType =
                RuntimeTypeResolver.GetType("ShotResult");
            Type surfaceType =
                RuntimeTypeResolver.GetType("SurfaceType");

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
                RuntimeTypeResolver.GetType("WeaponSpreadState");
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
            Type recoilType = RuntimeTypeResolver.GetType(
                "PlayerRecoilController");
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
                RuntimeTypeResolver.GetType("SurfaceDescriptor");
            Type surfaceType =
                RuntimeTypeResolver.GetType("SurfaceType");
            Type resolverType =
                RuntimeTypeResolver.GetType("SurfaceResolver");
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
                RuntimeTypeResolver.GetType("SurfaceImpactStyle");
            Type surfaceType =
                RuntimeTypeResolver.GetType("SurfaceType");

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
        public void MetalImpactIgnoresUnrelatedConcreteFromAnotherPool()
        {
            // A different weapon/player may legitimately retain a concrete
            // impact while this pool presents a metal hit. The assertion must
            // inspect the pool under test, not every same-named scene object.
            GameObject unrelatedConcrete = new GameObject("Concrete(Clone)");

            try
            {
                MetalImpactDoesNotReuseConcreteVisual();
                Assert.That(unrelatedConcrete.activeSelf, Is.True,
                    "检验本次金属命中不能清除其他角色/池仍在显示的混凝土弹痕。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unrelatedConcrete);
            }
        }

        [Test]
        public void MetalImpactDoesNotReuseConcreteVisual()
        {
            Type controllerType = RuntimeTypeResolver.GetType(
                "WeaponImpactFeedbackController");
            Type shotResultType =
                RuntimeTypeResolver.GetType("ShotResult");
            Type damageResultType =
                RuntimeTypeResolver.GetType("DamageResult");
            Type surfaceType =
                RuntimeTypeResolver.GetType("SurfaceType");
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

                Component pool = (Component)controllerType
                    .GetProperty("EffectPool").GetValue(controller);
                Assert.That(pool, Is.Not.Null);
                GameObject metal = FindActiveEffectInPool(pool, "Metal Impact(Clone)");
                GameObject sparks = FindActiveEffectInPool(pool, "Metal Spark Burst");
                Assert.That(
                    metal.transform.Find("Silver Metal Dent"),
                    Is.Not.Null);
                Assert.That(
                    metal.transform.Find("Hot Impact Core"),
                    Is.Not.Null);
                Assert.That(
                    (int)pool.GetType().GetProperty("ConcreteActiveCount").GetValue(pool),
                    Is.Zero,
                    "金属命中不得借用本次池的混凝土弹痕。");
                Assert.That(
                    (int)pool.GetType().GetProperty("MetalActiveCount").GetValue(pool),
                    Is.EqualTo(1));
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
                RuntimeTypeResolver.GetType("PlayerCrosshairPresenter");
            Type feedbackType =
                RuntimeTypeResolver.GetType("HitFeedbackKind");
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
            Type profileType = RuntimeTypeResolver.GetType(
                "CombatFeedbackAudioProfile");
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
            Type feedbackType = RuntimeTypeResolver.GetType(
                "PlayerCombatFeedbackController");
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageInfoType =
                RuntimeTypeResolver.GetType("DamageInfo");
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            ownedFeedbackScene = SceneManager.GetSceneByPath(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
            yield return null;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component feedback = player.GetComponent(feedbackType);
            Component health = player.GetComponent(healthType);
            int feedbackCountBefore =
                (int)feedbackType.GetProperty("DamageFeedbackCount")
                    .GetValue(feedback);
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
                Is.EqualTo(feedbackCountBefore + 1));
            Assert.That(
                player.GetComponents<AudioSource>().Length,
                Is.GreaterThanOrEqualTo(1));
        }

        private static GameObject FindActiveEffectInPool(Component pool, string effectName)
        {
            GameObject found = null;
            int activeMatches = 0;

            foreach (Transform child in pool.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.activeInHierarchy && child.name == effectName)
                {
                    found = child.gameObject;
                    activeMatches++;
                }
            }

            Assert.That(activeMatches, Is.EqualTo(1),
                $"本次 {pool.gameObject.name} 池应恰好显示一个 {effectName}，不应选中其他池的同名对象。");
            return found;
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
