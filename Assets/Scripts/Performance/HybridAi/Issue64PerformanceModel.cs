using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Performance.HybridAi
{
    public sealed class Issue64BenchmarkEnvironment
    {
        public Issue64BenchmarkEnvironment(
            string benchmarkVersion,
            string unityVersion,
            string entitiesVersion,
            string operatingSystem,
            string processor,
            string graphicsDevice,
            int systemMemoryMb,
            int graphicsMemoryMb,
            string qualityLevel,
            int width,
            int height,
            bool fullscreen,
            int vSyncCount,
            int targetFrameRate,
            string buildType,
            string sceneName,
            string contentVersion,
            int warmupFrames,
            int sampleFrames,
            double fixedDeltaTimeSeconds,
            int runsPerCase,
            string measurementScope = "adapter-step-wall-clock")
        {
            BenchmarkVersion = Required(benchmarkVersion, nameof(benchmarkVersion));
            UnityVersion = Required(unityVersion, nameof(unityVersion));
            EntitiesVersion = Required(entitiesVersion, nameof(entitiesVersion));
            OperatingSystem = Required(operatingSystem, nameof(operatingSystem));
            Processor = Required(processor, nameof(processor));
            GraphicsDevice = Required(graphicsDevice, nameof(graphicsDevice));
            SystemMemoryMb = Positive(systemMemoryMb, nameof(systemMemoryMb));
            GraphicsMemoryMb = Math.Max(0, graphicsMemoryMb);
            QualityLevel = Required(qualityLevel, nameof(qualityLevel));
            Width = Positive(width, nameof(width));
            Height = Positive(height, nameof(height));
            Fullscreen = fullscreen;
            VSyncCount = Math.Max(0, vSyncCount);
            TargetFrameRate = targetFrameRate;
            BuildType = Required(buildType, nameof(buildType));
            SceneName = Required(sceneName, nameof(sceneName));
            ContentVersion = Required(contentVersion, nameof(contentVersion));
            WarmupFrames = Math.Max(0, warmupFrames);
            SampleFrames = Positive(sampleFrames, nameof(sampleFrames));
            FixedDeltaTimeSeconds = fixedDeltaTimeSeconds > 0d
                ? fixedDeltaTimeSeconds
                : throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaTimeSeconds));
            RunsPerCase = Positive(runsPerCase, nameof(runsPerCase));
            MeasurementScope = Required(
                measurementScope,
                nameof(measurementScope));
        }

        public string BenchmarkVersion { get; }
        public string UnityVersion { get; }
        public string EntitiesVersion { get; }
        public string OperatingSystem { get; }
        public string Processor { get; }
        public string GraphicsDevice { get; }
        public int SystemMemoryMb { get; }
        public int GraphicsMemoryMb { get; }
        public string QualityLevel { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Fullscreen { get; }
        public int VSyncCount { get; }
        public int TargetFrameRate { get; }
        public string BuildType { get; }
        public string SceneName { get; }
        public string ContentVersion { get; }
        public int WarmupFrames { get; }
        public int SampleFrames { get; }
        public double FixedDeltaTimeSeconds { get; }
        public int RunsPerCase { get; }
        public string MeasurementScope { get; }

        public string ComparisonKey => string.Join(
            "|",
            BenchmarkVersion,
            UnityVersion,
            EntitiesVersion,
            OperatingSystem,
            Processor,
            GraphicsDevice,
            SystemMemoryMb,
            GraphicsMemoryMb,
            QualityLevel,
            Width,
            Height,
            Fullscreen,
            VSyncCount,
            TargetFrameRate,
            BuildType,
            SceneName,
            ContentVersion,
            WarmupFrames,
            SampleFrames,
            FixedDeltaTimeSeconds.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture),
            RunsPerCase,
            MeasurementScope);

        private static string Required(string value, string name)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("Value is required.", name);
        }

        private static int Positive(int value, string name)
        {
            return value > 0
                ? value
                : throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct Issue64FrameSample
    {
        public Issue64FrameSample(
            double frameMilliseconds,
            Issue64AdapterMetricsSnapshot adapterMetrics)
        {
            FrameMilliseconds = Math.Max(0d, frameMilliseconds);
            AdapterMetrics = adapterMetrics;
        }

        public double FrameMilliseconds { get; }
        public Issue64AdapterMetricsSnapshot AdapterMetrics { get; }
    }

    public sealed class Issue64PerformanceMetrics
    {
        public Issue64PerformanceMetrics(
            double averageFrameMilliseconds,
            double p95FrameMilliseconds,
            double p99FrameMilliseconds,
            double averageMainThreadMilliseconds,
            double p95MainThreadMilliseconds,
            double p99MainThreadMilliseconds,
            double averageGcBytesPerFrame,
            long peakGcBytesPerFrame,
            double averageMemoryBytes,
            long peakMemoryBytes,
            double averageDecisionLatencyMilliseconds,
            double p95DecisionLatencyMilliseconds,
            double p99DecisionLatencyMilliseconds)
        {
            AverageFrameMilliseconds = averageFrameMilliseconds;
            P95FrameMilliseconds = p95FrameMilliseconds;
            P99FrameMilliseconds = p99FrameMilliseconds;
            AverageMainThreadMilliseconds = averageMainThreadMilliseconds;
            P95MainThreadMilliseconds = p95MainThreadMilliseconds;
            P99MainThreadMilliseconds = p99MainThreadMilliseconds;
            AverageGcBytesPerFrame = averageGcBytesPerFrame;
            PeakGcBytesPerFrame = peakGcBytesPerFrame;
            AverageMemoryBytes = averageMemoryBytes;
            PeakMemoryBytes = peakMemoryBytes;
            AverageDecisionLatencyMilliseconds =
                averageDecisionLatencyMilliseconds;
            P95DecisionLatencyMilliseconds = p95DecisionLatencyMilliseconds;
            P99DecisionLatencyMilliseconds = p99DecisionLatencyMilliseconds;
        }

        public double AverageFrameMilliseconds { get; }
        public double P95FrameMilliseconds { get; }
        public double P99FrameMilliseconds { get; }
        public double AverageMainThreadMilliseconds { get; }
        public double P95MainThreadMilliseconds { get; }
        public double P99MainThreadMilliseconds { get; }
        public double AverageGcBytesPerFrame { get; }
        public long PeakGcBytesPerFrame { get; }
        public double AverageMemoryBytes { get; }
        public long PeakMemoryBytes { get; }
        public double AverageDecisionLatencyMilliseconds { get; }
        public double P95DecisionLatencyMilliseconds { get; }
        public double P99DecisionLatencyMilliseconds { get; }
    }

    public sealed class Issue64BenchmarkRunResult
    {
        public Issue64BenchmarkRunResult(
            HybridAiBenchmarkMode mode,
            int enemyCount,
            int seed,
            int runIndex,
            int sampleCount,
            int activeCount,
            int ghostCount,
            string intentDigest,
            Issue64PerformanceMetrics metrics)
        {
            Mode = mode;
            EnemyCount = enemyCount;
            Seed = seed;
            RunIndex = runIndex;
            SampleCount = sampleCount;
            ActiveCount = activeCount;
            GhostCount = ghostCount;
            IntentDigest = intentDigest ?? string.Empty;
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        public HybridAiBenchmarkMode Mode { get; }
        public int EnemyCount { get; }
        public int Seed { get; }
        public int RunIndex { get; }
        public int SampleCount { get; }
        public int ActiveCount { get; }
        public int GhostCount { get; }
        public string IntentDigest { get; }
        public Issue64PerformanceMetrics Metrics { get; }
    }

    public sealed class Issue64DensityComparison
    {
        public Issue64DensityComparison(
            int enemyCount,
            Issue64BenchmarkRunResult gameObject,
            Issue64BenchmarkRunResult ecs)
        {
            EnemyCount = enemyCount;
            GameObject = gameObject ??
                throw new ArgumentNullException(nameof(gameObject));
            Ecs = ecs ?? throw new ArgumentNullException(nameof(ecs));
        }

        public int EnemyCount { get; }
        public Issue64BenchmarkRunResult GameObject { get; }
        public Issue64BenchmarkRunResult Ecs { get; }
        public bool IntentDigestMatches => string.Equals(
            GameObject.IntentDigest,
            Ecs.IntentDigest,
            StringComparison.Ordinal);
    }

    public enum Issue64GateOutcome
    {
        Continue = 0,
        Stop = 1
    }

    public sealed class Issue64GateDecision
    {
        public Issue64GateDecision(
            Issue64GateOutcome outcome,
            IEnumerable<string> reasons)
        {
            Outcome = outcome;
            Reasons = Array.AsReadOnly(
                (reasons ?? Array.Empty<string>()).ToArray());
        }

        public Issue64GateOutcome Outcome { get; }
        public IReadOnlyList<string> Reasons { get; }
    }

    public sealed class Issue64BenchmarkReport
    {
        public Issue64BenchmarkReport(
            Issue64BenchmarkEnvironment environment,
            IEnumerable<Issue64DensityComparison> comparisons,
            Issue64GateDecision decision)
        {
            Environment = environment ??
                throw new ArgumentNullException(nameof(environment));
            Comparisons = Array.AsReadOnly(
                (comparisons ?? throw new ArgumentNullException(
                    nameof(comparisons)))
                .OrderBy(value => value.EnemyCount)
                .ToArray());
            Decision = decision ??
                throw new ArgumentNullException(nameof(decision));
        }

        public Issue64BenchmarkEnvironment Environment { get; }
        public IReadOnlyList<Issue64DensityComparison> Comparisons { get; }
        public Issue64GateDecision Decision { get; }
    }
}
