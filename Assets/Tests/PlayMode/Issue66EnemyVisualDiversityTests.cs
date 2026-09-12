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
    }
}
