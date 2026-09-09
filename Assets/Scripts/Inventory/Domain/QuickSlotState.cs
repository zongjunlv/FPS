using System;
using System.Collections.Generic;

[Serializable]
public sealed class QuickSlotSnapshot
{
    public List<string> Bindings { get; set; } = new();
}

public sealed class QuickSlotState
{
    private readonly string[] bindings;

    public QuickSlotState(int slotCount)
    {
        SlotCount = Math.Max(1, slotCount);
        bindings = new string[SlotCount];
    }

    public event Action Changed;

    public int SlotCount { get; }

    public string GetBoundId(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < bindings.Length
            ? bindings[slotIndex]
            : string.Empty;
    }

    public QuickSlotSnapshot CaptureSnapshot()
    {
        return new QuickSlotSnapshot
        {
            Bindings = new List<string>(bindings)
        };
    }

    public bool TryPrepareRestore(
        QuickSlotSnapshot snapshot,
        Func<string, bool> isKnownItem,
        out string[] preparedBindings)
    {
        preparedBindings = null;

        if (snapshot?.Bindings == null ||
            snapshot.Bindings.Count != SlotCount || isKnownItem == null)
        {
            return false;
        }

        var candidate = new string[SlotCount];

        for (int index = 0; index < SlotCount; index++)
        {
            string stableId = snapshot.Bindings[index]?.Trim() ?? string.Empty;

            if (stableId.Length > 0 && !isKnownItem(stableId))
            {
                return false;
            }

            candidate[index] = stableId;
        }

        preparedBindings = candidate;
        return true;
    }

    public bool RestorePrepared(IReadOnlyList<string> preparedBindings)
    {
        return RestorePrepared(preparedBindings, true, out _);
    }

    public bool RestorePrepared(
        IReadOnlyList<string> preparedBindings,
        bool publishChanged,
        out bool changed)
    {
        changed = false;

        if (preparedBindings == null || preparedBindings.Count != SlotCount)
        {
            return false;
        }

        for (int index = 0; index < SlotCount; index++)
        {
            string stableId = preparedBindings[index];

            if (stableId == null)
            {
                return false;
            }

            if (!string.Equals(
                    bindings[index],
                    stableId,
                    StringComparison.Ordinal))
            {
                changed = true;
            }
        }

        for (int index = 0; index < SlotCount; index++)
        {
            bindings[index] = preparedBindings[index];
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
        QuickSlotSnapshot snapshot,
        Func<string, bool> isKnownItem)
    {
        return TryPrepareRestore(snapshot, isKnownItem, out var prepared) &&
               RestorePrepared(prepared);
    }

    public bool Bind(int slotIndex, string stableId)
    {
        if (slotIndex < 0 || slotIndex >= bindings.Length ||
            string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        string normalized = stableId.Trim();

        if (string.Equals(
                bindings[slotIndex],
                normalized,
                StringComparison.Ordinal))
        {
            return false;
        }

        bindings[slotIndex] = normalized;
        Changed?.Invoke();
        return true;
    }

    public bool Clear(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= bindings.Length ||
            string.IsNullOrEmpty(bindings[slotIndex]))
        {
            return false;
        }

        bindings[slotIndex] = string.Empty;
        Changed?.Invoke();
        return true;
    }
}
