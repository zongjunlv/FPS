using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ContentValidationCli
{
    [Serializable]
    private sealed class JsonReport
    {
        public int errors;
        public int warnings;
        public JsonIssue[] issues;
    }

    [Serializable]
    private sealed class JsonIssue
    {
        public string code;
        public string severity;
        public string message;
        public string assetPath;
    }

    // -batchmode -quit -executeMethod ContentValidationCli.Run
    // Optional: -contentValidationRoot Assets/... -contentValidationReport /path/report.json
    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("此入口仅用于无界面校验；编辑器中请使用 FPS/Content/Validate Content。");
        int exitCode;
        try
        {
            string root = Option("-contentValidationRoot");
            ContentValidationReport report = ContentAssetValidator.Validate(root == null ? null : new[] { root });
            foreach (ContentValidationIssue issue in report.Issues)
            {
                string message = $"[{issue.Code}] {issue.AssetPath}: {issue.Message}";
                if (issue.Severity == ContentValidationSeverity.Error) Debug.LogError(message, issue.Asset);
                else Debug.LogWarning(message, issue.Asset);
            }
            string output = Option("-contentValidationReport");
            if (output != null)
            {
                string fullPath = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, JsonUtility.ToJson(new JsonReport
                {
                    errors = report.ErrorCount,
                    warnings = report.WarningCount,
                    issues = report.Issues.Select(issue => new JsonIssue
                    {
                        code = issue.Code, severity = issue.Severity.ToString(),
                        message = issue.Message, assetPath = issue.AssetPath
                    }).ToArray()
                }, true));
            }
            Debug.Log($"[CONTENT_VALIDATION] errors={report.ErrorCount} warnings={report.WarningCount}");
            exitCode = report.IsValid ? 0 : 1;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exitCode = 2;
        }
        EditorApplication.Exit(exitCode);
    }

    private static string Option(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] != name) continue;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-"))
                throw new ArgumentException($"Missing value for {name}.");
            return args[index + 1];
        }
        return null;
    }
}
