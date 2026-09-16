using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue82PlayerAppearanceRuntimeTests
{
    [UnityTest]
    public IEnumerator FactorySwitchesAllAppearancesAndPreservesAuthorityRoot()
    {
        PlayerAppearanceCatalog catalog = Resources.Load<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.ResourcesPath);
        GameObject prefab = Resources.Load<GameObject>(
            "Content/Characters/PlayerAppearances/PlayerAppearanceHost");
        Assert.That(catalog, Is.Not.Null);
        Assert.That(prefab, Is.Not.Null);
        GameObject root = Object.Instantiate(prefab,
            new Vector3(12f, 0f, -5f), Quaternion.Euler(0f, 25f, 0f));
        try
        {
            PlayerAppearanceHost host = root.GetComponent<PlayerAppearanceHost>();
            CharacterController collision = root.GetComponent<CharacterController>();
            Vector3 rootPosition = root.transform.position;
            Quaternion rootRotation = root.transform.rotation;
            float height = collision.height;
            float radius = collision.radius;
            Vector3 center = collision.center;

            foreach (PlayerAppearanceDefinition definition in catalog.Definitions)
            {
                GameObject instance = host.Apply(definition.StableId);
                yield return null;
                Assert.That(host.UsedFallback, Is.False, definition.StableId);
                Assert.That(host.CurrentDefinition, Is.SameAs(definition));
                Assert.That(instance.transform.parent, Is.SameAs(host.VisualRoot));
                Assert.That(instance.GetComponent<Animator>().avatar,
                    Is.SameAs(definition.Avatar));
                Assert.That(instance.GetComponent<LODGroup>(), Is.Not.Null);
                Assert.That(root.transform.position, Is.EqualTo(rootPosition));
                Assert.That(root.transform.rotation, Is.EqualTo(rootRotation));
                Assert.That(collision.height, Is.EqualTo(height));
                Assert.That(collision.radius, Is.EqualTo(radius));
                Assert.That(collision.center, Is.EqualTo(center));
                Assert.That(host.VisualRoot.GetComponentsInChildren<
                    PlayerAppearanceInstance>(true)
                    .Count(value => value.gameObject.activeSelf), Is.EqualTo(1));
            }

            GameObject fallback = host.Apply("network.unknown.appearance");
            yield return null;
            Assert.That(host.UsedFallback, Is.True);
            Assert.That(host.CurrentDefinition, Is.SameAs(catalog.DefaultDefinition));
            Assert.That(fallback.GetComponent<PlayerAppearanceInstance>().StableId,
                Is.EqualTo(catalog.DefaultAppearanceId));
        }
        finally
        {
            Object.Destroy(root);
        }
    }
}
