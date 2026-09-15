using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class Issue68PlayerGameplayRigAssetTests
{
    private const string RigPath =
        "Assets/Resources/Player/PlayerGameplayRig.prefab";
    private const string CityNewPath =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

    [Test]
    public void ReusableRigOwnsCompleteSerializedDependencies()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RigPath);

        try
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.tag, Is.EqualTo("Player"));
            Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(root.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
            AssertSingle<PlayerGameplayRig>(root);
            AssertSingle<PlayerController>(root);
            AssertSingle<PlayerInputReader>(root);
            AssertSingle<PlayerAnimatorController>(root);
            AssertSingle<PlayerAnimationEvents>(root);
            AssertSingle<CharacterController>(root);
            AssertSingle<PlayerRecoilController>(root);
            AssertSingle<PlayerCombatController>(root);
            AssertSingle<WeaponLoadoutController>(root);
            AssertSingle<ShotTracerPool>(root);
            AssertSingle<PlayerCombatCompositionRoot>(root);

            PlayerGameplayRig rig = root.GetComponent<PlayerGameplayRig>();
            Assert.That(rig.TryValidate(out string error), Is.True, error);
            AssertCompositionReferences(root.GetComponent<
                PlayerCombatCompositionRoot>());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void ReusableRigContainsCameraArmsAndTwoPlayableWeapons()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RigPath);

        try
        {
            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
            WeaponController[] weapons =
                root.GetComponentsInChildren<WeaponController>(true);
            WeaponLoadoutController loadout =
                root.GetComponent<WeaponLoadoutController>();

            Assert.That(cameras, Has.Length.EqualTo(1));
            Assert.That(cameras[0].name, Is.EqualTo("MainCamera"));
            Assert.That(cameras[0].CompareTag("MainCamera"), Is.True);
            Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Empty,
                "First-person arms are missing from the reusable rig.");
            Assert.That(weapons, Has.Length.EqualTo(2));
            Assert.That(weapons.Select(weapon => weapon.name),
                Is.EquivalentTo(new[] { "AR", "Pistol" }));
            Assert.That(loadout.WeaponCount, Is.EqualTo(2));

            for (int index = 0; index < weapons.Length; index++)
            {
                Assert.That(loadout.GetWeapon(index), Is.SameAs(weapons[index]));
                Assert.That(weapons[index].GetComponent<Animator>(), Is.Not.Null);
                Assert.That(weapons[index].MuzzleTransform, Is.Not.Null);
                Assert.That(
                    weapons[index].MuzzleTransform.IsChildOf(root.transform),
                    Is.True);
            }

            foreach (Transform transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject),
                    Is.Zero,
                    $"{GetPath(transform, root.transform)} has a missing script.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void CityNewUsesReusableRigWithoutGameplayOverrides()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(CityNewPath);

        try
        {
            PlayerGameplayRig rig = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    PlayerGameplayRig>(true))
                .SingleOrDefault();
            Assert.That(rig, Is.Not.Null);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    rig.gameObject),
                Is.EqualTo(RigPath));
            Assert.That(PrefabUtility.GetAddedComponents(rig.gameObject),
                Is.Empty,
                "Gameplay components must be owned by the reusable prefab.");
            Assert.That(PrefabUtility.GetAddedGameObjects(rig.gameObject),
                Is.Empty,
                "Weapons and presentation must be owned by the reusable prefab.");

            PropertyModification[] modifications =
                PrefabUtility.GetPropertyModifications(rig.gameObject) ??
                Array.Empty<PropertyModification>();
            GameObject prefabRoot =
                AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);

            foreach (PropertyModification modification in modifications)
            {
                bool rootTarget =
                    modification.target == prefabRoot ||
                    modification.target == prefabRoot.transform;
                bool placementOnly =
                    modification.propertyPath == "m_Name" ||
                    modification.propertyPath.StartsWith("m_LocalPosition") ||
                    modification.propertyPath.StartsWith("m_LocalRotation") ||
                    modification.propertyPath.StartsWith(
                        "m_LocalEulerAnglesHint") ||
                    modification.propertyPath == "m_RootOrder";
                Assert.That(rootTarget && placementOnly, Is.True,
                    $"Unexpected CityNew gameplay override: " +
                    $"{modification.target?.name}.{modification.propertyPath}");
            }

            Assert.That(scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        CityNewPlayerModeInstaller>(true))
                    .Count(),
                Is.EqualTo(1));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void ReusableRigCanInstantiateIntoAnEmptyPreviewScene()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;

        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Assert.That(prefab, Is.Not.Null);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Assert.That(instance, Is.Not.Null);
            PlayerGameplayRig rig = instance.GetComponent<PlayerGameplayRig>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.TryValidate(out string error), Is.True, error);
            Assert.That(instance.GetComponentsInChildren<Camera>(true),
                Has.Length.EqualTo(1));
            Assert.That(instance.GetComponentsInChildren<WeaponController>(true),
                Has.Length.EqualTo(2));
        }
        finally
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }

            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void AssertCompositionReferences(
        PlayerCombatCompositionRoot root)
    {
        var serialized = new SerializedObject(root);
        string[] fields =
        {
            "input",
            "player",
            "recoil",
            "animator",
            "combat",
            "loadout",
            "tracerPool",
            "combatSoundEvents",
            "feedbackAudio",
            "hudVisualProfile"
        };

        foreach (string field in fields)
        {
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, field);
            Assert.That(property.objectReferenceValue, Is.Not.Null, field);
        }
    }

    private static void AssertSingle<T>(GameObject root) where T : Component
    {
        Assert.That(root.GetComponents<T>(), Has.Length.EqualTo(1),
            $"The rig root must contain exactly one {typeof(T).Name}.");
    }

    private static string GetPath(Transform target, Transform root)
    {
        return target == root
            ? root.name
            : root.name + "/" + AnimationUtility.CalculateTransformPath(
                target,
                root);
    }
}
