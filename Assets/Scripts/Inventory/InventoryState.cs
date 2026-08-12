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

public sealed class InventoryState
{
    private readonly List<InventorySlot> slots;
    private readonly IReadOnlyList<InventorySlot> readOnlySlots;

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

        int remainingCapacity = 0;

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
                    return false;
                }

                remainingCapacity += Math.Max(
                    0,
                    item.MaximumStack - slot.Quantity);
            }

            if (remainingCapacity >= quantity)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAdd(InventoryItemSpec item, int quantity = 1)
    {
        if (!CanAdd(item, quantity))
        {
            return false;
        }

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

        Changed?.Invoke();
        return true;
    }

    public bool TryRemove(string stableId, int quantity = 1)
    {
        if (quantity <= 0 || GetQuantity(stableId) < quantity)
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
        if (slotIndex < 0 || slotIndex >= slots.Count || quantity <= 0)
        {
            return false;
        }

        InventorySlot slot = slots[slotIndex];

        if (slot.IsEmpty || slot.Quantity < quantity)
        {
            return false;
        }

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
}
