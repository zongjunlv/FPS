using System;
using System.Collections.Generic;
using FPS.SaveGame;
using UnityEngine;

/// <summary>Maps data-only saves onto a prepared player, without replaying rewards.</summary>
public sealed class RunSnapshotRuntimeAdapter : MonoBehaviour
{
    private Health health;
    private PlayerUpgradeController upgrades;
    private PlayerRuntimeCombatStats stats;
    private WeaponLoadoutController loadout;

    private bool Resolve(out string error)
    {
        health = GetComponent<Health>();
        upgrades = GetComponent<PlayerUpgradeController>();
        stats = GetComponent<PlayerRuntimeCombatStats>();
        loadout = GetComponent<WeaponLoadoutController>();
        error = health == null || upgrades == null || stats == null || loadout == null
            ? "玩家存档依赖尚未就绪。" : string.Empty;
        return error.Length == 0;
    }

    public RunSnapshot Capture()
    {
        if (!Resolve(out string error)) throw new InvalidOperationException(error);
        if (health.IsDead || upgrades.IsChoiceOpen || upgrades.PendingChoiceCount > 0 || loadout.IsSwitching)
            throw new InvalidOperationException("请在存活且完成选卡、切枪后保存。");
        var snapshot = new RunSnapshot
        {
            Seed = upgrades.RunSeed,
            Health = health.CurrentHealth,
            Armor = health.CurrentArmor,
            CurrentWeaponId = loadout.CurrentWeapon.StableId,
            UpgradeSelectionHistory = new List<string>(upgrades.SelectionHistory)
        };
        for (int i = 0; i < loadout.WeaponCount; i++)
        {
            WeaponController weapon = loadout.GetWeapon(i);
            weapon.PrepareSnapshotState(stats);
            snapshot.Weapons.Add(new WeaponAmmoSnapshot
            {
                WeaponId = weapon.StableId,
                Magazine = weapon.CurrentAmmo,
                Reserve = weapon.ReserveAmmo
            });
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in upgrades.SelectionHistory)
            if (seen.Add(id)) snapshot.Upgrades.Add(new UpgradeLevelSnapshot
                { UpgradeId = id, Level = upgrades.GetUpgradeLevel(id) });
        if (!ValidateSnapshot(snapshot, out error)) throw new InvalidOperationException(error);
        return snapshot;
    }

    public bool ValidateSnapshot(RunSnapshot snapshot, out string error)
    {
        if (!Resolve(out error)) return false;
        if (snapshot == null || snapshot.SchemaVersion != RunSnapshot.CurrentSchemaVersion ||
            snapshot.Weapons == null || snapshot.Upgrades == null ||
            snapshot.UpgradeSelectionHistory == null ||
            snapshot.Weapons.Count != loadout.WeaponCount || snapshot.Upgrades.Count > 128 ||
            snapshot.UpgradeSelectionHistory.Count > 4096)
        {
            error = "快照版本、武器数量或升级结构无效。";
            return false;
        }
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (UpgradeLevelSnapshot entry in snapshot.Upgrades)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.UpgradeId) || entry.Level <= 0 ||
                !levels.TryAdd(entry.UpgradeId, entry.Level))
            {
                error = "快照升级条目无效或重复。";
                return false;
            }
        }
        foreach (string id in snapshot.UpgradeSelectionHistory)
        {
            if (id == null || !levels.TryGetValue(id, out int count) || count <= 0)
            {
                error = "升级历史与等级不一致。";
                return false;
            }
            levels[id] = count - 1;
        }
        foreach (int count in levels.Values)
            if (count != 0)
            {
                error = "升级历史与等级不一致。";
                return false;
            }
        if (!upgrades.TryPreviewSnapshotUpgrades(snapshot.UpgradeSelectionHistory,
                out RunUpgradeState restored, out float maxHealth, out float maxArmor, out error)) return false;
        if (!Finite(snapshot.Health) || !Finite(snapshot.Armor) || snapshot.Health <= 0f ||
            snapshot.Health > maxHealth || snapshot.Armor < 0f || snapshot.Armor > maxArmor)
        {
            error = "生命或护甲超出已保存升级的有效范围。";
            return false;
        }
        var weapons = new Dictionary<string, WeaponController>(StringComparer.Ordinal);
        for (int i = 0; i < loadout.WeaponCount; i++)
        {
            WeaponController weapon = loadout.GetWeapon(i);
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.StableId) || !weapons.TryAdd(weapon.StableId, weapon))
            {
                error = "当前武器缺少唯一稳定 ID。";
                return false;
            }
        }
        if (snapshot.CurrentWeaponId == null || !weapons.ContainsKey(snapshot.CurrentWeaponId))
        {
            error = "当前武器 ID 不存在。";
            return false;
        }
        foreach (WeaponAmmoSnapshot entry in snapshot.Weapons)
        {
            if (entry == null || entry.WeaponId == null || !weapons.Remove(entry.WeaponId, out WeaponController weapon) ||
                entry.Magazine < 0 || entry.Magazine > Mathf.Max(1, Mathf.RoundToInt(
                    weapon.BaseMagazineCapacity * restored.WeaponModifiers.MagazineCapacityMultiplier)) ||
                entry.Reserve < 0 || entry.Reserve > weapon.BaseMaximumReserveAmmo)
            {
                error = "弹药数量越界或武器 ID 无效、重复。";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    public bool TryRestore(RunSnapshot snapshot, out string error)
    {
        if (!ValidateSnapshot(snapshot, out error)) return false;
        upgrades.RestoreSnapshotUpgrades(snapshot.Seed, snapshot.UpgradeSelectionHistory);
        int equippedIndex = 0;
        for (int i = 0; i < loadout.WeaponCount; i++)
        {
            WeaponController weapon = loadout.GetWeapon(i);
            weapon.PrepareSnapshotState(stats);
            WeaponAmmoSnapshot ammo = snapshot.Weapons.Find(value => value.WeaponId == weapon.StableId);
            weapon.TryRestoreAmmo(ammo.Magazine, ammo.Reserve);
            if (weapon.StableId == snapshot.CurrentWeaponId) equippedIndex = i;
        }
        loadout.RestoreEquippedWeapon(equippedIndex);
        health.TryRestoreSnapshotVitals(snapshot.Health, snapshot.Armor);
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
