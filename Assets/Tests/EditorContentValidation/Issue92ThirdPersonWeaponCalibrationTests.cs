using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue92ThirdPersonWeaponCalibrationTests
{
    private const string PreviewScenePath =
        "Assets/Scenes/CharacterCalibration/WeaponCalibration.unity";
    private PlayerAppearanceCatalog appearances;
    private ThirdPersonWeaponCatalog weapons;
    private ThirdPersonWeaponCalibrationMatrix matrix;

    [SetUp]
    public void SetUp()
    {
        appearances = AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.DefaultAssetPath);
        weapons = AssetDatabase.LoadAssetAtPath<ThirdPersonWeaponCatalog>(
            ThirdPersonWeaponCatalog.DefaultAssetPath);
        matrix = AssetDatabase.LoadAssetAtPath<
            ThirdPersonWeaponCalibrationMatrix>(
            ThirdPersonWeaponCalibrationMatrix.DefaultAssetPath);
    }

    [Test]
    public void CatalogPersistsTwoIndependentlyCalibratedWeaponPrefabs()
    {
        Assert.That(weapons, Is.Not.Null);
        Assert.That(weapons.Definitions.Count, Is.EqualTo(2));
        Assert.That(weapons.Definitions.Select(value => value.Kind),
            Is.EquivalentTo(new[]
            {
                ThirdPersonWeaponKind.Rifle,
                ThirdPersonWeaponKind.Handgun
            }));
        Assert.That(weapons.TryValidate(out string error), Is.True, error);

        ThirdPersonWeaponRig rifle = weapons.Definitions.Single(value =>
            value.Kind == ThirdPersonWeaponKind.Rifle).CalibratedPrefab
            .GetComponent<ThirdPersonWeaponRig>();
        ThirdPersonWeaponRig handgun = weapons.Definitions.Single(value =>
            value.Kind == ThirdPersonWeaponKind.Handgun).CalibratedPrefab
            .GetComponent<ThirdPersonWeaponRig>();
        Assert.That(rifle.CalibrationRoot.localPosition,
            Is.Not.EqualTo(handgun.CalibrationRoot.localPosition),
            "步枪和手枪必须保存各自校准值，不能复制 AR 最终 Transform。");
        Assert.That(rifle.CalibrationRoot.localRotation,
            Is.Not.EqualTo(handgun.CalibrationRoot.localRotation));
    }

    [Test]
    public void EveryWeaponHasCompleteSafeCalibrationHierarchy()
    {
        foreach (ThirdPersonWeaponDefinition definition in weapons.Definitions)
        {
            ThirdPersonWeaponRig rig =
                definition.CalibratedPrefab.GetComponent<ThirdPersonWeaponRig>();
            Assert.That(rig, Is.Not.Null, definition.StableId);
            Assert.That(rig.TryValidate(out string error), Is.True,
                $"{definition.StableId}: {error}");
            Assert.That(rig.RightHandReference.localPosition,
                Is.EqualTo(Vector3.zero), definition.StableId);
            Assert.That(rig.LeftHandGrip, Is.Not.Null, definition.StableId);
            Assert.That(rig.AimPoint, Is.Not.Null, definition.StableId);
            Assert.That(rig.MuzzlePoint, Is.Not.Null, definition.StableId);
            Assert.That(rig.CasingEjectionPoint, Is.Not.Null,
                definition.StableId);
            Assert.That(Vector3.Dot(rig.MuzzlePoint.forward,
                    rig.transform.forward), Is.GreaterThan(0.9f),
                $"{definition.StableId} 枪口方向必须沿 +Z。");
            Assert.That(definition.CalibratedPrefab
                    .GetComponentsInChildren<Collider>(true), Is.Empty,
                definition.StableId);
            Assert.That(definition.CalibratedPrefab
                    .GetComponentsInChildren<Rigidbody>(true), Is.Empty,
                definition.StableId);
        }
    }

    [Test]
    public void CalibrationMatrixCoversThreeCharactersByTwoWeapons()
    {
        Assert.That(appearances, Is.Not.Null);
        Assert.That(matrix, Is.Not.Null);
        string[] expected =
            (from appearance in appearances.Definitions
             from weapon in weapons.Definitions
             select $"{appearance.StableId}|{weapon.StableId}")
            .OrderBy(value => value).ToArray();
        string[] actual = matrix.Entries.Select(entry =>
                $"{entry.AppearanceId}|{entry.WeaponId}")
            .OrderBy(value => value).ToArray();
        Assert.That(actual, Has.Length.EqualTo(6));
        Assert.That(actual, Is.EqualTo(expected));

        ThirdPersonWeaponCalibrationAuditReport report =
            ThirdPersonWeaponCalibrationAuditor.Audit(
                weapons, appearances, matrix);
        Assert.That(report.IsValid, Is.True,
            string.Join(Environment.NewLine,
                report.Issues.Select(issue => issue.Message)));
    }

    [Test]
    public void ReplicaUsesCatalogAndContainsNoLegacyMagicOffsets()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Networking/CoopPlayerReplica.prefab");
        NetworkPlayerAppearancePresenter presenter =
            prefab.GetComponent<NetworkPlayerAppearancePresenter>();
        Assert.That(presenter.WeaponCatalog, Is.SameAs(weapons));
        string[] forbidden =
        {
            "weaponLocalPosition", "weaponLocalEuler", "weaponLocalScale"
        };
        string[] fields = typeof(NetworkPlayerAppearancePresenter)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                       BindingFlags.Public)
            .Select(value => value.Name).ToArray();
        Assert.That(fields.Intersect(forbidden), Is.Empty,
            "运行时 Presenter 不得继续保存手工魔法偏移。 ");
    }

    [Test]
    public void PreviewSceneSwitchesCharactersWeaponsAndViews()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            PreviewScenePath);
        try
        {
            WeaponCalibrationPreviewController preview = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    WeaponCalibrationPreviewController>(true)).Single();
            for (int character = 0; character < 3; character++)
            for (int weapon = 0; weapon < 2; weapon++)
            {
                preview.SelectCharacter(character);
                preview.SelectWeapon(weapon);
                Assert.That(preview.AppearanceInstance, Is.Not.Null);
                Assert.That(preview.WeaponInstance, Is.Not.Null);
                Assert.That(preview.SelectedCharacter, Is.EqualTo(character));
                Assert.That(preview.SelectedWeapon, Is.EqualTo(weapon));
            }
            foreach (WeaponCalibrationView view in Enum.GetValues(
                         typeof(WeaponCalibrationView)))
            {
                preview.SetView(view);
                Assert.That(preview.SelectedView, Is.EqualTo(view));
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
