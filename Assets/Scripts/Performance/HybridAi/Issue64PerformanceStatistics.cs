using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Performance.HybridAi
{
    public sealed class Issue64PerformanceAccumulator
    {
        private readonly List<Issue64FrameSample> samples = new();

        public int Count => samples.Count;

        public void Add(Issue64FrameSample sample)
        {
            samples.Add(sample);
        }

        public void Clear()
        {
            samples.Clear();
        }

        public Issue64BenchmarkRunResult Complete(
            HybridAiBenchmarkMode mode,
            int enemyCount,
            int seed,
            int runIndex,
            string intentDigestOverride = null)
        {
            if (samples.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one sample is required.");
            }

            var last = samples[samples.Count - 1].AdapterMetrics;
            var frame = samples.Select(value => value.FrameMilliseconds);
            var main = samples.Select(
                value => value.AdapterMetrics.MainThreadMilliseconds);
            var gc = samples.Select(
                value => (double)value.AdapterMetrics.GcBytes);
            var memory = samples.Select(
                value => (double)value.AdapterMetrics.MemoryBytes);
            var decisionMean = samples.Select(
                value => value.AdapterMetrics
                    .MeanDecisionLatencyMilliseconds);
            var decisionP95 = samples.Select(
                value => value.AdapterMetrics
                    .P95DecisionLatencyMilliseconds);
            var decisionP99 = samples.Select(
                value => value.AdapterMetrics
                    .P99DecisionLatencyMilliseconds);

            return new Issue64BenchmarkRunResult(
                mode,
                enemyCount,
                seed,
                runIndex,
                samples.Count,
                last.ActiveCount,
                samples.Max(value => value.AdapterMetrics.GhostCount),
                intentDigestOverride ?? last.IntentDigest,
                new Issue64PerformanceMetrics(
                    Average(frame),
                    Percentile(frame, 0.95d),
                    Percentile(frame, 0.99d),
                    Average(main),
                    Percentile(main, 0.95d),
                    Percentile(main, 0.99d),
                    Average(gc),
                    (long)Math.Ceiling(gc.Max()),
                    Average(memory),
                    (long)Math.Ceiling(memory.Max()),
                    Average(decisionMean),
                    Percentile(decisionP95, 0.95d),
                    Percentile(decisionP99, 0.99d)));
        }

        public static double Average(IEnumerable<double> source)
        {
            double[] values = source?.ToArray() ?? Array.Empty<double>();
            return values.Length == 0 ? 0d : values.Average();
        }

        /// <summary>Nearest-rank percentile, stable for any input order.</summary>
        public static double Percentile(
            IEnumerable<double> source,
            double percentile)
        {
            if (percentile < 0d || percentile > 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(percentile));
            }

            double[] values = source?.OrderBy(value => value).ToArray() ??
                Array.Empty<double>();

            if (values.Length == 0)
            {
                return 0d;
            }

            int rank = Math.Max(
                0,
                (int)Math.Ceiling(percentile * values.Length) - 1);
            return values[Math.Min(rank, values.Length - 1)];
        }
    }
}
