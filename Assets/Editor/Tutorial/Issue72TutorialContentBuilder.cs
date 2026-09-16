using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Issue72TutorialContentBuilder
{
    public const string DefinitionFolder =
        "Assets/Resources/Tutorial/Definitions";
    public const string DefinitionPath =
        DefinitionFolder + "/DefaultTutorialSequence.asset";

    [MenuItem("FPS/Content/Issue 72/Rebuild Tutorial Flow")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the tutorial flow.");
        }

        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);
        TutorialTrainingEnvironment environment = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialTrainingEnvironment>(true))
            .Single();
        EnsureSceneContent(scene, environment);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameModeScenePaths.Tutorial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Issue #72 教学步骤状态机与顶部 HUD 配置已生成。");
    }

    public static void EnsureSceneContent(
        Scene scene,
        TutorialTrainingEnvironment environment)
    {
        if (!scene.IsValid() || environment == null)
        {
            throw new ArgumentException("Tutorial scene or environment is invalid.");
        }

        TutorialSequenceDefinition definition = EnsureDefinition();
        TutorialFlowController[] controllers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialFlowController>(true))
            .ToArray();
        TutorialFlowController controller;
        if (controllers.Length == 0)
        {
            controller = environment.gameObject.AddComponent<
                TutorialFlowController>();
        }
        else
        {
            controller = controllers[0];
            for (int index = 1; index < controllers.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(controllers[index]);
            }
        }

        controller.Configure(definition, environment);
        EditorUtility.SetDirty(controller);
    }

    public static TutorialSequenceDefinition EnsureDefinition()
    {
        EnsureFolder(DefinitionFolder);
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<
                TutorialSequenceDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        definition.Configure(
            "tutorial.main",
            "新手训练",
            2,
            CreateDefaultSteps());
        if (!definition.TryValidate(out string error))
        {
            throw new InvalidOperationException(error);
        }

        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static TutorialStepDefinition[] CreateDefaultSteps()
    {
        return new[]
        {
            Step("tutorial.move.forward", "第一章 · 基础移动", "向前移动",
                "按住 W，向前实际移动 1 米", TutorialEvidenceType.MoveForwardDistance, 1),
            Step("tutorial.move.backward", "第一章 · 基础移动", "向后移动",
                "按住 S，向后实际移动 1 米", TutorialEvidenceType.MoveBackwardDistance, 1),
            Step("tutorial.move.left", "第一章 · 基础移动", "向左移动",
                "按住 A，向左实际移动 1 米", TutorialEvidenceType.MoveLeftDistance, 1),
            Step("tutorial.move.right", "第一章 · 基础移动", "向右移动",
                "按住 D，向右实际移动 1 米", TutorialEvidenceType.MoveRightDistance, 1),
            Step("tutorial.move.jump", "第一章 · 基础移动", "跳跃",
                "按空格键完成一次跳跃", TutorialEvidenceType.Jump, 1),
            Step("tutorial.move.sprint", "第一章 · 基础移动", "冲刺",
                "按住 Shift 向前冲刺", TutorialEvidenceType.Sprint, 1),
            Step("tutorial.move.crouch", "第一章 · 基础移动", "下蹲",
                "按 C 完成一次下蹲", TutorialEvidenceType.Crouch, 1),
            Step("tutorial.shoot.hip", "第二章 · 射击训练", "腰射命中",
                "使用鼠标左键命中射击墙", TutorialEvidenceType.HipFireHit, 3),
            Step("tutorial.shoot.aim", "第二章 · 射击训练", "瞄准射击",
                "按住鼠标右键瞄准并完成命中", TutorialEvidenceType.AimFireHit, 3),
            Step("tutorial.shoot.tap", "第二章 · 射击训练", "点射控制",
                "使用短点射控制后坐力", TutorialEvidenceType.SemiAutomaticShot, 3),
            Step("tutorial.shoot.burst", "第二章 · 射击训练", "连续射击",
                "按住鼠标左键完成一次连续射击", TutorialEvidenceType.AutomaticBurst, 1),
            Step("tutorial.weapon.switch", "第三章 · 武器操作", "切换武器",
                "使用数字键或鼠标滚轮切换武器", TutorialEvidenceType.WeaponSwitch, 2),
            Step("tutorial.weapon.reload", "第三章 · 武器操作", "更换弹匣",
                "按 R 完成一次换弹", TutorialEvidenceType.Reload, 1),
            Step("tutorial.damage.body", "第四章 · 伤害认知", "命中躯干",
                "射击固定靶的躯干部位", TutorialEvidenceType.BodyHit, 1),
            Step("tutorial.damage.head", "第四章 · 伤害认知", "命中弱点",
                "射击固定靶的头部并观察伤害差异", TutorialEvidenceType.HeadHit, 1)
        };
    }

    private static TutorialStepDefinition Step(
        string id,
        string chapter,
        string title,
        string instruction,
        TutorialEvidenceType evidence,
        int target)
    {
        return new TutorialStepDefinition(
            id,
            chapter,
            title,
            instruction,
            evidence,
            target,
            $"已完成：{title}");
    }

    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        string[] segments = path.Split('/');
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }

            current = next;
        }
    }
}
