using System;

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
