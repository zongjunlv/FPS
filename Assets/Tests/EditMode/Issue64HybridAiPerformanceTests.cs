using System;
using System.Collections.Generic;
using FPS.Performance.HybridAi;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue64HybridAiPerformanceTests
    {
        [TearDown]
        public void TearDown()
        {
            Issue64BenchmarkAdapterRegistry.Clear();
        }

        [Test]
        public void StatisticsProduceAverageP95P99GcMemoryAndDecisionMetrics()
        {
            var accumulator = new Issue64PerformanceAccumulator();
            for (int index = 1; index <= 100; index++)
            {
                accumulator.Add(new Issue64FrameSample(
                    index,
                    Snapshot(
                        active: 100,
                        main: index * 0.5d,
                        gc: index,
                        memory: 1000 + index,
                        decisionMean: index * 0.1d,
                        decisionP95: index * 0.2d,
                        decisionP99: index * 0.3d)));
            }

            Issue64BenchmarkRunResult result = accumulator.Complete(
                HybridAiBenchmarkMode.GameObject,
                100,
                64,
                0);

            Assert.That(result.Metrics.AverageFrameMilliseconds,
                Is.EqualTo(50.5d));
            Assert.That(result.Metrics.P95FrameMilliseconds,
                Is.EqualTo(95d));
            Assert.That(result.Metrics.P99FrameMilliseconds,
                Is.EqualTo(99d));
            Assert.That(result.Metrics.P95MainThreadMilliseconds,
                Is.EqualTo(47.5d));
            Assert.That(result.Metrics.PeakGcBytesPerFrame,
                Is.EqualTo(100L));
            Assert.That(result.Metrics.PeakMemoryBytes,
                Is.EqualTo(1100L));
            Assert.That(result.Metrics.AverageDecisionLatencyMilliseconds,
                Is.EqualTo(5.05d).Within(0.0001d));
            Assert.That(result.Metrics.P95DecisionLatencyMilliseconds,
                Is.EqualTo(19d));
            Assert.That(result.Metrics.P99DecisionLatencyMilliseconds,
                Is.EqualTo(29.7d).Within(0.0001d));
        }

        [Test]
        public void SessionUsesIdenticalWarmupAndSampleBoundaries()
        {
            var adapter = new FakeAdapter(HybridAiBenchmarkMode.Ecs);
            using var session = new Issue64BenchmarkSession(
                adapter,
                enemyCount: 300,
                seed: 64003,
                runIndex: 2,
                warmupFrames: 2,
                sampleFrames: 3,
                fixedDeltaTime: 0.02f);

            session.Step(90d);
            session.Step(80d);
            session.Step(10d);
            session.Step(11d);
            session.Step(12d);

            Issue64BenchmarkRunResult result = session.Complete();

            Assert.That(adapter.ConfiguredEnemyCount, Is.EqualTo(300));
            Assert.That(adapter.ConfiguredSeed, Is.EqualTo(64003));
            Assert.That(adapter.StepCount, Is.EqualTo(5));
            Assert.That(result.SampleCount, Is.EqualTo(3));
            Assert.That(result.Metrics.AverageFrameMilliseconds,
                Is.EqualTo(11d));
        }

        [Test]
        public void ValidHundredThreeHundredFiveHundredPairsContinueEcsRoute()
        {
            Issue64GateDecision decision = Issue64PerformanceGate.Evaluate(
                PassingComparisons());

            Assert.That(decision.Outcome,
                Is.EqualTo(Issue64GateOutcome.Continue));
            Assert.That(decision.Reasons, Is.Empty);
        }

        [Test]
        public void MissingDensityStopsInsteadOfPublishingPartialConclusion()
        {
            List<Issue64DensityComparison> comparisons = PassingComparisons();
            comparisons.RemoveAt(1);

            Issue64GateDecision decision = Issue64PerformanceGate.Evaluate(
                comparisons);

            Assert.That(decision.Outcome,
                Is.EqualTo(Issue64GateOutcome.Stop));
            StringAssert.Contains("100、300、500", decision.Reasons[0]);
        }

        [Test]
        public void IntentMismatchGhostAndPerformanceRegressionAllStop()
        {
            List<Issue64DensityComparison> comparisons = PassingComparisons();
            Issue64BenchmarkRunResult go = Run(
                HybridAiBenchmarkMode.GameObject,
                500,
                frame: 20d,
                p99: 30d,
                main: 16d,
                gc: 100d,
                memory: 1000L,
                decisionP95: 8d,
                digest: "go-intents");
            Issue64BenchmarkRunResult ecs = Run(
                HybridAiBenchmarkMode.Ecs,
                500,
                frame: 22d,
                p99: 36d,
                main: 18d,
                gc: 130d,
                memory: 1300L,
                decisionP95: 8d,
                digest: "ecs-intents",
                ghosts: 1);
            comparisons[2] = new Issue64DensityComparison(500, go, ecs);

            Issue64GateDecision decision = Issue64PerformanceGate.Evaluate(
                comparisons);

            Assert.That(decision.Outcome,
                Is.EqualTo(Issue64GateOutcome.Stop));
            string allReasons = string.Join("\n", decision.Reasons);
            StringAssert.Contains("意图摘要不一致", allReasons);
            StringAssert.Contains("幽灵实体", allReasons);
            StringAssert.Contains("P99", allReasons);
            StringAssert.Contains("GC", allReasons);
            StringAssert.Contains("峰值内存", allReasons);
            StringAssert.Contains("平均帧时间降幅不足", allReasons);
        }

        [Test]
        public void ReportIsStableSortedAndRecordsFullEnvironment()
        {
            List<Issue64DensityComparison> comparisons = PassingComparisons();
            comparisons.Reverse();
            var environment = Environment();
            var report = new Issue64BenchmarkReport(
                environment,
                comparisons,
                Issue64PerformanceGate.Evaluate(comparisons));

            string first = Issue64PerformanceReportWriter.ToJson(report);
            string second = Issue64PerformanceReportWriter.ToJson(report);
            string markdown = Issue64PerformanceReportWriter.ToMarkdown(report);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.IndexOf("\"enemyCount\": 100",
                    StringComparison.Ordinal),
                Is.LessThan(first.IndexOf("\"enemyCount\": 300",
                    StringComparison.Ordinal)));
            StringAssert.Contains("\"unityVersion\": \"6000.0.60f1\"",
                first);
            StringAssert.Contains("\"entitiesVersion\": \"1.3.14\"",
                first);
            StringAssert.Contains("\"qualityLevel\": \"PC\"", first);
            StringAssert.Contains("\"p95DecisionLatencyMilliseconds\"",
                first);
            StringAssert.Contains("CONTINUE", markdown);
            StringAssert.Contains("100 | GO", markdown);
        }

        [Test]
        public void ComparisonKeyChangesWhenSamplingOrQualityChanges()
        {
            Issue64BenchmarkEnvironment baseline = Environment();
            var changed = new Issue64BenchmarkEnvironment(
                "issue64-v1", "6000.0.60f1", "1.3.14", "macOS",
                "Apple M2", "Apple M2", 16384, 0, "Ultra",
                1920, 1080, false, 0, -1, "Development", "CityNew",
                "git-abc", 300, 3600, 1d / 60d, 3);

            Assert.That(changed.ComparisonKey,
                Is.Not.EqualTo(baseline.ComparisonKey));
        }

        [Test]
        public void RegistryKeepsConcreteAdaptersOutOfReportAssembly()
        {
            Issue64BenchmarkAdapterRegistry.Register(
                HybridAiBenchmarkMode.GameObject,
                () => new FakeAdapter(HybridAiBenchmarkMode.GameObject));

            bool created = Issue64BenchmarkAdapterRegistry.TryCreate(
                HybridAiBenchmarkMode.GameObject,
                out IIssue64BenchmarkAdapter adapter);

            Assert.That(created, Is.True);
            Assert.That(adapter.Mode,
                Is.EqualTo(HybridAiBenchmarkMode.GameObject));
            adapter.Dispose();
        }

        [Test]
        public void SessionDigestIncludesEveryStepNotOnlyFinalSnapshot()
        {
            string first = RunDigest(new[] { "early-a", "same-final" });
            string second = RunDigest(new[] { "early-b", "same-final" });

            Assert.That(first, Is.Not.EqualTo(second));
        }

        private static string RunDigest(string[] digests)
        {
            using var session = new Issue64BenchmarkSession(
                new DigestSequenceAdapter(digests),
                1,
                64,
                0,
                0,
                digests.Length,
                0.02f);
            while (!session.IsComplete)
            {
                session.Step(1d);
            }

            return session.Complete().IntentDigest;
        }

        private static List<Issue64DensityComparison> PassingComparisons()
        {
            return new List<Issue64DensityComparison>
            {
                Pair(100, 10d, 10.2d, 8d, 8.2d, 4d, 4d),
                Pair(300, 20d, 16d, 18d, 14d, 8d, 5d),
                Pair(500, 30d, 22d, 27d, 20d, 12d, 8d)
            };
        }

        private static Issue64DensityComparison Pair(
            int count,
            double goFrame,
            double ecsFrame,
            double goMain,
            double ecsMain,
            double goDecision,
            double ecsDecision)
        {
            return new Issue64DensityComparison(
                count,
                Run(HybridAiBenchmarkMode.GameObject, count, goFrame,
                    goFrame * 1.4d, goMain, 100d, 1000L, goDecision,
                    "same-intents"),
                Run(HybridAiBenchmarkMode.Ecs, count, ecsFrame,
                    Math.Min(goFrame * 1.4d, ecsFrame * 1.6d), ecsMain,
                    90d, 1150L, ecsDecision, "same-intents"));
        }

        private static Issue64BenchmarkRunResult Run(
            HybridAiBenchmarkMode mode,
            int count,
            double frame,
            double p99,
            double main,
            double gc,
            long memory,
            double decisionP95,
            string digest,
            int ghosts = 0)
        {
            return new Issue64BenchmarkRunResult(
                mode, count, 64064, 0, 3600, count, ghosts, digest,
                new Issue64PerformanceMetrics(
                    frame, frame * 1.2d, p99,
                    main, main * 1.2d, main * 1.4d,
                    gc, (long)Math.Ceiling(gc * 2d),
                    memory * 0.9d, memory,
                    decisionP95 * 0.75d,
                    decisionP95,
                    decisionP95 * 1.2d));
        }

        private static Issue64AdapterMetricsSnapshot Snapshot(
            int active,
            double main,
            long gc,
            long memory,
            double decisionMean,
            double decisionP95,
            double decisionP99)
        {
            return new Issue64AdapterMetricsSnapshot(
                active, 0, decisionMean, decisionP95, decisionP99,
                main, gc, memory, "digest");
        }

        private static Issue64BenchmarkEnvironment Environment()
        {
            return new Issue64BenchmarkEnvironment(
                "issue64-v1", "6000.0.60f1", "1.3.14", "macOS",
                "Apple M2", "Apple M2", 16384, 0, "PC",
                1920, 1080, false, 0, -1, "Development", "CityNew",
                "git-abc", 300, 3600, 1d / 60d, 3);
        }

        private sealed class FakeAdapter : IIssue64BenchmarkAdapter
        {
            public FakeAdapter(HybridAiBenchmarkMode mode)
            {
                Mode = mode;
            }

            public HybridAiBenchmarkMode Mode { get; }
            public bool IsAvailable => true;
            public int ConfiguredEnemyCount { get; private set; }
            public int ConfiguredSeed { get; private set; }
            public int StepCount { get; private set; }

            public void Configure(int enemyCount, int seed)
            {
                ConfiguredEnemyCount = enemyCount;
                ConfiguredSeed = seed;
            }

            public void Step(float deltaTime)
            {
                StepCount++;
            }

            public Issue64AdapterMetricsSnapshot CaptureMetrics()
            {
                return Snapshot(
                    ConfiguredEnemyCount,
                    main: 1d,
                    gc: 0,
                    memory: 1024,
                    decisionMean: 0.2d,
                    decisionP95: 0.3d,
                    decisionP99: 0.4d);
            }

            public void Reset()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class DigestSequenceAdapter : IIssue64BenchmarkAdapter
        {
            private readonly string[] digests;
            private int index = -1;

            public DigestSequenceAdapter(string[] digests)
            {
                this.digests = digests;
            }

            public HybridAiBenchmarkMode Mode =>
                HybridAiBenchmarkMode.GameObject;
            public bool IsAvailable => true;

            public void Configure(int enemyCount, int seed)
            {
            }

            public void Step(float deltaTime)
            {
                index++;
            }

            public Issue64AdapterMetricsSnapshot CaptureMetrics()
            {
                return new Issue64AdapterMetricsSnapshot(
                    1, 0, 0.1d, 0.2d, 0.3d, 0.4d, 0L, 1L,
                    digests[index]);
            }

            public void Reset()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
