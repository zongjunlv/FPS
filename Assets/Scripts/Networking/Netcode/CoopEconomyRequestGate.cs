using FPS.Networking.Domain;

namespace FPS.Networking.Netcode
{
    /// <summary>
    /// One outstanding intent per player prevents double-clicks and lets the UI
    /// distinguish a processed/rejected command from an accepted snapshot change.
    /// An acknowledgement alone is never interpreted as a successful settlement.
    /// </summary>
    public sealed class CoopEconomyRequestGate
    {
        private uint sequence;
        private int inventoryRevision;
        private int choiceGeneration;
        private int pendingChoices;
        private AuthoritativeEconomyCommandKind kind;

        public bool IsPending { get; private set; }
        public bool WasAccepted { get; private set; }
        public uint PendingSequence => IsPending ? sequence : 0;

        public bool Begin(NetcodeEconomyCommand command,
            NetcodeProgressionState baseline)
        {
            if (IsPending || command.Sequence == 0) return false;
            sequence = command.Sequence;
            kind = command.Kind;
            inventoryRevision = baseline.InventoryRevision;
            choiceGeneration = baseline.ChoiceGeneration;
            pendingChoices = baseline.PendingUpgradeChoices;
            WasAccepted = false;
            IsPending = true;
            return true;
        }

        public bool Observe(NetcodeProgressionState snapshot)
        {
            if (!IsPending ||
                snapshot.AcknowledgedEconomySequence < sequence) return false;
            WasAccepted = kind == AuthoritativeEconomyCommandKind.SelectUpgrade
                ? snapshot.ChoiceGeneration != choiceGeneration ||
                  snapshot.PendingUpgradeChoices < pendingChoices
                : snapshot.InventoryRevision != inventoryRevision;
            IsPending = false;
            return true;
        }

        public void Reset()
        {
            IsPending = false;
            WasAccepted = false;
            sequence = 0;
        }
    }
}
