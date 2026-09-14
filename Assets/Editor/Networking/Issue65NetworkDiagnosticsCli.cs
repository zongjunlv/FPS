using System;
using System.IO;
using FPS.Networking.Diagnostics;
using UnityEditor;
using UnityEngine;

public static class Issue65NetworkDiagnosticsCli
{
    private const string DefaultOutputDirectory =
        "artifacts/networking/issue65";

    [MenuItem("Tools/FPS/Networking/Issue 65/运行诊断 Fixture")]
    public static void RunFixtureFromMenu()
    {
        RunFixture(Environment.GetCommandLineArgs(), false);
    }

    /// <summary>
    /// Unity -executeMethod entry point. This runs only the deterministic,
    /// in-process diagnostics fixture, never a real Host/Client acceptance run.
    /// Options: -issue65-output &lt;directory&gt;,
    /// -issue65-content-version &lt;version&gt;,
    /// -issue65-frames &lt;count&gt;.
    /// </summary>
    public static void RunFixtureFromCommandLine()
    {
        RunFixture(Environment.GetCommandLineArgs(), true);
    }

    private static void RunFixture(string[] arguments, bool commandLine)
    {
        try
        {
            string outputDirectory = Argument(
                arguments,
                "-issue65-output",
                DefaultOutputDirectory);
            string contentVersion = Argument(
                arguments,
                "-issue65-content-version",
                Application.version);
            int frames = IntegerArgument(
                arguments,
                "-issue65-frames",
                600);
            var metadata = new NetworkDiagnosticRunMetadata(
                "fixture-" + DateTime.UtcNow.ToString(
                    "yyyyMMddTHHmmssZ"),
                NetworkDiagnosticsReportWriter.SchemaVersion,
                Application.unityVersion,
                "none-fixture",
                NetworkDiagnosticEvidenceKind.DeterministicFixture,
                "single-process-in-memory",
                1,
                Application.platform.ToString(),
                Application.platform.ToString(),
                string.IsNullOrWhiteSpace(contentVersion)
                    ? "unknown"
                    : contentVersion);
            NetworkDiagnosticReport report =
                Issue65NetworkDiagnosticFixture.Run(metadata, frames);
            WriteReport(report, outputDirectory);

            Debug.Log(
                $"[Issue65NetworkDiagnostics] {report.Gate.Outcome}; " +
                $"fixture only, acceptanceEligible=" +
                $"{report.Gate.AcceptanceEligible}; " +
                $"output={Path.GetFullPath(outputDirectory)}");

            if (commandLine &&
                report.Gate.Outcome == NetworkDiagnosticsGateOutcome.Fail)
            {
                EditorApplication.Exit(1);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (commandLine)
            {
                EditorApplication.Exit(1);
                return;
            }

            throw;
        }
    }

    public static void WriteReport(
        NetworkDiagnosticReport report,
        string outputDirectory)
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

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "fixture-report.json"),
            NetworkDiagnosticsReportWriter.ToStableJson(report));
        File.WriteAllText(
            Path.Combine(outputDirectory, "fixture-report.md"),
            NetworkDiagnosticsReportWriter.ToMarkdown(report));
    }

    private static string Argument(
        string[] arguments,
        string key,
        string fallback)
    {
        for (int index = 0; index + 1 < arguments.Length; index++)
        {
            if (string.Equals(
                    arguments[index],
                    key,
                    StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return fallback;
    }

    private static int IntegerArgument(
        string[] arguments,
        string key,
        int fallback)
    {
        string raw = Argument(arguments, key, null);
        return int.TryParse(raw, out int value) ? value : fallback;
    }
}
