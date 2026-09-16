using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PlayerAppearanceAuditReport
{
    private readonly List<string> issues = new();
    public IReadOnlyList<string> Issues => issues;
    public bool IsValid => issues.Count == 0;
    public void Add(string value) => issues.Add(value);
}

public static class PlayerAppearanceCatalogAuditor
{
    public static PlayerAppearanceAuditReport Audit(
        PlayerAppearanceCatalog catalog = null)
    {
        var report = new PlayerAppearanceAuditReport();
        catalog ??= AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.DefaultAssetPath);
        if (catalog == null)
        {
            report.Add("玩家外观目录资源缺失。");
            return report;
        }
        if (!catalog.TryValidate(out string error)) report.Add(error);
        AuditDefinitions(catalog, report);
        AuditHostPrefab(catalog, report);
        AuditPreviewScene(catalog, report);
        return report;
    }

    public static void AppendTo(ContentValidationReport contentReport)
    {
        PlayerAppearanceAuditReport report = Audit();
        foreach (string issue in report.Issues)
            contentReport.Add("PLAYER_APPEARANCE_INVALID", issue,
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    PlayerAppearanceCatalog.DefaultAssetPath));
    }

    private static void AuditDefinitions(PlayerAppearanceCatalog catalog,
        PlayerAppearanceAuditReport report)
    {
        foreach (PlayerAppearanceDefinition definition in catalog.Definitions)
        {
            if (definition == null) continue;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(definition)))
                report.Add($"外观定义未持久化：{definition.StableId}");
            GameObject prefab = definition.VisualPrefab;
            if (prefab == null) continue;
            if (prefab.transform.localPosition != Vector3.zero ||
                prefab.transform.localRotation != Quaternion.identity ||
                prefab.transform.localScale != Vector3.one)
                report.Add($"外观 Prefab 根节点不是单位变换：{definition.StableId}");
            if (prefab.GetComponentInChildren<CharacterController>(true) != null ||
                prefab.GetComponentInChildren<PlayerController>(true) != null)
                report.Add($"外观包含权威玩家逻辑：{definition.StableId}");
        }
    }

    private static void AuditHostPrefab(PlayerAppearanceCatalog catalog,
        PlayerAppearanceAuditReport report)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PlayerAppearanceCatalog.HostPrefabAssetPath);
        if (prefab == null)
        {
            report.Add("玩家逻辑/表现分离验证 Prefab 缺失。");
            return;
        }
        CharacterController collision = prefab.GetComponent<CharacterController>();
        PlayerAppearanceHost host = prefab.GetComponent<PlayerAppearanceHost>();
        if (collision == null || host == null || host.VisualRoot == null ||
            host.VisualRoot.parent != prefab.transform)
        {
            report.Add("玩家逻辑根、权威碰撞与表现根没有正确分离。");
            return;
        }
        PlayerCollisionReference reference = catalog.DefaultDefinition
            .CollisionReference;
        if (!Mathf.Approximately(collision.height, reference.Height) ||
            !Mathf.Approximately(collision.radius, reference.Radius) ||
            collision.center != reference.Center ||
            prefab.transform.localScale != Vector3.one ||
            host.VisualRoot.localScale != Vector3.one)
            report.Add("外观比例影响了逻辑根或权威碰撞参考。");
    }

    private static void AuditPreviewScene(PlayerAppearanceCatalog catalog,
        PlayerAppearanceAuditReport report)
    {
        const string path = PlayerAppearanceCatalog.PreviewSceneAssetPath;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            report.Add("玩家外观预览场景缺失。");
            return;
        }
        Scene scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            PlayerAppearancePreviewController[] controllers = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    PlayerAppearancePreviewController>(true)).ToArray();
            if (controllers.Length != 1 || controllers[0].Catalog != catalog ||
                controllers[0].PreviewAnchor == null ||
                controllers[0].PreviewInstance == null ||
                string.IsNullOrWhiteSpace(controllers[0].SelectedDisplayName))
                report.Add("预览场景无法稳定显示、命名或切换角色外观。");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
