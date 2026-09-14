using System;
using System.Collections.Generic;

namespace FPS.Networking.Diagnostics
{
    /// <summary>
    /// Deterministic, in-process fixture used to validate metric plumbing and
    /// report gates. It is not a socket, transport, or multi-process test.
    /// </summary>
    public static class Issue65NetworkDiagnosticFixture
    {
        public const string MeasurementScope =
            "deterministic-in-process-metric-pipeline-fixture";

        public static NetworkDiagnosticReport Run(
            NetworkDiagnosticRunMetadata metadata,
            int framesPerScenario = 600,
            double deltaTimeSeconds = 1d / 60d)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (metadata.EvidenceKind !=
                NetworkDiagnosticEvidenceKind.DeterministicFixture)
            {
                throw new ArgumentException(
                    "The in-process fixture cannot emit multi-process evidence.",
                    nameof(metadata));
            }

            if (framesPerScenario < 240)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(framesPerScenario),
                    "At least 240 frames are required for useful samples.");
            }

            if (deltaTimeSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTimeSeconds));
            }

            var results = new List<NetworkDiagnosticScenarioResult>();
            for (int scenarioIndex = 0;
                 scenarioIndex < Issue65NetworkConditionMatrix.Required.Count;
                 scenarioIndex++)
            {
                NetworkConditionScenario scenario =
                    Issue65NetworkConditionMatrix.Required[scenarioIndex];
                var accumulator = new NetworkDiagnosticsAccumulator(scenario);

                for (int frame = 0; frame < framesPerScenario; frame++)
                {
                    accumulator.Advance(deltaTimeSeconds);
                    accumulator.RecordTraffic(
                        72L + (frame % 3) * 4L,
                        116L + (frame % 5) * 6L);

                    bool divergent = frame > 0 && frame % 240 == 0;
                    accumulator.RecordStateComparison(
                        divergent,
                        divergent ? 0.18d + scenarioIndex * 0.03d : 0d,
                        divergent
                            ? scenario.RoundTripLatencyMilliseconds + 24d
                            : 0d);

                    if (frame % 10 == 0)
                    {
                        accumulator.RecordCommandSent();
                        if (ShouldDrop(scenario, frame))
                        {
                            accumulator.RecordCommandDroppedByCondition();
                        }
                        else
                        {
                            accumulator.RecordCommandAccepted();
                            accumulator.RecordHitFeedback(
                                SimulatedFeedbackMilliseconds(
                                    scenario,
                                    frame));
                        }
                    }

                    if (frame > 0 && frame % 180 == 0 &&
                        scenario.RoundTripLatencyMilliseconds > 0)
                    {
                        accumulator.RecordCorrection(
                            0.08d + scenarioIndex * 0.035d);
                    }
                }

                if (scenarioIndex == 0)
                {
                    RecordRejected(
                        accumulator,
                        NetworkCommandRejectionReason.TooOld);
                    RecordRejected(
                        accumulator,
                        NetworkCommandRejectionReason.Duplicate);
                    RecordRejected(
                        accumulator,
                        NetworkCommandRejectionReason.IllegalState);
                }

                results.Add(accumulator.Complete());
            }

            NetworkDiagnosticsGateDecision gate =
                NetworkDiagnosticsGate.Evaluate(metadata, results);
            return new NetworkDiagnosticReport(metadata, results, gate);
        }

        private static void RecordRejected(
            NetworkDiagnosticsAccumulator accumulator,
            NetworkCommandRejectionReason reason)
        {
            accumulator.RecordCommandSent();
            accumulator.RecordCommandRejected(reason);
        }

        private static bool ShouldDrop(
            NetworkConditionScenario scenario,
            int frame)
        {
            if (!scenario.HasControlledLoss)
            {
                return false;
            }

            uint hash = Hash((uint)scenario.SimulationSeed, (uint)frame);
            return hash % 10000u <
                (uint)scenario.PacketLossBasisPoints;
        }

        private static double SimulatedFeedbackMilliseconds(
            NetworkConditionScenario scenario,
            int frame)
        {
            if (scenario.JitterMilliseconds == 0)
            {
                return 16d;
            }

            uint hash = Hash(
                (uint)(scenario.SimulationSeed + 97),
                (uint)frame);
            int span = scenario.JitterMilliseconds * 2 + 1;
            int jitter = (int)(hash % (uint)span) -
                scenario.JitterMilliseconds;
            return Math.Max(
                0d,
                scenario.RoundTripLatencyMilliseconds + 16d + jitter);
        }

        private static uint Hash(uint seed, uint value)
        {
            uint result = seed ^ (value + 0x9e3779b9u + (seed << 6) +
                (seed >> 2));
            result ^= result >> 16;
            result *= 0x7feb352du;
            result ^= result >> 15;
            result *= 0x846ca68bu;
            result ^= result >> 16;
            return result;
        }
    }
}
