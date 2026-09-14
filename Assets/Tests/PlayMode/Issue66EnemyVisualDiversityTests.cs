using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue66EnemyVisualDiversityTests
    {
        private static readonly IReadOnlyDictionary<string, string> ExpectedAddresses =
            new Dictionary<string, string>
            {
                ["enemy.archetype.spider_assault"] = "enemy/trilobite-assault",
                ["enemy.archetype.spider_raider"] = "enemy/spider",
                ["enemy.archetype.spider_suppressor"] = "enemy/eye-drone-suppressor",
                ["enemy.archetype.spider_support"] = "enemy/eye-drone-support",
                ["enemy.archetype.spider_elite"] = "enemy/quad-shell-elite"
            };

        [Test]
        public void ContentCatalogMapsEveryCombatRoleToItsExpectedVisualAddress()
        {
            CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.EnemyArchetypes.Count, Is.EqualTo(5));

            foreach (EnemyArchetypeDefinition archetype in catalog.EnemyArchetypes)
            {
                Assert.That(ExpectedAddresses, Contains.Key(archetype.StableId));
                Assert.That(
                    archetype.TemplateAddress,
                    Is.EqualTo(ExpectedAddresses[archetype.StableId]),
                    archetype.StableId);
            }

            Assert.That(
                catalog.EnemyArchetypes.Select(archetype => archetype.TemplateAddress).Distinct().Count(),
                Is.EqualTo(5),
                "支援无人机使用独立地址和配色，因此五种角色都应拥有独立模板。");
        }

        [UnityTest]
        public IEnumerator NewEnemyAddressesLoadCombatReadyAnimatedPrefabs()
        {
            foreach (string address in ExpectedAddresses.Values.Where(value => value != "enemy/spider"))
            {
                AsyncOperationHandle<GameObject> load =
                    Addressables.LoadAssetAsync<GameObject>(address);
                yield return load;

                try
                {
                    Assert.That(load.Status, Is.EqualTo(AsyncOperationStatus.Succeeded), address);
                    GameObject prefab = load.Result;
                    Assert.That(prefab, Is.Not.Null, address);
                    Assert.That(prefab.GetComponent<EnemyController>(), Is.Not.Null, address);
                    Assert.That(prefab.GetComponent<EnemyVisualAnimator>(), Is.Not.Null, address);
                    Assert.That(prefab.GetComponentInChildren<Animator>(true), Is.Not.Null, address);
                    Assert.That(
                        prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController,
                        Is.Not.Null,
                        address);
                    Assert.That(prefab.GetComponent<BoxCollider>(), Is.Not.Null, address);
                    Assert.That(prefab.GetComponent<BoxCollider>().size.sqrMagnitude, Is.GreaterThan(0.25f));
                }
                finally
                {
                    if (load.IsValid())
                    {
                        Addressables.Release(load);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator AttackAndPoolResetRestoreLocomotionAnimatorState()
        {
            AsyncOperationHandle<GameObject> load =
                Addressables.LoadAssetAsync<GameObject>("enemy/trilobite-assault");
            yield return load;
            Assert.That(load.Status, Is.EqualTo(AsyncOperationStatus.Succeeded));
            GameObject instance = Object.Instantiate(load.Result);

            try
            {
                EnemyVisualAnimator visual = instance.GetComponent<EnemyVisualAnimator>();
                Assert.That(visual, Is.Not.Null);
                visual.PlayAttack();
                Assert.That(visual.IsAttacking, Is.True);
                visual.PrepareForPool();
                Assert.That(visual.IsAttacking, Is.False);
                visual.ResetForSpawn();
                yield return null;
                Assert.That(visual.IsAttacking, Is.False);
            }
            finally
            {
                Object.Destroy(instance);
                if (load.IsValid())
                {
                    Addressables.Release(load);
                }
            }
        }

        [UnityTest]
        public IEnumerator TrilobiteShowsItsNameWithoutAbilityOrAffixStatus()
        {
            AsyncOperationHandle<GameObject> load =
                Addressables.LoadAssetAsync<GameObject>("enemy/trilobite-assault");
            yield return load;
            Assert.That(load.Status, Is.EqualTo(AsyncOperationStatus.Succeeded));
            GameObject instance = Object.Instantiate(load.Result);

            try
            {
                yield return null;
                EnemyController enemy = instance.GetComponent<EnemyController>();
                EnemyBurnEffectController overhead =
                    instance.GetComponent<EnemyBurnEffectController>();
                Assert.That(enemy.DisplayName, Is.EqualTo("TRILOBITE"));
                Assert.That(overhead, Is.Not.Null);
                Assert.That(overhead.StatusVisible, Is.True);
                Assert.That(overhead.StatusText, Is.EqualTo("TRILOBITE"));
            }
            finally
            {
                Object.Destroy(instance);
                if (load.IsValid())
                {
                    Addressables.Release(load);
                }
            }
        }

        [UnityTest]
        public IEnumerator EyeDroneRolesUseDistinctPaletteAndBeaconColor()
        {
            AsyncOperationHandle<GameObject> suppressorLoad =
                Addressables.LoadAssetAsync<GameObject>(
                    "enemy/eye-drone-suppressor");
            AsyncOperationHandle<GameObject> supportLoad =
                Addressables.LoadAssetAsync<GameObject>(
                    "enemy/eye-drone-support");
            yield return suppressorLoad;
            yield return supportLoad;

            try
            {
                Assert.That(suppressorLoad.Status,
                    Is.EqualTo(AsyncOperationStatus.Succeeded));
                Assert.That(supportLoad.Status,
                    Is.EqualTo(AsyncOperationStatus.Succeeded));
                Material suppressorBody = BodyMaterial(suppressorLoad.Result);
                Material supportBody = BodyMaterial(supportLoad.Result);
                Material suppressorBeacon = BeaconMaterial(
                    suppressorLoad.Result);
                Material supportBeacon = BeaconMaterial(supportLoad.Result);

                Assert.That(suppressorBody, Is.Not.Null);
                Assert.That(supportBody, Is.Not.Null);
                Assert.That(suppressorBody, Is.Not.SameAs(supportBody));
                Assert.That(
                    ColorDistance(
                        suppressorBody.GetColor("_BaseColor"),
                        supportBody.GetColor("_BaseColor")),
                    Is.GreaterThan(0.4f));
                Assert.That(suppressorBeacon, Is.Not.Null);
                Assert.That(supportBeacon, Is.Not.Null);
                Assert.That(
                    ColorDistance(
                        suppressorBeacon.GetColor("_BaseColor"),
                        supportBeacon.GetColor("_BaseColor")),
                    Is.GreaterThan(0.4f));
            }
            finally
            {
                if (suppressorLoad.IsValid())
                {
                    Addressables.Release(suppressorLoad);
                }

                if (supportLoad.IsValid())
                {
                    Addressables.Release(supportLoad);
                }
            }
        }

        [UnityTest]
        public IEnumerator EveryNewVisualUsesExplicitHitRegionsAndAdaptiveOverhead()
        {
            foreach (string address in ExpectedAddresses.Values.Where(
                         value => value != "enemy/spider"))
            {
                AsyncOperationHandle<GameObject> load =
                    Addressables.LoadAssetAsync<GameObject>(address);
                yield return load;
                GameObject instance = null;

                try
                {
                    Assert.That(load.Status,
                        Is.EqualTo(AsyncOperationStatus.Succeeded), address);
                    GameObject prefab = load.Result;
                    Assert.That(prefab.GetComponent<Health>(), Is.Not.Null,
                        address);
                    DamageHitbox[] prefabHitboxes =
                        prefab.GetComponentsInChildren<DamageHitbox>(true);
                    Assert.That(prefabHitboxes, Has.Length.EqualTo(2),
                        $"{address} 必须在 Prefab 中显式保存身体和头部命中区。");

                    instance = Object.Instantiate(prefab);
                    yield return null;
                    DamageHitbox[] hitboxes =
                        instance.GetComponentsInChildren<DamageHitbox>(true);
                    DamageHitbox body = hitboxes.Single(
                        value => value.Region == HitRegion.Body);
                    DamageHitbox head = hitboxes.Single(
                        value => value.Region == HitRegion.Head);
                    Assert.That(body.DamageMultiplier,
                        Is.EqualTo(1f).Within(0.001f), address);
                    Assert.That(head.DamageMultiplier,
                        Is.EqualTo(2f).Within(0.001f), address);
                    Assert.That(
                        head.GetComponent<BoxCollider>().bounds.center.y,
                        Is.GreaterThan(
                            body.GetComponent<BoxCollider>().bounds.center.y),
                        address);

                    Bounds visualBounds = VisualBounds(instance);
                    AssertHitboxFitsVisual(body, visualBounds, address);
                    AssertHitboxFitsVisual(head, visualBounds, address);

                    BoxCollider envelope =
                        instance.GetComponent<BoxCollider>();
                    Assert.That(envelope, Is.Not.Null, address);
                    Assert.That(envelope.enabled, Is.False,
                        "根包围盒不能抢先截获伤害射线。");
                    EnemyBurnEffectController overhead =
                        instance.GetComponent<EnemyBurnEffectController>();
                    float expectedHeight = envelope.center.y +
                        envelope.size.y * 0.5f +
                        overhead.OverheadClearance;
                    Assert.That(
                        overhead.OverheadLocalHeight,
                        Is.EqualTo(expectedHeight).Within(0.015f),
                        address);
                    Assert.That(
                        overhead.OverheadLocalHeight,
                        Is.GreaterThan(visualBounds.max.y -
                            instance.transform.position.y),
                        address);
                }
                finally
                {
                    if (instance != null)
                    {
                        Object.Destroy(instance);
                    }

                    if (load.IsValid())
                    {
                        Addressables.Release(load);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator MissingLocomotionClipUsesConfigurableHoverFeedback()
        {
            AsyncOperationHandle<GameObject> droneLoad =
                Addressables.LoadAssetAsync<GameObject>(
                    "enemy/eye-drone-suppressor");
            AsyncOperationHandle<GameObject> trilobiteLoad =
                Addressables.LoadAssetAsync<GameObject>(
                    "enemy/trilobite-assault");
            yield return droneLoad;
            yield return trilobiteLoad;
            GameObject drone = null;
            GameObject trilobite = null;

            try
            {
                drone = Object.Instantiate(droneLoad.Result);
                trilobite = Object.Instantiate(trilobiteLoad.Result);
                yield return null;
                EnemyVisualAnimator droneAnimator =
                    drone.GetComponent<EnemyVisualAnimator>();
                EnemyVisualAnimator trilobiteAnimator =
                    trilobite.GetComponent<EnemyVisualAnimator>();
                Transform droneVisual = drone.transform.Find("Visual");
                Transform trilobiteVisual = trilobite.transform.Find("Visual");
                Vector3 dronePosition = droneVisual.localPosition;
                Quaternion droneRotation = droneVisual.localRotation;
                Vector3 trilobitePosition = trilobiteVisual.localPosition;
                Quaternion trilobiteRotation = trilobiteVisual.localRotation;

                Assert.That(droneAnimator.UsesProceduralLocomotion, Is.True);
                Assert.That(droneAnimator.HoverBobAmplitude,
                    Is.GreaterThan(0f));
                Assert.That(droneAnimator.HoverMoveTiltDegrees,
                    Is.GreaterThan(0f));
                Assert.That(trilobiteAnimator.UsesProceduralLocomotion,
                    Is.False,
                    "具备 Run 动画的模型不应叠加悬浮程序动画。");

                droneAnimator.UpdateLocomotionFeedback(
                    Vector3.right,
                    0.1f);
                trilobiteAnimator.UpdateLocomotionFeedback(
                    Vector3.right,
                    0.1f);
                Assert.That(droneAnimator.IsProceduralMovementActive,
                    Is.True);
                Assert.That(
                    Vector3.Distance(
                        dronePosition,
                        droneVisual.localPosition) > 0.001f ||
                    Quaternion.Angle(
                        droneRotation,
                        droneVisual.localRotation) > 0.1f,
                    Is.True,
                    "悬浮移动必须提供轻微起伏或倾斜反馈。");
                Assert.That(trilobiteAnimator.IsProceduralMovementActive,
                    Is.False);
                Assert.That(trilobiteVisual.localPosition,
                    Is.EqualTo(trilobitePosition));
                Assert.That(trilobiteVisual.localRotation,
                    Is.EqualTo(trilobiteRotation));

                droneAnimator.ResetForSpawn();
                Assert.That(droneAnimator.IsProceduralMovementActive,
                    Is.False);
                Assert.That(droneVisual.localPosition,
                    Is.EqualTo(dronePosition));
                Assert.That(droneVisual.localRotation,
                    Is.EqualTo(droneRotation));
            }
            finally
            {
                if (drone != null)
                {
                    Object.Destroy(drone);
                }

                if (trilobite != null)
                {
                    Object.Destroy(trilobite);
                }

                if (droneLoad.IsValid())
                {
                    Addressables.Release(droneLoad);
                }

                if (trilobiteLoad.IsValid())
                {
                    Addressables.Release(trilobiteLoad);
                }
            }
        }

        private static Material BodyMaterial(GameObject prefab)
        {
            Transform visual = prefab.transform.Find("Visual");
            return visual != null
                ? visual.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(value => value.sharedMaterials)
                    .FirstOrDefault(value => value != null)
                : null;
        }

        private static Material BeaconMaterial(GameObject prefab)
        {
            Transform beacon = prefab.transform.Find("Role Beacon");
            return beacon != null
                ? beacon.GetComponent<Renderer>()?.sharedMaterial
                : null;
        }

        private static float ColorDistance(Color left, Color right)
        {
            var difference = new Vector4(
                left.r - right.r,
                left.g - right.g,
                left.b - right.b,
                left.a - right.a);
            return difference.magnitude;
        }

        private static Bounds VisualBounds(GameObject instance)
        {
            Renderer[] renderers = instance.transform.Find("Visual")
                .GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;

            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void AssertHitboxFitsVisual(
            DamageHitbox hitbox,
            Bounds visualBounds,
            string address)
        {
            Bounds hitboxBounds = hitbox.GetComponent<BoxCollider>().bounds;
            Bounds tolerance = visualBounds;
            tolerance.Expand(0.18f);
            Assert.That(tolerance.Contains(hitboxBounds.min), Is.True,
                $"{address}: {hitbox.Region} 命中区最小点超出模型轮廓。");
            Assert.That(tolerance.Contains(hitboxBounds.max), Is.True,
                $"{address}: {hitbox.Region} 命中区最大点超出模型轮廓。");
        }
    }
}
