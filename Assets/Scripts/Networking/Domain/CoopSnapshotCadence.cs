using System;

namespace FPS.Networking.Domain
{
    /// <summary>Snapshot publication is independent of authoritative simulation.</summary>
    public static class CoopSnapshotCadence
    {
        public const int MaximumSnapshotsPerSecond = 20;

        public static int RateFor(int simulationTickRate) =>
            Math.Min(MaximumSnapshotsPerSecond, Math.Max(1, simulationTickRate));

        public static double IntervalTicksFor(int simulationTickRate) =>
            simulationTickRate / (double)RateFor(simulationTickRate);
    }
}
