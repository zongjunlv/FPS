using DomainRejectionReason =
    FPS.Networking.Domain.CommandRejectionReason;

namespace FPS.Networking.Diagnostics
{
    public static class NetworkDiagnosticsDomainMapper
    {
        public static NetworkCommandRejectionReason MapRejection(
            DomainRejectionReason reason)
        {
            return reason switch
            {
                DomainRejectionReason.None =>
                    NetworkCommandRejectionReason.None,
                DomainRejectionReason.UnknownPlayer =>
                    NetworkCommandRejectionReason.Unauthorized,
                DomainRejectionReason.DuplicateNonce =>
                    NetworkCommandRejectionReason.Duplicate,
                DomainRejectionReason.DuplicateClientTick =>
                    NetworkCommandRejectionReason.Duplicate,
                DomainRejectionReason.TimestampTooOld =>
                    NetworkCommandRejectionReason.TooOld,
                DomainRejectionReason.AimRateExceeded =>
                    NetworkCommandRejectionReason.RateLimited,
                DomainRejectionReason.FireRateExceeded =>
                    NetworkCommandRejectionReason.RateLimited,
                DomainRejectionReason.JumpRateExceeded =>
                    NetworkCommandRejectionReason.RateLimited,
                DomainRejectionReason.InvalidMovement =>
                    NetworkCommandRejectionReason.IllegalState,
                DomainRejectionReason.ImpossibleDisplacement =>
                    NetworkCommandRejectionReason.IllegalState,
                DomainRejectionReason.InvalidAim =>
                    NetworkCommandRejectionReason.IllegalState,
                DomainRejectionReason.StanceBlocked =>
                    NetworkCommandRejectionReason.IllegalState,
                DomainRejectionReason.InvalidSequence =>
                    NetworkCommandRejectionReason.InvalidPayload,
                DomainRejectionReason.TimestampInFuture =>
                    NetworkCommandRejectionReason.InvalidPayload,
                _ => NetworkCommandRejectionReason.Unknown
            };
        }
    }
}
