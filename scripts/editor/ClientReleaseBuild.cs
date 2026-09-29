using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Execute with Unity Pipeline run_script; outside Assets to avoid a domain reload.
public static class ClientReleaseBuild
{
    private const string PendingKey = "FPS.ClientRelease.Pending";

    public static string Schedule(string outputRoot, string targetName,
        string sourceCommit, string version)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer || SessionState.GetBool(PendingKey, false))
            throw new InvalidOperationException("Editor must be idle before a release build.");
        if (!Regex.IsMatch(sourceCommit ?? "", "^[0-9a-f]{40}$"))
            throw new ArgumentException("Provide the full committed source revision.");
        if (PlayerSettings.bundleVersion != version)
            throw new InvalidOperationException("Product version does not match release version.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save or discard dirty scenes before building.");

        BuildTarget target = targetName switch
        {
            "macos" => BuildTarget.StandaloneOSX,
            "windows" => BuildTarget.StandaloneWindows64,
            _ => throw new ArgumentException("Release targets are macos or windows.")
        };
        string root = Path.GetFullPath(outputRoot);
        string artifact = target == BuildTarget.StandaloneOSX
            ? "macOS/FPS-PVE-Demo.app" : "Windows/FPS-PVE-Demo.exe";
        string destination = Path.Combine(root, artifact);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Refusing to overwrite an existing release artifact.");
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled)
            .Select(s => s.path).ToArray();
        if (scenes.Length != 5 || !scenes[0].EndsWith("/ModeEntry.unity") ||
            !scenes.Any(s => s.EndsWith("/CityNew.unity")))
            throw new InvalidOperationException("The complete five-scene client is required.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string manifestPath = Path.Combine(root,
            $"build-provenance-{(targetName == "macos" ? "macOS" : "Windows")}.json");
        var manifest = new Manifest
        {
            target = target.ToString(), productVersion = version,
            sourceCommit = sourceCommit, unityVersion = Application.unityVersion,
            artifact = artifact, scenes = scenes,
            packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .OrderBy(p => p.name).Select(p => new Package
                { name = p.name, version = p.version }).ToArray(),
            startedAtUtc = DateTime.UtcNow.ToString("O")
        };
        Write(manifestPath, manifest);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.delayCall += () => Build(manifestPath, destination, target, manifest);
        return manifestPath;
    }

    private static void Build(string manifestPath, string destination,
        BuildTarget target, Manifest manifest)
    {
        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        StandaloneBuildSubtarget previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
        int previousArchitecture = PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone);
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        var previousAddressables = settings != null
            ? settings.BuildAddressablesWithPlayerBuild
            : AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        try
        {
            manifest.state = "running";
            Write(manifestPath, manifest);
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            if (target == BuildTarget.StandaloneOSX)
                PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 2); // Universal.
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException($"Could not switch build target to {target}.");
            if (settings != null)
                settings.BuildAddressablesWithPlayerBuild =
                    AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = manifest.scenes, target = target,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                locationPathName = destination, options = BuildOptions.None
            });
            manifest.totalErrors = report.summary.totalErrors;
            manifest.totalWarnings = report.summary.totalWarnings;
            manifest.totalBytes = report.summary.totalSize;
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Release build failed: {report.summary.result}.");
            manifest.state = "completed";
        }
        catch (Exception exception)
        {
            manifest.state = "failed";
            manifest.failure = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            try
            {
                if (settings != null) settings.BuildAddressablesWithPlayerBuild = previousAddressables;
                PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, previousArchitecture);
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
                if (EditorUserBuildSettings.activeBuildTarget != previousTarget &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, previousTarget))
                    throw new InvalidOperationException("Could not restore the original editor build target.");
            }
            catch (Exception exception)
            {
                manifest.state = "failed";
                manifest.failure = exception.Message;
                Debug.LogException(exception);
            }
            manifest.finishedAtUtc = DateTime.UtcNow.ToString("O");
            Write(manifestPath, manifest);
            SessionState.EraseBool(PendingKey);
            Debug.Log($"[CLIENT_RELEASE] {manifest.target}: {manifest.state}");
        }
    }

    private static void Write(string path, Manifest manifest) =>
        File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));

    private sealed class Package { public string name; public string version; }
    private sealed class Manifest
    {
        public string schemaVersion = "fps-client-release-v1";
        public string state = "scheduled";
        public string sourceCommit;
        public string productVersion;
        public string unityVersion;
        public string target;
        public string subtarget = "Player";
        public string buildOptions = "None";
        public string apiCompatibilityVersion = "0.1.0";
        public string battleProtocolVersion = "2";
        public string contentVersion = "citynew-v1";
        public string artifact;
        public string[] scenes;
        public Package[] packages;
        public string startedAtUtc;
        public string finishedAtUtc;
        public int totalErrors;
        public int totalWarnings;
        public ulong totalBytes;
        public string failure;
    }
}
