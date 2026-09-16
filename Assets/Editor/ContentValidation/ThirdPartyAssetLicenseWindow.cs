using System;
using UnityEditor;
using UnityEngine;

public sealed class ThirdPartyAssetLicenseWindow : EditorWindow
{
    private ThirdPartyLicenseAuditReport report;
    private Vector2 scroll;

    [MenuItem("FPS/Content/Third-Party License Audit")]
    public static void Open()
    {
        var window = GetWindow<ThirdPartyAssetLicenseWindow>("第三方授权准入");
        window.minSize = new Vector2(720f, 440f);
        window.Show();
    }

    private void OnEnable()
    {
        report = ThirdPartyAssetLicenseAuditor.Audit();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "角色模型和动画分别校验。缺少来源、许可原文、下载日期或 SHA-256 时，正式内容质量门禁会失败。",
            MessageType.Info);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("重新校验", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                report = ThirdPartyAssetLicenseAuditor.Audit();
            }
            GUILayout.FlexibleSpace();
            if (report != null)
            {
                GUILayout.Label(
                    $"正式角色 {report.ApprovedModelCount}  动画集 {report.ApprovedAnimationSetCount}  错误 {report.ErrorCount}",
                    EditorStyles.miniLabel);
            }
        }

        if (report == null)
        {
            return;
        }
        EditorGUILayout.HelpBox(
            report.IsValid
                ? "授权准入通过，当前名单可以进入后续角色管线。"
                : "授权准入失败，存在资产被阻止进入正式角色名单。",
            report.IsValid ? MessageType.Info : MessageType.Error);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (ThirdPartyLicenseAuditEntry entry in report.Entries)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Color previous = GUI.color;
                GUI.color = entry.Severity switch
                {
                    ThirdPartyLicenseAuditSeverity.Approved =>
                        new Color(0.68f, 1f, 0.78f),
                    ThirdPartyLicenseAuditSeverity.Warning =>
                        new Color(1f, 0.86f, 0.46f),
                    _ => new Color(1f, 0.62f, 0.62f)
                };
                EditorGUILayout.LabelField(
                    $"{Status(entry.Severity)}  {entry.DisplayName}",
                    EditorStyles.boldLabel);
                GUI.color = previous;
                EditorGUILayout.LabelField(entry.SubjectId, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(entry.Message, EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrWhiteSpace(entry.AssetPath))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.SelectableLabel(
                            entry.AssetPath,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        UnityEngine.Object asset =
                            AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
                        using (new EditorGUI.DisabledScope(asset == null))
                        {
                            if (GUILayout.Button("定位", GUILayout.Width(60f)))
                            {
                                Selection.activeObject = asset;
                                EditorGUIUtility.PingObject(asset);
                            }
                        }
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static string Status(ThirdPartyLicenseAuditSeverity severity) =>
        severity switch
        {
            ThirdPartyLicenseAuditSeverity.Approved => "通过",
            ThirdPartyLicenseAuditSeverity.Warning => "警告",
            _ => "阻止"
        };
}
