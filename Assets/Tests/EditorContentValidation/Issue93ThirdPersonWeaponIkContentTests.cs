using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EditorAnimatorController = UnityEditor.Animations.AnimatorController;

public sealed class Issue93ThirdPersonWeaponIkContentTests
{
    private const string PreviewScenePath =
        "Assets/Scenes/CharacterCalibration/WeaponCalibration.unity";
    private const string ControllerPath =
        "Assets/Resources/Content/Characters/Humanoid/Animation/" +
        "HumanoidCombat.controller";

    [Test]
    public void UpperBodyLayerEnablesIkPassWithoutAffectingLegMask()
    {
        EditorAnimatorController controller = AssetDatabase.LoadAssetAtPath<
            EditorAnimatorController>(ControllerPath);
        AnimatorControllerLayer upper = controller.layers.Single(layer =>
            layer.name == ThirdPersonAnimationParameters.UpperBodyLayerName);
        Assert.That(upper.iKPass, Is.True);
        Assert.That(upper.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.LeftHandIK), Is.True);
        Assert.That(upper.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.RightHandIK), Is.True);
        Assert.That(upper.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.LeftLeg), Is.False);
        Assert.That(upper.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.RightLeg), Is.False);

        BlendTree aim = upper.stateMachine.states.Single(child =>
            child.state.name == "Aim").state.motion as BlendTree;
        Assert.That(aim, Is.Not.Null);
        Assert.That(aim.children.Single(child => child.threshold == -1f)
            .motion.name, Is.EqualTo("Pistol_Aim_Up"));
        Assert.That(aim.children.Single(child => child.threshold == 1f)
            .motion.name, Is.EqualTo("Pistol_Aim_Down"));
    }

    [Test]
    public void EveryWeaponProvidesTheLeftHandAndFeedbackPoints()
    {
        ThirdPersonWeaponCatalog weapons = AssetDatabase.LoadAssetAtPath<
            ThirdPersonWeaponCatalog>(ThirdPersonWeaponCatalog.DefaultAssetPath);
        foreach (ThirdPersonWeaponDefinition definition in weapons.Definitions)
        {
            ThirdPersonWeaponRig rig =
                definition.CalibratedPrefab.GetComponent<ThirdPersonWeaponRig>();
            Assert.That(rig.LeftHandGrip, Is.Not.Null, definition.StableId);
            Assert.That(rig.AimPoint, Is.Not.Null, definition.StableId);
            Assert.That(rig.MuzzlePoint, Is.Not.Null, definition.StableId);
        }
    }

    [Test]
    public void PreviewSwitchesBetweenBoundedUpNeutralAndDownAim()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(PreviewScenePath);
        try
        {
            WeaponCalibrationPreviewController preview = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    WeaponCalibrationPreviewController>(true)).Single();
            preview.Refresh();
            preview.SetAimPitch(-90f);
            Assert.That(preview.SelectedAimPitch,
                Is.EqualTo(-ThirdPersonWeaponIkController
                    .MaximumAimPitchDegrees));
            Assert.That(preview.WeaponIk.AimDirection.y,
                Is.GreaterThan(0f));
            Assert.That(preview.WeaponIk.PresentationAimParameter,
                Is.EqualTo(-ThirdPersonWeaponIkController
                    .MaximumPresentationPitchDegrees / 89f).Within(0.001f));
            preview.SetAimPitch(0f);
            Assert.That(preview.WeaponIk.AimDirection.y,
                Is.EqualTo(0f).Within(0.001f));
            preview.SetAimPitch(90f);
            Assert.That(preview.SelectedAimPitch,
                Is.EqualTo(ThirdPersonWeaponIkController
                    .MaximumAimPitchDegrees));
            Assert.That(preview.WeaponIk.AimDirection.y,
                Is.LessThan(0f));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void CalibrationAuditStillPassesAfterIkIntegration()
    {
        ThirdPersonWeaponCalibrationAuditReport report =
            ThirdPersonWeaponCalibrationAuditor.Audit(
                AssetDatabase.LoadAssetAtPath<ThirdPersonWeaponCatalog>(
                    ThirdPersonWeaponCatalog.DefaultAssetPath),
                AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
                    PlayerAppearanceCatalog.DefaultAssetPath),
                AssetDatabase.LoadAssetAtPath<
                    ThirdPersonWeaponCalibrationMatrix>(
                    ThirdPersonWeaponCalibrationMatrix.DefaultAssetPath));
        Assert.That(report.IsValid, Is.True,
            string.Join(Environment.NewLine,
                report.Issues.Select(issue => issue.Message)));
    }
}
