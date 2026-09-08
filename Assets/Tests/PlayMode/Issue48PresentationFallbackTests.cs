using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class Issue48PresentationFallbackTests
{
    [UnityTest]
    public IEnumerator MissingIconsReturnCachedPlaceholderAndDisposeOwnedResources()
    {
        var catalog = new HudIconCatalog(_ => null);
        Sprite placeholder = catalog.Get(HudIconId.Health);
        Texture2D texture = placeholder.texture;
        Assert.That(placeholder, Is.Not.Null);
        foreach (HudIconId id in Enum.GetValues(typeof(HudIconId)))
        {
            Assert.That(catalog.UsesPlaceholder(id), Is.True);
            Assert.That(catalog.Get(id), Is.SameAs(placeholder));
        }
        Assert.That(catalog.GetWeaponIcon("pistol"), Is.SameAs(placeholder));
        catalog.Dispose();
        catalog.Dispose();
        yield return null;
        Assert.That(placeholder == null, Is.True);
        Assert.That(texture == null, Is.True);
    }

    [UnityTest]
    public IEnumerator DisposingOneCatalogDoesNotDestroySharedSourceOrOtherCatalog()
    {
        Texture2D texture = new(32, 32);
        Sprite source = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
        var first = new HudIconCatalog(_ => source);
        var second = new HudIconCatalog(_ => source);
        Sprite ownedCrop = first.Get(HudIconId.Rifle);
        first.Dispose();
        yield return null;
        Assert.That(ownedCrop == null, Is.True);
        Assert.That(source != null, Is.True);
        Assert.That(second.Get(HudIconId.Health), Is.SameAs(source));
        Assert.That(second.Get(HudIconId.Rifle) != null, Is.True);
        second.Dispose();
        Object.Destroy(source);
        Object.Destroy(texture);
        yield return null;
    }

    [UnityTest]
    public IEnumerator MissingDeathPrefabProducesSmallTemporaryNonPhysicalFeedback()
    {
        GameObject effect = EnemyDeathEffectController.Present(null, Vector3.one);
        Material ownedMaterial = effect.GetComponent<LineRenderer>().sharedMaterial;
        Assert.That(effect.GetComponent<FallbackFeedbackEffect>(), Is.Not.Null);
        Assert.That(effect.GetComponentsInChildren<Collider>(), Is.Empty);
        Assert.That(effect.GetComponentsInChildren<Light>(), Is.Empty);
        Assert.That(effect.GetComponent<LineRenderer>().enabled, Is.True);
        yield return new WaitForSeconds(0.4f);
        yield return null;
        Assert.That(effect == null, Is.True);
        Assert.That(ownedMaterial == null, Is.True);
    }

    [UnityTest]
    public IEnumerator MissingImpactPrefabStillUsesBoundedReusablePool()
    {
        GameObject owner = new("Fallback Impact Test");
        CombatEffectPool pool = CombatEffectPool.Ensure(owner.transform, null);
        var hit = new ShotResult(true, Vector3.zero, Vector3.up,
            SurfaceType.Concrete, DamageResult.None);
        pool.PresentImpact(hit);
        Assert.That(pool.ConcreteActiveCount, Is.EqualTo(1));
        FallbackFeedbackEffect fallback = owner.GetComponentInChildren<FallbackFeedbackEffect>();
        Assert.That(fallback, Is.Not.Null);
        Assert.That(fallback.GetComponent<LineRenderer>().enabled, Is.True);
        Assert.That(fallback.GetComponent<Collider>(), Is.Null);
        int capacity = pool.ConcreteCapacity;
        pool.ReturnAll();
        Assert.That(pool.ConcreteActiveCount, Is.Zero);
        pool.PresentImpact(hit);
        Assert.That(pool.ConcreteCapacity, Is.EqualTo(capacity));
        Assert.That(pool.ConcreteActiveCount, Is.EqualTo(1));
        Object.Destroy(owner);
        yield return null;
        Assert.That(fallback == null, Is.True);
    }
}
