using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Diagnostics
{
    public enum NetworkDiagnosticsGateOutcome
    {
        Pass = 0,
        Fail = 1,
        FixtureOnly = 2
    }

    public sealed class NetworkDiagnosticsGateDecision
    {
        public NetworkDiagnosticsGateDecision(
            NetworkDiagnosticsGateOutcome outcome,
            IEnumerable<string> reasons,
            bool metricThresholdsPassed,
            bool acceptanceEligible)
        {
            Outcome = outcome;
            Reasons = Array.AsReadOnly((reasons ?? Array.Empty<string>())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
            MetricThresholdsPassed = metricThresholdsPassed;
            AcceptanceEligible = acceptanceEligible;
        }

        public NetworkDiagnosticsGateOutcome Outcome { get; }
        public IReadOnlyList<string> Reasons { get; }
        public bool MetricThresholdsPassed { get; }
        public bool AcceptanceEligible { get; }
    }

    public sealed class NetworkDiagnosticsGateThresholds
    {
        public NetworkDiagnosticsGateThresholds(
            int minimumHitFeedbackSamples = 20,
            double hitFeedbackP95BudgetAboveRttMilliseconds = 100d,
            double maximumCorrectionsPerMinute = 60d,
            double maximumCorrectionP95Magnitude = 0.75d,
            double maximumCorrectionMagnitude = 2d,
            double maximumStateDivergenceRate = 0.02d,
            double maximumStateDivergenceMagnitude = 1.25d,
            double maximumStateDivergenceDurationMilliseconds = 500d,
            double maximumUplinkBytesPerSecond = 65536d,
            double maximumDownlinkBytesPerSecond = 131072d)
        {
            MinimumHitFeedbackSamples = Math.Max(1,
                minimumHitFeedbackSamples);
            HitFeedbackP95BudgetAboveRttMilliseconds = Positive(
                hitFeedbackP95BudgetAboveRttMilliseconds);
            MaximumCorrectionsPerMinute = Positive(
                maximumCorrectionsPerMinute);
            MaximumCorrectionP95Magnitude = Positive(
                maximumCorrectionP95Magnitude);
            MaximumCorrectionMagnitude = Positive(maximumCorrectionMagnitude);
            MaximumStateDivergenceRate = Positive(
                maximumStateDivergenceRate);
            MaximumStateDivergenceMagnitude = Positive(
                maximumStateDivergenceMagnitude);
            MaximumStateDivergenceDurationMilliseconds = Positive(
                maximumStateDivergenceDurationMilliseconds);
            MaximumUplinkBytesPerSecond = Positive(
                maximumUplinkBytesPerSecond);
            MaximumDownlinkBytesPerSecond = Positive(
                maximumDownlinkBytesPerSecond);
        }

        public int MinimumHitFeedbackSamples { get; }
        public double HitFeedbackP95BudgetAboveRttMilliseconds { get; }
        public double MaximumCorrectionsPerMinute { get; }
        public double MaximumCorrectionP95Magnitude { get; }
        public double MaximumCorrectionMagnitude { get; }
        public double MaximumStateDivergenceRate { get; }
        public double MaximumStateDivergenceMagnitude { get; }
        public double MaximumStateDivergenceDurationMilliseconds { get; }
        public double MaximumUplinkBytesPerSecond { get; }
        public double MaximumDownlinkBytesPerSecond { get; }

        private static double Positive(double value)
        {
            return value > 0d
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public static class NetworkDiagnosticsGate
    {
        private static readonly NetworkCommandRejectionReason[]
            RequiredRejectionCoverage =
            {
                NetworkCommandRejectionReason.TooOld,
                NetworkCommandRejectionReason.Duplicate,
                NetworkCommandRejectionReason.IllegalState
            };

        public static NetworkDiagnosticsGateDecision Evaluate(
            NetworkDiagnosticRunMetadata metadata,
            IEnumerable<NetworkDiagnosticScenarioResult> scenarioResults,
            NetworkDiagnosticsGateThresholds thresholds = null)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            thresholds ??= new NetworkDiagnosticsGateThresholds();
            NetworkDiagnosticScenarioResult[] results = (scenarioResults ??
                    throw new ArgumentNullException(nameof(scenarioResults)))
                .OrderBy(value => value.Scenario.StableId,
                    StringComparer.Ordinal)
                .ToArray();
            var failures = new List<string>();
            ValidateMatrix(results, failures);

            for (int index = 0; index < results.Length; index++)
            {
                ValidateScenario(results[index], thresholds, failures);
            }

            ValidateRejectionCoverage(results, failures);

            if (metadata.EvidenceKind ==
                    NetworkDiagnosticEvidenceKind.MultiProcessPlayer &&
                metadata.ProcessCount < 2)
            {
                failures.Add("evidence:multi-process-requires-two-processes");
            }

            bool metricPass = failures.Count == 0;
            bool acceptanceEligible = metricPass && metadata.IsRealMultiProcess;

            if (!metricPass)
            {
                return new NetworkDiagnosticsGateDecision(
                    NetworkDiagnosticsGateOutcome.Fail,
                    failures,
                    false,
                    false);
            }

            if (!metadata.IsRealMultiProcess)
            {
                failures.Add(
                    "evidence:fixture-is-not-real-multi-process-proof");
                return new NetworkDiagnosticsGateDecision(
                    NetworkDiagnosticsGateOutcome.FixtureOnly,
                    failures,
                    true,
                    false);
            }

            return new NetworkDiagnosticsGateDecision(
                NetworkDiagnosticsGateOutcome.Pass,
                Array.Empty<string>(),
                true,
                acceptanceEligible);
        }

        private static void ValidateMatrix(
            IReadOnlyList<NetworkDiagnosticScenarioResult> results,
            ICollection<string> failures)
        {
            string[] expected = Issue65NetworkConditionMatrix.Required
                .Select(value => value.StableId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] actual = results.Select(value => value.Scenario.StableId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                failures.Add("matrix:requires-exact-0-80-150-and-loss-cases");
                return;
            }

            for (int index = 0; index < results.Count; index++)
            {
                NetworkConditionScenario expectedScenario =
                    Issue65NetworkConditionMatrix.Find(
                        results[index].Scenario.StableId);
                NetworkConditionScenario actualScenario =
                    results[index].Scenario;

                if (expectedScenario == null ||
                    expectedScenario.RoundTripLatencyMilliseconds !=
                    actualScenario.RoundTripLatencyMilliseconds ||
                    expectedScenario.PacketLossBasisPoints !=
                    actualScenario.PacketLossBasisPoints ||
                    expectedScenario.JitterMilliseconds !=
                    actualScenario.JitterMilliseconds)
                {
                    failures.Add(
                        $"{actualScenario.StableId}:condition-mismatch");
                }
            }
        }

        private static void ValidateScenario(
            NetworkDiagnosticScenarioResult result,
            NetworkDiagnosticsGateThresholds thresholds,
            ICollection<string> failures)
        {
            string prefix = result.Scenario.StableId + ":";

            if (result.DurationSeconds <= 0d)
            {
                failures.Add(prefix + "empty-duration");
            }

            if (result.HitFeedbackSampleCount <
                thresholds.MinimumHitFeedbackSamples)
            {
                failures.Add(prefix + "insufficient-hit-feedback-samples");
            }

            double hitBudget = result.Scenario
                .RoundTripLatencyMilliseconds +
                thresholds.HitFeedbackP95BudgetAboveRttMilliseconds;
            if (result.Scenario.PacketLossBasisPoints > 0)
            {
                // Reliable gameplay input may need one RTT to retransmit the
                // packet represented by the P95 sample under loss.
                hitBudget += result.Scenario.RoundTripLatencyMilliseconds;
            }
            if (result.P95HitFeedbackMilliseconds > hitBudget)
            {
                failures.Add(prefix + "hit-feedback-p95-over-budget");
            }

            if (result.CorrectionsPerMinute >
                thresholds.MaximumCorrectionsPerMinute)
            {
                failures.Add(prefix + "correction-rate-over-budget");
            }

            if (result.CorrectionCount >= 20 &&
                result.P95CorrectionMagnitude >
                thresholds.MaximumCorrectionP95Magnitude + 0.001d)
            {
                failures.Add(prefix + "correction-p95-over-budget");
            }

            if (result.MaximumCorrectionMagnitude >
                thresholds.MaximumCorrectionMagnitude)
            {
                failures.Add(prefix + "correction-maximum-over-budget");
            }

            if (result.StateComparisonCount <= 0)
            {
                failures.Add(prefix + "missing-state-comparisons");
            }

            if (result.StateDivergenceRate >
                thresholds.MaximumStateDivergenceRate)
            {
                failures.Add(prefix + "state-divergence-rate-over-budget");
            }

            if (result.MaximumStateDivergenceMagnitude >
                thresholds.MaximumStateDivergenceMagnitude)
            {
                failures.Add(prefix + "state-divergence-over-budget");
            }

            if (result.MaximumStateDivergenceDurationMilliseconds >
                thresholds.MaximumStateDivergenceDurationMilliseconds)
            {
                failures.Add(prefix + "persistent-state-divergence");
            }

            if (result.UplinkBytesPerSecond >
                thresholds.MaximumUplinkBytesPerSecond)
            {
                failures.Add(prefix + "uplink-over-budget");
            }

            if (result.DownlinkBytesPerSecond >
                thresholds.MaximumDownlinkBytesPerSecond)
            {
                failures.Add(prefix + "downlink-over-budget");
            }

            if (result.UplinkBytes <= 0 || result.DownlinkBytes <= 0)
            {
                failures.Add(prefix + "missing-directional-traffic");
            }

            if (result.SentCommandCount <= 0)
            {
                failures.Add(prefix + "missing-command-samples");
            }

            if (result.Scenario.HasControlledLoss &&
                result.DroppedCommandCount <= 0)
            {
                failures.Add(prefix + "controlled-loss-not-observed");
            }

            if (result.RejectionCount(
                    NetworkCommandRejectionReason.Unknown) > 0)
            {
                failures.Add(prefix + "unknown-rejection-reason");
            }

            int accounted = result.AcceptedCommandCount +
                result.DroppedCommandCount + result.RejectedCommandCount;
            if (accounted != result.SentCommandCount)
            {
                failures.Add(prefix + "command-accounting-mismatch");
            }
        }

        private static void ValidateRejectionCoverage(
            IReadOnlyList<NetworkDiagnosticScenarioResult> results,
            ICollection<string> failures)
        {
            for (int reasonIndex = 0;
                 reasonIndex < RequiredRejectionCoverage.Length;
                 reasonIndex++)
            {
                NetworkCommandRejectionReason reason =
                    RequiredRejectionCoverage[reasonIndex];
                int count = 0;

                for (int resultIndex = 0;
                     resultIndex < results.Count;
                     resultIndex++)
                {
                    count += results[resultIndex].RejectionCount(reason);
                }

                if (count == 0)
                {
                    failures.Add(
                        $"rejection-coverage:missing-{reason}");
                }
            }
        }
    }
}
