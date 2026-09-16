using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue81HumanoidCharacterPipelineTests
{
    private HumanoidCharacterStandard standard;

    [SetUp]
    public void SetUp()
    {
        standard = AssetDatabase.LoadAssetAtPath<HumanoidCharacterStandard>(
            HumanoidCharacterStandard.DefaultAssetPath);
    }

    [Test]
    public void StandardContainsThreeCharactersAndTenRequiredMotions()
    {
        Assert.That(standard, Is.Not.Null);
        Assert.That(standard.TryValidate(out string error), Is.True, error);
        Assert.That(standard.TargetHeight,
            Is.EqualTo(HumanoidCharacterStandard.StandardHeight));
        Assert.That(standard.ForwardAxis, Is.EqualTo(Vector3.forward));
        Assert.That(standard.UpAxis, Is.EqualTo(Vector3.up));
        Assert.That(standard.Characters.Count, Is.EqualTo(3));
        Assert.That(standard.Characters.Select(value => value.StableId).Distinct().Count(),
            Is.EqualTo(3));
        Assert.That(standard.AnimationSet.RequiredClips.Count, Is.EqualTo(10));
        Assert.That(standard.AnimationSet.RequiredClips,
            Has.All.Matches<AnimationClip>(clip => clip != null && clip.isHumanMotion));
    }

    [Test]
    public void EverySourceModelUsesValidHumanoidAvatarAndImportStandard()
    {
        string[] paths = standard.Characters
            .Select(entry => entry.Prefab.GetComponent<HumanoidCharacterMarker>()
                .SourceModelPath)
            .Distinct()
            .ToArray();
        Assert.That(paths, Has.Length.EqualTo(2));
        foreach (string path in paths)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Human), path);
            Assert.That(importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel), path);
            Assert.That(importer.bakeAxisConversion, Is.True, path);
            Assert.That(importer.globalScale, Is.GreaterThan(0f), path);
            Assert.That(importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None), path);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Avatar>()
                .Single();
            Assert.That(avatar.isValid, Is.True, path);
            Assert.That(avatar.isHuman, Is.True, path);
            string[] humanBones = avatar.humanDescription.human
                .Select(value => value.humanName)
                .ToArray();
            CollectionAssert.IsSubsetOf(new[]
            {
                HumanBodyBones.Hips.ToString(),
                HumanBodyBones.Spine.ToString(),
                HumanBodyBones.Head.ToString(),
                HumanBodyBones.LeftUpperArm.ToString(),
                HumanBodyBones.LeftLowerArm.ToString(),
                HumanBodyBones.LeftHand.ToString(),
                HumanBodyBones.RightUpperArm.ToString(),
                HumanBodyBones.RightLowerArm.ToString(),
                HumanBodyBones.RightHand.ToString(),
                HumanBodyBones.LeftUpperLeg.ToString(),
                HumanBodyBones.LeftLowerLeg.ToString(),
                HumanBodyBones.LeftFoot.ToString(),
                HumanBodyBones.RightUpperLeg.ToString(),
                HumanBodyBones.RightLowerLeg.ToString(),
                HumanBodyBones.RightFoot.ToString()
            }, humanBones, path);
        }
    }

    [Test]
    public void PrefabsShareControllerAndUsePositiveNormalizedTransforms()
    {
        foreach (HumanoidCharacterEntry entry in standard.Characters)
        {
            Animator animator = entry.Prefab.GetComponent<Animator>();
            Assert.That(animator.runtimeAnimatorController,
                Is.SameAs(standard.SharedAnimatorController), entry.StableId);
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman,
                Is.True, entry.StableId);
            Assert.That(entry.Prefab.transform.localPosition,
                Is.EqualTo(Vector3.zero), entry.StableId);
            Assert.That(entry.Prefab.transform.localRotation,
                Is.EqualTo(Quaternion.identity), entry.StableId);
            Assert.That(entry.Prefab.transform.localScale,
                Is.EqualTo(Vector3.one), entry.StableId);
            foreach (Transform transform in entry.Prefab
                         .GetComponentsInChildren<Transform>(true))
            {
                Assert.That(transform.localScale.x, Is.GreaterThan(0f),
                    $"{entry.StableId}/{transform.name}");
                Assert.That(transform.localScale.y, Is.GreaterThan(0f),
                    $"{entry.StableId}/{transform.name}");
                Assert.That(transform.localScale.z, Is.GreaterThan(0f),
                    $"{entry.StableId}/{transform.name}");
            }
            foreach (Renderer renderer in entry.Prefab
                         .GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(renderer.sharedMaterials,
                    Has.All.Not.Null, $"{entry.StableId}/{renderer.name}");
            }
        }
    }

    [Test]
    public void PreviewSceneSwitchesExactlyOneCharacterPerClick()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            HumanoidCharacterStandard.PreviewScenePath);
        try
        {
            HumanoidCharacterPreviewController controller = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    HumanoidCharacterPreviewController>(true))
                .Single();
            Assert.That(controller.CharacterInstances, Has.Length.EqualTo(3));
            Assert.That(controller.ActiveCharacterCount, Is.EqualTo(1));
            Assert.That(controller.SelectedCharacter, Is.EqualTo(0));

            controller.NextCharacter();
            Assert.That(controller.SelectedCharacter, Is.EqualTo(1));
            Assert.That(controller.ActiveCharacterCount, Is.EqualTo(1));
            Assert.That(controller.CharacterInstances[1].activeSelf, Is.True);
            controller.PreviousCharacter();
            Assert.That(controller.SelectedCharacter, Is.EqualTo(0));
            Assert.That(controller.ActiveCharacterCount, Is.EqualTo(1));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void AutomatedPipelineAuditPassesAvatarMappingScaleAndPreviewChecks()
    {
        HumanoidPipelineAuditReport report =
            HumanoidCharacterPipelineAuditor.Audit(standard);
        Assert.That(
            report.IsValid,
            Is.True,
            string.Join(Environment.NewLine, report.Issues.Select(
                issue => $"[{issue.Code}] {issue.Message} {issue.AssetPath}")));
    }
}
