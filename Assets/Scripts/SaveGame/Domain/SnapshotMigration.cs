using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace FPS.SaveGame
{
    /// <summary>Result of decoding a snapshot, including migration metadata.</summary>
    public sealed class SnapshotDecodeResult
    {
        public bool Success { get; }
        public RunSnapshot Snapshot { get; }
        public int OriginalSchemaVersion { get; }
        public bool WasMigrated => Success && OriginalSchemaVersion != RunSnapshot.CurrentSchemaVersion;
        public string Error { get; }

        private SnapshotDecodeResult(bool success, RunSnapshot snapshot, int originalSchemaVersion, string error)
        {
            Success = success;
            Snapshot = snapshot;
            OriginalSchemaVersion = originalSchemaVersion;
            Error = error;
        }

        public static SnapshotDecodeResult Succeeded(RunSnapshot snapshot, int originalSchemaVersion) =>
            new SnapshotDecodeResult(true, snapshot, originalSchemaVersion, null);

        public static SnapshotDecodeResult Failed(string error, int originalSchemaVersion = 0) =>
            new SnapshotDecodeResult(false, null, originalSchemaVersion, error);
    }

    /// <summary>
    /// Owns historical wire formats. Old DTOs must remain frozen so migrations do not
    /// silently change when the current runtime snapshot grows new fields.
    /// </summary>
    internal static class SnapshotMigration
    {
        public const int OldestSupportedVersion = 1;

        public static SnapshotDecodeResult Decode(string json)
        {
            int version;
            try
            {
                version = ReadVersion(json);
            }
            catch (Exception exception) when (IsSerializationError(exception))
            {
                return SnapshotDecodeResult.Failed("无法解析存档：" + exception.Message);
            }

            if (version < OldestSupportedVersion || version > RunSnapshot.CurrentSchemaVersion)
                return SnapshotDecodeResult.Failed("不支持的存档版本：" + version, version);

            return version == RunSnapshot.CurrentSchemaVersion
                ? DecodeCurrent(json, version)
                : DecodeV1(json);
        }

        private static SnapshotDecodeResult DecodeCurrent(string json, int version)
        {
            try
            {
                var loaded = Deserialize<RunSnapshot>(json);
                if (!SnapshotValidation.TryValidate(loaded, out string error))
                    return SnapshotDecodeResult.Failed(error, version);
                if (!string.Equals(loaded.Checksum, SnapshotChecksum.Compute(loaded), StringComparison.Ordinal))
                    return SnapshotDecodeResult.Failed("存档校验失败，内容可能已经损坏。", version);
                return SnapshotDecodeResult.Succeeded(loaded, version);
            }
            catch (Exception exception) when (IsSerializationError(exception))
            {
                return SnapshotDecodeResult.Failed("无法解析存档：" + exception.Message, version);
            }
        }

        private static SnapshotDecodeResult DecodeV1(string json)
        {
            const int version = 1;
            try
            {
                RunSnapshotV1 old = Deserialize<RunSnapshotV1>(json);
                if (!ValidateV1(old, out string error))
                    return SnapshotDecodeResult.Failed(error, version);
                if (!string.Equals(old.Checksum, ComputeV1Checksum(old), StringComparison.Ordinal))
                    return SnapshotDecodeResult.Failed("存档校验失败，内容可能已经损坏。", version);

                RunSnapshot migrated = MigrateV1ToV2(old);
                if (!SnapshotValidation.TryValidate(migrated, out error))
                    return SnapshotDecodeResult.Failed("迁移后的存档无效：" + error, version);
                migrated.Checksum = SnapshotChecksum.Compute(migrated);
                return SnapshotDecodeResult.Succeeded(migrated, version);
            }
            catch (Exception exception) when (IsSerializationError(exception))
            {
                return SnapshotDecodeResult.Failed("无法解析存档：" + exception.Message, version);
            }
        }

        private static RunSnapshot MigrateV1ToV2(RunSnapshotV1 old) => new RunSnapshot
        {
            SchemaVersion = 2,
            Seed = old.Seed,
            Health = old.Health,
            Armor = old.Armor,
            CurrentWeaponId = old.CurrentWeaponId,
            Weapons = old.Weapons,
            Upgrades = old.Upgrades,
            UpgradeSelectionHistory = old.UpgradeSelectionHistory,
            // Fields introduced in v2 deliberately use their safe, not-started defaults.
            InventorySlots = new List<InventorySlotSnapshot>(),
            QuickSlots = new List<QuickSlotSnapshot>(),
            SelectedQuickSlotIndex = -1,
            Mission = new MissionSnapshot(),
            Wave = new WaveSnapshot(),
            Enemies = new List<EnemySnapshot>(),
            PlayerEffects = new List<GameplayEffectSnapshot>(),
            PlayerPosition = new Float3Snapshot(),
            PlayerRotation = Float4Snapshot.Identity
        };

        private static int ReadVersion(string json)
        {
            SnapshotVersionHeader header = Deserialize<SnapshotVersionHeader>(json);
            if (header == null) throw new SerializationException("存档内容为空。");
            return header.SchemaVersion;
        }

        private static T Deserialize<T>(string json) where T : class
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T;
        }

        private static bool ValidateV1(RunSnapshotV1 snapshot, out string error)
        {
            error = null;
            if (snapshot == null) error = "存档内容为空。";
            else if (snapshot.SchemaVersion != 1) error = "不支持的存档版本：" + snapshot.SchemaVersion;
            else if (!FiniteNonNegative(snapshot.Health) || !FiniteNonNegative(snapshot.Armor)) error = "生命或护甲数值无效。";
            else if (snapshot.Weapons == null || snapshot.Weapons.Count == 0 || snapshot.Weapons.Count > SnapshotValidation.MaximumWeapons) error = "武器列表无效。";
            else if (snapshot.Upgrades == null || snapshot.Upgrades.Count > SnapshotValidation.MaximumUpgrades) error = "升级列表无效。";
            else if (snapshot.UpgradeSelectionHistory == null) error = "升级选择历史无效。";
            if (error != null) return false;

            var weapons = new HashSet<string>(StringComparer.Ordinal);
            foreach (WeaponAmmoSnapshot weapon in snapshot.Weapons)
                if (weapon == null || !ValidId(weapon.WeaponId) || !weapons.Add(weapon.WeaponId) || weapon.Magazine < 0 || weapon.Reserve < 0)
                { error = "武器标识重复、缺失或弹药数值无效。"; return false; }
            if (!weapons.Contains(snapshot.CurrentWeaponId))
            { error = "当前武器不在存档武器列表内。"; return false; }

            var levels = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (UpgradeLevelSnapshot upgrade in snapshot.Upgrades)
                if (upgrade == null || !ValidId(upgrade.UpgradeId) || levels.ContainsKey(upgrade.UpgradeId) || upgrade.Level <= 0)
                { error = "升级标识重复、缺失或等级无效。"; return false; }
                else levels.Add(upgrade.UpgradeId, upgrade.Level);
            var selected = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in snapshot.UpgradeSelectionHistory)
            {
                if (id == null || !levels.ContainsKey(id)) { error = "升级选择历史包含未知升级。"; return false; }
                selected.TryGetValue(id, out int count);
                selected[id] = count + 1;
            }
            foreach (var pair in levels)
                if (!selected.TryGetValue(pair.Key, out int count) || count != pair.Value)
                { error = "升级选择历史与升级等级不一致。"; return false; }
            return true;
        }

        private static string ComputeV1Checksum(RunSnapshotV1 snapshot)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(snapshot.SchemaVersion);
                writer.Write(snapshot.Seed);
                writer.Write(snapshot.Health);
                writer.Write(snapshot.Armor);
                writer.Write(snapshot.CurrentWeaponId ?? string.Empty);
                writer.Write(snapshot.Weapons.Count);
                foreach (WeaponAmmoSnapshot weapon in snapshot.Weapons)
                { writer.Write(weapon.WeaponId ?? string.Empty); writer.Write(weapon.Magazine); writer.Write(weapon.Reserve); }
                writer.Write(snapshot.Upgrades.Count);
                foreach (UpgradeLevelSnapshot upgrade in snapshot.Upgrades)
                { writer.Write(upgrade.UpgradeId ?? string.Empty); writer.Write(upgrade.Level); }
                writer.Write(snapshot.UpgradeSelectionHistory.Count);
                foreach (string id in snapshot.UpgradeSelectionHistory) writer.Write(id ?? string.Empty);
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
        private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        private static bool IsSerializationError(Exception exception) => exception is SerializationException ||
            exception is ArgumentException || exception is FormatException || exception is OverflowException;

        [DataContract]
        private sealed class SnapshotVersionHeader
        {
            [DataMember(Order = 0, IsRequired = true)] public int SchemaVersion;
        }

        [DataContract]
        private sealed class RunSnapshotV1
        {
            [DataMember(Order = 0, IsRequired = true)] public int SchemaVersion;
            [DataMember(Order = 1, IsRequired = true)] public int Seed;
            [DataMember(Order = 2, IsRequired = true)] public float Health;
            [DataMember(Order = 3, IsRequired = true)] public float Armor;
            [DataMember(Order = 4, IsRequired = true)] public string CurrentWeaponId;
            [DataMember(Order = 5, IsRequired = true)] public List<WeaponAmmoSnapshot> Weapons;
            [DataMember(Order = 6, IsRequired = true)] public List<UpgradeLevelSnapshot> Upgrades;
            [DataMember(Order = 7, IsRequired = true)] public List<string> UpgradeSelectionHistory;
            [DataMember(Order = 8, IsRequired = true)] public string Checksum;
        }
    }
}
