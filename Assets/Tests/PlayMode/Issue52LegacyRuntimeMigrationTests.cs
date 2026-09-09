using System.Collections;
using System.Collections.Generic;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class Issue52LegacyRuntimeMigrationTests
{
    [UnityTest]
    public IEnumerator LegacyV1RestoreKeepsFreshWorldPoseInventoryAndWave()
    {
        yield return SceneManager.LoadSceneAsync(
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
        yield return WaitReady();

        RunSnapshotRuntimeAdapter adapter =
            Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
        PlayerInventoryController inventory =
            adapter.GetComponent<PlayerInventoryController>();
        WaveDirector director = WaveDirector.Active;
        Vector3 position = adapter.transform.position;
        PlayerInventorySnapshot inventoryBefore = inventory.CaptureSnapshot();
        WaveProgressSnapshot waveBefore = director.CurrentProgress;
        RunSnapshot current = adapter.Capture();
        var legacy = new RunSnapshot
        {
            Seed = current.Seed + 1,
            Health = Mathf.Max(1f, current.Health - 15f),
            Armor = current.Armor,
            CurrentWeaponId = current.CurrentWeaponId,
            Weapons = new List<WeaponAmmoSnapshot>(),
            Upgrades = new List<UpgradeLevelSnapshot>(),
            UpgradeSelectionHistory = new List<string>(),
            InventorySlots = new List<FPS.SaveGame.InventorySlotSnapshot>(),
            QuickSlots = new List<FPS.SaveGame.QuickSlotSnapshot>(),
            Mission = new MissionSnapshot(),
            Wave = new WaveSnapshot(),
            Enemies = new List<EnemySnapshot>(),
            PlayerEffects = new List<GameplayEffectSnapshot>(),
            PlayerPosition = new Float3Snapshot(),
            PlayerRotation = Float4Snapshot.Identity
        };
        foreach (WeaponAmmoSnapshot weapon in current.Weapons)
        {
            legacy.Weapons.Add(new WeaponAmmoSnapshot
            {
                WeaponId = weapon.WeaponId,
                Magazine = Mathf.Min(1, weapon.Magazine),
                Reserve = Mathf.Min(2, weapon.Reserve)
            });
        }

        Assert.That(
            adapter.TryRestoreLegacyV1Snapshot(legacy, out string error),
            Is.True,
            error);
        Assert.That(adapter.transform.position, Is.EqualTo(position));
        Assert.That(
            inventory.CaptureSnapshot().Inventory.Slots.Count,
            Is.EqualTo(inventoryBefore.Inventory.Slots.Count));
        Assert.That(director.CurrentProgress.CurrentWave,
            Is.EqualTo(waveBefore.CurrentWave));
        Assert.That(director.IsRunning, Is.True);
        Assert.That(adapter.Capture().Health, Is.EqualTo(legacy.Health));
    }

    private static IEnumerator WaitReady()
    {
        float timeout = Time.realtimeSinceStartup + 35f;
        while (Time.realtimeSinceStartup < timeout)
        {
            RunSnapshotRuntimeAdapter adapter =
                Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
            CityNewMissionController mission =
                Object.FindAnyObjectByType<CityNewMissionController>();
            if (adapter != null && WaveDirector.Active != null &&
                WaveDirector.Active.IsRunning && mission != null &&
                mission.Terminal != null)
            {
                yield break;
            }
            yield return null;
        }
        Assert.Fail("旧版迁移测试等待战局初始化超时。");
    }
}
