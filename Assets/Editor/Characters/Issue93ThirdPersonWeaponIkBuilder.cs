using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using EditorAnimatorController = UnityEditor.Animations.AnimatorController;

public static class Issue93ThirdPersonWeaponIkBuilder
{
    [MenuItem("FPS/Content/Issue 93/Rebuild Weapon IK And Aim Constraints")]
    public static void Build()
    {
        EditorAnimatorController controller = AssetDatabase.LoadAssetAtPath<
            EditorAnimatorController>(
            Issue91ThirdPersonAnimationBuilder.ControllerPath);
        if (controller == null)
            throw new InvalidOperationException("第三人称动画控制器不存在。");
        AnimatorControllerLayer[] layers = controller.layers;
        int upperIndex = Array.FindIndex(layers, layer =>
            layer.name == ThirdPersonAnimationParameters.UpperBodyLayerName);
        if (upperIndex < 0)
            throw new InvalidOperationException("第三人称动画缺少 Upper Body 层。");
        layers[upperIndex].iKPass = true;
        controller.layers = layers;
        Issue91ThirdPersonAnimationBuilder.ApplyAimPitchConvention(controller);
        EditorUtility.SetDirty(controller);

        Issue92ThirdPersonWeaponCalibrationBuilder.Build();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ThirdPersonWeaponCatalog weapons = AssetDatabase.LoadAssetAtPath<
            ThirdPersonWeaponCatalog>(ThirdPersonWeaponCatalog.DefaultAssetPath);
        string error = "第三人称武器目录不存在。";
        if (weapons == null || !weapons.TryValidate(out error))
            throw new InvalidOperationException(
                $"Issue #93 武器 IK 前置校准无效：{error}");
        Debug.Log(
            "Issue #93 已启用 Upper Body IK Pass、左手握持、受限瞄准、" +
            "动作权重混合及枪口反馈射线对齐。 ");
    }
}
