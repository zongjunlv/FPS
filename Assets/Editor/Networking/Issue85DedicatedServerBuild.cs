using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class Issue85DedicatedServerBuild
{
    private const string CityNewScene =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";
    private static BuildTarget? forcedTarget;
    private static string forcedOutput;

    [Serializable]
    private sealed class BuildManifest
    {
        public string schemaVersion = "fps-dedicated-build-v2";
        public string target;
        public string output;
        public string scene;
        public string unityVersion;
        public string productVersion;
        public string protocolVersion;
        public string contentVersion;
        public string builtAtUtc;
        public ulong totalBytes;
        public string sha256;
    }

    public static void Build()
    {
        BuildTarget target = forcedTarget ??
            ResolveTarget(Option("-serverBuildTarget"));
        string output = forcedOutput ?? Option("-buildOutput");
        if (string.IsNullOrWhiteSpace(output))
            output = DefaultOutput(target);
        output = Path.GetFullPath(output);
        string directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        StandaloneBuildSubtarget previousSubtarget =
            EditorUserBuildSettings.standaloneBuildSubtarget;
        try
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException(
                    $"无法切换专用服务器构建平台：{target}");
            EditorUserBuildSettings.standaloneBuildSubtarget =
                StandaloneBuildSubtarget.Server;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { CityNewScene },
                locationPathName = output,
                target = target,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.Development
            };
            Debug.Log($"[DEDICATED_SERVER_BUILD][START] target={target} " +
                      $"scene={CityNewScene} output={output}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"专用服务器构建失败：{report.summary.result}，" +
                    $"errors={report.summary.totalErrors}");

            string protocolVersion = Option("-serverProtocolVersion");
            if (string.IsNullOrWhiteSpace(protocolVersion))
                protocolVersion = "1";
            string contentVersion = Option("-serverContentVersion");
            if (string.IsNullOrWhiteSpace(contentVersion))
                contentVersion = "citynew-v1";
            string manifestPath = Path.Combine(
                Path.GetDirectoryName(output) ?? ".",
                "dedicated-server-build.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(new BuildManifest
            {
                target = target.ToString(),
                output = output,
                scene = CityNewScene,
                unityVersion = Application.unityVersion,
                productVersion = PlayerSettings.bundleVersion,
                protocolVersion = protocolVersion.Trim(),
                contentVersion = contentVersion.Trim(),
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                totalBytes = report.summary.totalSize,
                sha256 = HashArtifact(output)
            }, true), new UTF8Encoding(false));
            Debug.Log($"[DEDICATED_SERVER_BUILD][SUCCEEDED] output={output} " +
                      $"bytes={report.summary.totalSize} manifest={manifestPath}");
        }
        finally
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
            if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, previousTarget);
        }
    }

    public static void BuildLinuxDeployment()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        forcedTarget = BuildTarget.StandaloneLinux64;
        forcedOutput = Path.Combine(root, "Builds", "CoopAuthoritative",
            "Linux", "FPSDedicatedServer.x86_64");
        try
        {
            Build();
        }
        finally
        {
            forcedTarget = null;
            forcedOutput = null;
        }
    }

    public static void RestoreHostClientTarget()
    {
#if UNITY_EDITOR_OSX
        BuildTarget target = BuildTarget.StandaloneOSX;
#elif UNITY_EDITOR_WIN
        BuildTarget target = BuildTarget.StandaloneWindows64;
#else
        BuildTarget target = BuildTarget.StandaloneLinux64;
#endif
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone, target))
            throw new InvalidOperationException($"无法恢复客户端构建平台：{target}");
        EditorUserBuildSettings.standaloneBuildSubtarget =
            StandaloneBuildSubtarget.Player;
        Debug.Log($"[DEDICATED_SERVER_BUILD][RESTORED] target={target} " +
                  "subtarget=Player");
    }

    private static BuildTarget ResolveTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
#if UNITY_EDITOR_OSX
            return BuildTarget.StandaloneOSX;
#elif UNITY_EDITOR_WIN
            return BuildTarget.StandaloneWindows64;
#else
            return BuildTarget.StandaloneLinux64;
#endif
        }
        return value.Trim().ToLowerInvariant() switch
        {
            "mac" or "macos" or "standaloneosx" => BuildTarget.StandaloneOSX,
            "windows" or "win64" or "standalonewindows64" =>
                BuildTarget.StandaloneWindows64,
            "linux" or "linux64" or "standalonelinux64" =>
                BuildTarget.StandaloneLinux64,
            _ => throw new ArgumentException($"不支持的专用服务器平台：{value}")
        };
    }

    private static string DefaultOutput(BuildTarget target)
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string file = target switch
        {
            BuildTarget.StandaloneOSX => "FPSDedicatedServer.app",
            BuildTarget.StandaloneWindows64 => "FPSDedicatedServer.exe",
            _ => "FPSDedicatedServer.x86_64"
        };
        return Path.Combine(root, "Builds", "DedicatedServer", file);
    }

    private static string Option(string key)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], key,
                    StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        }
        return string.Empty;
    }

    private static string HashArtifact(string path)
    {
        using SHA256 hash = SHA256.Create();
        if (File.Exists(path))
        {
            using FileStream stream = File.OpenRead(path);
            return Hex(hash.ComputeHash(stream));
        }
        if (!Directory.Exists(path))
            throw new FileNotFoundException("服务器构建产物不存在。", path);
        foreach (string file in Directory.GetFiles(path, "*",
                     SearchOption.AllDirectories).OrderBy(value => value,
                     StringComparer.Ordinal))
        {
            string relative = file.Substring(path.Length).Replace('\\', '/');
            byte[] name = Encoding.UTF8.GetBytes(relative + "\n");
            hash.TransformBlock(name, 0, name.Length, name, 0);
            using FileStream stream = File.OpenRead(file);
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.TransformBlock(buffer, 0, read, buffer, 0);
        }
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Hex(hash.Hash);
    }

    private static string Hex(byte[] value) =>
        BitConverter.ToString(value).Replace("-", string.Empty)
            .ToLowerInvariant();
}
