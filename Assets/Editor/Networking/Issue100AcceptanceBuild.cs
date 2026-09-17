using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class Issue100AcceptanceBuild
{
    private const string CityNewScene =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

    [Serializable]
    private sealed class AcceptanceBuildManifest
    {
        public string schemaVersion = "issue100-build-v1";
        public string target;
        public string scene;
        public string unityVersion;
        public string productVersion;
        public string builtAtUtc;
        public string serverBuildSubtarget;
        public string serverOutput;
        public string serverSha256;
        public ulong serverBytes;
        public string clientOutput;
        public string clientSha256;
        public ulong clientBytes;
    }

    public static void Build()
    {
        BuildTarget target = ResolveTarget(Option("-issue100BuildTarget"));
        string root = Option("-issue100BuildRoot");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ??
                ".", "Builds", "Issue100");
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        string server = Path.Combine(root, ServerName(target));
        string client = Path.Combine(root, ClientName(target));

        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        StandaloneBuildSubtarget previousSubtarget =
            EditorUserBuildSettings.standaloneBuildSubtarget;
        AddressableAssetSettings addressableSettings =
            AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetSettings.PlayerBuildOption previousAddressableOption =
            addressableSettings != null
                ? addressableSettings.BuildAddressablesWithPlayerBuild
                : AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        try
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException(
                    $"无法切换 Issue100 构建平台：{target}");

            BuildAddressablesForPlayer(addressableSettings);
            if (addressableSettings != null)
            {
                addressableSettings.BuildAddressablesWithPlayerBuild =
                    AddressableAssetSettings.PlayerBuildOption
                        .DoNotBuildWithPlayer;
            }
            StandaloneBuildSubtarget serverSubtarget =
                ResolveServerSubtarget(target);
            BuildReport serverReport = BuildPlayer(server, target,
                serverSubtarget);
            BuildReport clientReport = BuildPlayer(client, target,
                StandaloneBuildSubtarget.Player);
            var manifest = new AcceptanceBuildManifest
            {
                target = target.ToString(),
                scene = CityNewScene,
                unityVersion = Application.unityVersion,
                productVersion = PlayerSettings.bundleVersion,
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                serverBuildSubtarget = serverSubtarget ==
                    StandaloneBuildSubtarget.Server
                        ? "DedicatedServer"
                        : "PlayerHeadlessFallback",
                serverOutput = server,
                serverSha256 = HashArtifact(server),
                serverBytes = serverReport.summary.totalSize,
                clientOutput = client,
                clientSha256 = HashArtifact(client),
                clientBytes = clientReport.summary.totalSize
            };
            string manifestPath = Path.Combine(root, "build-manifest.json");
            File.WriteAllText(manifestPath,
                JsonUtility.ToJson(manifest, true),
                new UTF8Encoding(false));
            Debug.Log("[ISSUE100_BUILD][SUCCEEDED] " + manifestPath);
        }
        finally
        {
            if (addressableSettings != null)
                addressableSettings.BuildAddressablesWithPlayerBuild =
                    previousAddressableOption;
            EditorUserBuildSettings.standaloneBuildSubtarget =
                previousSubtarget;
            if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, previousTarget);
        }
    }

    private static void BuildAddressablesForPlayer(
        AddressableAssetSettings settings)
    {
        if (settings == null) return;
        EditorUserBuildSettings.standaloneBuildSubtarget =
            StandaloneBuildSubtarget.Player;
        Debug.Log("[ISSUE100_BUILD][ADDRESSABLES_START] " +
                  EditorUserBuildSettings.activeBuildTarget);
        AddressableAssetSettings.BuildPlayerContent(
            out AddressablesPlayerBuildResult result);
        if (!string.IsNullOrWhiteSpace(result.Error))
            throw new InvalidOperationException(
                "Issue100 Addressables 构建失败：" + result.Error);
        Debug.Log("[ISSUE100_BUILD][ADDRESSABLES_SUCCEEDED]");
    }

    private static BuildReport BuildPlayer(string output, BuildTarget target,
        StandaloneBuildSubtarget subtarget)
    {
        EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;
        var options = new BuildPlayerOptions
        {
            scenes = new[] { CityNewScene },
            locationPathName = output,
            target = target,
            subtarget = (int)subtarget,
            options = BuildOptions.Development
        };
        Debug.Log($"[ISSUE100_BUILD][START] subtarget={subtarget} " +
                  $"output={output}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"Issue100 {subtarget} 构建失败：" +
                $"{report.summary.result}，errors={report.summary.totalErrors}");
        return report;
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
            throw new FileNotFoundException("构建产物不存在。", path);

        foreach (string file in Directory.GetFiles(path, "*",
                     SearchOption.AllDirectories).OrderBy(value => value,
                     StringComparer.Ordinal))
        {
            string relative = file.Substring(path.Length)
                .Replace('\\', '/');
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

    private static string Hex(byte[] bytes) => string.Concat(
        bytes.Select(value => value.ToString("x2")));

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
            "mac" or "macos" or "standaloneosx" =>
                BuildTarget.StandaloneOSX,
            "windows" or "win64" or "standalonewindows64" =>
                BuildTarget.StandaloneWindows64,
            "linux" or "linux64" or "standalonelinux64" =>
                BuildTarget.StandaloneLinux64,
            _ => throw new ArgumentException(
                $"不支持的 Issue100 构建平台：{value}")
        };
    }

    private static StandaloneBuildSubtarget ResolveServerSubtarget(
        BuildTarget target)
    {
        string contents = EditorApplication.applicationContentsPath;
        DirectoryInfo unityApp = Directory.GetParent(contents);
        DirectoryInfo editorRoot = unityApp?.Parent;
        string playbackRoot = editorRoot == null
            ? string.Empty
            : Path.Combine(editorRoot.FullName, "PlaybackEngines");
        string platformFolder = target switch
        {
            BuildTarget.StandaloneOSX => "MacStandaloneSupport",
            BuildTarget.StandaloneWindows64 => "WindowsStandaloneSupport",
            _ => "LinuxStandaloneSupport"
        };
        string variations = Path.Combine(playbackRoot, platformFolder,
            "Variations");
        bool hasServerVariation = Directory.Exists(variations) &&
            Directory.EnumerateFileSystemEntries(variations)
                .Select(Path.GetFileName)
                .Any(value => value != null &&
                    value.Contains("_server_",
                        StringComparison.OrdinalIgnoreCase));
        if (hasServerVariation) return StandaloneBuildSubtarget.Server;
        Debug.LogWarning("[ISSUE100_BUILD][SERVER_FALLBACK] 当前编辑器未安装 " +
                         target + " Dedicated Server 模块；使用无本地玩家的 " +
                         "PlayerHeadlessFallback 运行权威服务器进程。");
        return StandaloneBuildSubtarget.Player;
    }

    private static string ServerName(BuildTarget target) => target switch
    {
        BuildTarget.StandaloneOSX => "FPSDedicatedServer.app",
        BuildTarget.StandaloneWindows64 => "FPSDedicatedServer.exe",
        _ => "FPSDedicatedServer.x86_64"
    };

    private static string ClientName(BuildTarget target) => target switch
    {
        BuildTarget.StandaloneOSX => "FPSAcceptanceClient.app",
        BuildTarget.StandaloneWindows64 => "FPSAcceptanceClient.exe",
        _ => "FPSAcceptanceClient.x86_64"
    };

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
}
