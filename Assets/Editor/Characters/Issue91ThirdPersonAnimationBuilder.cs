using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using EditorAnimatorController = UnityEditor.Animations.AnimatorController;

public static class Issue91ThirdPersonAnimationBuilder
{
    public const string ControllerPath =
        "Assets/Resources/Content/Characters/Humanoid/Animation/" +
        "HumanoidCombat.controller";
    public const string UpperBodyMaskPath =
        "Assets/Resources/Content/Characters/Humanoid/Animation/" +
        "UpperBodyCombat.mask";
    private const string AnimationModelPath =
        "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/" +
        "Animations/UAL1_Standard.fbx";

    private static readonly string[] RequiredClips =
    {
        "Idle_Loop", "Walk_Loop", "Sprint_Loop",
        "Crouch_Idle_Loop", "Crouch_Fwd_Loop", "Jump_Start",
        "Jump_Loop", "Jump_Land", "Pistol_Idle_Loop",
        "Pistol_Aim_Down", "Pistol_Aim_Neutral", "Pistol_Aim_Up",
        "Pistol_Shoot", "Pistol_Reload", "Interact"
    };

    [MenuItem("FPS/Content/Issue 91/Rebuild Third Person Animation")]
    public static void Build()
    {
        Dictionary<string, AnimationClip> clips = LoadClips();
        EditorAnimatorController controller = BuildController(clips);
        ApplyControllerToCharacterContent(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Issue #91 已生成第三人称分层动画：方向移动、蹲伏、跳跃、" +
            "落地，以及上半身瞄准、射击、换弹和切枪。\n");
    }

    public static EditorAnimatorController BuildController(
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        ValidateClips(clips);
        EditorAnimatorController controller = ResetController();
        AddParameters(controller);
        BuildBaseLayer(controller, clips);
        BuildUpperBodyLayer(controller, clips, BuildUpperBodyMask());
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static Dictionary<string, AnimationClip> LoadClips()
    {
        return AssetDatabase.LoadAllAssetsAtPath(AnimationModelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith(
                "__preview__", StringComparison.Ordinal))
            .ToDictionary(clip => clip.name, StringComparer.Ordinal);
    }

    private static void ValidateClips(
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        foreach (string clipName in RequiredClips)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip) ||
                clip == null || !clip.isHumanMotion)
            {
                throw new InvalidOperationException(
                    $"Issue #91 缺少有效 Humanoid 动画：{clipName}");
            }
        }
    }

    private static EditorAnimatorController ResetController()
    {
        EditorAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<EditorAnimatorController>(
                ControllerPath);
        if (controller == null)
        {
            controller = EditorAnimatorController
                .CreateAnimatorControllerAtPath(ControllerPath);
        }

        while (controller.layers.Length > 0)
            controller.RemoveLayer(0);
        foreach (AnimatorControllerParameter parameter in
                 controller.parameters.ToArray())
            controller.RemoveParameter(parameter);

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(
                     ControllerPath))
        {
            if (asset == null || asset == controller) continue;
            Object.DestroyImmediate(asset, true);
        }
        return controller;
    }

    private static void AddParameters(EditorAnimatorController controller)
    {
        controller.AddParameter(ThirdPersonAnimationParameters.MoveXName,
            AnimatorControllerParameterType.Float);
        controller.AddParameter(ThirdPersonAnimationParameters.MoveYName,
            AnimatorControllerParameterType.Float);
        controller.AddParameter(ThirdPersonAnimationParameters.SpeedName,
            AnimatorControllerParameterType.Float);
        controller.AddParameter(
            ThirdPersonAnimationParameters.VerticalSpeedName,
            AnimatorControllerParameterType.Float);
        controller.AddParameter(ThirdPersonAnimationParameters.GroundedName,
            AnimatorControllerParameterType.Bool);
        controller.AddParameter(ThirdPersonAnimationParameters.CrouchingName,
            AnimatorControllerParameterType.Bool);
        controller.AddParameter(ThirdPersonAnimationParameters.AimingName,
            AnimatorControllerParameterType.Bool);
        controller.AddParameter(ThirdPersonAnimationParameters.AimPitchName,
            AnimatorControllerParameterType.Float);
        controller.AddParameter(ThirdPersonAnimationParameters.ShootName,
            AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ThirdPersonAnimationParameters.ReloadName,
            AnimatorControllerParameterType.Trigger);
        controller.AddParameter(
            ThirdPersonAnimationParameters.SwitchWeaponName,
            AnimatorControllerParameterType.Trigger);
    }

    private static void BuildBaseLayer(
        EditorAnimatorController controller,
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        var machine = new AnimatorStateMachine
        {
            name = ThirdPersonAnimationParameters.BaseLayerName
        };
        AssetDatabase.AddObjectToAsset(machine, controller);
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = ThirdPersonAnimationParameters.BaseLayerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            stateMachine = machine
        });

        AnimatorState locomotion = AddState(machine, "Locomotion",
            BuildDirectionalTree(controller, "Standing Directional",
                clips["Idle_Loop"], clips["Walk_Loop"],
                clips["Sprint_Loop"]), new Vector3(180f, 40f));
        AnimatorState crouch = AddState(machine, "Crouch",
            BuildCrouchTree(controller, clips), new Vector3(180f, 135f));
        AnimatorState jumpStart = AddState(machine, "Jump Start",
            clips["Jump_Start"], new Vector3(475f, 40f));
        AnimatorState airborne = AddState(machine, "Airborne",
            clips["Jump_Loop"], new Vector3(475f, 135f));
        AnimatorState land = AddState(machine, "Land",
            clips["Jump_Land"], new Vector3(475f, 230f));
        machine.defaultState = locomotion;

        AddBoolTransition(locomotion, crouch,
            ThirdPersonAnimationParameters.CrouchingName, true, 0.12f);
        AddBoolTransition(crouch, locomotion,
            ThirdPersonAnimationParameters.CrouchingName, false, 0.12f);

        AnimatorStateTransition jump = machine.AddAnyStateTransition(
            jumpStart);
        ConfigureImmediate(jump, 0.08f);
        jump.AddCondition(AnimatorConditionMode.IfNot, 0f,
            ThirdPersonAnimationParameters.GroundedName);
        jump.AddCondition(AnimatorConditionMode.Greater, 0.05f,
            ThirdPersonAnimationParameters.VerticalSpeedName);

        AnimatorStateTransition fall = machine.AddAnyStateTransition(
            airborne);
        ConfigureImmediate(fall, 0.1f);
        fall.AddCondition(AnimatorConditionMode.IfNot, 0f,
            ThirdPersonAnimationParameters.GroundedName);
        fall.AddCondition(AnimatorConditionMode.Less, 0.05f,
            ThirdPersonAnimationParameters.VerticalSpeedName);

        AnimatorStateTransition startToAir = jumpStart.AddTransition(airborne);
        startToAir.hasExitTime = true;
        startToAir.exitTime = 0.78f;
        startToAir.duration = 0.08f;

        AnimatorStateTransition toLand = airborne.AddTransition(land);
        ConfigureImmediate(toLand, 0.08f);
        toLand.AddCondition(AnimatorConditionMode.If, 0f,
            ThirdPersonAnimationParameters.GroundedName);

        AddExitTransition(land, locomotion,
            ThirdPersonAnimationParameters.CrouchingName, false);
        AddExitTransition(land, crouch,
            ThirdPersonAnimationParameters.CrouchingName, true);
    }

    private static void BuildUpperBodyLayer(
        EditorAnimatorController controller,
        IReadOnlyDictionary<string, AnimationClip> clips,
        AvatarMask mask)
    {
        var machine = new AnimatorStateMachine
        {
            name = ThirdPersonAnimationParameters.UpperBodyLayerName
        };
        AssetDatabase.AddObjectToAsset(machine, controller);
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = ThirdPersonAnimationParameters.UpperBodyLayerName,
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = mask,
            stateMachine = machine
        });

        AnimatorState relaxed = AddState(machine, "Weapon Idle",
            clips["Pistol_Idle_Loop"], new Vector3(180f, 40f));
        AnimatorState aim = AddState(machine, "Aim",
            BuildAimTree(controller, clips), new Vector3(180f, 135f));
        AnimatorState shoot = AddState(machine, "Shoot",
            clips["Pistol_Shoot"], new Vector3(475f, 40f));
        AnimatorState reload = AddState(machine, "Reload",
            clips["Pistol_Reload"], new Vector3(475f, 135f));
        AnimatorState switchWeapon = AddState(machine, "Switch Weapon",
            clips["Interact"], new Vector3(475f, 230f));
        machine.defaultState = aim;

        AddBoolTransition(relaxed, aim,
            ThirdPersonAnimationParameters.AimingName, true, 0.1f);
        AddBoolTransition(aim, relaxed,
            ThirdPersonAnimationParameters.AimingName, false, 0.1f);
        AddTriggerTransition(machine, shoot,
            ThirdPersonAnimationParameters.ShootName, 0.035f);
        AddTriggerTransition(machine, reload,
            ThirdPersonAnimationParameters.ReloadName, 0.08f);
        AddTriggerTransition(machine, switchWeapon,
            ThirdPersonAnimationParameters.SwitchWeaponName, 0.08f);

        AddUpperBodyReturn(shoot, aim, relaxed, 0.86f);
        AddUpperBodyReturn(reload, aim, relaxed, 0.94f);
        AddUpperBodyReturn(switchWeapon, aim, relaxed, 0.82f);
    }

    private static BlendTree BuildDirectionalTree(
        EditorAnimatorController controller,
        string name,
        AnimationClip idle,
        AnimationClip walk,
        AnimationClip sprint)
    {
        var tree = NewDirectionalTree(controller, name);
        tree.children = new[]
        {
            Child(idle, Vector2.zero),
            Child(walk, new Vector2(0f, 0.45f)),
            Child(walk, new Vector2(0f, -0.45f), mirror: true),
            Child(walk, new Vector2(-0.45f, 0f), mirror: true),
            Child(walk, new Vector2(0.45f, 0f)),
            Child(sprint, new Vector2(0f, 1f)),
            Child(sprint, new Vector2(0f, -1f), mirror: true),
            Child(sprint, new Vector2(-1f, 0f), mirror: true),
            Child(sprint, new Vector2(1f, 0f))
        };
        return tree;
    }

    private static BlendTree BuildCrouchTree(
        EditorAnimatorController controller,
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        var tree = NewDirectionalTree(controller, "Crouch Directional");
        AnimationClip move = clips["Crouch_Fwd_Loop"];
        tree.children = new[]
        {
            Child(clips["Crouch_Idle_Loop"], Vector2.zero),
            Child(move, new Vector2(0f, 1f)),
            Child(move, new Vector2(0f, -1f), mirror: true),
            Child(move, new Vector2(-1f, 0f), mirror: true),
            Child(move, new Vector2(1f, 0f))
        };
        return tree;
    }

    private static BlendTree BuildAimTree(
        EditorAnimatorController controller,
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        var tree = new BlendTree
        {
            name = "Aim Pitch",
            blendType = BlendTreeType.Simple1D,
            blendParameter = ThirdPersonAnimationParameters.AimPitchName,
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.children = new[]
        {
            ThresholdChild(clips["Pistol_Aim_Down"], -1f),
            ThresholdChild(clips["Pistol_Aim_Neutral"], 0f),
            ThresholdChild(clips["Pistol_Aim_Up"], 1f)
        };
        return tree;
    }

    private static BlendTree NewDirectionalTree(
        EditorAnimatorController controller,
        string name)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = ThirdPersonAnimationParameters.MoveXName,
            blendParameterY = ThirdPersonAnimationParameters.MoveYName,
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    private static ChildMotion Child(
        Motion motion,
        Vector2 position,
        bool mirror = false) => new()
    {
        motion = motion,
        position = position,
        mirror = mirror,
        timeScale = 1f,
        cycleOffset = 0f,
        directBlendParameter = string.Empty
    };

    private static ChildMotion ThresholdChild(Motion motion, float threshold) =>
        new()
        {
            motion = motion,
            threshold = threshold,
            timeScale = 1f,
            cycleOffset = 0f,
            directBlendParameter = string.Empty
        };

    private static AnimatorState AddState(
        AnimatorStateMachine machine,
        string name,
        Motion motion,
        Vector3 position)
    {
        AnimatorState state = machine.AddState(name, position);
        state.motion = motion;
        state.writeDefaultValues = false;
        return state;
    }

    private static void AddBoolTransition(
        AnimatorState from,
        AnimatorState to,
        string parameter,
        bool value,
        float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureImmediate(transition, duration);
        transition.AddCondition(value
                ? AnimatorConditionMode.If
                : AnimatorConditionMode.IfNot,
            0f, parameter);
    }

    private static void AddExitTransition(
        AnimatorState from,
        AnimatorState to,
        string boolParameter,
        bool value)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 0.86f;
        transition.duration = 0.1f;
        transition.AddCondition(value
                ? AnimatorConditionMode.If
                : AnimatorConditionMode.IfNot,
            0f, boolParameter);
    }

    private static void AddTriggerTransition(
        AnimatorStateMachine machine,
        AnimatorState to,
        string trigger,
        float duration)
    {
        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        ConfigureImmediate(transition, duration);
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    private static void AddUpperBodyReturn(
        AnimatorState action,
        AnimatorState aim,
        AnimatorState relaxed,
        float exitTime)
    {
        AnimatorStateTransition aimReturn = action.AddTransition(aim);
        aimReturn.hasExitTime = true;
        aimReturn.exitTime = exitTime;
        aimReturn.duration = 0.08f;
        aimReturn.AddCondition(AnimatorConditionMode.If, 0f,
            ThirdPersonAnimationParameters.AimingName);

        AnimatorStateTransition idleReturn = action.AddTransition(relaxed);
        idleReturn.hasExitTime = true;
        idleReturn.exitTime = exitTime;
        idleReturn.duration = 0.08f;
        idleReturn.AddCondition(AnimatorConditionMode.IfNot, 0f,
            ThirdPersonAnimationParameters.AimingName);
    }

    private static void ConfigureImmediate(
        AnimatorStateTransition transition,
        float duration)
    {
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        transition.orderedInterruption = true;
    }

    private static AvatarMask BuildUpperBodyMask()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
            UpperBodyMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask { name = "UpperBodyCombat" };
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }
        for (int index = 0;
             index < (int)AvatarMaskBodyPart.LastBodyPart;
             index++)
        {
            mask.SetHumanoidBodyPartActive(
                (AvatarMaskBodyPart)index, false);
        }
        AvatarMaskBodyPart[] active =
        {
            AvatarMaskBodyPart.Body,
            AvatarMaskBodyPart.Head,
            AvatarMaskBodyPart.LeftArm,
            AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers,
            AvatarMaskBodyPart.RightFingers,
            AvatarMaskBodyPart.LeftHandIK,
            AvatarMaskBodyPart.RightHandIK
        };
        foreach (AvatarMaskBodyPart bodyPart in active)
            mask.SetHumanoidBodyPartActive(bodyPart, true);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static void ApplyControllerToCharacterContent(
        RuntimeAnimatorController controller)
    {
        HumanoidCharacterStandard standard = AssetDatabase.LoadAssetAtPath<
            HumanoidCharacterStandard>(
            HumanoidCharacterStandard.DefaultAssetPath);
        if (standard == null)
            throw new InvalidOperationException("HumanoidCharacterStandard 缺失。");

        foreach (HumanoidCharacterEntry entry in standard.Characters)
        {
            string path = AssetDatabase.GetAssetPath(entry.Prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Animator animator = root.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException($"角色缺少 Animator：{path}");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        standard.Configure(standard.StableId, Math.Max(standard.Version, 2),
            controller, standard.AnimationSet, standard.Characters);
        EditorUtility.SetDirty(standard);
    }
}
