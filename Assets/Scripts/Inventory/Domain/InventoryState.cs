using System;
using System.Collections.Generic;

public readonly struct InventoryItemSpec
{
    public InventoryItemSpec(string stableId, int maximumStack)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException(
                "An inventory item requires a stable ID.",
                nameof(stableId));
        }

        StableId = stableId.Trim();
        MaximumStack = Math.Max(1, maximumStack);
    }

    public string StableId { get; }
    public int MaximumStack { get; }
}

public readonly struct InventorySlot
{
    public InventorySlot(string stableId, int maximumStack, int quantity)
    {
        StableId = stableId;
        MaximumStack = Math.Max(1, maximumStack);
        Quantity = Math.Max(0, quantity);
    }

    public string StableId { get; }
    public int MaximumStack { get; }
    public int Quantity { get; }
    public bool IsEmpty => string.IsNullOrEmpty(StableId) || Quantity <= 0;
}

[Serializable]
public sealed class InventorySlotSnapshot
{
    public string StableId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

[Serializable]
public sealed class InventorySnapshot
{
    public List<InventorySlotSnapshot> Slots { get; set; } = new();
}

public readonly struct InventoryAddResult
{
    public InventoryAddResult(int requested, int accepted)
    {
        Requested = Math.Max(0, requested);
        Accepted = Math.Max(0, Math.Min(Requested, accepted));
    }

    public int Requested { get; }
    public int Accepted { get; }
    public int Remaining => Requested - Accepted;
    public bool Changed => Accepted > 0;
}

public enum InventoryOperationKind
{
    None,
    Move,
    Swap,
    Merge,
    Split,
    Extract,
    Compact
}

public enum InventoryOperationFailure
{
    None,
    InvalidIndex,
    SameSlot,
    EmptySource,
    EmptyDestination,
    DestinationNotEmpty,
    ItemMismatch,
    DestinationFull,
    InvalidQuantity,
    QuantityExceedsSource,
    NoChange
}

public readonly struct InventoryOperationResult
{
    public InventoryOperationResult(
        bool succeeded,
        InventoryOperationKind kind,
        InventoryOperationFailure failure,
        int transferredQuantity)
    {
        Succeeded = succeeded;
        Kind = kind;
        Failure = failure;
        TransferredQuantity = Math.Max(0, transferredQuantity);
    }

    public bool Succeeded { get; }
    public InventoryOperationKind Kind { get; }
    public InventoryOperationFailure Failure { get; }
    public int TransferredQuantity { get; }

    public static InventoryOperationResult Success(
        InventoryOperationKind kind,
        int quantity = 0)
    {
        return new InventoryOperationResult(
            true,
            kind,
            InventoryOperationFailure.None,
            quantity);
    }

    public static InventoryOperationResult Failed(
        InventoryOperationFailure failure)
    {
        return new InventoryOperationResult(
            false,
            InventoryOperationKind.None,
            failure,
            0);
    }
}

public sealed class InventoryState
{
    private readonly List<InventorySlot> slots;
    private readonly IReadOnlyList<InventorySlot> readOnlySlots;
    private bool transactionActive;

    public InventoryState(int capacity)
    {
        Capacity = Math.Max(1, capacity);
        slots = new List<InventorySlot>(Capacity);
        readOnlySlots = slots.AsReadOnly();

        for (int index = 0; index < Capacity; index++)
        {
            slots.Add(default);
        }
    }

    public event Action Changed;

    public int Capacity { get; }
    public IReadOnlyList<InventorySlot> Slots => readOnlySlots;
    public bool IsTransactionActive => transactionActive;
    public int OccupiedSlotCount
    {
        get
        {
            int count = 0;

            for (int index = 0; index < slots.Count; index++)
            {
                if (!slots[index].IsEmpty)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public InventorySlot GetSlot(int index)
    {
        return index >= 0 && index < slots.Count
            ? slots[index]
            : default;
    }

    public InventorySnapshot CaptureSnapshot()
    {
        var snapshot = new InventorySnapshot
        {
            Slots = new List<InventorySlotSnapshot>(slots.Count)
        };

        for (int index = 0; index < slots.Count; index++)
        {
            InventorySlot slot = slots[index];
            snapshot.Slots.Add(new InventorySlotSnapshot
            {
                StableId = slot.IsEmpty ? string.Empty : slot.StableId,
                Quantity = slot.IsEmpty ? 0 : slot.Quantity
            });
        }

        return snapshot;
    }

    public bool TryPrepareRestore(
        InventorySnapshot snapshot,
        Func<string, InventoryItemSpec?> resolveItem,
        out InventorySlot[] preparedSlots)
    {
        preparedSlots = null;

        if (transactionActive || snapshot?.Slots == null ||
            snapshot.Slots.Count != Capacity || resolveItem == null)
        {
            return false;
        }

        var candidate = new InventorySlot[Capacity];

        for (int index = 0; index < Capacity; index++)
        {
            InventorySlotSnapshot savedSlot = snapshot.Slots[index];

            if (savedSlot == null)
            {
                return false;
            }

            string stableId = savedSlot.StableId?.Trim() ?? string.Empty;

            if (stableId.Length == 0)
            {
                if (savedSlot.Quantity != 0)
                {
                    return false;
                }

                candidate[index] = default;
                continue;
            }

            InventoryItemSpec? resolved = resolveItem(stableId);

            if (!resolved.HasValue || savedSlot.Quantity <= 0 ||
                savedSlot.Quantity > resolved.Value.MaximumStack ||
                !string.Equals(
                    resolved.Value.StableId,
                    stableId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            candidate[index] = new InventorySlot(
                stableId,
                resolved.Value.MaximumStack,
                savedSlot.Quantity);
        }

        preparedSlots = candidate;
        return true;
    }

    public bool RestorePrepared(IReadOnlyList<InventorySlot> preparedSlots)
    {
        return RestorePrepared(preparedSlots, true, out _);
    }

    public bool RestorePrepared(
        IReadOnlyList<InventorySlot> preparedSlots,
        bool publishChanged,
        out bool changed)
    {
        changed = false;

        if (transactionActive || preparedSlots == null ||
            preparedSlots.Count != Capacity)
        {
            return false;
        }

        for (int index = 0; index < Capacity; index++)
        {
            InventorySlot slot = preparedSlots[index];

            if ((!slot.IsEmpty &&
                 (string.IsNullOrWhiteSpace(slot.StableId) ||
                  slot.Quantity <= 0 ||
                  slot.Quantity > slot.MaximumStack)) ||
                (slot.IsEmpty && slot.Quantity != 0))
            {
                return false;
            }
        }

        for (int index = 0; index < Capacity; index++)
        {
            if (!SlotsEqual(slots[index], preparedSlots[index]))
            {
                changed = true;
                break;
            }
        }

        for (int index = 0; index < Capacity; index++)
        {
            slots[index] = preparedSlots[index];
        }

        if (changed && publishChanged)
        {
            Changed?.Invoke();
        }

        return true;
    }

    public void PublishRestoreChanged()
    {
        Changed?.Invoke();
    }

    public bool TryRestore(
        InventorySnapshot snapshot,
        Func<string, InventoryItemSpec?> resolveItem)
    {
        return TryPrepareRestore(snapshot, resolveItem, out var preparedSlots) &&
               RestorePrepared(preparedSlots);
    }

    public int GetQuantity(string stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return 0;
        }

        int quantity = 0;

        for (int index = 0; index < slots.Count; index++)
        {
            InventorySlot slot = slots[index];

            if (!slot.IsEmpty && string.Equals(
                    slot.StableId,
                    stableId,
                    StringComparison.Ordinal))
            {
                quantity += slot.Quantity;
            }
        }

        return quantity;
    }

    public bool CanAdd(InventoryItemSpec item, int quantity = 1)
    {
        if (string.IsNullOrWhiteSpace(item.StableId) ||
            quantity <= 0)
        {
            return false;
        }

        int remainingCapacity = GetAvailableCapacity(item, out bool valid);
        return valid && remainingCapacity >= quantity;
    }

    public InventoryAddResult Add(
        InventoryItemSpec item,
        int quantity = 1)
    {
        if (transactionActive ||
            string.IsNullOrWhiteSpace(item.StableId) || quantity <= 0)
        {
            return new InventoryAddResult(quantity, 0);
        }

        int capacity = GetAvailableCapacity(item, out bool valid);

        if (!valid || capacity <= 0)
        {
            return new InventoryAddResult(quantity, 0);
        }

        int accepted = Math.Min(quantity, capacity);
        ApplyAdd(item, accepted);
        Changed?.Invoke();
        return new InventoryAddResult(quantity, accepted);
    }

    private int GetAvailableCapacity(
        InventoryItemSpec item,
        out bool valid)
    {
        valid = true;
        long remainingCapacity = 0;

        for (int index = 0; index < slots.Count; index++)
        {
            InventorySlot slot = slots[index];

            if (slot.IsEmpty)
            {
                remainingCapacity += item.MaximumStack;
            }
            else if (string.Equals(
                         slot.StableId,
                         item.StableId,
                         StringComparison.Ordinal))
            {
                if (slot.MaximumStack != item.MaximumStack)
                {
                    valid = false;
                    return 0;
                }

                remainingCapacity += Math.Max(
                    0,
                    item.MaximumStack - slot.Quantity);
            }

            remainingCapacity = Math.Min(int.MaxValue, remainingCapacity);
        }

        return (int)remainingCapacity;
    }

    public bool TryAdd(InventoryItemSpec item, int quantity = 1)
    {
        if (transactionActive || !CanAdd(item, quantity))
        {
            return false;
        }

        ApplyAdd(item, quantity);
        Changed?.Invoke();
        return true;
    }

    private void ApplyAdd(InventoryItemSpec item, int quantity)
    {
        int remaining = quantity;

        for (int index = 0; index < slots.Count && remaining > 0; index++)
        {
            InventorySlot slot = slots[index];

            if (slot.IsEmpty || !string.Equals(
                    slot.StableId,
                    item.StableId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            int added = Math.Min(
                remaining,
                item.MaximumStack - slot.Quantity);
            slots[index] = new InventorySlot(
                item.StableId,
                item.MaximumStack,
                slot.Quantity + added);
            remaining -= added;
        }

        for (int index = 0; index < slots.Count && remaining > 0; index++)
        {
            if (!slots[index].IsEmpty)
            {
                continue;
            }

            int added = Math.Min(remaining, item.MaximumStack);
            slots[index] = new InventorySlot(
                item.StableId,
                item.MaximumStack,
                added);
            remaining -= added;
        }

    }

    public bool TryRemove(string stableId, int quantity = 1)
    {
        if (transactionActive || quantity <= 0 ||
            GetQuantity(stableId) < quantity)
        {
            return false;
        }

        int remaining = quantity;

        for (int index = slots.Count - 1; index >= 0 && remaining > 0; index--)
        {
            InventorySlot slot = slots[index];

            if (slot.IsEmpty || !string.Equals(
                    slot.StableId,
                    stableId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            int removed = Math.Min(remaining, slot.Quantity);
            int nextQuantity = slot.Quantity - removed;
            slots[index] = nextQuantity > 0
                ? new InventorySlot(
                    slot.StableId,
                    slot.MaximumStack,
                    nextQuantity)
                : default;
            remaining -= removed;
        }

        Changed?.Invoke();
        return true;
    }

    public bool TryRemoveAt(int slotIndex, int quantity = 1)
    {
        return TryExtractAt(slotIndex, quantity, out _);
    }

    public InventoryOperationResult Move(
        int sourceIndex,
        int destinationIndex)
    {
        if (transactionActive)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        InventoryOperationFailure validation = ValidatePair(
            sourceIndex,
            destinationIndex,
            out InventorySlot source,
            out InventorySlot destination);

        if (validation != InventoryOperationFailure.None)
        {
            return InventoryOperationResult.Failed(validation);
        }

        if (!destination.IsEmpty)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.DestinationNotEmpty);
        }

        slots[destinationIndex] = source;
        slots[sourceIndex] = default;
        Changed?.Invoke();
        return InventoryOperationResult.Success(
            InventoryOperationKind.Move,
            source.Quantity);
    }

    public InventoryOperationResult Swap(
        int leftIndex,
        int rightIndex)
    {
        if (transactionActive)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        InventoryOperationFailure validation = ValidatePair(
            leftIndex,
            rightIndex,
            out InventorySlot left,
            out InventorySlot right);

        if (validation != InventoryOperationFailure.None)
        {
            return InventoryOperationResult.Failed(validation);
        }

        if (right.IsEmpty)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.EmptyDestination);
        }

        if (SlotsEqual(left, right))
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        slots[leftIndex] = right;
        slots[rightIndex] = left;
        Changed?.Invoke();
        return InventoryOperationResult.Success(
            InventoryOperationKind.Swap);
    }

    public InventoryOperationResult Merge(
        int sourceIndex,
        int destinationIndex)
    {
        if (transactionActive)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        InventoryOperationFailure validation = ValidatePair(
            sourceIndex,
            destinationIndex,
            out InventorySlot source,
            out InventorySlot destination);

        if (validation != InventoryOperationFailure.None)
        {
            return InventoryOperationResult.Failed(validation);
        }

        if (destination.IsEmpty)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.EmptyDestination);
        }

        if (!AreSameItem(source, destination))
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.ItemMismatch);
        }

        int available = destination.MaximumStack - destination.Quantity;

        if (available <= 0)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.DestinationFull);
        }

        int transferred = Math.Min(source.Quantity, available);
        int sourceRemaining = source.Quantity - transferred;
        slots[sourceIndex] = sourceRemaining > 0
            ? new InventorySlot(
                source.StableId,
                source.MaximumStack,
                sourceRemaining)
            : default;
        slots[destinationIndex] = new InventorySlot(
            destination.StableId,
            destination.MaximumStack,
            destination.Quantity + transferred);
        Changed?.Invoke();
        return InventoryOperationResult.Success(
            InventoryOperationKind.Merge,
            transferred);
    }

    public InventoryOperationResult Split(
        int sourceIndex,
        int destinationIndex,
        int quantity)
    {
        if (transactionActive)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        InventoryOperationFailure validation = ValidatePair(
            sourceIndex,
            destinationIndex,
            out InventorySlot source,
            out InventorySlot destination);

        if (validation != InventoryOperationFailure.None)
        {
            return InventoryOperationResult.Failed(validation);
        }

        if (quantity <= 0)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.InvalidQuantity);
        }

        if (quantity >= source.Quantity)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.QuantityExceedsSource);
        }

        if (!destination.IsEmpty)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.DestinationNotEmpty);
        }

        slots[sourceIndex] = new InventorySlot(
            source.StableId,
            source.MaximumStack,
            source.Quantity - quantity);
        slots[destinationIndex] = new InventorySlot(
            source.StableId,
            source.MaximumStack,
            quantity);
        Changed?.Invoke();
        return InventoryOperationResult.Success(
            InventoryOperationKind.Split,
            quantity);
    }

    public InventoryOperationResult Transfer(
        int sourceIndex,
        int destinationIndex)
    {
        if (transactionActive)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        if (!IsValidIndex(sourceIndex) || !IsValidIndex(destinationIndex))
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.InvalidIndex);
        }

        if (sourceIndex == destinationIndex)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.SameSlot);
        }

        InventorySlot source = slots[sourceIndex];
        InventorySlot destination = slots[destinationIndex];

        if (source.IsEmpty)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.EmptySource);
        }

        if (destination.IsEmpty)
        {
            return Move(sourceIndex, destinationIndex);
        }

        return AreSameItem(source, destination)
            ? Merge(sourceIndex, destinationIndex)
            : Swap(sourceIndex, destinationIndex);
    }

    public InventoryOperationResult Compact()
    {
        if (transactionActive)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        int destinationIndex = 0;
        int movedStacks = 0;

        for (int sourceIndex = 0; sourceIndex < slots.Count; sourceIndex++)
        {
            InventorySlot slot = slots[sourceIndex];

            if (slot.IsEmpty)
            {
                continue;
            }

            if (sourceIndex != destinationIndex)
            {
                slots[destinationIndex] = slot;
                slots[sourceIndex] = default;
                movedStacks++;
            }

            destinationIndex++;
        }

        if (movedStacks <= 0)
        {
            return InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);
        }

        Changed?.Invoke();
        return InventoryOperationResult.Success(
            InventoryOperationKind.Compact,
            movedStacks);
    }

    public bool TryExtractAt(
        int slotIndex,
        int quantity,
        out InventorySlot extracted)
    {
        extracted = default;

        if (transactionActive || !IsValidIndex(slotIndex) || quantity <= 0)
        {
            return false;
        }

        InventorySlot slot = slots[slotIndex];

        if (slot.IsEmpty || slot.Quantity < quantity)
        {
            return false;
        }

        extracted = new InventorySlot(
            slot.StableId,
            slot.MaximumStack,
            quantity);
        int nextQuantity = slot.Quantity - quantity;
        slots[slotIndex] = nextQuantity > 0
            ? new InventorySlot(
                slot.StableId,
                slot.MaximumStack,
                nextQuantity)
            : default;
        Changed?.Invoke();
        return true;
    }

    public bool TryConsumeAt(
        int slotIndex,
        int quantity,
        Func<bool> commitEffect)
    {
        if (transactionActive || commitEffect == null ||
            !IsValidIndex(slotIndex) || quantity <= 0)
        {
            return false;
        }

        InventorySlot original = slots[slotIndex];

        if (original.IsEmpty || original.Quantity < quantity)
        {
            return false;
        }

        int nextQuantity = original.Quantity - quantity;
        slots[slotIndex] = nextQuantity > 0
            ? new InventorySlot(
                original.StableId,
                original.MaximumStack,
                nextQuantity)
            : default;
        transactionActive = true;
        bool committed = false;

        try
        {
            committed = commitEffect();
        }
        finally
        {
            if (!committed)
            {
                slots[slotIndex] = original;
            }

            transactionActive = false;
        }

        if (!committed)
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    private InventoryOperationFailure ValidatePair(
        int sourceIndex,
        int destinationIndex,
        out InventorySlot source,
        out InventorySlot destination)
    {
        source = default;
        destination = default;

        if (!IsValidIndex(sourceIndex) || !IsValidIndex(destinationIndex))
        {
            return InventoryOperationFailure.InvalidIndex;
        }

        if (sourceIndex == destinationIndex)
        {
            return InventoryOperationFailure.SameSlot;
        }

        source = slots[sourceIndex];
        destination = slots[destinationIndex];
        return source.IsEmpty
            ? InventoryOperationFailure.EmptySource
            : InventoryOperationFailure.None;
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < slots.Count;
    }

    private static bool AreSameItem(
        InventorySlot left,
        InventorySlot right)
    {
        return string.Equals(
                   left.StableId,
                   right.StableId,
                   StringComparison.Ordinal) &&
               left.MaximumStack == right.MaximumStack;
    }

    private static bool SlotsEqual(
        InventorySlot left,
        InventorySlot right)
    {
        return AreSameItem(left, right) && left.Quantity == right.Quantity;
    }
}
