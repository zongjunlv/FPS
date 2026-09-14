using System;
using System.Diagnostics;

namespace FPS.Performance.HybridAi
{
    /// <summary>
    /// Frame-driven benchmark session. A MonoBehaviour or an automated player
    /// runner supplies the observed frame time; this class owns warm-up and
    /// sample boundaries so both implementations use identical conditions.
    /// </summary>
    public sealed class Issue64BenchmarkSession : IDisposable
    {
        private readonly IIssue64BenchmarkAdapter adapter;
        private readonly Issue64PerformanceAccumulator accumulator = new();
        private readonly int enemyCount;
        private readonly int seed;
        private readonly int runIndex;
        private readonly int warmupFrames;
        private readonly int sampleFrames;
        private readonly float fixedDeltaTime;
        private readonly StableIntentDigestAccumulator intentDigest = new();
        private int frames;
        private bool disposed;

        public Issue64BenchmarkSession(
            IIssue64BenchmarkAdapter adapter,
            int enemyCount,
            int seed,
            int runIndex,
            int warmupFrames,
            int sampleFrames,
            float fixedDeltaTime)
        {
            this.adapter = adapter ??
                throw new ArgumentNullException(nameof(adapter));
            this.enemyCount = enemyCount > 0
                ? enemyCount
                : throw new ArgumentOutOfRangeException(nameof(enemyCount));
            this.seed = seed;
            this.runIndex = runIndex >= 0
                ? runIndex
                : throw new ArgumentOutOfRangeException(nameof(runIndex));
            this.warmupFrames = Math.Max(0, warmupFrames);
            this.sampleFrames = sampleFrames > 0
                ? sampleFrames
                : throw new ArgumentOutOfRangeException(nameof(sampleFrames));
            this.fixedDeltaTime = fixedDeltaTime > 0f
                ? fixedDeltaTime
                : throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));

            if (!adapter.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"{adapter.Mode} benchmark adapter is unavailable.");
            }

            adapter.Configure(enemyCount, seed);
        }

        public bool IsComplete => accumulator.Count >= sampleFrames;
        public int WarmupFramesRemaining => Math.Max(0, warmupFrames - frames);
        public int SampleFramesCollected => accumulator.Count;

        public void Step(double observedFrameMilliseconds)
        {
            ThrowIfDisposed();

            if (IsComplete)
            {
                throw new InvalidOperationException(
                    "Benchmark session is already complete.");
            }

            adapter.Step(fixedDeltaTime);
            frames++;
            Issue64AdapterMetricsSnapshot metrics = adapter.CaptureMetrics();
            intentDigest.Add(metrics.IntentDigest);

            if (frames <= warmupFrames)
            {
                return;
            }

            accumulator.Add(new Issue64FrameSample(
                observedFrameMilliseconds,
                metrics));
        }

        /// <summary>
        /// Measures only the shared AI adapter step using a monotonic clock.
        /// Rendering, presentation and Editor repaint are deliberately outside
        /// this number and the report environment must state that scope.
        /// </summary>
        public void Step()
        {
            ThrowIfDisposed();

            if (IsComplete)
            {
                throw new InvalidOperationException(
                    "Benchmark session is already complete.");
            }

            long startedAt = Stopwatch.GetTimestamp();
            adapter.Step(fixedDeltaTime);
            long endedAt = Stopwatch.GetTimestamp();
            frames++;
            Issue64AdapterMetricsSnapshot metrics = adapter.CaptureMetrics();
            intentDigest.Add(metrics.IntentDigest);

            if (frames <= warmupFrames)
            {
                return;
            }

            double milliseconds = (endedAt - startedAt) * 1000d /
                Stopwatch.Frequency;
            accumulator.Add(new Issue64FrameSample(
                milliseconds,
                metrics));
        }

        public Issue64BenchmarkRunResult Complete()
        {
            ThrowIfDisposed();

            if (!IsComplete)
            {
                throw new InvalidOperationException(
                    $"Expected {sampleFrames} sample frames, got " +
                    $"{accumulator.Count}.");
            }

            return accumulator.Complete(
                adapter.Mode,
                enemyCount,
                seed,
                runIndex,
                intentDigest.Value);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            adapter.Reset();
            adapter.Dispose();
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(Issue64BenchmarkSession));
            }
        }

        private sealed class StableIntentDigestAccumulator
        {
            private const ulong Offset = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;
            private ulong hash = Offset;
            private int count;

            public string Value =>
                $"fnv1a64:{hash:x16}:{count}";

            public void Add(string value)
            {
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= (byte)(character & 0xff);
                    hash *= Prime;
                    hash ^= (byte)(character >> 8);
                    hash *= Prime;
                }

                hash ^= 0xff;
                hash *= Prime;
                count++;
            }
        }
    }
}
