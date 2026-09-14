using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Performance.HybridAi
{
    public sealed class Issue64PerformanceGateThresholds
    {
        public Issue64PerformanceGateThresholds(
            double maximumLowDensityRegressionRatio = 1.05d,
            double maximumP99RegressionRatio = 1.05d,
            double maximumGcRatio = 1.00d,
            double maximumMemoryRatio = 1.20d,
            double requiredHighDensityFrameRatio = 0.85d,
            double requiredHighDensityMainThreadRatio = 0.85d,
            double requiredHighDensityDecisionRatio = 0.75d)
        {
            MaximumLowDensityRegressionRatio =
                Positive(maximumLowDensityRegressionRatio);
            MaximumP99RegressionRatio = Positive(maximumP99RegressionRatio);
            MaximumGcRatio = Positive(maximumGcRatio);
            MaximumMemoryRatio = Positive(maximumMemoryRatio);
            RequiredHighDensityFrameRatio =
                Positive(requiredHighDensityFrameRatio);
            RequiredHighDensityMainThreadRatio =
                Positive(requiredHighDensityMainThreadRatio);
            RequiredHighDensityDecisionRatio =
                Positive(requiredHighDensityDecisionRatio);
        }

        public double MaximumLowDensityRegressionRatio { get; }
        public double MaximumP99RegressionRatio { get; }
        public double MaximumGcRatio { get; }
        public double MaximumMemoryRatio { get; }
        public double RequiredHighDensityFrameRatio { get; }
        public double RequiredHighDensityMainThreadRatio { get; }
        public double RequiredHighDensityDecisionRatio { get; }

        private static double Positive(double value)
        {
            return value > 0d
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public static class Issue64PerformanceGate
    {
        private static readonly int[] RequiredEnemyCounts = { 100, 300, 500 };

        public static Issue64GateDecision Evaluate(
            IEnumerable<Issue64DensityComparison> comparisons,
            Issue64PerformanceGateThresholds thresholds = null)
        {
            thresholds ??= new Issue64PerformanceGateThresholds();
            var ordered = (comparisons ??
                    throw new ArgumentNullException(nameof(comparisons)))
                .OrderBy(value => value.EnemyCount)
                .ToArray();
            var reasons = new List<string>();

            int[] actualCounts = ordered.Select(value => value.EnemyCount)
                .ToArray();
            if (!RequiredEnemyCounts.SequenceEqual(actualCounts))
            {
                reasons.Add(
                    "必须包含且仅包含 100、300、500 敌人的成对结果。");
                return new Issue64GateDecision(
                    Issue64GateOutcome.Stop,
                    reasons);
            }

            foreach (Issue64DensityComparison pair in ordered)
            {
                ValidatePair(pair, thresholds, reasons);
            }

            return new Issue64GateDecision(
                reasons.Count == 0
                    ? Issue64GateOutcome.Continue
                    : Issue64GateOutcome.Stop,
                reasons);
        }

        private static void ValidatePair(
            Issue64DensityComparison pair,
            Issue64PerformanceGateThresholds thresholds,
            ICollection<string> reasons)
        {
            Issue64BenchmarkRunResult go = pair.GameObject;
            Issue64BenchmarkRunResult ecs = pair.Ecs;
            int count = pair.EnemyCount;

            if (go.Mode != HybridAiBenchmarkMode.GameObject ||
                ecs.Mode != HybridAiBenchmarkMode.Ecs ||
                go.EnemyCount != count || ecs.EnemyCount != count ||
                go.Seed != ecs.Seed || go.RunIndex != ecs.RunIndex ||
                go.SampleCount != ecs.SampleCount)
            {
                reasons.Add($"{count} 敌人：GO/ECS 不是同条件成对样本。");
            }

            if (!pair.IntentDigestMatches)
            {
                reasons.Add($"{count} 敌人：GO/ECS 意图摘要不一致。");
            }

            if (go.GhostCount != 0 || ecs.GhostCount != 0)
            {
                reasons.Add($"{count} 敌人：生命周期出现幽灵实体。");
            }

            if (go.ActiveCount != count || ecs.ActiveCount != count)
            {
                reasons.Add($"{count} 敌人：活跃数量未达到配置值。");
            }

            double frameRatio = Ratio(
                ecs.Metrics.AverageFrameMilliseconds,
                go.Metrics.AverageFrameMilliseconds);
            double mainRatio = Ratio(
                ecs.Metrics.AverageMainThreadMilliseconds,
                go.Metrics.AverageMainThreadMilliseconds);
            double decisionRatio = Ratio(
                ecs.Metrics.P95DecisionLatencyMilliseconds,
                go.Metrics.P95DecisionLatencyMilliseconds);
            double p99Ratio = Ratio(
                ecs.Metrics.P99FrameMilliseconds,
                go.Metrics.P99FrameMilliseconds);
            double gcRatio = Ratio(
                ecs.Metrics.AverageGcBytesPerFrame,
                go.Metrics.AverageGcBytesPerFrame,
                zeroOverZeroValue: 1d);
            double memoryRatio = Ratio(
                ecs.Metrics.PeakMemoryBytes,
                go.Metrics.PeakMemoryBytes);

            if (p99Ratio > thresholds.MaximumP99RegressionRatio)
            {
                reasons.Add(
                    $"{count} 敌人：ECS P99 帧时间比 GO 高 " +
                    $"{PercentAboveOne(p99Ratio):F1}%。");
            }

            if (gcRatio > thresholds.MaximumGcRatio)
            {
                reasons.Add(
                    $"{count} 敌人：ECS 平均 GC/帧比 GO 高 " +
                    $"{PercentAboveOne(gcRatio):F1}%。");
            }

            if (memoryRatio > thresholds.MaximumMemoryRatio)
            {
                reasons.Add(
                    $"{count} 敌人：ECS 峰值内存比 GO 高 " +
                    $"{PercentAboveOne(memoryRatio):F1}%。");
            }

            if (count == 100)
            {
                if (frameRatio > thresholds.MaximumLowDensityRegressionRatio)
                {
                    reasons.Add("100 敌人：ECS 平均帧时间回退超过 5%。");
                }

                if (mainRatio > thresholds.MaximumLowDensityRegressionRatio)
                {
                    reasons.Add("100 敌人：ECS 主线程回退超过 5%。");
                }

                return;
            }

            if (frameRatio > thresholds.RequiredHighDensityFrameRatio)
            {
                reasons.Add(
                    $"{count} 敌人：ECS 平均帧时间降幅不足 15%。");
            }

            if (mainRatio > thresholds.RequiredHighDensityMainThreadRatio)
            {
                reasons.Add(
                    $"{count} 敌人：ECS 主线程降幅不足 15%。");
            }

            if (decisionRatio > thresholds.RequiredHighDensityDecisionRatio)
            {
                reasons.Add(
                    $"{count} 敌人：ECS 决策 P95 延迟降幅不足 25%。");
            }
        }

        private static double Ratio(
            double numerator,
            double denominator,
            double zeroOverZeroValue = double.PositiveInfinity)
        {
            if (denominator <= 0d)
            {
                return numerator <= 0d
                    ? zeroOverZeroValue
                    : double.PositiveInfinity;
            }

            return numerator / denominator;
        }

        private static double PercentAboveOne(double ratio)
        {
            return Math.Max(0d, (ratio - 1d) * 100d);
        }
    }
}
