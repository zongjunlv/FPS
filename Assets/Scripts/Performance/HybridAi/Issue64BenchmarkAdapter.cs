using System;
using System.Collections.Generic;

namespace FPS.Performance.HybridAi
{
    public enum HybridAiBenchmarkMode
    {
        GameObject = 0,
        Ecs = 1
    }

    /// <summary>
    /// Narrow boundary used by the Issue 64 benchmark. Concrete GO and ECS
    /// implementations can live in their owning assemblies without coupling
    /// the metrics/reporting code to either implementation.
    /// </summary>
    public interface IIssue64BenchmarkAdapter : IDisposable
    {
        HybridAiBenchmarkMode Mode { get; }
        bool IsAvailable { get; }

        void Configure(int enemyCount, int seed);
        void Step(float deltaTime);
        Issue64AdapterMetricsSnapshot CaptureMetrics();
        void Reset();
    }

    public readonly struct Issue64AdapterMetricsSnapshot
    {
        public Issue64AdapterMetricsSnapshot(
            int activeCount,
            int ghostCount,
            double meanDecisionLatencyMilliseconds,
            double p95DecisionLatencyMilliseconds,
            double p99DecisionLatencyMilliseconds,
            double mainThreadMilliseconds,
            long gcBytes,
            long memoryBytes,
            string intentDigest)
        {
            ActiveCount = Math.Max(0, activeCount);
            GhostCount = Math.Max(0, ghostCount);
            MeanDecisionLatencyMilliseconds =
                Math.Max(0d, meanDecisionLatencyMilliseconds);
            P95DecisionLatencyMilliseconds =
                Math.Max(0d, p95DecisionLatencyMilliseconds);
            P99DecisionLatencyMilliseconds =
                Math.Max(0d, p99DecisionLatencyMilliseconds);
            MainThreadMilliseconds = Math.Max(0d, mainThreadMilliseconds);
            GcBytes = Math.Max(0L, gcBytes);
            MemoryBytes = Math.Max(0L, memoryBytes);
            IntentDigest = intentDigest ?? string.Empty;
        }

        public int ActiveCount { get; }
        public int GhostCount { get; }
        public double MeanDecisionLatencyMilliseconds { get; }
        public double P95DecisionLatencyMilliseconds { get; }
        public double P99DecisionLatencyMilliseconds { get; }
        public double MainThreadMilliseconds { get; }
        public long GcBytes { get; }
        public long MemoryBytes { get; }
        /// <summary>
        /// Stable digest of this step's complete, stable-id-ordered intent
        /// stream. The benchmark session folds every warm-up and sample step
        /// into the final run digest so transient mismatches cannot disappear.
        /// </summary>
        public string IntentDigest { get; }
    }

    public static class Issue64BenchmarkAdapterRegistry
    {
        private static readonly Dictionary<HybridAiBenchmarkMode,
            Func<IIssue64BenchmarkAdapter>> Factories = new();

        public static void Register(
            HybridAiBenchmarkMode mode,
            Func<IIssue64BenchmarkAdapter> factory)
        {
            Factories[mode] = factory ??
                throw new ArgumentNullException(nameof(factory));
        }

        public static void Unregister(HybridAiBenchmarkMode mode)
        {
            Factories.Remove(mode);
        }

        public static bool TryCreate(
            HybridAiBenchmarkMode mode,
            out IIssue64BenchmarkAdapter adapter)
        {
            adapter = null;

            if (!Factories.TryGetValue(mode, out var factory))
            {
                return false;
            }

            adapter = factory();
            return adapter != null;
        }

        public static void Clear()
        {
            Factories.Clear();
        }
    }
}
