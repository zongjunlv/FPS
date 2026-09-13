using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FPS.Simulation.Offline;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FPS.Editor.Balance
{
    public sealed class Issue63BalanceRunResult
    {
        internal Issue63BalanceRunResult(
            string reportPath,
            string markdownPath,
            string reproductionDirectory,
            int reproductionCount,
            Issue63BalanceReport report)
        {
            ReportPath = reportPath;
            MarkdownPath = markdownPath;
            ReproductionDirectory = reproductionDirectory;
            ReproductionCount = reproductionCount;
            Report = report;
        }

        public string ReportPath { get; }
        public string MarkdownPath { get; }
        public string ReproductionDirectory { get; }
        public int ReproductionCount { get; }
        public Issue63BalanceReport Report { get; }
    }

    public static class Issue63BalanceSimulationService
    {
        public static Issue63BalanceRunResult Run(
            long seedStart,
            int seedCount,
            int parallelism,
            string outputPath,
            string reproductionDirectory)
        {
            if (seedCount < 1)
                throw new ArgumentOutOfRangeException(nameof(seedCount));
            if (parallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(parallelism));

            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string absoluteOutput = ResolvePath(projectRoot, outputPath);
            string absoluteReproductions = ResolvePath(
                projectRoot,
                reproductionDirectory);
            long[] seeds = CreateSeeds(seedStart, seedCount);
            var stopwatch = Stopwatch.StartNew();

            // This is the only phase allowed to touch Unity content.
            Issue63PreparedBatch prepared =
                CityNewOfflineBalanceContentAdapter.PrepareDefault(seeds);
            int effectiveParallelism = Math.Min(
                parallelism,
                Math.Max(1, prepared.Entries.Count));

            // Every worker receives a scenario made only from FPS.Simulation
            // immutable DTOs. It cannot reach ScriptableObject or AssetDatabase.
            var results = new OfflineRunResult[prepared.Entries.Count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = effectiveParallelism
            };
            Parallel.For(
                0,
                prepared.Entries.Count,
                options,
                index =>
                {
                    Issue63PreparedSeed entry = prepared.Entries[index];
                    results[index] = OfflineBalanceSimulator.Run(
                        entry.Scenario,
                        entry.Seed);
                });
            Array.Sort(results, (left, right) => left.Seed.CompareTo(right.Seed));

            Issue63BalanceReport report =
                Issue63BalanceReportWriter.Create(
                    prepared,
                    results,
                    effectiveParallelism);
            Issue63BalanceReportWriter.WriteJson(report, absoluteOutput);
            string markdownPath = Path.ChangeExtension(absoluteOutput, ".md");
            Issue63BalanceReportWriter.WriteMarkdown(
                report,
                markdownPath,
                stopwatch.Elapsed.TotalSeconds);
            int reproductionCount =
                Issue63BalanceReportWriter.WriteReproductions(
                    prepared,
                    results,
                    absoluteReproductions);
            stopwatch.Stop();

            return new Issue63BalanceRunResult(
                absoluteOutput,
                markdownPath,
                absoluteReproductions,
                reproductionCount,
                report);
        }

        private static long[] CreateSeeds(long seedStart, int seedCount)
        {
            var seeds = new long[seedCount];
            for (int index = 0; index < seedCount; index++)
                seeds[index] = checked(seedStart + index);
            return seeds;
        }

        private static string ResolvePath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Output path is required.", nameof(path));
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(projectRoot, path));
        }
    }

    public static class Issue63BalanceSimulationCli
    {
        public const long DefaultSeedStart = 630000L;
        public const int DefaultSeedCount = 1000;
        public const string DefaultOutput =
            "artifacts/balance/issue63/report.json";
        public const string DefaultReproductionDirectory =
            "artifacts/balance/issue63/reproductions";

        [MenuItem("FPS/Balance/Run Issue 63 Offline Gate (1000 Seeds)")]
        public static void RunFromMenu()
        {
            Issue63BalanceRunResult result =
                Issue63BalanceSimulationService.Run(
                    DefaultSeedStart,
                    DefaultSeedCount,
                    Math.Max(1, Environment.ProcessorCount - 1),
                    DefaultOutput,
                    DefaultReproductionDirectory);
            Debug.Log(Describe(result));
            EditorUtility.RevealInFinder(result.ReportPath);
        }

        /// <summary>
        /// Unity -executeMethod entry point. Recognized command line options:
        /// -issue63SeedStart, -issue63SeedCount, -issue63Parallelism,
        /// -issue63Output and -issue63ReproductionDirectory.
        /// Both "-key value" and "-key=value" forms are accepted.
        /// </summary>
        public static void Run()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            long seedStart = ReadLong(
                arguments,
                "-issue63SeedStart",
                DefaultSeedStart);
            int seedCount = ReadInt(
                arguments,
                "-issue63SeedCount",
                DefaultSeedCount);
            int parallelism = ReadInt(
                arguments,
                "-issue63Parallelism",
                Math.Max(1, Environment.ProcessorCount - 1));
            string output = ReadString(
                arguments,
                "-issue63Output",
                DefaultOutput);
            string reproductions = ReadString(
                arguments,
                "-issue63ReproductionDirectory",
                DefaultReproductionDirectory);

            Issue63BalanceRunResult result =
                Issue63BalanceSimulationService.Run(
                    seedStart,
                    seedCount,
                    parallelism,
                    output,
                    reproductions);
            Debug.Log(Describe(result));
        }

        private static string Describe(Issue63BalanceRunResult result)
        {
            return "Issue 63 offline balance simulation completed. " +
                   "Seeds=" + result.Report.Results.Count +
                   ", anomalies=" + result.ReproductionCount +
                   ", report='" + result.ReportPath +
                   "', summary='" + result.MarkdownPath + "'.";
        }

        private static int ReadInt(
            IReadOnlyList<string> arguments,
            string key,
            int fallback)
        {
            string raw = ReadString(arguments, key, null);
            if (raw == null) return fallback;
            if (!int.TryParse(raw, out int value) || value < 1)
                throw new ArgumentException(key + " must be a positive integer.");
            return value;
        }

        private static long ReadLong(
            IReadOnlyList<string> arguments,
            string key,
            long fallback)
        {
            string raw = ReadString(arguments, key, null);
            if (raw == null) return fallback;
            if (!long.TryParse(raw, out long value))
                throw new ArgumentException(key + " must be an integer.");
            return value;
        }

        private static string ReadString(
            IReadOnlyList<string> arguments,
            string key,
            string fallback)
        {
            for (int index = 0; index < arguments.Count; index++)
            {
                string argument = arguments[index];
                if (string.Equals(argument, key, StringComparison.Ordinal))
                {
                    if (index + 1 >= arguments.Count ||
                        arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException(key + " requires a value.");
                    }
                    return arguments[index + 1];
                }

                string prefix = key + "=";
                if (argument.StartsWith(prefix, StringComparison.Ordinal))
                    return argument.Substring(prefix.Length);
            }

            return fallback;
        }
    }
}
