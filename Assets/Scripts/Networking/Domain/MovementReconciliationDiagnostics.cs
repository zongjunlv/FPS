namespace FPS.Networking.Domain
{
    /// <summary>One value-only reconciliation sample, not wire payload or retained history.</summary>
    public readonly struct PredictionReconciliationDiagnostic
    {
        public PredictionReconciliationDiagnostic(
            PlayerMovementState before, PlayerMovementState replay,
            AuthoritativePlayerState authoritative, long previousPredictedTick,
            long previousAcceptedTick, long replayTick, int pendingBefore,
            int pendingAfter, PlayerInputCommand lastPendingCommand,
            bool hasPendingCommand, double speedMultiplier)
        {
            Before = before;
            Replay = replay;
            Authoritative = authoritative;
            PreviousPredictedTick = previousPredictedTick;
            PreviousAcceptedTick = previousAcceptedTick;
            ReplayTick = replayTick;
            PendingBefore = pendingBefore;
            PendingAfter = pendingAfter;
            LastPendingCommand = lastPendingCommand;
            HasPendingCommand = hasPendingCommand;
            SpeedMultiplier = speedMultiplier;
        }

        public PlayerMovementState Before { get; }
        public PlayerMovementState Replay { get; }
        public AuthoritativePlayerState Authoritative { get; }
        public long PreviousPredictedTick { get; }
        public long PreviousAcceptedTick { get; }
        public long ReplayTick { get; }
        public int PendingBefore { get; }
        public int PendingAfter { get; }
        public PlayerInputCommand LastPendingCommand { get; }
        public bool HasPendingCommand { get; }
        public double SpeedMultiplier { get; }
    }

    /// <summary>Exact rejected validation operands; observer cannot change the decision.</summary>
    public readonly struct CommandRejectionDiagnostic
    {
        public CommandRejectionDiagnostic(long serverTick,
            PlayerInputCommand command, CommandRejectionReason reason,
            PlayerMovementState before, PlayerMovementState candidate,
            long lastAcceptedClientTick, int elapsedTicks,
            bool candidateIntegrated, double claimedTolerance,
            double speedMultiplier)
        {
            ServerTick = serverTick;
            Command = command;
            Reason = reason;
            Before = before;
            Candidate = candidate;
            LastAcceptedClientTick = lastAcceptedClientTick;
            ElapsedTicks = elapsedTicks;
            CandidateIntegrated = candidateIntegrated;
            ClaimedTolerance = claimedTolerance;
            SpeedMultiplier = speedMultiplier;
        }

        public long ServerTick { get; }
        public PlayerInputCommand Command { get; }
        public CommandRejectionReason Reason { get; }
        public PlayerMovementState Before { get; }
        public PlayerMovementState Candidate { get; }
        public long LastAcceptedClientTick { get; }
        public int ElapsedTicks { get; }
        public bool CandidateIntegrated { get; }
        public double ClaimedTolerance { get; }
        public double SpeedMultiplier { get; }
    }
}
