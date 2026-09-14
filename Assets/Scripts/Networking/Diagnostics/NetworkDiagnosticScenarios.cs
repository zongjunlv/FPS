using System;
using System.Collections.Generic;

namespace FPS.Networking.Diagnostics
{
    public sealed class NetworkConditionScenario
    {
        public NetworkConditionScenario(
            string stableId,
            int roundTripLatencyMilliseconds,
            int packetLossBasisPoints,
            int jitterMilliseconds,
            int simulationSeed)
        {
            StableId = Required(stableId, nameof(stableId));
            RoundTripLatencyMilliseconds = Math.Max(
                0,
                roundTripLatencyMilliseconds);
            PacketLossBasisPoints = Math.Min(
                10000,
                Math.Max(0, packetLossBasisPoints));
            JitterMilliseconds = Math.Max(0, jitterMilliseconds);
            SimulationSeed = simulationSeed;
        }

        public string StableId { get; }
        public int RoundTripLatencyMilliseconds { get; }
        public int PacketLossBasisPoints { get; }
        public int JitterMilliseconds { get; }
        public int SimulationSeed { get; }
        public bool HasControlledLoss => PacketLossBasisPoints > 0;

        private static string Required(string value, string name)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new ArgumentException("Value is required.", name);
        }
    }

    public static class Issue65NetworkConditionMatrix
    {
        private static readonly NetworkConditionScenario[] Scenarios =
        {
            new("rtt-000-loss-00", 0, 0, 0, 65000),
            new("rtt-080-loss-00", 80, 0, 8, 65080),
            new("rtt-150-loss-00", 150, 0, 15, 65150),
            new("rtt-080-loss-05", 80, 500, 8, 65580)
        };

        public static IReadOnlyList<NetworkConditionScenario> Required =>
            Scenarios;

        public static NetworkConditionScenario Find(string stableId)
        {
            for (int index = 0; index < Scenarios.Length; index++)
            {
                if (string.Equals(
                        Scenarios[index].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return Scenarios[index];
                }
            }

            return null;
        }
    }

    public enum NetworkDiagnosticEvidenceKind
    {
        DeterministicFixture = 0,
        MultiProcessPlayer = 1
    }

    public enum NetworkCommandRejectionReason
    {
        None = 0,
        TooOld = 1,
        Duplicate = 2,
        IllegalState = 3,
        Unauthorized = 4,
        InvalidPayload = 5,
        RateLimited = 6,
        Unknown = 255
    }
}
