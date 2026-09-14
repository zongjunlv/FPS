using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FPS.Performance.HybridAi;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Issue64PerformanceReportExporter
{
    public const string DefaultBenchmarkVersion = "issue64-v1";

    public static Issue64BenchmarkEnvironment CaptureEnvironment(
        string contentVersion,
        int warmupFrames,
        int sampleFrames,
        int runsPerCase,
        double fixedDeltaTimeSeconds,
        string measurementScope = "adapter-step-wall-clock",
        int configuredWidth = 0,
        int configuredHeight = 0,
        bool? configuredFullscreen = null)
    {
        string entitiesVersion = PackageInfo.GetAllRegisteredPackages()
            .FirstOrDefault(package => string.Equals(
                package.name,
                "com.unity.entities",
                StringComparison.Ordinal))
            ?.version ?? "not-installed";
        string quality = QualitySettings.names.Length > 0
            ? QualitySettings.names[QualitySettings.GetQualityLevel()]
            : "unknown";
        string sceneName = SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = "no-active-scene";
        }

        return new Issue64BenchmarkEnvironment(
            DefaultBenchmarkVersion,
            Application.unityVersion,
            entitiesVersion,
            SystemInfo.operatingSystem,
            SystemInfo.processorType,
            SystemInfo.graphicsDeviceName,
            SystemInfo.systemMemorySize,
            SystemInfo.graphicsMemorySize,
            quality,
            configuredWidth > 0 ? configuredWidth : Math.Max(1, Screen.width),
            configuredHeight > 0
                ? configuredHeight
                : Math.Max(1, Screen.height),
            configuredFullscreen ?? Screen.fullScreen,
            QualitySettings.vSyncCount,
            Application.targetFrameRate,
            Debug.isDebugBuild ? "Development" : "Release",
            sceneName,
            string.IsNullOrWhiteSpace(contentVersion)
                ? "unknown"
                : contentVersion.Trim(),
            warmupFrames,
            sampleFrames,
            fixedDeltaTimeSeconds,
            runsPerCase,
            measurementScope);
    }

    public static Issue64BenchmarkReport BuildReport(
        Issue64BenchmarkEnvironment environment,
        IEnumerable<Issue64DensityComparison> comparisons,
        Issue64PerformanceGateThresholds thresholds = null)
    {
        Issue64DensityComparison[] ordered = comparisons?
            .OrderBy(value => value.EnemyCount)
            .ToArray() ?? throw new ArgumentNullException(nameof(comparisons));
        Issue64GateDecision decision = Issue64PerformanceGate.Evaluate(
            ordered,
            thresholds);
        return new Issue64BenchmarkReport(environment, ordered, decision);
    }

    public static void WriteReport(
        Issue64BenchmarkReport report,
        string outputDirectory,
        string fileStem = "issue64-hybrid-ai-ab")
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException(
                "Output directory is required.",
                nameof(outputDirectory));
        }

        if (string.IsNullOrWhiteSpace(fileStem))
        {
            throw new ArgumentException(
                "File stem is required.",
                nameof(fileStem));
        }

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, fileStem + ".json"),
            Issue64PerformanceReportWriter.ToJson(report));
        File.WriteAllText(
            Path.Combine(outputDirectory, fileStem + ".md"),
            Issue64PerformanceReportWriter.ToMarkdown(report));
    }
}
