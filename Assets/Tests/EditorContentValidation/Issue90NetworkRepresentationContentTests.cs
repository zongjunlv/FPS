using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class Issue90NetworkRepresentationContentTests
{
    private const string ReplicaPath =
        "Assets/Resources/Networking/CoopPlayerReplica.prefab";
    [Test]
    public void ReplicaUsesCharacterPresenterInsteadOfPrimitivePlaceholder()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            ReplicaPath);
        Assert.That(prefab, Is.Not.Null);
        NetworkPlayerAppearancePresenter presenter =
            prefab.GetComponent<NetworkPlayerAppearancePresenter>();
        Assert.That(presenter, Is.Not.Null);
        Assert.That(presenter.VisualRoot, Is.Not.Null);
        Assert.That(presenter.VisualRoot.name,
            Is.EqualTo("ThirdPersonVisualRoot"));
        Assert.That(prefab.transform.Find("RemotePlayerVisual"), Is.Null,
            "旧胶囊占位表现必须被真实人物工厂替换。");
        ThirdPersonWeaponCatalog catalog = AssetDatabase.LoadAssetAtPath<
            ThirdPersonWeaponCatalog>(
            ThirdPersonWeaponCatalog.DefaultAssetPath);
        Assert.That(presenter.WeaponCatalog, Is.SameAs(catalog));
        Assert.That(presenter.ThirdPersonWeaponPrefab,
            Is.SameAs(catalog.Definitions[0].CalibratedPrefab));
    }

    [Test]
    public void ReplicaContainsNoLocalOnlyCameraAudioHudOrGameplayRig()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            ReplicaPath);
        Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<AudioListener>(true),
            Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<Canvas>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<PlayerInputReader>(true),
            Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<PlayerGameplayRig>(true),
            Is.Empty);

        PlayerGameplayRig localRig = PlayerGameplayRig.LoadPrefab();
        Assert.That(localRig, Is.Not.Null);
        Assert.That(localRig.GetComponentsInChildren<Camera>(true),
            Has.Length.EqualTo(1));
        Assert.That(localRig.GetComponentsInChildren<AudioListener>(true),
            Has.Length.EqualTo(1));
    }

    [Test]
    public void ThirdPersonWeaponIsRenderOnlyAndCarriesNoGameplayComponents()
    {
        ThirdPersonWeaponCatalog catalog = AssetDatabase.LoadAssetAtPath<
            ThirdPersonWeaponCatalog>(
            ThirdPersonWeaponCatalog.DefaultAssetPath);
        Assert.That(catalog, Is.Not.Null);
        foreach (ThirdPersonWeaponDefinition definition in catalog.Definitions)
        {
            GameObject prefab = definition.CalibratedPrefab;
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true),
                Is.Not.Empty, definition.StableId);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),
                Has.Exactly(1).TypeOf<ThirdPersonWeaponRig>(),
                definition.StableId);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty, definition.StableId);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty, definition.StableId);
            Assert.That(prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty, definition.StableId);
        }
    }
}
