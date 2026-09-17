using System;

namespace FPS.Networking.Netcode
{
    /// <summary>Deterministic idle lease used by the server and unit tests.</summary>
    public sealed class DedicatedServerIdlePolicy
    {
        private readonly double timeoutSeconds;
        private double lastClientActivitySeconds;

        public DedicatedServerIdlePolicy(double timeoutSeconds,
            double startedAtSeconds)
        {
            if (timeoutSeconds <= 0d || double.IsNaN(timeoutSeconds) ||
                double.IsInfinity(timeoutSeconds))
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            timeoutSeconds = Math.Max(0.001d, timeoutSeconds);
            this.timeoutSeconds = timeoutSeconds;
            lastClientActivitySeconds = startedAtSeconds;
        }

        public double LastClientActivitySeconds =>
            lastClientActivitySeconds;

        public bool ShouldRecycle(double nowSeconds, int connectedClients)
        {
            if (connectedClients < 0)
                throw new ArgumentOutOfRangeException(nameof(connectedClients));
            if (connectedClients > 0)
            {
                lastClientActivitySeconds = Math.Max(
                    lastClientActivitySeconds, nowSeconds);
                return false;
            }
            return nowSeconds - lastClientActivitySeconds >= timeoutSeconds;
        }
    }
}
