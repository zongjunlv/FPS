using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public enum NetworkPacketKind
    {
        InputCommand,
        WorldSnapshot,
        HitFeedback
    }

    public readonly struct NetworkPacket
    {
        public NetworkPacket(
            long packetId,
            NetworkPacketKind kind,
            int sourcePeerId,
            int destinationPeerId,
            long sentTick,
            int payloadBytes,
            long correlationTick = -1)
        {
            if (packetId <= 0)
                throw new ArgumentOutOfRangeException(nameof(packetId));
            if (sourcePeerId < 0)
                throw new ArgumentOutOfRangeException(nameof(sourcePeerId));
            if (destinationPeerId < 0 || destinationPeerId == sourcePeerId)
                throw new ArgumentOutOfRangeException(nameof(destinationPeerId));
            if (sentTick < 0)
                throw new ArgumentOutOfRangeException(nameof(sentTick));
            if (payloadBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadBytes));
            if (correlationTick > sentTick)
                throw new ArgumentOutOfRangeException(nameof(correlationTick));
            PacketId = packetId;
            Kind = kind;
            SourcePeerId = sourcePeerId;
            DestinationPeerId = destinationPeerId;
            SentTick = sentTick;
            PayloadBytes = payloadBytes;
            CorrelationTick = correlationTick;
        }

        public long PacketId { get; }
        public NetworkPacketKind Kind { get; }
        public int SourcePeerId { get; }
        public int DestinationPeerId { get; }
        public long SentTick { get; }
        public int PayloadBytes { get; }
        public long CorrelationTick { get; }
    }

    public sealed class NetworkConditionProfile
    {
        public NetworkConditionProfile(
            string name,
            int roundTripLatencyMilliseconds,
            int packetLossBasisPoints,
            int jitterMilliseconds = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Profile name is required.", nameof(name));
            if (roundTripLatencyMilliseconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(roundTripLatencyMilliseconds));
            if (packetLossBasisPoints < 0 || packetLossBasisPoints > 10000)
                throw new ArgumentOutOfRangeException(nameof(packetLossBasisPoints));
            if (jitterMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(jitterMilliseconds));
            Name = name.Trim();
            RoundTripLatencyMilliseconds = roundTripLatencyMilliseconds;
            PacketLossBasisPoints = packetLossBasisPoints;
            JitterMilliseconds = jitterMilliseconds;
        }

        public string Name { get; }
        public int RoundTripLatencyMilliseconds { get; }
        public int PacketLossBasisPoints { get; }
        public int JitterMilliseconds { get; }

        public static NetworkConditionProfile Latency0() =>
            new("0ms", 0, 0);
        public static NetworkConditionProfile Latency80() =>
            new("80ms", 80, 0);
        public static NetworkConditionProfile Latency150() =>
            new("150ms", 150, 0);
        public static NetworkConditionProfile Lossy80(
            int packetLossBasisPoints = 500) =>
            new("80ms-loss", 80, packetLossBasisPoints, 8);
    }

    public readonly struct NetworkTransmission
    {
        public NetworkTransmission(
            NetworkPacket packet,
            bool dropped,
            long deliveryTick)
        {
            Packet = packet;
            Dropped = dropped;
            DeliveryTick = deliveryTick;
        }

        public NetworkPacket Packet { get; }
        public bool Dropped { get; }
        public long DeliveryTick { get; }
    }

    public sealed class NetworkScenarioMetrics
    {
        public NetworkScenarioMetrics(
            string profileName,
            int roundTripLatencyMilliseconds,
            int configuredPacketLossBasisPoints,
            int sentPackets,
            int deliveredPackets,
            int droppedPackets,
            long sentBytes,
            long deliveredBytes,
            double observedPacketLossRatio,
            double averageHitFeedbackMilliseconds,
            double p95HitFeedbackMilliseconds,
            int correctionCount,
            int snapCorrectionCount,
            double averageCorrectionDistance,
            double maximumCorrectionDistance,
            double averageStateDivergence,
            double maximumStateDivergence)
        {
            ProfileName = profileName;
            RoundTripLatencyMilliseconds = roundTripLatencyMilliseconds;
            ConfiguredPacketLossBasisPoints = configuredPacketLossBasisPoints;
            SentPackets = sentPackets;
            DeliveredPackets = deliveredPackets;
            DroppedPackets = droppedPackets;
            SentBytes = sentBytes;
            DeliveredBytes = deliveredBytes;
            ObservedPacketLossRatio = observedPacketLossRatio;
            AverageHitFeedbackMilliseconds = averageHitFeedbackMilliseconds;
            P95HitFeedbackMilliseconds = p95HitFeedbackMilliseconds;
            CorrectionCount = correctionCount;
            SnapCorrectionCount = snapCorrectionCount;
            AverageCorrectionDistance = averageCorrectionDistance;
            MaximumCorrectionDistance = maximumCorrectionDistance;
            AverageStateDivergence = averageStateDivergence;
            MaximumStateDivergence = maximumStateDivergence;
        }

        public string ProfileName { get; }
        public int RoundTripLatencyMilliseconds { get; }
        public int ConfiguredPacketLossBasisPoints { get; }
        public int SentPackets { get; }
        public int DeliveredPackets { get; }
        public int DroppedPackets { get; }
        public long SentBytes { get; }
        public long DeliveredBytes { get; }
        public double ObservedPacketLossRatio { get; }
        public double AverageHitFeedbackMilliseconds { get; }
        public double P95HitFeedbackMilliseconds { get; }
        public int CorrectionCount { get; }
        public int SnapCorrectionCount { get; }
        public double AverageCorrectionDistance { get; }
        public double MaximumCorrectionDistance { get; }
        public double AverageStateDivergence { get; }
        public double MaximumStateDivergence { get; }
    }

    /// <summary>
    /// Deterministic transport-condition model for repeatable 0/80/150 ms and
    /// packet-loss acceptance runs. It schedules metadata only; serialization
    /// and sockets remain adapter responsibilities.
    /// </summary>
    public sealed class DeterministicNetworkConditionModel
    {
        private readonly long seed;
        private readonly int tickRate;
        private readonly NetworkConditionProfile profile;
        private readonly List<NetworkTransmission> pending = new();
        private readonly List<double> feedbackMilliseconds = new();
        private readonly List<double> correctionDistances = new();
        private readonly List<double> divergences = new();
        private int sentPackets;
        private int deliveredPackets;
        private int droppedPackets;
        private long sentBytes;
        private long deliveredBytes;
        private int correctionCount;
        private int snapCorrectionCount;

        public DeterministicNetworkConditionModel(
            long seed,
            int tickRate,
            NetworkConditionProfile profile)
        {
            if (tickRate < 1 || tickRate > 1000)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            this.seed = seed;
            this.tickRate = tickRate;
            this.profile = profile ??
                throw new ArgumentNullException(nameof(profile));
        }

        public NetworkConditionProfile Profile => profile;

        public NetworkTransmission Transmit(NetworkPacket packet)
        {
            sentPackets++;
            sentBytes += packet.PayloadBytes;
            bool dropped = SampleBasisPoints(packet.PacketId, 0) <
                profile.PacketLossBasisPoints;
            int jitter = profile.JitterMilliseconds == 0
                ? 0
                : SampleJitter(packet.PacketId);
            double oneWayMilliseconds = Math.Max(
                0d,
                profile.RoundTripLatencyMilliseconds * 0.5d + jitter);
            long delayTicks = (long)Math.Ceiling(
                oneWayMilliseconds * tickRate / 1000d);
            var transmission = new NetworkTransmission(
                packet,
                dropped,
                packet.SentTick + delayTicks);
            if (dropped)
                droppedPackets++;
            else
                pending.Add(transmission);
            return transmission;
        }

        public IReadOnlyList<NetworkTransmission> Drain(long currentTick)
        {
            NetworkTransmission[] delivered = pending
                .Where(value => value.DeliveryTick <= currentTick)
                .OrderBy(value => value.DeliveryTick)
                .ThenBy(value => value.Packet.PacketId)
                .ToArray();
            if (delivered.Length == 0) return delivered;
            var ids = new HashSet<long>(
                delivered.Select(value => value.Packet.PacketId));
            pending.RemoveAll(value => ids.Contains(value.Packet.PacketId));
            foreach (NetworkTransmission transmission in delivered)
            {
                deliveredPackets++;
                deliveredBytes += transmission.Packet.PayloadBytes;
                if (transmission.Packet.Kind == NetworkPacketKind.HitFeedback)
                {
                    feedbackMilliseconds.Add(
                        (transmission.DeliveryTick -
                         (transmission.Packet.CorrelationTick >= 0
                             ? transmission.Packet.CorrelationTick
                             : transmission.Packet.SentTick)) *
                        1000d / tickRate);
                }
            }
            return delivered;
        }

        public void RecordCorrection(PredictionCorrection correction)
        {
            if (!correction.WasCorrected) return;
            correctionCount++;
            if (correction.Kind == PredictionCorrectionKind.Snap)
                snapCorrectionCount++;
            correctionDistances.Add(correction.ErrorDistance);
        }

        public void RecordStateDivergence(double distance)
        {
            if (!CoopGameplayRules.Finite(distance) || distance < 0d)
                throw new ArgumentOutOfRangeException(nameof(distance));
            divergences.Add(distance);
        }

        public NetworkScenarioMetrics CaptureMetrics()
        {
            return new NetworkScenarioMetrics(
                profile.Name,
                profile.RoundTripLatencyMilliseconds,
                profile.PacketLossBasisPoints,
                sentPackets,
                deliveredPackets,
                droppedPackets,
                sentBytes,
                deliveredBytes,
                sentPackets > 0 ? (double)droppedPackets / sentPackets : 0d,
                Average(feedbackMilliseconds),
                Percentile(feedbackMilliseconds, 0.95d),
                correctionCount,
                snapCorrectionCount,
                Average(correctionDistances),
                correctionDistances.Count > 0
                    ? correctionDistances.Max()
                    : 0d,
                Average(divergences),
                divergences.Count > 0 ? divergences.Max() : 0d);
        }

        private int SampleJitter(long packetId)
        {
            int span = profile.JitterMilliseconds * 2 + 1;
            return SampleBasisPoints(packetId, 1) % span -
                profile.JitterMilliseconds;
        }

        private int SampleBasisPoints(long packetId, int stream)
        {
            ulong value = unchecked((ulong)seed);
            value ^= unchecked((ulong)packetId) + 0x9E3779B97F4A7C15UL;
            value ^= unchecked((ulong)stream) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (int)(value % 10000UL);
        }

        private static double Average(IReadOnlyCollection<double> values) =>
            values.Count == 0 ? 0d : values.Average();

        private static double Percentile(
            IEnumerable<double> values,
            double percentile)
        {
            double[] ordered = values.OrderBy(value => value).ToArray();
            if (ordered.Length == 0) return 0d;
            int rank = Math.Max(
                0,
                (int)Math.Ceiling(percentile * ordered.Length) - 1);
            return ordered[Math.Min(rank, ordered.Length - 1)];
        }
    }
}
