using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue82PlayerAppearanceTests
{
    private PlayerAppearanceCatalog catalog;

    [SetUp]
    public void SetUp()
    {
        catalog = AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.DefaultAssetPath);
    }

    [Test]
    public void CatalogContainsThreeCompleteCosmeticOnlyDefinitions()
    {
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryValidate(out string error), Is.True, error);
        Assert.That(catalog.Definitions.Count, Is.EqualTo(3));
        Assert.That(catalog.Definitions.Select(value => value.StableId).Distinct()
            .Count(), Is.EqualTo(3));
        foreach (PlayerAppearanceDefinition definition in catalog.Definitions)
        {
            Assert.That(definition.Avatar.isValid && definition.Avatar.isHuman,
                Is.True, definition.StableId);
            Assert.That(definition.AnimatorController, Is.Not.Null);
            Assert.That(definition.Materials, Is.Not.Empty);
            Assert.That(definition.CollisionReference.IsValid, Is.True);
            Assert.That(definition.LodConfiguration.IsValid, Is.True);
            Assert.That(definition.VisualPrefab.GetComponent<PlayerController>(),
                Is.Null);
            Assert.That(definition.VisualPrefab.GetComponent<CharacterController>(),
                Is.Null);
        }
    }

    [Test]
    public void CatalogRejectsDuplicateDefinitionsAndInvalidAvatar()
    {
        PlayerAppearanceCatalog duplicate =
            ScriptableObject.CreateInstance<PlayerAppearanceCatalog>();
        PlayerAppearanceDefinition invalid =
            ScriptableObject.CreateInstance<PlayerAppearanceDefinition>();
        var visual = new GameObject("Invalid Appearance");
        try
        {
            duplicate.Configure(catalog.DefaultAppearanceId, new[]
            {
                catalog.Definitions[0], catalog.Definitions[0],
                catalog.Definitions[2]
            });
            Assert.That(duplicate.TryValidate(out string duplicateError), Is.False);
            Assert.That(duplicateError, Does.Contain("重复"));

            invalid.Configure("invalid.avatar", "无效角色", visual, null,
                catalog.DefaultDefinition.AnimatorController,
                catalog.DefaultDefinition.Materials,
                catalog.DefaultDefinition.CollisionReference,
                catalog.DefaultDefinition.ViewOffset,
                catalog.DefaultDefinition.LodConfiguration);
            Assert.That(invalid.TryValidate(out string avatarError), Is.False);
            Assert.That(avatarError, Does.Contain("Avatar"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(duplicate);
            UnityEngine.Object.DestroyImmediate(invalid);
            UnityEngine.Object.DestroyImmediate(visual);
        }
    }

    [Test]
    public void UnknownIdFallsBackWithoutChangingLogicRootOrCollision()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(
            PlayerAppearanceCatalog.HostPrefabAssetPath);
        try
        {
            Transform rootTransform = root.transform;
            CharacterController collision = root.GetComponent<CharacterController>();
            Vector3 position = rootTransform.localPosition;
            Quaternion rotation = rootTransform.localRotation;
            Vector3 scale = rootTransform.localScale;
            float height = collision.height;
            float radius = collision.radius;
            Vector3 center = collision.center;
            PlayerAppearanceHost host = root.GetComponent<PlayerAppearanceHost>();

            GameObject instance = host.Apply("unknown.character.id");
            Assert.That(host.UsedFallback, Is.True);
            Assert.That(host.CurrentDefinition, Is.SameAs(catalog.DefaultDefinition));
            Assert.That(instance.transform.parent, Is.SameAs(host.VisualRoot));
            Assert.That(instance.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(instance.transform.localRotation,
                Is.EqualTo(Quaternion.identity));
            Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(instance.GetComponentInChildren<CharacterController>(true),
                Is.Null);
            Assert.That(rootTransform.localPosition, Is.EqualTo(position));
            Assert.That(rootTransform.localRotation, Is.EqualTo(rotation));
            Assert.That(rootTransform.localScale, Is.EqualTo(scale));
            Assert.That(collision.height, Is.EqualTo(height));
            Assert.That(collision.radius, Is.EqualTo(radius));
            Assert.That(collision.center, Is.EqualTo(center));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void PreviewSwitchesNamesAndRotatesWholeCharacter()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            PlayerAppearanceCatalog.PreviewSceneAssetPath);
        try
        {
            PlayerAppearancePreviewController preview = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    PlayerAppearancePreviewController>(true)).Single();
            string firstName = preview.SelectedDisplayName;
            string firstId = preview.PreviewInstance
                .GetComponent<PlayerAppearanceInstance>().StableId;
            Quaternion before = preview.PreviewAnchor.localRotation;
            preview.RotateBy(35f);
            preview.NextCharacter();
            Assert.That(preview.SelectedDisplayName, Is.Not.EqualTo(firstName));
            Assert.That(preview.PreviewInstance
                .GetComponent<PlayerAppearanceInstance>().StableId,
                Is.Not.EqualTo(firstId));
            Assert.That(preview.PreviewAnchor.localRotation,
                Is.Not.EqualTo(before));
            Assert.That(preview.PreviewAnchor
                .GetComponentsInChildren<PlayerAppearanceInstance>(true)
                .Count(value => value.gameObject.activeSelf), Is.EqualTo(1));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void AutomatedAppearanceAuditPasses()
    {
        PlayerAppearanceAuditReport report =
            PlayerAppearanceCatalogAuditor.Audit(catalog);
        Assert.That(report.IsValid, Is.True,
            string.Join(Environment.NewLine, report.Issues));
    }
}
