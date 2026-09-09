using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FPS.SaveGame
{
    /// <summary>Versioned data only: never retains scene objects or asset instances.</summary>
    [Serializable, DataContract]
    public sealed class RunSnapshot
    {
        public const int CurrentSchemaVersion = 2;
        [DataMember(Order = 0, IsRequired = true)] public int SchemaVersion = CurrentSchemaVersion;
        [DataMember(Order = 1, IsRequired = true)] public int Seed;
        [DataMember(Order = 2, IsRequired = true)] public float Health;
        [DataMember(Order = 3, IsRequired = true)] public float Armor;
        [DataMember(Order = 4, IsRequired = true)] public string CurrentWeaponId;
        [DataMember(Order = 5, IsRequired = true)] public List<WeaponAmmoSnapshot> Weapons = new List<WeaponAmmoSnapshot>();
        [DataMember(Order = 6, IsRequired = true)] public List<UpgradeLevelSnapshot> Upgrades = new List<UpgradeLevelSnapshot>();
        [DataMember(Order = 7, IsRequired = true)] public List<string> UpgradeSelectionHistory = new List<string>();
        [DataMember(Order = 8, IsRequired = true)] public List<InventorySlotSnapshot> InventorySlots = new List<InventorySlotSnapshot>();
        [DataMember(Order = 9, IsRequired = true)] public List<QuickSlotSnapshot> QuickSlots = new List<QuickSlotSnapshot>();
        [DataMember(Order = 10, IsRequired = true)] public int SelectedQuickSlotIndex = -1;
        [DataMember(Order = 11, IsRequired = true)] public float InventoryCooldownRemainingSeconds;
        [DataMember(Order = 12, IsRequired = true)] public MissionSnapshot Mission = new MissionSnapshot();
        [DataMember(Order = 13, IsRequired = true)] public WaveSnapshot Wave = new WaveSnapshot();
        [DataMember(Order = 14, IsRequired = true)] public List<EnemySnapshot> Enemies = new List<EnemySnapshot>();
        [DataMember(Order = 15, IsRequired = true)] public List<GameplayEffectSnapshot> PlayerEffects = new List<GameplayEffectSnapshot>();
        [DataMember(Order = 16, IsRequired = true)] public Float3Snapshot PlayerPosition = new Float3Snapshot();
        [DataMember(Order = 17, IsRequired = true)] public Float4Snapshot PlayerRotation = Float4Snapshot.Identity;
        [DataMember(Order = 18, IsRequired = true)] public float CameraPitch;
        [DataMember(Order = 19, IsRequired = true)] public bool PlayerCrouching;
        [DataMember(Order = 20, IsRequired = true)] public string Checksum;
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

    [Serializable, DataContract]
    public sealed class InventorySlotSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public int SlotIndex;
        [DataMember(Order = 1, IsRequired = true)] public string ItemId;
        [DataMember(Order = 2, IsRequired = true)] public int Quantity;
    }

    [Serializable, DataContract]
    public sealed class QuickSlotSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public int SlotIndex;
        [DataMember(Order = 1, IsRequired = true)] public string ItemId;
    }

    /// <summary>Integer phase values are mapped by the Unity-facing adapter.</summary>
    [Serializable, DataContract]
    public sealed class MissionSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public int Phase = 1;
        [DataMember(Order = 1, IsRequired = true)] public bool TerminalCompleted;
        [DataMember(Order = 2, IsRequired = true)] public float TerminalProgressNormalized;
        [DataMember(Order = 3, IsRequired = true)] public int EliminatedTargets;
        [DataMember(Order = 4, IsRequired = true)] public int RequiredTargets = 1;
        [DataMember(Order = 5, IsRequired = true)] public int ExperienceLevel = 1;
        [DataMember(Order = 6, IsRequired = true)] public int CurrentExperience;
        [DataMember(Order = 7, IsRequired = true)] public int TotalExperience;
        [DataMember(Order = 8, IsRequired = true)] public int LevelUpCount;
        [DataMember(Order = 9, IsRequired = true)] public int RewardedKillCount;
        [DataMember(Order = 10, IsRequired = true)] public int WaveRewardCount;
        [DataMember(Order = 11, IsRequired = true)] public int FinalRewardCount;
        [DataMember(Order = 12, IsRequired = true)] public bool FinalRewardRequested;
        [DataMember(Order = 13, IsRequired = true)] public int StatisticsKills;
        [DataMember(Order = 14, IsRequired = true)] public float StatisticsDamage;
        [DataMember(Order = 15, IsRequired = true)] public float ElapsedSeconds;
        [DataMember(Order = 16, IsRequired = true)] public List<int> ProgressionRewardedSpawnIds = new List<int>();
        [DataMember(Order = 17, IsRequired = true)] public bool ProgressionRunEnded;
        [DataMember(Order = 18, IsRequired = true)] public List<int> LootProcessedSpawnIds = new List<int>();
        [DataMember(Order = 19, IsRequired = true)] public List<int> LootRewardedWaves = new List<int>();
        [DataMember(Order = 20, IsRequired = true)] public int EnemyRewardCount;
        [DataMember(Order = 21, IsRequired = true)] public int SpawnedRewardStackCount;
        [DataMember(Order = 22, IsRequired = true)] public bool LootAcceptingRewards = true;
        [DataMember(Order = 23, IsRequired = true)] public bool HasLastDeathPosition;
        [DataMember(Order = 24, IsRequired = true)] public Float3Snapshot LastDeathPosition = new Float3Snapshot();
        [DataMember(Order = 25, IsRequired = true)] public int StatisticsShotsFired;
        [DataMember(Order = 26, IsRequired = true)] public int StatisticsHits;
        [DataMember(Order = 27, IsRequired = true)] public int StatisticsCompletedWaves;
        [DataMember(Order = 28, IsRequired = true)] public int StatisticsDamageTakenCount;
        [DataMember(Order = 29, IsRequired = true)] public bool StatisticsAuthoritativeKillTracking;
        [DataMember(Order = 30, IsRequired = true)] public List<int> StatisticsRewardedSpawnIds = new List<int>();
        [DataMember(Order = 31, IsRequired = true)] public int ExperienceToNextLevel;
    }

    /// <summary>Phase uses the numeric value of the runtime wave phase.</summary>
    [Serializable, DataContract]
    public sealed class WaveSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public int CurrentWave;
        [DataMember(Order = 1, IsRequired = true)] public int Phase;
        [DataMember(Order = 2, IsRequired = true)] public int TotalEnemyCount;
        [DataMember(Order = 3, IsRequired = true)] public int MaximumAliveCount;
        [DataMember(Order = 4, IsRequired = true)] public List<int> SpawnedIds = new List<int>();
        [DataMember(Order = 5, IsRequired = true)] public List<int> ActiveIds = new List<int>();
        [DataMember(Order = 6, IsRequired = true)] public List<int> SettledIds = new List<int>();
        [DataMember(Order = 7, IsRequired = true)] public float IntermissionRemaining;
        [DataMember(Order = 8, IsRequired = true)] public float SpawnCooldownRemaining;
        [DataMember(Order = 9, IsRequired = true)] public int NextSpawnId = 1;
        [DataMember(Order = 10, IsRequired = true)] public int RemainingThreatBudget;
    }

    [Serializable, DataContract]
    public sealed class EnemySnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public int WaveNumber;
        [DataMember(Order = 1, IsRequired = true)] public int SpawnId;
        [DataMember(Order = 2, IsRequired = true)] public string EnemyTypeId;
        [DataMember(Order = 3, IsRequired = true)] public Float3Snapshot Position = new Float3Snapshot();
        [DataMember(Order = 4, IsRequired = true)] public Float4Snapshot Rotation = Float4Snapshot.Identity;
        [DataMember(Order = 5, IsRequired = true)] public float Health;
        [DataMember(Order = 6, IsRequired = true)] public float Armor;
        [DataMember(Order = 7, IsRequired = true)] public int AwarenessState;
        [DataMember(Order = 8, IsRequired = true)] public int AttackState;
        [DataMember(Order = 9, IsRequired = true)] public List<GameplayEffectSnapshot> Effects = new List<GameplayEffectSnapshot>();
    }

    [Serializable, DataContract]
    public sealed class GameplayEffectSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public string EffectId;
        [DataMember(Order = 1, IsRequired = true)] public string SourceId;
        [DataMember(Order = 2, IsRequired = true)] public string SourceKey;
        [DataMember(Order = 3, IsRequired = true)] public int DurationPolicy;
        [DataMember(Order = 4, IsRequired = true)] public float TickRemaining;
        [DataMember(Order = 5, IsRequired = true)] public List<GameplayEffectStackSnapshot> Stacks = new List<GameplayEffectStackSnapshot>();
    }

    [Serializable, DataContract]
    public sealed class GameplayEffectStackSnapshot
    {
        [DataMember(Order = 0, IsRequired = true)] public string SourceId;
        [DataMember(Order = 1, IsRequired = true)] public string SourceKey;
        [DataMember(Order = 2, IsRequired = true)] public float RemainingDuration;
        [DataMember(Order = 3, IsRequired = true)] public long Order;
    }

    [Serializable, DataContract]
    public sealed class Float3Snapshot
    {
        public Float3Snapshot() { }
        public Float3Snapshot(float x, float y, float z) { X = x; Y = y; Z = z; }

        [DataMember(Order = 0, IsRequired = true)] public float X;
        [DataMember(Order = 1, IsRequired = true)] public float Y;
        [DataMember(Order = 2, IsRequired = true)] public float Z;
    }

    [Serializable, DataContract]
    public sealed class Float4Snapshot
    {
        public static Float4Snapshot Identity => new Float4Snapshot(0f, 0f, 0f, 1f);

        public Float4Snapshot() { W = 1f; }
        public Float4Snapshot(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        [DataMember(Order = 0, IsRequired = true)] public float X;
        [DataMember(Order = 1, IsRequired = true)] public float Y;
        [DataMember(Order = 2, IsRequired = true)] public float Z;
        [DataMember(Order = 3, IsRequired = true)] public float W;
    }
}
