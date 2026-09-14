using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FPS.Performance.HybridAi
{
    public static class Issue64PerformanceReportWriter
    {
        public const string SchemaVersion =
            "issue64-hybrid-ai-performance-v1";

        public static string ToJson(Issue64BenchmarkReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var text = new StringBuilder(8192);
            text.Append("{\n  \"schemaVersion\": ");
            AppendString(text, SchemaVersion);
            text.Append(",\n  \"environment\": ");
            AppendEnvironment(text, report.Environment, 2);
            text.Append(",\n  \"decision\": {\n    \"outcome\": ");
            AppendString(text, report.Decision.Outcome.ToString());
            text.Append(",\n    \"reasons\": [");
            for (int index = 0; index < report.Decision.Reasons.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(", ");
                }

                AppendString(text, report.Decision.Reasons[index]);
            }

            text.Append("\n    ]\n  },\n  \"comparisons\": [");
            for (int index = 0; index < report.Comparisons.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                text.Append("\n");
                AppendComparison(text, report.Comparisons[index], 4);
            }

            text.Append("\n  ]\n}\n");
            return text.ToString();
        }

        public static string ToMarkdown(Issue64BenchmarkReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            Issue64BenchmarkEnvironment environment = report.Environment;
            var text = new StringBuilder(4096);
            text.AppendLine("# Issue 64：Hybrid ECS 群体 AI A/B 压测");
            text.AppendLine();
            text.Append("结论：**")
                .Append(report.Decision.Outcome == Issue64GateOutcome.Continue
                    ? "CONTINUE（继续 ECS 路线）"
                    : "STOP（停止扩展并回退 GO）")
                .AppendLine("**");
            text.AppendLine();
            text.AppendLine("## 固定环境");
            text.AppendLine();
            text.Append("- Unity / Entities：")
                .Append(environment.UnityVersion).Append(" / ")
                .AppendLine(environment.EntitiesVersion);
            text.Append("- CPU / GPU：")
                .Append(environment.Processor).Append(" / ")
                .AppendLine(environment.GraphicsDevice);
            text.Append("- 内存：")
                .Append(environment.SystemMemoryMb).Append(" MB；显存 ")
                .Append(environment.GraphicsMemoryMb).AppendLine(" MB");
            text.Append("- 画质与分辨率：")
                .Append(environment.QualityLevel).Append("，")
                .Append(environment.Width).Append('×')
                .Append(environment.Height).Append(environment.Fullscreen
                    ? " 全屏"
                    : " 窗口")
                .Append("，VSync ").Append(environment.VSyncCount)
                .Append("，目标帧率 ")
                .AppendLine(environment.TargetFrameRate.ToString(
                    CultureInfo.InvariantCulture));
            text.Append("- 场景 / 内容版本：")
                .Append(environment.SceneName).Append(" / ")
                .AppendLine(environment.ContentVersion);
            text.Append("- 采样：预热 ")
                .Append(environment.WarmupFrames).Append(" 帧，采样 ")
                .Append(environment.SampleFrames).Append(" 帧，每档 ")
                .Append(environment.RunsPerCase).AppendLine(" 轮");
            text.Append("- 测量范围：")
                .AppendLine(environment.MeasurementScope);
            text.AppendLine();
            text.AppendLine("## 结果");
            text.AppendLine();
            text.AppendLine(
                "| 敌人 | 模式 | 平均帧 | P95 | P99 | 主线程均值 | " +
                "GC/帧 | 峰值内存 | 决策P95 |");
            text.AppendLine(
                "| ---: | --- | ---: | ---: | ---: | ---: | ---: | " +
                "---: | ---: |");
            foreach (Issue64DensityComparison comparison in report.Comparisons)
            {
                AppendMarkdownRun(text, comparison.GameObject);
                AppendMarkdownRun(text, comparison.Ecs);
            }

            text.AppendLine();
            text.AppendLine("## 量化门禁");
            text.AppendLine();
            if (report.Decision.Reasons.Count == 0)
            {
                text.AppendLine("- 全部硬门禁通过。");
            }
            else
            {
                foreach (string reason in report.Decision.Reasons)
                {
                    text.Append("- ").AppendLine(reason);
                }
            }

            return text.ToString();
        }

        private static void AppendComparison(
            StringBuilder text,
            Issue64DensityComparison comparison,
            int indent)
        {
            string pad = new string(' ', indent);
            text.Append(pad).Append("{\n")
                .Append(pad).Append("  \"enemyCount\": ")
                .Append(comparison.EnemyCount).Append(",\n")
                .Append(pad).Append("  \"intentDigestMatches\": ")
                .Append(comparison.IntentDigestMatches ? "true" : "false")
                .Append(",\n")
                .Append(pad).Append("  \"gameObject\": ");
            AppendRun(text, comparison.GameObject, indent + 2);
            text.Append(",\n").Append(pad).Append("  \"ecs\": ");
            AppendRun(text, comparison.Ecs, indent + 2);
            text.Append("\n").Append(pad).Append('}');
        }

        private static void AppendRun(
            StringBuilder text,
            Issue64BenchmarkRunResult run,
            int indent)
        {
            string pad = new string(' ', indent);
            text.Append("{\n")
                .Append(pad).Append("  \"mode\": ");
            AppendString(text, run.Mode.ToString());
            text.Append(",\n").Append(pad).Append("  \"enemyCount\": ")
                .Append(run.EnemyCount)
                .Append(",\n").Append(pad).Append("  \"seed\": ")
                .Append(run.Seed)
                .Append(",\n").Append(pad).Append("  \"runIndex\": ")
                .Append(run.RunIndex)
                .Append(",\n").Append(pad).Append("  \"sampleCount\": ")
                .Append(run.SampleCount)
                .Append(",\n").Append(pad).Append("  \"activeCount\": ")
                .Append(run.ActiveCount)
                .Append(",\n").Append(pad).Append("  \"ghostCount\": ")
                .Append(run.GhostCount)
                .Append(",\n").Append(pad).Append("  \"intentDigest\": ");
            AppendString(text, run.IntentDigest);
            text.Append(",\n").Append(pad).Append("  \"metrics\": ");
            AppendMetrics(text, run.Metrics, indent + 2);
            text.Append("\n").Append(pad).Append('}');
        }

        private static void AppendMetrics(
            StringBuilder text,
            Issue64PerformanceMetrics metrics,
            int indent)
        {
            string pad = new string(' ', indent);
            text.Append("{\n");
            AppendNumber(text, pad, "averageFrameMilliseconds",
                metrics.AverageFrameMilliseconds, true);
            AppendNumber(text, pad, "p95FrameMilliseconds",
                metrics.P95FrameMilliseconds, true);
            AppendNumber(text, pad, "p99FrameMilliseconds",
                metrics.P99FrameMilliseconds, true);
            AppendNumber(text, pad, "averageMainThreadMilliseconds",
                metrics.AverageMainThreadMilliseconds, true);
            AppendNumber(text, pad, "p95MainThreadMilliseconds",
                metrics.P95MainThreadMilliseconds, true);
            AppendNumber(text, pad, "p99MainThreadMilliseconds",
                metrics.P99MainThreadMilliseconds, true);
            AppendNumber(text, pad, "averageGcBytesPerFrame",
                metrics.AverageGcBytesPerFrame, true);
            AppendInteger(text, pad, "peakGcBytesPerFrame",
                metrics.PeakGcBytesPerFrame, true);
            AppendNumber(text, pad, "averageMemoryBytes",
                metrics.AverageMemoryBytes, true);
            AppendInteger(text, pad, "peakMemoryBytes",
                metrics.PeakMemoryBytes, true);
            AppendNumber(text, pad, "averageDecisionLatencyMilliseconds",
                metrics.AverageDecisionLatencyMilliseconds, true);
            AppendNumber(text, pad, "p95DecisionLatencyMilliseconds",
                metrics.P95DecisionLatencyMilliseconds, true);
            AppendNumber(text, pad, "p99DecisionLatencyMilliseconds",
                metrics.P99DecisionLatencyMilliseconds, false);
            text.Append(pad).Append('}');
        }

        private static void AppendEnvironment(
            StringBuilder text,
            Issue64BenchmarkEnvironment environment,
            int indent)
        {
            string pad = new string(' ', indent);
            text.Append("{\n");
            AppendText(text, pad, "benchmarkVersion",
                environment.BenchmarkVersion, true);
            AppendText(text, pad, "comparisonKey",
                environment.ComparisonKey, true);
            AppendText(text, pad, "unityVersion",
                environment.UnityVersion, true);
            AppendText(text, pad, "entitiesVersion",
                environment.EntitiesVersion, true);
            AppendText(text, pad, "operatingSystem",
                environment.OperatingSystem, true);
            AppendText(text, pad, "processor", environment.Processor, true);
            AppendText(text, pad, "graphicsDevice",
                environment.GraphicsDevice, true);
            AppendInteger(text, pad, "systemMemoryMb",
                environment.SystemMemoryMb, true);
            AppendInteger(text, pad, "graphicsMemoryMb",
                environment.GraphicsMemoryMb, true);
            AppendText(text, pad, "qualityLevel",
                environment.QualityLevel, true);
            AppendInteger(text, pad, "width", environment.Width, true);
            AppendInteger(text, pad, "height", environment.Height, true);
            AppendBoolean(text, pad, "fullscreen",
                environment.Fullscreen, true);
            AppendInteger(text, pad, "vSyncCount",
                environment.VSyncCount, true);
            AppendInteger(text, pad, "targetFrameRate",
                environment.TargetFrameRate, true);
            AppendText(text, pad, "buildType", environment.BuildType, true);
            AppendText(text, pad, "sceneName", environment.SceneName, true);
            AppendText(text, pad, "contentVersion",
                environment.ContentVersion, true);
            AppendInteger(text, pad, "warmupFrames",
                environment.WarmupFrames, true);
            AppendInteger(text, pad, "sampleFrames",
                environment.SampleFrames, true);
            AppendNumber(text, pad, "fixedDeltaTimeSeconds",
                environment.FixedDeltaTimeSeconds, true);
            AppendInteger(text, pad, "runsPerCase",
                environment.RunsPerCase, true);
            AppendText(text, pad, "measurementScope",
                environment.MeasurementScope, false);
            text.Append(pad).Append('}');
        }

        private static void AppendMarkdownRun(
            StringBuilder text,
            Issue64BenchmarkRunResult run)
        {
            Issue64PerformanceMetrics metric = run.Metrics;
            text.Append("| ").Append(run.EnemyCount).Append(" | ")
                .Append(run.Mode == HybridAiBenchmarkMode.GameObject
                    ? "GO"
                    : "ECS")
                .Append(" | ").Append(F(metric.AverageFrameMilliseconds))
                .Append(" ms | ").Append(F(metric.P95FrameMilliseconds))
                .Append(" ms | ").Append(F(metric.P99FrameMilliseconds))
                .Append(" ms | ")
                .Append(F(metric.AverageMainThreadMilliseconds))
                .Append(" ms | ").Append(F(metric.AverageGcBytesPerFrame))
                .Append(" B | ").Append(F(metric.PeakMemoryBytes / 1048576d))
                .Append(" MB | ")
                .Append(F(metric.P95DecisionLatencyMilliseconds))
                .AppendLine(" ms |");
        }

        private static void AppendText(
            StringBuilder text,
            string pad,
            string name,
            string value,
            bool comma)
        {
            text.Append(pad).Append("  \"").Append(name).Append("\": ");
            AppendString(text, value);
            text.Append(comma ? ",\n" : "\n");
        }

        private static void AppendNumber(
            StringBuilder text,
            string pad,
            string name,
            double value,
            bool comma)
        {
            text.Append(pad).Append("  \"").Append(name).Append("\": ")
                .Append(value.ToString("R", CultureInfo.InvariantCulture))
                .Append(comma ? ",\n" : "\n");
        }

        private static void AppendInteger(
            StringBuilder text,
            string pad,
            string name,
            long value,
            bool comma)
        {
            text.Append(pad).Append("  \"").Append(name).Append("\": ")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append(comma ? ",\n" : "\n");
        }

        private static void AppendBoolean(
            StringBuilder text,
            string pad,
            string name,
            bool value,
            bool comma)
        {
            text.Append(pad).Append("  \"").Append(name).Append("\": ")
                .Append(value ? "true" : "false")
                .Append(comma ? ",\n" : "\n");
        }

        private static void AppendString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 32)
                        {
                            text.Append("\\u")
                                .Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            text.Append(character);
                        }
                        break;
                }
            }
            text.Append('"');
        }

        private static string F(double value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
