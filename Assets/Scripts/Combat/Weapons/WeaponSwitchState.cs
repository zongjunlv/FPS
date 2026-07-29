using System;

public sealed class WeaponSwitchState
{
    public int CurrentIndex { get; private set; }
    public int PendingIndex { get; private set; }
    public bool IsSwitching { get; private set; }
    public float Elapsed { get; private set; }

    private readonly int weaponCount;
    public WeaponSwitchState(int weaponCount, int initialIndex)
    {
        this.weaponCount = Math.Max(1, weaponCount);
        CurrentIndex = Math.Clamp(
            initialIndex,
            0,
            this.weaponCount - 1);
        PendingIndex = CurrentIndex;
    }

    public bool TryBeginSwitch(int targetIndex)
    {
        if (targetIndex < 0 ||
            targetIndex >= weaponCount ||
            targetIndex == CurrentIndex)
        {
            return false;
        }

        PendingIndex = targetIndex;
        Elapsed = 0f;
        IsSwitching = true;
        return true;
    }

    public bool Advance(float deltaTime, float switchDuration)
    {
        if (!IsSwitching)
        {
            return false;
        }

        Elapsed += Math.Max(0f, deltaTime);

        if (Elapsed < Math.Max(0.01f, switchDuration))
        {
            return false;
        }

        CurrentIndex = PendingIndex;
        IsSwitching = false;
        Elapsed = 0f;
        return true;
    }

    public bool Interrupt()
    {
        if (!IsSwitching)
        {
            return false;
        }

        PendingIndex = CurrentIndex;
        Elapsed = 0f;
        IsSwitching = false;
        return true;
    }
}
