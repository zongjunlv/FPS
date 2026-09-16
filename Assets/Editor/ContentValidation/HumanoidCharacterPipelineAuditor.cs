using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class HumanoidPipelineAuditIssue
{
    public string Code { get; }
    public string Message { get; }
    public string AssetPath { get; }

    public HumanoidPipelineAuditIssue(string code, string message, string path)
    {
        Code = code;
        Message = message;
        AssetPath = path;
    }
}

public sealed class HumanoidPipelineAuditReport
{
    private readonly List<HumanoidPipelineAuditIssue> issues = new();
    public IReadOnlyList<HumanoidPipelineAuditIssue> Issues => issues;
    public bool IsValid => issues.Count == 0;

    internal void Add(string code, string message, string path) =>
        issues.Add(new HumanoidPipelineAuditIssue(code, message, path));
}

public static class HumanoidCharacterPipelineAuditor
{
    private static readonly HumanBodyBones[] RequiredBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot
    };

    public static HumanoidPipelineAuditReport Audit(
        HumanoidCharacterStandard standard = null)
    {
        var report = new HumanoidPipelineAuditReport();
        standard ??= AssetDatabase.LoadAssetAtPath<HumanoidCharacterStandard>(
            HumanoidCharacterStandard.DefaultAssetPath);
        if (standard == null)
        {
            report.Add(
                "HUMANOID_STANDARD_MISSING",
                "Humanoid 角色标准资产不存在。",
                HumanoidCharacterStandard.DefaultAssetPath);
            return report;
        }
        string standardPath = AssetDatabase.GetAssetPath(standard);
        if (!standard.TryValidate(out string standardError))
        {
            report.Add("HUMANOID_STANDARD_INVALID", standardError, standardPath);
        }
        ValidateAnimationSet(standard, report, standardPath);
        foreach (HumanoidCharacterEntry entry in standard.Characters)
        {
            ValidateCharacter(entry, standard, report);
        }
        ValidatePreviewScene(standard, report);
        return report;
    }

    public static void AppendTo(ContentValidationReport destination)
    {
        HumanoidPipelineAuditReport report = Audit();
        foreach (HumanoidPipelineAuditIssue issue in report.Issues)
        {
            destination.Add(
                issue.Code,
                issue.Message,
                AssetDatabase.LoadMainAssetAtPath(issue.AssetPath),
                issue.AssetPath,
                ContentValidationSeverity.Error);
        }
    }

    private static void ValidateAnimationSet(
        HumanoidCharacterStandard standard,
        HumanoidPipelineAuditReport report,
        string standardPath)
    {
        IReadOnlyList<AnimationClip> clips = standard.AnimationSet?.RequiredClips;
        if (clips == null || clips.Count != 10)
        {
            report.Add(
                "HUMANOID_MOTION_SET_INCOMPLETE",
                "共享动作集必须完整配置站立、移动、冲刺、下蹲、起跳、滞空、落地、瞄准、射击和换弹。",
                standardPath);
            return;
        }
        foreach (AnimationClip clip in clips)
        {
            if (clip == null || !clip.isHumanMotion)
            {
                report.Add(
                    "HUMANOID_MOTION_INVALID",
                    $"动作 '{clip?.name ?? "<missing>"}' 不是有效 Humanoid Motion。",
                    clip == null ? standardPath : AssetDatabase.GetAssetPath(clip));
            }
        }
        string animationPath = AssetDatabase.GetAssetPath(clips[0]);
        ModelImporter animationImporter =
            AssetImporter.GetAtPath(animationPath) as ModelImporter;
        if (animationImporter == null ||
            animationImporter.animationType != ModelImporterAnimationType.Human ||
            animationImporter.avatarSetup !=
            ModelImporterAvatarSetup.CreateFromThisModel ||
            !animationImporter.bakeAxisConversion ||
            animationImporter.globalScale <= 0f)
        {
            report.Add(
                "HUMANOID_ANIMATION_IMPORT_INVALID",
                "动画库必须使用独立有效 Humanoid Avatar、轴向转换和正比例导入。",
                animationPath);
        }
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(animationPath)
            .OfType<Avatar>()
            .FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            report.Add(
                "HUMANOID_ANIMATION_AVATAR_INVALID",
                "动画库没有有效 Humanoid Avatar。",
                animationPath);
        }
    }

    private static void ValidateCharacter(
        HumanoidCharacterEntry entry,
        HumanoidCharacterStandard standard,
        HumanoidPipelineAuditReport report)
    {
        if (entry?.Prefab == null)
        {
            report.Add(
                "HUMANOID_PREFAB_MISSING",
                "Humanoid 角色记录缺少 Prefab。",
                HumanoidCharacterStandard.DefaultAssetPath);
            return;
        }
        string prefabPath = AssetDatabase.GetAssetPath(entry.Prefab);
        Animator prefabAnimator = entry.Prefab.GetComponent<Animator>();
        HumanoidCharacterMarker marker =
            entry.Prefab.GetComponent<HumanoidCharacterMarker>();
        if (prefabAnimator == null || marker == null ||
            marker.StableId != entry.StableId)
        {
            report.Add(
                "HUMANOID_PREFAB_COMPONENT_INVALID",
                "角色 Prefab 缺少 Animator、标准化标记或稳定 ID 不一致。" +
                $" animator={prefabAnimator != null} marker={marker != null} " +
                $"entry='{entry.StableId}' markerId='{marker?.StableId ?? "<missing>"}'",
                prefabPath);
            return;
        }
        if (prefabAnimator.runtimeAnimatorController !=
            standard.SharedAnimatorController)
        {
            report.Add(
                "HUMANOID_CONTROLLER_NOT_SHARED",
                "角色没有复用统一 Animator Controller。",
                prefabPath);
        }
        Avatar avatar = prefabAnimator.avatar;
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            report.Add(
                "HUMANOID_AVATAR_INVALID",
                "角色没有有效 Humanoid Avatar。",
                prefabPath);
            return;
        }
        ValidateHumanMapping(avatar, report, marker.SourceModelPath);

        ModelImporter importer =
            AssetImporter.GetAtPath(marker.SourceModelPath) as ModelImporter;
        if (importer == null ||
            importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            !importer.bakeAxisConversion || importer.globalScale <= 0f ||
            importer.materialImportMode != ModelImporterMaterialImportMode.None)
        {
            report.Add(
                "HUMANOID_MODEL_IMPORT_INVALID",
                "角色模型未遵循 Humanoid、轴向、正比例和外置材质导入标准。",
                marker.SourceModelPath);
        }

        GameObject instance = Object.Instantiate(entry.Prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            if (!Approximately(instance.transform.localPosition, Vector3.zero) ||
                Quaternion.Angle(instance.transform.localRotation, Quaternion.identity) > 0.01f ||
                !Approximately(instance.transform.localScale, Vector3.one))
            {
                report.Add(
                    "HUMANOID_ROOT_TRANSFORM_INVALID",
                    "角色根节点必须零位移、零旋转和单位缩放。",
                    prefabPath);
            }
            foreach (Transform transform in instance.GetComponentsInChildren<Transform>(true))
            {
                Vector3 scale = transform.localScale;
                if (!Finite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                {
                    report.Add(
                        "HUMANOID_SCALE_INVALID",
                        $"骨骼或模型节点 '{transform.name}' 存在负数、零或异常缩放。",
                        prefabPath);
                    break;
                }
            }
            Bounds bounds = CalculateBounds(instance);
            if (Mathf.Abs(bounds.size.y - entry.ExpectedHeight) > 0.035f ||
                Mathf.Abs(bounds.min.y) > 0.035f)
            {
                report.Add(
                    "HUMANOID_DIMENSIONS_INVALID",
                    $"角色应为 {entry.ExpectedHeight:F2} 米且脚底位于 Y=0；" +
                    $"当前高度 {bounds.size.y:F3}，脚底 {bounds.min.y:F3}。",
                    prefabPath);
            }
            Animator animator = instance.GetComponent<Animator>();
            animator.Rebind();
            foreach (HumanBodyBones bone in RequiredBones)
            {
                Transform mapped = animator.GetBoneTransform(bone);
                if (mapped == null || !Finite(mapped.position) ||
                    mapped.localScale.x <= 0f || mapped.localScale.y <= 0f ||
                    mapped.localScale.z <= 0f)
                {
                    report.Add(
                        "HUMANOID_RUNTIME_MAPPING_INVALID",
                        $"运行时人体映射缺少或损坏：{bone}。",
                        prefabPath);
                    break;
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void ValidateHumanMapping(
        Avatar avatar,
        HumanoidPipelineAuditReport report,
        string assetPath)
    {
        HashSet<string> mapped = avatar.humanDescription.human
            .Select(value => value.humanName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (HumanBodyBones bone in RequiredBones)
        {
            if (!mapped.Contains(bone.ToString()))
            {
                report.Add(
                    "HUMANOID_BONE_MAPPING_MISSING",
                    $"Avatar 人体映射缺少 {bone}。",
                    assetPath);
            }
        }
    }

    private static void ValidatePreviewScene(
        HumanoidCharacterStandard standard,
        HumanoidPipelineAuditReport report)
    {
        string path = HumanoidCharacterStandard.PreviewScenePath;
        if (!System.IO.File.Exists(path))
        {
            report.Add(
                "HUMANOID_PREVIEW_SCENE_MISSING",
                "Humanoid 校准预览场景不存在。",
                path);
            return;
        }
        Scene scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            HumanoidCharacterPreviewController[] controllers = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    HumanoidCharacterPreviewController>(true))
                .ToArray();
            if (controllers.Length != 1 ||
                controllers[0].Standard != standard ||
                controllers[0].CharacterInstances.Length != 3 ||
                controllers[0].ActiveCharacterCount != 1)
            {
                report.Add(
                    "HUMANOID_PREVIEW_SCENE_INVALID",
                    "预览场景必须包含唯一控制器、三个角色且一次仅显示一个。",
                    path);
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.zero);
        }
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static bool Approximately(Vector3 a, Vector3 b) =>
        (a - b).sqrMagnitude <= 0.000001f;

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z);
}
