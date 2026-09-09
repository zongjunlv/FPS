using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FPS.SaveGame
{
    /// <summary>Versioned data only: never retains scene objects or asset instances.</summary>
    [Serializable, DataContract]
    public sealed class RunSnapshot
    {
        public const int CurrentSchemaVersion = 1;
        [DataMember(Order = 0, IsRequired = true)] public int SchemaVersion = CurrentSchemaVersion;
        [DataMember(Order = 1, IsRequired = true)] public int Seed;
        [DataMember(Order = 2, IsRequired = true)] public float Health;
        [DataMember(Order = 3, IsRequired = true)] public float Armor;
        [DataMember(Order = 4, IsRequired = true)] public string CurrentWeaponId;
        [DataMember(Order = 5, IsRequired = true)] public List<WeaponAmmoSnapshot> Weapons = new List<WeaponAmmoSnapshot>();
        [DataMember(Order = 6, IsRequired = true)] public List<UpgradeLevelSnapshot> Upgrades = new List<UpgradeLevelSnapshot>();
        [DataMember(Order = 7, IsRequired = true)] public List<string> UpgradeSelectionHistory = new List<string>();
        [DataMember(Order = 8, IsRequired = true)] public string Checksum;
    }

    [Serializable, DataContract]
    public sealed class WeaponAmmoSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public string WeaponId;
        [DataMember(Order = 1, IsRequired = true)] public int Magazine;
        [DataMember(Order = 2, IsRequired = true)] public int Reserve;
    }

    [Serializable, DataContract]
    public sealed class UpgradeLevelSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public string UpgradeId;
        [DataMember(Order = 1, IsRequired = true)] public int Level;
    }
}
