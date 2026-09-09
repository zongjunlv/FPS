using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FPS.SaveGame
{
    public static class SnapshotChecksum
    {
        // A length-prefixed, fixed-order binary representation avoids culture, JSON
        // whitespace, dictionary enumeration and ambiguous string concatenation.
        public static string Compute(RunSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(snapshot.SchemaVersion);
                writer.Write(snapshot.Seed);
                writer.Write(snapshot.Health);
                writer.Write(snapshot.Armor);
                writer.Write(snapshot.CurrentWeaponId ?? string.Empty);
                writer.Write(snapshot.Weapons?.Count ?? 0);
                if (snapshot.Weapons != null)
                    foreach (var weapon in snapshot.Weapons)
                    {
                        writer.Write(weapon.WeaponId ?? string.Empty);
                        writer.Write(weapon.Magazine);
                        writer.Write(weapon.Reserve);
                    }
                writer.Write(snapshot.Upgrades?.Count ?? 0);
                if (snapshot.Upgrades != null)
                    foreach (var upgrade in snapshot.Upgrades)
                    {
                        writer.Write(upgrade.UpgradeId ?? string.Empty);
                        writer.Write(upgrade.Level);
                    }
                writer.Write(snapshot.UpgradeSelectionHistory?.Count ?? 0);
                if (snapshot.UpgradeSelectionHistory != null)
                    foreach (var id in snapshot.UpgradeSelectionHistory) writer.Write(id ?? string.Empty);
                writer.Flush();
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(stream.ToArray());
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
        }
    }

    public static class SnapshotValidation
    {
        public static bool TryValidate(RunSnapshot snapshot, out string error)
        {
            error = null;
            if (snapshot == null) error = "存档内容为空。";
            else if (snapshot.SchemaVersion != RunSnapshot.CurrentSchemaVersion)
                error = "不支持的存档版本：" + snapshot.SchemaVersion;
            else if (!FiniteNonNegative(snapshot.Health) || !FiniteNonNegative(snapshot.Armor))
                error = "生命或护甲数值无效。";
            else if (snapshot.Weapons == null || snapshot.Weapons.Count == 0 || snapshot.Weapons.Count > 128)
                error = "武器列表无效。";
            else if (snapshot.Upgrades == null || snapshot.Upgrades.Count > 1024)
                error = "升级列表无效。";
            if (error != null) return false;

            var weapons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var weapon in snapshot.Weapons)
            {
                if (weapon == null || !ValidId(weapon.WeaponId) || !weapons.Add(weapon.WeaponId)
                    || weapon.Magazine < 0 || weapon.Reserve < 0)
                {
                    error = "武器标识重复、缺失或弹药数值无效。";
                    return false;
                }
            }
            if (snapshot.CurrentWeaponId == null || !weapons.Contains(snapshot.CurrentWeaponId))
            {
                error = "当前武器不在存档武器列表内。";
                return false;
            }
            var upgrades = new HashSet<string>(StringComparer.Ordinal);
            foreach (var upgrade in snapshot.Upgrades)
            {
                if (upgrade == null || !ValidId(upgrade.UpgradeId) || !upgrades.Add(upgrade.UpgradeId) || upgrade.Level <= 0)
                {
                    error = "升级标识重复、缺失或等级无效。";
                    return false;
                }
            }
            if (snapshot.UpgradeSelectionHistory == null || snapshot.UpgradeSelectionHistory.Count > 100000)
            {
                error = "升级选择历史无效。";
                return false;
            }
            var historyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in snapshot.UpgradeSelectionHistory)
            {
                if (id == null || !upgrades.Contains(id))
                {
                    error = "升级选择历史包含未知升级。";
                    return false;
                }
                historyCounts.TryGetValue(id, out var count);
                historyCounts[id] = count + 1;
            }
            foreach (var upgrade in snapshot.Upgrades)
            {
                if (!historyCounts.TryGetValue(upgrade.UpgradeId, out var count) || count != upgrade.Level)
                {
                    error = "升级选择历史与升级等级不一致。";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
        private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }
}
