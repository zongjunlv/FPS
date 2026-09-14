using System;
using System.Globalization;
using System.Text;

namespace FPS.Networking.Diagnostics
{
    public static class NetworkDiagnosticsReportWriter
    {
        public const string SchemaVersion =
            "issue65-network-diagnostics-v1";

        public const string FixtureLimitation =
            "This deterministic in-process fixture creates no real sockets " +
            "or independent Host/Client player processes and does not measure " +
            "transport serialization, process scheduling, rendering, or " +
            "physics. It is not multi-process acceptance evidence.";

        public static string ToStableJson(NetworkDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var builder = new StringBuilder(8192);
            builder.Append("{\n");
            Property(builder, 1, "schemaVersion", SchemaVersion, true);
            Property(
                builder,
                1,
                "measurementScope",
                MeasurementScope(report),
                true);
            Property(builder, 1, "limitations", Limitations(report), true);
            builder.Append("  \"metadata\": {\n");
            NetworkDiagnosticRunMetadata metadata = report.Metadata;
            Property(builder, 2, "runId", metadata.RunId, true);
            Property(builder, 2, "implementationVersion",
                metadata.ImplementationVersion, true);
            Property(builder, 2, "unityVersion", metadata.UnityVersion, true);
            Property(builder, 2, "transportName", metadata.TransportName,
                true);
            Property(builder, 2, "evidenceKind", metadata.EvidenceKind.ToString(),
                true);
            Property(builder, 2, "topology", metadata.Topology, true);
            Number(builder, 2, "processCount", metadata.ProcessCount, true);
            Property(builder, 2, "hostPlatform", metadata.HostPlatform, true);
            Property(builder, 2, "clientPlatform", metadata.ClientPlatform,
                true);
            Property(builder, 2, "contentVersion", metadata.ContentVersion,
                true);
            Boolean(builder, 2, "isRealMultiProcess",
                metadata.IsRealMultiProcess, false);
            builder.Append("  },\n");
            builder.Append("  \"gate\": {\n");
            Property(builder, 2, "outcome", report.Gate.Outcome.ToString(), true);
            Boolean(builder, 2, "metricThresholdsPassed",
                report.Gate.MetricThresholdsPassed, true);
            Boolean(builder, 2, "acceptanceEligible",
                report.Gate.AcceptanceEligible, true);
            builder.Append("    \"reasons\": [");
            for (int index = 0; index < report.Gate.Reasons.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                Quoted(builder, report.Gate.Reasons[index]);
            }

            builder.Append("]\n  },\n");
            builder.Append("  \"scenarios\": [\n");
            for (int index = 0; index < report.Scenarios.Count; index++)
            {
                AppendScenario(builder, report.Scenarios[index]);
                builder.Append(index + 1 < report.Scenarios.Count
                    ? "    },\n"
                    : "    }\n");
            }

            builder.Append("  ]\n}\n");
            return builder.ToString();
        }

        public static string ToMarkdown(NetworkDiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var builder = new StringBuilder(8192);
            builder.AppendLine("# Issue 65 网络验收诊断报告");
            builder.AppendLine();
            builder.Append("- 门禁结论：**")
                .Append(report.Gate.Outcome)
                .AppendLine("**");
            builder.Append("- 可作为正式多进程验收证据：**")
                .Append(report.Gate.AcceptanceEligible ? "是" : "否")
                .AppendLine("**");
            builder.Append("- 证据类型：`")
                .Append(report.Metadata.EvidenceKind)
                .AppendLine("`");
            builder.Append("- 进程数：")
                .Append(report.Metadata.ProcessCount)
                .AppendLine();
            builder.AppendLine();
            builder.AppendLine("## 测量范围与限制");
            builder.AppendLine();
            if (!report.Metadata.IsRealMultiProcess)
            {
                builder.AppendLine(
                    "> **重要：这是确定性进程内诊断 Fixture。它不会创建真实 Socket，也没有独立 Host/Client 玩家进程；不包含传输序列化、进程调度、渲染或物理成本，不能冒充真实多进程验收证据。**");
            }
            else
            {
                builder.AppendLine(
                    "> 本报告来自真实多进程玩家构建；结论仅覆盖报告中记录的平台、内容版本、传输和网络条件。Fixture 结果不可替代本证据。");
            }

            builder.AppendLine();
            builder.Append("测量范围：`")
                .Append(MeasurementScope(report))
                .AppendLine("`");
            builder.AppendLine();
            builder.AppendLine("## 场景指标");
            builder.AppendLine();
            builder.AppendLine(
                "| 场景 | RTT/丢包 | 命中反馈 P95 | 校正 次/分钟 / P95 / 最大 | 上/下行 B/s | 状态分歧 次/比例/最大/持续 | 命令 接受/丢包/拒绝 |");
            builder.AppendLine(
                "|---|---:|---:|---:|---:|---:|---:|");
            for (int index = 0; index < report.Scenarios.Count; index++)
            {
                NetworkDiagnosticScenarioResult result =
                    report.Scenarios[index];
                builder.Append("| ").Append(result.Scenario.StableId)
                    .Append(" | ")
                    .Append(result.Scenario.RoundTripLatencyMilliseconds)
                    .Append("ms / ")
                    .Append(Format(result.Scenario.PacketLossBasisPoints / 100d))
                    .Append("% | ")
                    .Append(Format(result.P95HitFeedbackMilliseconds))
                    .Append("ms | ")
                    .Append(Format(result.CorrectionsPerMinute)).Append(" / ")
                    .Append(Format(result.P95CorrectionMagnitude)).Append("m / ")
                    .Append(Format(result.MaximumCorrectionMagnitude))
                    .Append("m | ")
                    .Append(Format(result.UplinkBytesPerSecond)).Append(" / ")
                    .Append(Format(result.DownlinkBytesPerSecond))
                    .Append(" | ")
                    .Append(result.StateDivergenceCount).Append(" / ")
                    .Append(Format(result.StateDivergenceRate * 100d))
                    .Append("% / ")
                    .Append(Format(result.MaximumStateDivergenceMagnitude))
                    .Append("m / ")
                    .Append(Format(
                        result.MaximumStateDivergenceDurationMilliseconds))
                    .Append("ms | ")
                    .Append(result.AcceptedCommandCount).Append(" / ")
                    .Append(result.DroppedCommandCount).Append(" / ")
                    .Append(result.RejectedCommandCount).AppendLine(" |");
            }

            builder.AppendLine();
            builder.AppendLine("## 命令拒绝原因");
            builder.AppendLine();
            builder.AppendLine("| 场景 | 原因 | 次数 |");
            builder.AppendLine("|---|---|---:|");
            bool hasRejection = false;
            for (int scenarioIndex = 0;
                 scenarioIndex < report.Scenarios.Count;
                 scenarioIndex++)
            {
                NetworkDiagnosticScenarioResult result =
                    report.Scenarios[scenarioIndex];
                for (int reasonIndex = 0;
                     reasonIndex < result.RejectionCounts.Count;
                     reasonIndex++)
                {
                    NetworkRejectionCount rejection =
                        result.RejectionCounts[reasonIndex];
                    builder.Append("| ").Append(result.Scenario.StableId)
                        .Append(" | ").Append(rejection.Reason)
                        .Append(" | ").Append(rejection.Count)
                        .AppendLine(" |");
                    hasRejection = true;
                }
            }

            if (!hasRejection)
            {
                builder.AppendLine("| - | - | 0 |");
            }

            builder.AppendLine();
            builder.AppendLine("## 门禁原因");
            builder.AppendLine();
            if (report.Gate.Reasons.Count == 0)
            {
                builder.AppendLine("- 无；全部硬门禁通过。");
            }
            else
            {
                for (int index = 0; index < report.Gate.Reasons.Count; index++)
                {
                    builder.Append("- `")
                        .Append(report.Gate.Reasons[index])
                        .AppendLine("`");
                }
            }

            return builder.ToString();
        }

        private static void AppendScenario(
            StringBuilder builder,
            NetworkDiagnosticScenarioResult result)
        {
            builder.Append("    {\n");
            Property(builder, 3, "stableId", result.Scenario.StableId, true);
            Number(builder, 3, "roundTripLatencyMilliseconds",
                result.Scenario.RoundTripLatencyMilliseconds, true);
            Number(builder, 3, "packetLossBasisPoints",
                result.Scenario.PacketLossBasisPoints, true);
            Number(builder, 3, "jitterMilliseconds",
                result.Scenario.JitterMilliseconds, true);
            Number(builder, 3, "simulationSeed",
                result.Scenario.SimulationSeed, true);
            Number(builder, 3, "durationSeconds", result.DurationSeconds, true);
            Number(builder, 3, "hitFeedbackSampleCount",
                result.HitFeedbackSampleCount, true);
            Number(builder, 3, "meanHitFeedbackMilliseconds",
                result.MeanHitFeedbackMilliseconds, true);
            Number(builder, 3, "p95HitFeedbackMilliseconds",
                result.P95HitFeedbackMilliseconds, true);
            Number(builder, 3, "p99HitFeedbackMilliseconds",
                result.P99HitFeedbackMilliseconds, true);
            Number(builder, 3, "correctionCount", result.CorrectionCount, true);
            Number(builder, 3, "correctionsPerMinute",
                result.CorrectionsPerMinute, true);
            Number(builder, 3, "meanCorrectionMagnitude",
                result.MeanCorrectionMagnitude, true);
            Number(builder, 3, "p95CorrectionMagnitude",
                result.P95CorrectionMagnitude, true);
            Number(builder, 3, "maximumCorrectionMagnitude",
                result.MaximumCorrectionMagnitude, true);
            Number(builder, 3, "uplinkBytes", result.UplinkBytes, true);
            Number(builder, 3, "downlinkBytes", result.DownlinkBytes, true);
            Number(builder, 3, "uplinkBytesPerSecond",
                result.UplinkBytesPerSecond, true);
            Number(builder, 3, "downlinkBytesPerSecond",
                result.DownlinkBytesPerSecond, true);
            Number(builder, 3, "stateComparisonCount",
                result.StateComparisonCount, true);
            Number(builder, 3, "stateDivergenceCount",
                result.StateDivergenceCount, true);
            Number(builder, 3, "stateDivergenceRate",
                result.StateDivergenceRate, true);
            Number(builder, 3, "maximumStateDivergenceMagnitude",
                result.MaximumStateDivergenceMagnitude, true);
            Number(builder, 3, "maximumStateDivergenceDurationMilliseconds",
                result.MaximumStateDivergenceDurationMilliseconds, true);
            Number(builder, 3, "sentCommandCount",
                result.SentCommandCount, true);
            Number(builder, 3, "acceptedCommandCount",
                result.AcceptedCommandCount, true);
            Number(builder, 3, "droppedCommandCount",
                result.DroppedCommandCount, true);
            Number(builder, 3, "rejectedCommandCount",
                result.RejectedCommandCount, true);
            builder.Append("      \"rejectionCounts\": [");
            for (int index = 0; index < result.RejectionCounts.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                NetworkRejectionCount rejection =
                    result.RejectionCounts[index];
                builder.Append("{\"reason\":");
                Quoted(builder, rejection.Reason.ToString());
                builder.Append(",\"count\":")
                    .Append(rejection.Count.ToString(
                        CultureInfo.InvariantCulture))
                    .Append('}');
            }

            builder.Append("]\n");
        }

        private static string MeasurementScope(NetworkDiagnosticReport report)
        {
            return report.Metadata.IsRealMultiProcess
                ? "real-multi-process-player-build-network-diagnostics"
                : Issue65NetworkDiagnosticFixture.MeasurementScope;
        }

        private static string Limitations(NetworkDiagnosticReport report)
        {
            return report.Metadata.IsRealMultiProcess
                ? "Evidence applies only to the recorded build, platforms, " +
                  "transport, content version, and network conditions."
                : FixtureLimitation;
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void Property(
            StringBuilder builder,
            int indent,
            string name,
            string value,
            bool comma)
        {
            builder.Append(' ', indent * 2);
            Quoted(builder, name);
            builder.Append(": ");
            Quoted(builder, value);
            builder.Append(comma ? ",\n" : "\n");
        }

        private static void Boolean(
            StringBuilder builder,
            int indent,
            string name,
            bool value,
            bool comma)
        {
            builder.Append(' ', indent * 2);
            Quoted(builder, name);
            builder.Append(": ").Append(value ? "true" : "false");
            builder.Append(comma ? ",\n" : "\n");
        }

        private static void Number(
            StringBuilder builder,
            int indent,
            string name,
            double value,
            bool comma)
        {
            builder.Append(' ', indent * 2);
            Quoted(builder, name);
            builder.Append(": ").Append(value.ToString(
                "0.######",
                CultureInfo.InvariantCulture));
            builder.Append(comma ? ",\n" : "\n");
        }

        private static void Quoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            string text = value ?? string.Empty;
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 32)
                        {
                            builder.Append("\\u")
                                .Append(((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
