using System;
using System.Collections.Generic;

namespace FPS.Networking.Domain
{
    public enum AuthoritativeWeaponAction : byte
    {
        Reload = 1,
        SwitchWeapon = 2
    }

    public enum AuthoritativeHitRegion : byte
    {
        None = 0,
        Body = 1,
        Head = 2
    }

    public enum AuthoritativeSurface : byte
    {
        None = 0,
        Concrete = 1,
        Metal = 2,
        Flesh = 3
    }

    public readonly struct AuthoritativeShotObstruction
    {
        public AuthoritativeShotObstruction(
            bool blocked,
            NetVector3 point,
            NetVector3 normal,
            AuthoritativeSurface surface)
        {
            Blocked = blocked;
            Point = point;
            Normal = normal;
            Surface = surface;
        }

        public bool Blocked { get; }
        public NetVector3 Point { get; }
        public NetVector3 Normal { get; }
        public AuthoritativeSurface Surface { get; }

        public static AuthoritativeShotObstruction Clear => new(
            false, default, default, AuthoritativeSurface.None);

        public static AuthoritativeShotObstruction At(
            NetVector3 point,
            NetVector3 normal,
            AuthoritativeSurface surface = AuthoritativeSurface.Concrete) =>
            new(true, point, normal, surface);
    }

    public sealed class AuthoritativeWeaponDefinition
    {
        public AuthoritativeWeaponDefinition(
            string weaponId,
            int magazineCapacity,
            int initialReserveAmmo,
            int fireIntervalTicks,
            int reloadDurationTicks,
            int switchDurationTicks,
            double hitscanRange,
            double baseDamage,
            double headDamageMultiplier,
            bool automatic)
        {
            WeaponId = string.IsNullOrWhiteSpace(weaponId)
                ? throw new ArgumentException("Weapon id is required.",
                    nameof(weaponId))
                : weaponId.Trim();
            if (magazineCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(magazineCapacity));
            if (initialReserveAmmo < 0)
                throw new ArgumentOutOfRangeException(nameof(initialReserveAmmo));
            if (fireIntervalTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(fireIntervalTicks));
            if (reloadDurationTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(reloadDurationTicks));
            if (switchDurationTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(switchDurationTicks));
            if (!PositiveFinite(hitscanRange))
                throw new ArgumentOutOfRangeException(nameof(hitscanRange));
            if (!PositiveFinite(baseDamage))
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            if (!PositiveFinite(headDamageMultiplier) ||
                headDamageMultiplier < 1d)
                throw new ArgumentOutOfRangeException(
                    nameof(headDamageMultiplier));

            MagazineCapacity = magazineCapacity;
            InitialReserveAmmo = initialReserveAmmo;
            FireIntervalTicks = fireIntervalTicks;
            ReloadDurationTicks = reloadDurationTicks;
            SwitchDurationTicks = switchDurationTicks;
            HitscanRange = hitscanRange;
            BaseDamage = baseDamage;
            HeadDamageMultiplier = headDamageMultiplier;
            Automatic = automatic;
        }

        public string WeaponId { get; }
        public int MagazineCapacity { get; }
        public int InitialReserveAmmo { get; }
        public int FireIntervalTicks { get; }
        public int ReloadDurationTicks { get; }
        public int SwitchDurationTicks { get; }
        public double HitscanRange { get; }
        public double BaseDamage { get; }
        public double HeadDamageMultiplier { get; }
        public bool Automatic { get; }

        public static IReadOnlyList<AuthoritativeWeaponDefinition>
            CreateProjectDefaults(CoopServerRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            int SecondsToTicks(double seconds) =>
                Math.Max(1, (int)Math.Ceiling(seconds * rules.TickRate));
            return new[]
            {
                new AuthoritativeWeaponDefinition(
                    "weapon.rifle",
                    50,
                    150,
                    rules.FireCooldownTicks,
                    SecondsToTicks(2.4d),
                    SecondsToTicks(0.56d),
                    rules.HitscanRange,
                    rules.ShotDamage,
                    2d,
                    automatic: true),
                new AuthoritativeWeaponDefinition(
                    "weapon.pistol",
                    12,
                    60,
                    SecondsToTicks(0.28d),
                    SecondsToTicks(2.2d),
                    SecondsToTicks(0.56d),
                    rules.HitscanRange,
                    20d,
                    2d,
                    automatic: false)
            };
        }

        private static bool PositiveFinite(double value) =>
            value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct AuthoritativeWeaponState
    {
        public AuthoritativeWeaponState(
            string weaponId,
            int magazineAmmo,
            int reserveAmmo,
            bool reloading,
            long reloadEndTick,
            long lastFireTick)
        {
            WeaponId = weaponId ?? string.Empty;
            MagazineAmmo = Math.Max(0, magazineAmmo);
            ReserveAmmo = Math.Max(0, reserveAmmo);
            Reloading = reloading;
            ReloadEndTick = reloadEndTick;
            LastFireTick = lastFireTick;
        }

        public string WeaponId { get; }
        public int MagazineAmmo { get; }
        public int ReserveAmmo { get; }
        public bool Reloading { get; }
        public long ReloadEndTick { get; }
        public long LastFireTick { get; }
    }

    public readonly struct WeaponActionResolution
    {
        public WeaponActionResolution(
            bool accepted,
            CommandRejectionReason rejectionReason,
            AuthoritativeWeaponAction action,
            string weaponId)
        {
            Accepted = accepted;
            RejectionReason = rejectionReason;
            Action = action;
            WeaponId = weaponId ?? string.Empty;
        }

        public bool Accepted { get; }
        public CommandRejectionReason RejectionReason { get; }
        public AuthoritativeWeaponAction Action { get; }
        public string WeaponId { get; }
    }
}
