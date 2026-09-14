using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Diagnostics
{
    public sealed class NetworkDiagnosticRunMetadata
    {
        public NetworkDiagnosticRunMetadata(
            string runId,
            string implementationVersion,
            string unityVersion,
            string transportName,
            NetworkDiagnosticEvidenceKind evidenceKind,
            string topology,
            int processCount,
            string hostPlatform,
            string clientPlatform,
            string contentVersion)
        {
            RunId = Required(runId, nameof(runId));
            ImplementationVersion = Required(
                implementationVersion,
                nameof(implementationVersion));
            UnityVersion = Required(unityVersion, nameof(unityVersion));
            TransportName = Required(transportName, nameof(transportName));
            EvidenceKind = evidenceKind;
            Topology = Required(topology, nameof(topology));
            ProcessCount = Math.Max(1, processCount);
            HostPlatform = Required(hostPlatform, nameof(hostPlatform));
            ClientPlatform = Required(clientPlatform, nameof(clientPlatform));
            ContentVersion = Required(contentVersion, nameof(contentVersion));
        }

        public string RunId { get; }
        public string ImplementationVersion { get; }
        public string UnityVersion { get; }
        public string TransportName { get; }
        public NetworkDiagnosticEvidenceKind EvidenceKind { get; }
        public string Topology { get; }
        public int ProcessCount { get; }
        public string HostPlatform { get; }
        public string ClientPlatform { get; }
        public string ContentVersion { get; }
        public bool IsRealMultiProcess =>
            EvidenceKind == NetworkDiagnosticEvidenceKind.MultiProcessPlayer &&
            ProcessCount >= 2;

        private static string Required(string value, string name)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new ArgumentException("Value is required.", name);
        }
    }

    public readonly struct NetworkRejectionCount
    {
        public NetworkRejectionCount(
            NetworkCommandRejectionReason reason,
            int count)
        {
            Reason = reason;
            Count = Math.Max(0, count);
        }

        public NetworkCommandRejectionReason Reason { get; }
        public int Count { get; }
    }

    public sealed class NetworkDiagnosticScenarioResult
    {
        public NetworkDiagnosticScenarioResult(
            NetworkConditionScenario scenario,
            double durationSeconds,
            int hitFeedbackSampleCount,
            double meanHitFeedbackMilliseconds,
            double p95HitFeedbackMilliseconds,
            double p99HitFeedbackMilliseconds,
            int correctionCount,
            double correctionsPerMinute,
            double meanCorrectionMagnitude,
            double p95CorrectionMagnitude,
            double maximumCorrectionMagnitude,
            long uplinkBytes,
            long downlinkBytes,
            double uplinkBytesPerSecond,
            double downlinkBytesPerSecond,
            int stateComparisonCount,
            int stateDivergenceCount,
            double stateDivergenceRate,
            double maximumStateDivergenceMagnitude,
            double maximumStateDivergenceDurationMilliseconds,
            int sentCommandCount,
            int acceptedCommandCount,
            int droppedCommandCount,
            IReadOnlyList<NetworkRejectionCount> rejectionCounts)
        {
            Scenario = scenario ?? throw new ArgumentNullException(
                nameof(scenario));
            DurationSeconds = Math.Max(0d, durationSeconds);
            HitFeedbackSampleCount = Math.Max(0, hitFeedbackSampleCount);
            MeanHitFeedbackMilliseconds = NonNegative(
                meanHitFeedbackMilliseconds);
            P95HitFeedbackMilliseconds = NonNegative(
                p95HitFeedbackMilliseconds);
            P99HitFeedbackMilliseconds = NonNegative(
                p99HitFeedbackMilliseconds);
            CorrectionCount = Math.Max(0, correctionCount);
            CorrectionsPerMinute = NonNegative(correctionsPerMinute);
            MeanCorrectionMagnitude = NonNegative(meanCorrectionMagnitude);
            P95CorrectionMagnitude = NonNegative(p95CorrectionMagnitude);
            MaximumCorrectionMagnitude = NonNegative(
                maximumCorrectionMagnitude);
            UplinkBytes = Math.Max(0L, uplinkBytes);
            DownlinkBytes = Math.Max(0L, downlinkBytes);
            UplinkBytesPerSecond = NonNegative(uplinkBytesPerSecond);
            DownlinkBytesPerSecond = NonNegative(downlinkBytesPerSecond);
            StateComparisonCount = Math.Max(0, stateComparisonCount);
            StateDivergenceCount = Math.Max(0, stateDivergenceCount);
            StateDivergenceRate = NonNegative(stateDivergenceRate);
            MaximumStateDivergenceMagnitude = NonNegative(
                maximumStateDivergenceMagnitude);
            MaximumStateDivergenceDurationMilliseconds = NonNegative(
                maximumStateDivergenceDurationMilliseconds);
            SentCommandCount = Math.Max(0, sentCommandCount);
            AcceptedCommandCount = Math.Max(0, acceptedCommandCount);
            DroppedCommandCount = Math.Max(0, droppedCommandCount);
            RejectionCounts = Array.AsReadOnly((rejectionCounts ??
                    Array.Empty<NetworkRejectionCount>())
                .OrderBy(value => value.Reason)
                .ToArray());
        }

        public NetworkConditionScenario Scenario { get; }
        public double DurationSeconds { get; }
        public int HitFeedbackSampleCount { get; }
        public double MeanHitFeedbackMilliseconds { get; }
        public double P95HitFeedbackMilliseconds { get; }
        public double P99HitFeedbackMilliseconds { get; }
        public int CorrectionCount { get; }
        public double CorrectionsPerMinute { get; }
        public double MeanCorrectionMagnitude { get; }
        public double P95CorrectionMagnitude { get; }
        public double MaximumCorrectionMagnitude { get; }
        public long UplinkBytes { get; }
        public long DownlinkBytes { get; }
        public double UplinkBytesPerSecond { get; }
        public double DownlinkBytesPerSecond { get; }
        public int StateComparisonCount { get; }
        public int StateDivergenceCount { get; }
        public double StateDivergenceRate { get; }
        public double MaximumStateDivergenceMagnitude { get; }
        public double MaximumStateDivergenceDurationMilliseconds { get; }
        public int SentCommandCount { get; }
        public int AcceptedCommandCount { get; }
        public int DroppedCommandCount { get; }
        public IReadOnlyList<NetworkRejectionCount> RejectionCounts { get; }
        public int RejectedCommandCount =>
            RejectionCounts.Sum(value => value.Count);

        public int RejectionCount(NetworkCommandRejectionReason reason)
        {
            for (int index = 0; index < RejectionCounts.Count; index++)
            {
                if (RejectionCounts[index].Reason == reason)
                {
                    return RejectionCounts[index].Count;
                }
            }

            return 0;
        }

        private static double NonNegative(double value)
        {
            return double.IsNaN(value) || value < 0d ? 0d : value;
        }
    }

    public sealed class NetworkDiagnosticReport
    {
        public NetworkDiagnosticReport(
            NetworkDiagnosticRunMetadata metadata,
            IEnumerable<NetworkDiagnosticScenarioResult> scenarios,
            NetworkDiagnosticsGateDecision gate)
        {
            Metadata = metadata ?? throw new ArgumentNullException(
                nameof(metadata));
            Scenarios = Array.AsReadOnly((scenarios ??
                    throw new ArgumentNullException(nameof(scenarios)))
                .OrderBy(value => value.Scenario.StableId,
                    StringComparer.Ordinal)
                .ToArray());
            Gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public NetworkDiagnosticRunMetadata Metadata { get; }
        public IReadOnlyList<NetworkDiagnosticScenarioResult> Scenarios {
            get;
        }
        public NetworkDiagnosticsGateDecision Gate { get; }
    }
}
