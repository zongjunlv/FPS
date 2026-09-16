using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EditorAnimatorController = UnityEditor.Animations.AnimatorController;

public sealed class Issue91ThirdPersonAnimationContentTests
{
    private const string ControllerPath =
        "Assets/Resources/Content/Characters/Humanoid/Animation/" +
        "HumanoidCombat.controller";
    private EditorAnimatorController controller;

    [SetUp]
    public void SetUp()
    {
        controller = AssetDatabase.LoadAssetAtPath<EditorAnimatorController>(
            ControllerPath);
    }

    [Test]
    public void ControllerSeparatesLocomotionAndMaskedUpperBodyCombat()
    {
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.layers, Has.Length.EqualTo(2));
        AnimatorControllerLayer baseLayer = controller.layers.Single(layer =>
            layer.name == ThirdPersonAnimationParameters.BaseLayerName);
        AnimatorControllerLayer upperLayer = controller.layers.Single(layer =>
            layer.name == ThirdPersonAnimationParameters.UpperBodyLayerName);
        Assert.That(baseLayer.defaultWeight, Is.EqualTo(1f));
        Assert.That(upperLayer.defaultWeight, Is.EqualTo(1f));
        Assert.That(upperLayer.avatarMask, Is.Not.Null);
        Assert.That(upperLayer.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.LeftLeg), Is.False);
        Assert.That(upperLayer.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.RightLeg), Is.False);
        Assert.That(upperLayer.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.LeftArm), Is.True);
        Assert.That(upperLayer.avatarMask.GetHumanoidBodyPartActive(
            AvatarMaskBodyPart.RightArm), Is.True);
    }

    [Test]
    public void BaseLayerCoversDirectionalMovementCrouchAndAirStates()
    {
        AnimatorStateMachine machine = controller.layers.Single(layer =>
            layer.name == ThirdPersonAnimationParameters.BaseLayerName)
            .stateMachine;
        string[] names = machine.states.Select(child => child.state.name)
            .ToArray();
        CollectionAssert.IsSubsetOf(new[]
        {
            "Locomotion", "Crouch", "Jump Start", "Airborne", "Land"
        }, names);

        BlendTree movement = machine.states.Single(child =>
            child.state.name == "Locomotion").state.motion as BlendTree;
        Assert.That(movement, Is.Not.Null);
        Assert.That(movement.blendType,
            Is.EqualTo(BlendTreeType.FreeformCartesian2D));
        Assert.That(movement.blendParameter,
            Is.EqualTo(ThirdPersonAnimationParameters.MoveXName));
        Assert.That(movement.blendParameterY,
            Is.EqualTo(ThirdPersonAnimationParameters.MoveYName));
        Assert.That(movement.children.Any(child => child.position.y < -0.9f),
            Is.True, "缺少向后移动样本。");
        Assert.That(movement.children.Any(child => child.position.x < -0.9f),
            Is.True, "缺少向左移动样本。");
        Assert.That(movement.children.Any(child => child.position.x > 0.9f),
            Is.True, "缺少向右移动样本。");
        Assert.That(movement.children.Any(child => child.position.y > 0.9f),
            Is.True, "缺少冲刺/快速前进样本。");
    }

    [Test]
    public void UpperBodyLayerProvidesAimPitchShootReloadAndSwitch()
    {
        AnimatorStateMachine machine = controller.layers.Single(layer =>
            layer.name == ThirdPersonAnimationParameters.UpperBodyLayerName)
            .stateMachine;
        string[] names = machine.states.Select(child => child.state.name)
            .ToArray();
        CollectionAssert.IsSubsetOf(new[]
        {
            "Weapon Idle", "Aim", "Shoot", "Reload", "Switch Weapon"
        }, names);
        BlendTree aim = machine.states.Single(child =>
            child.state.name == "Aim").state.motion as BlendTree;
        Assert.That(aim, Is.Not.Null);
        Assert.That(aim.blendType, Is.EqualTo(BlendTreeType.Simple1D));
        Assert.That(aim.children.Select(child => child.threshold),
            Is.EquivalentTo(new[] { -1f, 0f, 1f }));

        string[] triggers = controller.parameters
            .Where(parameter => parameter.type ==
                AnimatorControllerParameterType.Trigger)
            .Select(parameter => parameter.name)
            .ToArray();
        CollectionAssert.IsSubsetOf(new[]
        {
            ThirdPersonAnimationParameters.ShootName,
            ThirdPersonAnimationParameters.ReloadName,
            ThirdPersonAnimationParameters.SwitchWeaponName
        }, triggers);
    }

    [Test]
    public void ThreeHumanoidCharactersReuseControllerWithoutRootMotion()
    {
        HumanoidCharacterStandard standard = AssetDatabase.LoadAssetAtPath<
            HumanoidCharacterStandard>(
            HumanoidCharacterStandard.DefaultAssetPath);
        Assert.That(standard, Is.Not.Null);
        Assert.That(standard.Characters.Count, Is.EqualTo(3));
        Assert.That(standard.SharedAnimatorController, Is.SameAs(controller));
        foreach (HumanoidCharacterEntry entry in standard.Characters)
        {
            Animator animator = entry.Prefab.GetComponent<Animator>();
            Assert.That(animator, Is.Not.Null, entry.StableId);
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman,
                Is.True, entry.StableId);
            Assert.That(animator.runtimeAnimatorController,
                Is.SameAs(controller), entry.StableId);
            Assert.That(animator.applyRootMotion, Is.False, entry.StableId);
        }
    }

    [Test]
    public void EveryControllerMotionIsFiniteHumanoidContent()
    {
        var clips = new HashSet<AnimationClip>();
        foreach (AnimatorControllerLayer layer in controller.layers)
        foreach (ChildAnimatorState child in layer.stateMachine.states)
            CollectClips(child.state.motion, clips);
        Assert.That(clips.Count, Is.GreaterThanOrEqualTo(15));
        foreach (AnimationClip clip in clips)
        {
            Assert.That(clip.isHumanMotion, Is.True, clip.name);
            Assert.That(float.IsFinite(clip.length) && clip.length > 0f,
                Is.True, clip.name);
        }
    }

    private static void CollectClips(
        Motion motion,
        ISet<AnimationClip> clips)
    {
        if (motion is AnimationClip clip)
        {
            clips.Add(clip);
            return;
        }
        if (motion is not BlendTree tree) return;
        foreach (ChildMotion child in tree.children)
            CollectClips(child.motion, clips);
    }

    [Test]
    public void ExistingCalibrationPreviewDrivesLayeredParameters()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            HumanoidCharacterStandard.PreviewScenePath);
        try
        {
            HumanoidCharacterPreviewController preview = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    HumanoidCharacterPreviewController>(true))
                .Single();
            preview.SelectMotion(2);
            Animator animator = preview.CharacterInstances[
                preview.SelectedCharacter].GetComponent<Animator>();
            Assert.That(animator.GetFloat(
                ThirdPersonAnimationParameters.MoveY), Is.EqualTo(1f));
            preview.SelectMotion(3);
            Assert.That(animator.GetBool(
                ThirdPersonAnimationParameters.Crouching), Is.True);
            preview.SelectMotion(7);
            Assert.That(animator.GetBool(
                ThirdPersonAnimationParameters.Aiming), Is.True);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
