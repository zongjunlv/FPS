using System;

namespace FPS.Networking.Domain
{
    /// <summary>
    /// Maps a local monotonic clock onto server ticks. A replicated tick is an
    /// observation, not a send permit: short gaps continue advancing locally.
    /// Old/duplicate samples do not keep a stalled connection alive.
    /// </summary>
    public sealed class ClientServerClock
    {
        private readonly int tickRate;
        private readonly double silenceLimitSeconds;
        private readonly double resynchronizeGapSeconds;
        private long lastObservedTick = -1;
        private int runGeneration;
        private double anchorTick;
        private double anchorSeconds;
        private double lastObservationSeconds;
        private double lastEstimatedTick;
        private double lastPresentationTick;
        private double filteredRttSeconds;

        public ClientServerClock(int tickRate,
            double silenceLimitSeconds = 2d,
            double resynchronizeGapSeconds = 0.5d)
        {
            if (tickRate < 1) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (!Finite(silenceLimitSeconds) || silenceLimitSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(silenceLimitSeconds));
            if (!Finite(resynchronizeGapSeconds) || resynchronizeGapSeconds <= 0d ||
                resynchronizeGapSeconds > silenceLimitSeconds)
                throw new ArgumentOutOfRangeException(nameof(resynchronizeGapSeconds));
            this.tickRate = tickRate;
            this.silenceLimitSeconds = silenceLimitSeconds;
            this.resynchronizeGapSeconds = resynchronizeGapSeconds;
        }

        public bool IsSynchronized => lastObservedTick >= 0;
        public int Revision { get; private set; }
        public long LastObservedTick => lastObservedTick;
        public double FilteredRttSeconds => filteredRttSeconds;
        // Keep one 20Hz publication interval plus two simulation ticks of
        // jitter margin even on a zero-RTT link. Otherwise a fast renderer
        // reaches the newest sample and holds before the next snapshot arrives.
        // RTT/2 remains a heuristic, never a measured one-way delay guarantee.
        public double InterpolationDelayTicks => Math.Min(tickRate * 0.2d,
            Math.Max(CoopSnapshotCadence.IntervalTicksFor(tickRate) + 2d,
                tickRate * (filteredRttSeconds * 0.5d + 0.035d)));

        public bool Observe(long serverTick, int generation,
            double nowSeconds, double roundTripSeconds)
        {
            if (serverTick < 0 || !Finite(nowSeconds)) return false;
            if (IsSynchronized && generation < runGeneration) return false;
            bool newRun = IsSynchronized && generation != runGeneration;
            if (!newRun && serverTick <= lastObservedTick) return false;
            bool discontinuity = !IsSynchronized || newRun ||
                nowSeconds < lastObservationSeconds ||
                nowSeconds - lastObservationSeconds > resynchronizeGapSeconds;
            double boundedRtt = Math.Max(0d, Math.Min(0.5d,
                Finite(roundTripSeconds) ? roundTripSeconds : 0d));
            filteredRttSeconds = discontinuity
                ? boundedRtt
                : filteredRttSeconds * 0.85d + boundedRtt * 0.15d;
            double observationTick = serverTick +
                filteredRttSeconds * tickRate * 0.5d;
            double estimate = IsSynchronized ? Estimate(nowSeconds) : 0d;
            bool reset = discontinuity ||
                Math.Abs(observationTick - estimate) >
                    Math.Max(2d, tickRate * 0.2d);
            if (reset)
            {
                // A fresh time origin also needs a fresh latency origin; a
                // pre-pause RTT estimate cannot describe the recovered link.
                filteredRttSeconds = boundedRtt;
                observationTick = serverTick + boundedRtt * tickRate * 0.5d;
                anchorTick = observationTick;
                lastEstimatedTick = observationTick;
                lastPresentationTick = Math.Max(0d,
                    observationTick - InterpolationDelayTicks);
                Revision++;
            }
            else
            {
                // Slew a running clock rather than interpreting every jittery
                // observation as a fresh time origin.
                double correction = Math.Max(-0.5d,
                    Math.Min(0.5d, observationTick - estimate));
                anchorTick = Math.Max(serverTick, estimate + correction);
            }
            anchorSeconds = nowSeconds;
            lastObservationSeconds = nowSeconds;
            lastObservedTick = serverTick;
            runGeneration = generation;
            return true;
        }

        public bool IsHealthy(double nowSeconds) => IsSynchronized &&
            Finite(nowSeconds) && nowSeconds >= lastObservationSeconds &&
            nowSeconds - lastObservationSeconds <= silenceLimitSeconds;

        public double Estimate(double nowSeconds)
        {
            if (!IsSynchronized) return 0d;
            if (!Finite(nowSeconds)) return lastEstimatedTick;
            double elapsed = Math.Max(0d, Math.Min(silenceLimitSeconds,
                nowSeconds - anchorSeconds));
            lastEstimatedTick = Math.Max(lastEstimatedTick,
                anchorTick + elapsed * tickRate);
            return lastEstimatedTick;
        }

        public double PresentationTick(double nowSeconds)
        {
            lastPresentationTick = Math.Max(lastPresentationTick,
                Estimate(nowSeconds) - InterpolationDelayTicks);
            return Math.Max(0d, lastPresentationTick);
        }

        public bool TryGetNextInputTick(double nowSeconds,
            long previousInputTick, out long inputTick)
        {
            inputTick = previousInputTick;
            if (!IsHealthy(nowSeconds)) return false;
            // Input execution timestamps use the conservative observed clock,
            // not RTT/2 as if it were an exact one-way latency. The monotonic
            // elapsed term still advances without a per-snapshot send permit.
            double elapsed = Math.Max(0d,
                Math.Min(silenceLimitSeconds,
                    nowSeconds - lastObservationSeconds));
            long available = (long)Math.Floor(lastObservedTick +
                elapsed * tickRate) + 1;
            if (available <= previousInputTick) return false;
            // No frame may emit a multi-second catch-up burst after a pause.
            inputTick = Math.Max(previousInputTick + 1,
                Math.Max(lastObservedTick + 1, available - 3));
            return true;
        }

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
