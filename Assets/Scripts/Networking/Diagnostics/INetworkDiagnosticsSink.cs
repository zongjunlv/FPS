namespace FPS.Networking.Diagnostics
{
    /// <summary>
    /// Transport and gameplay adapters report observations through this port.
    /// It deliberately does not depend on Netcode or the networking domain.
    /// </summary>
    public interface INetworkDiagnosticsSink
    {
        void Advance(double elapsedSeconds);
        void RecordHitFeedback(double roundTripMilliseconds);
        void RecordCorrection(double magnitude);
        void RecordTraffic(long uplinkDeltaBytes, long downlinkDeltaBytes);
        void RecordStateComparison(
            bool divergent,
            double divergenceMagnitude = 0d,
            double divergenceDurationMilliseconds = 0d);
        void RecordCommandSent();
        void RecordCommandAccepted();
        void RecordCommandDroppedByCondition();
        void RecordCommandRejected(NetworkCommandRejectionReason reason);
    }
}
