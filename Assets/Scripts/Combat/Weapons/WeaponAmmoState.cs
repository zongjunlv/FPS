using System;

public sealed class WeaponAmmoState
{
    public int MagazineCapacity { get; private set; }
    public int CurrentAmmo { get; private set; }
    public int ReserveAmmo { get; private set; }
    public int MaximumReserveAmmo { get; }
    public bool IsReloading { get; private set; }
    public bool CanReload =>
        !IsReloading &&
        CurrentAmmo < MagazineCapacity &&
        ReserveAmmo > 0;

    private float reloadElapsed;

    public WeaponAmmoState(
        int magazineCapacity,
        int initialReserveAmmo)
        : this(
            magazineCapacity,
            initialReserveAmmo,
            initialReserveAmmo)
    {
    }

    public WeaponAmmoState(
        int magazineCapacity,
        int initialReserveAmmo,
        int maximumReserveAmmo)
    {
        MagazineCapacity = Math.Max(1, magazineCapacity);
        CurrentAmmo = MagazineCapacity;
        MaximumReserveAmmo = Math.Max(
            0,
            Math.Max(initialReserveAmmo, maximumReserveAmmo));
        ReserveAmmo = Math.Min(
            Math.Max(0, initialReserveAmmo),
            MaximumReserveAmmo);
    }

    public bool TryConsumeRound()
    {
        if (IsReloading || CurrentAmmo <= 0)
        {
            return false;
        }

        CurrentAmmo--;
        return true;
    }

    public bool TryRestore(int magazine, int reserve)
    {
        if (magazine < 0 || magazine > MagazineCapacity ||
            reserve < 0 || reserve > MaximumReserveAmmo)
        {
            return false;
        }

        CancelReload();
        CurrentAmmo = magazine;
        ReserveAmmo = reserve;
        return true;
    }

    public bool SetMagazineCapacity(int magazineCapacity)
    {
        int nextCapacity = Math.Max(1, magazineCapacity);

        if (nextCapacity == MagazineCapacity)
        {
            return false;
        }

        int spentRounds = Math.Max(0, MagazineCapacity - CurrentAmmo);
        int nextAmmo = Math.Max(0, nextCapacity - spentRounds);

        if (nextAmmo < CurrentAmmo)
        {
            ReserveAmmo = Math.Min(
                MaximumReserveAmmo,
                ReserveAmmo + CurrentAmmo - nextAmmo);
        }

        MagazineCapacity = nextCapacity;
        CurrentAmmo = Math.Min(nextAmmo, MagazineCapacity);
        return true;
    }

    public int AddReserveAmmo(int requestedAmount)
    {
        if (requestedAmount <= 0 || ReserveAmmo >= MaximumReserveAmmo)
        {
            return 0;
        }

        int accepted = Math.Min(
            requestedAmount,
            MaximumReserveAmmo - ReserveAmmo);
        ReserveAmmo += accepted;
        return accepted;
    }

    public int AddMagazineAmmo(int requestedAmount)
    {
        if (requestedAmount <= 0 || CurrentAmmo >= MagazineCapacity)
        {
            return 0;
        }

        int accepted = Math.Min(
            requestedAmount,
            MagazineCapacity - CurrentAmmo);
        CurrentAmmo += accepted;
        return accepted;
    }

    public bool TryBeginReload()
    {
        if (!CanReload)
        {
            return false;
        }

        IsReloading = true;
        reloadElapsed = 0f;
        return true;
    }

    public bool AdvanceReload(
        float deltaTime,
        float reloadDuration)
    {
        if (!IsReloading)
        {
            return false;
        }

        reloadElapsed += Math.Max(0f, deltaTime);

        if (reloadElapsed < Math.Max(0.01f, reloadDuration))
        {
            return false;
        }

        CompleteReload();
        IsReloading = false;
        reloadElapsed = 0f;
        return true;
    }

    public bool CancelReload()
    {
        if (!IsReloading)
        {
            return false;
        }

        IsReloading = false;
        reloadElapsed = 0f;
        return true;
    }

    private void CompleteReload()
    {
        int missingRounds = MagazineCapacity - CurrentAmmo;
        int transferredRounds = Math.Min(missingRounds, ReserveAmmo);

        CurrentAmmo += transferredRounds;
        ReserveAmmo -= transferredRounds;
    }
}
