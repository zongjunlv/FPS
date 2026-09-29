using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Unity.Pipeline.Commands;

/// <summary>Runs regressions in the already-open editor, without opening a test window.</summary>
[InitializeOnLoad]
public static class CoopRepairCliTests
{
    private const string PendingKey = "FPS.CoopRepair.TestOutput";
    private const string BuildPendingKey = "FPS.CoopRepair.BuildOutput";
    private static TestRunnerApi api;

    static CoopRepairCliTests()
    {
        api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Results());
    }

    [CliCommand("coop_repair_tests", "Run focused coop regressions in the current editor")]
    public static string Run(
        [CliArg("filter", "Test class/name filter", Required = true)] string filter,
        [CliArg("output", "NUnit result path", Required = true)] string output,
        [CliArg("mode", "EditMode or PlayMode")] string mode = "EditMode")
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Editor must be compiled and outside Play mode.");
        if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
            throw new InvalidOperationException("A repair test run is already active.");
        string fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        SessionState.SetString(PendingKey, fullPath);
        try
        {
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = Enum.Parse<TestMode>(mode, true),
                testNames = filter.Split(';')
            }));
        }
        catch
        {
            SessionState.EraseString(PendingKey);
            throw;
        }
        return fullPath;
    }

    [CliCommand("coop_repair_build", "Schedule same-version acceptance builds without a short RPC timeout")]
    public static string Build(
        [CliArg("output", "Build directory", Required = true)] string output,
        [CliArg("target", "macos, linux or windows")] string target = "macos")
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
            !string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")) ||
            !string.IsNullOrEmpty(SessionState.GetString(BuildPendingKey, "")))
            throw new InvalidOperationException("Editor must be idle before scheduling a build.");
        string fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(fullPath);
        string status = Path.Combine(fullPath, "repair-build-status.json");
        SessionState.SetString(BuildPendingKey, fullPath);
        File.WriteAllText(status, JsonUtility.ToJson(new BuildStatus { state = "scheduled" }));
        EditorApplication.delayCall += () =>
        {
            var result = new BuildStatus { state = "running" };
            File.WriteAllText(status, JsonUtility.ToJson(result));
            try
            {
                Issue100AcceptanceBuild.BuildAt(fullPath, target);
                result.state = "completed";
            }
            catch (Exception exception)
            {
                result.state = "failed";
                result.failure = exception.ToString();
                Debug.LogException(exception);
            }
            finally
            {
                result.finishedAtUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(status, JsonUtility.ToJson(result, true));
                SessionState.EraseString(BuildPendingKey);
            }
        };
        return status;
    }

    [Serializable]
    private sealed class BuildStatus
    {
        public string state;
        public string failure;
        public string finishedAtUtc;
    }

    private sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            string output = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(output)) return;
            TestRunnerApi.SaveResultToFile(result, output);
            SessionState.EraseString(PendingKey);
            Debug.Log($"[CoopRepairTests] pass={result.PassCount} fail={result.FailCount} output={output}");
        }
    }
}
