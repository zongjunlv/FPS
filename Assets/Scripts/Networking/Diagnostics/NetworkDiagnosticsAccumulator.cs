using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Diagnostics
{
    public sealed class NetworkDiagnosticsAccumulator : INetworkDiagnosticsSink
    {
        private readonly NetworkConditionScenario scenario;
        private readonly List<double> hitFeedbackMilliseconds = new();
        private readonly List<double> correctionMagnitudes = new();
        private readonly SortedDictionary<NetworkCommandRejectionReason, int>
            rejectionCounts = new();
        private double durationSeconds;
        private long uplinkBytes;
        private long downlinkBytes;
        private int stateComparisonCount;
        private int stateDivergenceCount;
        private double maximumStateDivergenceMagnitude;
        private double maximumStateDivergenceDurationMilliseconds;
        private int sentCommandCount;
        private int acceptedCommandCount;
        private int droppedCommandCount;

        public NetworkDiagnosticsAccumulator(NetworkConditionScenario scenario)
        {
            this.scenario = scenario ?? throw new ArgumentNullException(
                nameof(scenario));
        }

        public void Advance(double elapsedSeconds)
        {
            durationSeconds += Math.Max(0d, elapsedSeconds);
        }

        public void RecordHitFeedback(double roundTripMilliseconds)
        {
            hitFeedbackMilliseconds.Add(Math.Max(
                0d,
                roundTripMilliseconds));
        }

        public void RecordCorrection(double magnitude)
        {
            correctionMagnitudes.Add(Math.Max(0d, magnitude));
        }

        public void RecordTraffic(long uplinkDeltaBytes, long downlinkDeltaBytes)
        {
            uplinkBytes += Math.Max(0L, uplinkDeltaBytes);
            downlinkBytes += Math.Max(0L, downlinkDeltaBytes);
        }

        public void RecordStateComparison(
            bool divergent,
            double divergenceMagnitude = 0d,
            double divergenceDurationMilliseconds = 0d)
        {
            stateComparisonCount++;

            if (!divergent)
            {
                return;
            }

            stateDivergenceCount++;
            maximumStateDivergenceMagnitude = Math.Max(
                maximumStateDivergenceMagnitude,
                Math.Max(0d, divergenceMagnitude));
            maximumStateDivergenceDurationMilliseconds = Math.Max(
                maximumStateDivergenceDurationMilliseconds,
                Math.Max(0d, divergenceDurationMilliseconds));
        }

        public void RecordCommandSent()
        {
            sentCommandCount++;
        }

        public void RecordCommandAccepted()
        {
            acceptedCommandCount++;
        }

        public void RecordCommandDroppedByCondition()
        {
            droppedCommandCount++;
        }

        public void RecordCommandRejected(
            NetworkCommandRejectionReason reason)
        {
            NetworkCommandRejectionReason normalized = reason ==
                NetworkCommandRejectionReason.None
                ? NetworkCommandRejectionReason.Unknown
                : reason;
            rejectionCounts.TryGetValue(normalized, out int count);
            rejectionCounts[normalized] = count + 1;
        }

        public NetworkDiagnosticScenarioResult Complete()
        {
            double safeDuration = Math.Max(durationSeconds, 0.000001d);
            double[] hit = Ordered(hitFeedbackMilliseconds);
            double[] correction = Ordered(correctionMagnitudes);
            NetworkRejectionCount[] rejections = rejectionCounts
                .Select(value => new NetworkRejectionCount(
                    value.Key,
                    value.Value))
                .ToArray();
            return new NetworkDiagnosticScenarioResult(
                scenario,
                durationSeconds,
                hit.Length,
                Average(hit),
                Percentile(hit, 0.95d),
                Percentile(hit, 0.99d),
                correction.Length,
                correction.Length * 60d / safeDuration,
                Average(correction),
                Percentile(correction, 0.95d),
                correction.Length == 0 ? 0d : correction[^1],
                uplinkBytes,
                downlinkBytes,
                uplinkBytes / safeDuration,
                downlinkBytes / safeDuration,
                stateComparisonCount,
                stateDivergenceCount,
                stateComparisonCount == 0
                    ? 0d
                    : (double)stateDivergenceCount / stateComparisonCount,
                maximumStateDivergenceMagnitude,
                maximumStateDivergenceDurationMilliseconds,
                sentCommandCount,
                acceptedCommandCount,
                droppedCommandCount,
                rejections);
        }

        public static double Percentile(
            IReadOnlyList<double> orderedValues,
            double percentile)
        {
            if (orderedValues == null || orderedValues.Count == 0)
            {
                return 0d;
            }

            if (percentile < 0d || percentile > 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(percentile));
            }

            int rank = Math.Max(
                0,
                (int)Math.Ceiling(percentile * orderedValues.Count) - 1);
            return orderedValues[Math.Min(rank, orderedValues.Count - 1)];
        }

        private static double[] Ordered(IEnumerable<double> source)
        {
            return source.OrderBy(value => value).ToArray();
        }

        private static double Average(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double total = 0d;
            for (int index = 0; index < values.Count; index++)
            {
                total += values[index];
            }

            return total / values.Count;
        }
    }
}
