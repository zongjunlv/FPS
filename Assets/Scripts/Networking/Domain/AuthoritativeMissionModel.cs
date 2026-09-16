using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public enum AuthoritativeMissionPhase : byte
    {
        ClearEnemies = 0,
        ActivateTerminal = 1,
        Extraction = 2,
        Victory = 3,
        Defeat = 4
    }

    public enum AuthoritativeMissionOutcomeReason : byte
    {
        None = 0,
        Extracted = 1,
        SquadWiped = 2,
        AllPlayersLeft = 3
    }

    public enum AuthoritativePlayerLifeState : byte
    {
        Alive = 0,
        Downed = 1,
        Disconnected = 2
    }

    public enum AuthoritativeMissionCommandKind : byte
    {
        HoldTerminal = 0,
        HoldRevive = 1,
        StartExtraction = 2
    }

    public enum AuthoritativeMissionRejection : byte
    {
        None = 0,
        UnknownPlayer = 1,
        InvalidSequence = 2,
        DuplicateNonce = 3,
        PlayerUnavailable = 4,
        WrongPhase = 5,
        OutOfRange = 6,
        InvalidTarget = 7,
        MatchEnded = 8
    }

    public sealed class AuthoritativeMissionDefinition
    {
        public AuthoritativeMissionDefinition(
            NetVector3 terminalPosition,
            NetVector3 extractionPosition,
            double terminalRadius = 3d,
            double extractionRadius = 5d,
            double reviveRadius = 2.5d,
            int terminalHoldTicks = 90,
            int extractionHoldTicks = 120,
            int reviveHoldTicks = 90,
            double revivedHealth = 35d)
        {
            if (!terminalPosition.IsFinite)
                throw new ArgumentOutOfRangeException(nameof(terminalPosition));
            if (!extractionPosition.IsFinite)
                throw new ArgumentOutOfRangeException(nameof(extractionPosition));
            if (!FinitePositive(terminalRadius) ||
                !FinitePositive(extractionRadius) ||
                !FinitePositive(reviveRadius) ||
                !FinitePositive(revivedHealth))
                throw new ArgumentOutOfRangeException(
                    "Mission radii and revived health must be positive.");
            if (terminalHoldTicks < 1 || extractionHoldTicks < 1 ||
                reviveHoldTicks < 1)
                throw new ArgumentOutOfRangeException(
                    "Mission hold durations must be at least one tick.");
            TerminalPosition = terminalPosition;
            ExtractionPosition = extractionPosition;
            TerminalRadius = terminalRadius;
            ExtractionRadius = extractionRadius;
            ReviveRadius = reviveRadius;
            TerminalHoldTicks = terminalHoldTicks;
            ExtractionHoldTicks = extractionHoldTicks;
            ReviveHoldTicks = reviveHoldTicks;
            RevivedHealth = revivedHealth;
        }

        public NetVector3 TerminalPosition { get; }
        public NetVector3 ExtractionPosition { get; }
        public double TerminalRadius { get; }
        public double ExtractionRadius { get; }
        public double ReviveRadius { get; }
        public int TerminalHoldTicks { get; }
        public int ExtractionHoldTicks { get; }
        public int ReviveHoldTicks { get; }
        public double RevivedHealth { get; }

        public static AuthoritativeMissionDefinition Default { get; } = new(
            new NetVector3(33.5d, 0.16d, 67.8d),
            new NetVector3(48.414d, 0.05d, 41.41d));

        private static bool FinitePositive(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    }

    public readonly struct AuthoritativeMissionCommand
    {
        public AuthoritativeMissionCommand(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeMissionCommandKind kind,
            int targetPlayerId = 0)
        {
            PlayerId = playerId;
            Sequence = sequence;
            Nonce = nonce;
            Kind = kind;
            TargetPlayerId = targetPlayerId;
        }

        public int PlayerId { get; }
        public uint Sequence { get; }
        public ulong Nonce { get; }
        public AuthoritativeMissionCommandKind Kind { get; }
        public int TargetPlayerId { get; }
    }

    public readonly struct AuthoritativeMissionResolution
    {
        public AuthoritativeMissionResolution(
            AuthoritativeMissionCommand command,
            bool accepted,
            AuthoritativeMissionRejection rejection)
        {
            Command = command;
            Accepted = accepted;
            Rejection = rejection;
        }

        public AuthoritativeMissionCommand Command { get; }
        public bool Accepted { get; }
        public AuthoritativeMissionRejection Rejection { get; }
    }

    public readonly struct AuthoritativePlayerMissionStats
    {
        public AuthoritativePlayerMissionStats(
            int playerId,
            int kills,
            double damageDealt,
            double damageTaken,
            int upgradesSelected)
        {
            PlayerId = playerId;
            Kills = Math.Max(0, kills);
            DamageDealt = Math.Max(0d, damageDealt);
            DamageTaken = Math.Max(0d, damageTaken);
            UpgradesSelected = Math.Max(0, upgradesSelected);
        }

        public int PlayerId { get; }
        public int Kills { get; }
        public double DamageDealt { get; }
        public double DamageTaken { get; }
        public int UpgradesSelected { get; }
    }

    public sealed class AuthoritativeMissionState
    {
        private readonly AuthoritativePlayerMissionStats[] statistics;

        public AuthoritativeMissionState(
            AuthoritativeMissionPhase phase,
            AuthoritativeMissionOutcomeReason outcomeReason,
            int revision,
            int terminalProgressTicks,
            int extractionProgressTicks,
            int reviveProgressTicks,
            int terminalPlayerId,
            int revivePlayerId,
            int downedPlayerId,
            AuthoritativeMissionDefinition definition,
            IEnumerable<AuthoritativePlayerMissionStats> statistics)
        {
            Phase = phase;
            OutcomeReason = outcomeReason;
            Revision = Math.Max(1, revision);
            TerminalProgressTicks = Math.Max(0, terminalProgressTicks);
            ExtractionProgressTicks = Math.Max(0, extractionProgressTicks);
            ReviveProgressTicks = Math.Max(0, reviveProgressTicks);
            TerminalPlayerId = Math.Max(0, terminalPlayerId);
            RevivePlayerId = Math.Max(0, revivePlayerId);
            DownedPlayerId = Math.Max(0, downedPlayerId);
            Definition = definition ?? throw new ArgumentNullException(
                nameof(definition));
            this.statistics = (statistics ??
                Array.Empty<AuthoritativePlayerMissionStats>())
                .OrderBy(value => value.PlayerId).ToArray();
        }

        public AuthoritativeMissionPhase Phase { get; }
        public AuthoritativeMissionOutcomeReason OutcomeReason { get; }
        public int Revision { get; }
        public int TerminalProgressTicks { get; }
        public int ExtractionProgressTicks { get; }
        public int ReviveProgressTicks { get; }
        public int TerminalPlayerId { get; }
        public int RevivePlayerId { get; }
        public int DownedPlayerId { get; }
        public AuthoritativeMissionDefinition Definition { get; }
        public IReadOnlyList<AuthoritativePlayerMissionStats> Statistics =>
            statistics;
        public bool IsOutcome => Phase == AuthoritativeMissionPhase.Victory ||
            Phase == AuthoritativeMissionPhase.Defeat;
        public double TerminalProgress => Math.Min(1d,
            (double)TerminalProgressTicks / Definition.TerminalHoldTicks);
        public double ExtractionProgress => Math.Min(1d,
            (double)ExtractionProgressTicks / Definition.ExtractionHoldTicks);
        public double ReviveProgress => Math.Min(1d,
            (double)ReviveProgressTicks / Definition.ReviveHoldTicks);
        public AuthoritativePlayerMissionStats Player(int playerId) =>
            statistics.Single(value => value.PlayerId == playerId);
    }
}
