using System;

public sealed class WeaponAmmoState
{
    public int MagazineCapacity { get; private set; }
    public int CurrentAmmo { get; private set; }
    public int ReserveAmmo { get; private set; }
    public bool IsReloading { get; private set; }
    public bool CanReload =>
        !IsReloading &&
        CurrentAmmo < MagazineCapacity &&
        ReserveAmmo > 0;

    private float reloadElapsed;

    public WeaponAmmoState(
        int magazineCapacity,
        int initialReserveAmmo)
    {
        MagazineCapacity = Math.Max(1, magazineCapacity);
        CurrentAmmo = MagazineCapacity;
        ReserveAmmo = Math.Max(0, initialReserveAmmo);
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
            ReserveAmmo += CurrentAmmo - nextAmmo;
        }

        MagazineCapacity = nextCapacity;
        CurrentAmmo = Math.Min(nextAmmo, MagazineCapacity);
        return true;
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
