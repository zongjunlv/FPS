using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class Issue90NetworkRepresentationContentTests
{
    private const string ReplicaPath =
        "Assets/Resources/Networking/CoopPlayerReplica.prefab";
    private const string WeaponPath =
        "Assets/Resources/Networking/ThirdPersonRifleVisual.prefab";

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
        Assert.That(presenter.ThirdPersonWeaponPrefab,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath)));
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
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            WeaponPath);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Renderer>(true),
            Is.Not.Empty);
        Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),
            Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(prefab.GetComponentsInChildren<AudioSource>(true), Is.Empty);
    }
}
