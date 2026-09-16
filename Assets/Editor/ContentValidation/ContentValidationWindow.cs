using UnityEditor;
using UnityEngine;

public sealed class ContentValidationWindow : EditorWindow
{
    private ContentValidationReport report;
    private Vector2 scroll;
    private bool errorsOnly;
    private string search = string.Empty;

    [MenuItem("FPS/Content/Validate Content")]
    public static void Open()
    {
        var window = GetWindow<ContentValidationWindow>("内容资产校验");
        window.minSize = new Vector2(620, 360);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("只读检查正式内容及其引用，不修改资产。点击结果可在 Project 和 Inspector 定位。", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("扫描正式内容", EditorStyles.toolbarButton, GUILayout.Width(110)))
                report = ContentAssetValidator.Validate();
            if (GUILayout.Button("第三方授权清单", EditorStyles.toolbarButton, GUILayout.Width(120)))
                ThirdPartyAssetLicenseWindow.Open();
            errorsOnly = GUILayout.Toggle(errorsOnly, "仅错误", EditorStyles.toolbarButton, GUILayout.Width(65));
            search = GUILayout.TextField(search, EditorStyles.toolbarTextField);
        }
        if (report == null)
        {
            EditorGUILayout.LabelField("点击“扫描正式内容”开始检查。", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        EditorGUILayout.LabelField($"错误 {report.ErrorCount}    警告 {report.WarningCount}", EditorStyles.boldLabel);
        if (report.Issues.Count == 0)
            EditorGUILayout.HelpBox("校验通过，未发现配置问题。", MessageType.Info);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (ContentValidationIssue issue in report.Issues)
        {
            if (errorsOnly && issue.Severity != ContentValidationSeverity.Error) continue;
            string text = $"{issue.Code} {issue.Message} {issue.AssetPath}";
            if (!string.IsNullOrEmpty(search) && text.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox($"[{issue.Code}] {issue.Message}",
                    issue.Severity == ContentValidationSeverity.Error ? MessageType.Error : MessageType.Warning);
                EditorGUILayout.SelectableLabel(issue.AssetPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                using (new EditorGUI.DisabledScope(issue.Asset == null))
                {
                    if (GUILayout.Button("定位资产", GUILayout.Width(90)))
                    {
                        Selection.activeObject = issue.Asset;
                        EditorGUIUtility.PingObject(issue.Asset);
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }
}
