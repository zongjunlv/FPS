using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FPS.AI.HybridEcs;
using FPS.Performance.HybridAi;
using UnityEditor;
using UnityEngine;

public static class Issue64PerformanceBenchmarkCli
{
    private static readonly int[] EnemyCounts = { 100, 300, 500 };
    private const int DefaultSeed = 64064;
    private const int DefaultWarmupFrames = 300;
    private const int DefaultSampleFrames = 3600;
    private const int DefaultRuns = 3;
    private const float DefaultFixedDeltaTime = 1f / 60f;
    private const string MeasurementScope =
        "AI adapter Step wall-clock only; rendering, animation, physics and " +
        "Editor repaint excluded. Main-thread, GC, memory and decision latency " +
        "come from the concrete adapter snapshot.";

    [MenuItem("Tools/FPS/Performance/Issue 64/运行 Hybrid AI A-B 压测")]
    public static void RunFromMenu()
    {
        try
        {
            Run(Environment.GetCommandLineArgs());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void RunFromCommandLine()
    {
        try
        {
            Run(Environment.GetCommandLineArgs());
            Debug.Log("ISSUE64_PERF_COMPLETED");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
                return;
            }

            throw;
        }
    }

    private static void Run(string[] arguments)
    {
        // Tests intentionally clear the registry. Re-register here so menu and
        // batch runs remain repeatable without requiring a domain reload.
        Issue64GameObjectBenchmarkRegistration.RegisterFactory();
        Issue64EcsBenchmarkAdapter.RegisterFactory();
        var options = Options.Parse(arguments);
        ConfigureQuality(options.QualityLevel);
        Directory.CreateDirectory(options.OutputDirectory);

        for (int round = 0; round < options.Runs; round++)
        {
            int seed = unchecked(options.Seed + round);
            var byModeAndCount = new Dictionary<string,
                Issue64BenchmarkRunResult>();

            for (int densityIndex = 0;
                 densityIndex < EnemyCounts.Length;
                 densityIndex++)
            {
                int enemyCount = EnemyCounts[densityIndex];
                bool ecsFirst = ((round + densityIndex) & 1) != 0;
                HybridAiBenchmarkMode first = ecsFirst
                    ? HybridAiBenchmarkMode.Ecs
                    : HybridAiBenchmarkMode.GameObject;
                HybridAiBenchmarkMode second = ecsFirst
                    ? HybridAiBenchmarkMode.GameObject
                    : HybridAiBenchmarkMode.Ecs;
                RunCase(first, enemyCount, seed, round, options,
                    byModeAndCount);
                RunCase(second, enemyCount, seed, round, options,
                    byModeAndCount);
            }

            var comparisons = new List<Issue64DensityComparison>(
                EnemyCounts.Length);
            foreach (int enemyCount in EnemyCounts)
            {
                comparisons.Add(new Issue64DensityComparison(
                    enemyCount,
                    byModeAndCount[Key(
                        HybridAiBenchmarkMode.GameObject,
                        enemyCount)],
                    byModeAndCount[Key(
                        HybridAiBenchmarkMode.Ecs,
                        enemyCount)]));
            }

            Issue64BenchmarkEnvironment environment =
                Issue64PerformanceReportExporter.CaptureEnvironment(
                    options.ContentVersion,
                    options.WarmupFrames,
                    options.SampleFrames,
                    options.Runs,
                    options.FixedDeltaTime,
                    MeasurementScope,
                    options.Width,
                    options.Height,
                    options.Fullscreen);
            Issue64BenchmarkReport report =
                Issue64PerformanceReportExporter.BuildReport(
                    environment,
                    comparisons);
            string stem = $"round-{round + 1:00}";
            Issue64PerformanceReportExporter.WriteReport(
                report,
                options.OutputDirectory,
                stem);
            Debug.Log(
                $"ISSUE64_PERF_ROUND {round + 1}/{options.Runs} " +
                $"{report.Decision.Outcome} " +
                Path.Combine(options.OutputDirectory, stem + ".json"));
        }
    }

    private static void RunCase(
        HybridAiBenchmarkMode mode,
        int enemyCount,
        int seed,
        int round,
        Options options,
        IDictionary<string, Issue64BenchmarkRunResult> output)
    {
        if (!Issue64BenchmarkAdapterRegistry.TryCreate(mode, out var adapter))
        {
            throw new InvalidOperationException(
                $"{mode} benchmark adapter is not registered.");
        }

        if (adapter.Mode != mode)
        {
            adapter.Dispose();
            throw new InvalidOperationException(
                $"Registered {mode} factory returned {adapter.Mode}.");
        }

        using var session = new Issue64BenchmarkSession(
            adapter,
            enemyCount,
            seed,
            round,
            options.WarmupFrames,
            options.SampleFrames,
            options.FixedDeltaTime);

        while (!session.IsComplete)
        {
            session.Step();
        }

        output.Add(Key(mode, enemyCount), session.Complete());
    }

    private static void ConfigureQuality(string qualityLevel)
    {
        int qualityIndex = Array.FindIndex(
            QualitySettings.names,
            value => string.Equals(
                value,
                qualityLevel,
                StringComparison.OrdinalIgnoreCase));
        if (qualityIndex < 0)
        {
            throw new InvalidOperationException(
                $"Unknown quality level '{qualityLevel}'.");
        }

        QualitySettings.SetQualityLevel(qualityIndex, true);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }

    private static string Key(HybridAiBenchmarkMode mode, int enemyCount)
    {
        return mode + ":" + enemyCount.ToString(
            CultureInfo.InvariantCulture);
    }

    private sealed class Options
    {
        public int Seed { get; private set; }
        public int WarmupFrames { get; private set; }
        public int SampleFrames { get; private set; }
        public int Runs { get; private set; }
        public float FixedDeltaTime { get; private set; }
        public string QualityLevel { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Fullscreen { get; private set; }
        public string ContentVersion { get; private set; }
        public string OutputDirectory { get; private set; }

        public static Options Parse(string[] arguments)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)
                ?.FullName ?? Environment.CurrentDirectory;
            return new Options
            {
                Seed = ReadInt(arguments, "-issue64-seed", DefaultSeed),
                WarmupFrames = Math.Max(0, ReadInt(
                    arguments,
                    "-issue64-warmup-frames",
                    DefaultWarmupFrames)),
                SampleFrames = Math.Max(1, ReadInt(
                    arguments,
                    "-issue64-sample-frames",
                    DefaultSampleFrames)),
                Runs = Math.Max(1, ReadInt(
                    arguments,
                    "-issue64-runs",
                    DefaultRuns)),
                FixedDeltaTime = Math.Max(0.0001f, ReadFloat(
                    arguments,
                    "-issue64-fixed-delta",
                    DefaultFixedDeltaTime)),
                QualityLevel = ReadString(
                    arguments,
                    "-issue64-quality",
                    "PC"),
                Width = Math.Max(1, ReadInt(
                    arguments,
                    "-issue64-width",
                    1920)),
                Height = Math.Max(1, ReadInt(
                    arguments,
                    "-issue64-height",
                    1080)),
                Fullscreen = ReadBool(
                    arguments,
                    "-issue64-fullscreen",
                    false),
                ContentVersion = ReadString(
                    arguments,
                    "-issue64-content-version",
                    "workspace-current"),
                OutputDirectory = ReadString(
                    arguments,
                    "-issue64-output",
                    Path.Combine(
                        projectRoot,
                        "artifacts/performance/issue64/raw"))
            };
        }

        private static int ReadInt(
            string[] arguments,
            string key,
            int fallback)
        {
            string value = ReadString(arguments, key, string.Empty);
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
                ? parsed
                : fallback;
        }

        private static float ReadFloat(
            string[] arguments,
            string key,
            float fallback)
        {
            string value = ReadString(arguments, key, string.Empty);
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsed)
                ? parsed
                : fallback;
        }

        private static bool ReadBool(
            string[] arguments,
            string key,
            bool fallback)
        {
            string value = ReadString(arguments, key, string.Empty);
            return bool.TryParse(value, out bool parsed)
                ? parsed
                : fallback;
        }

        private static string ReadString(
            string[] arguments,
            string key,
            string fallback)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                    arguments[index],
                    key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return fallback;
        }
    }
}
