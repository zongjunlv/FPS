using System;
using System.Collections;
using System.IO;
using System.Linq;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class Issue50SnapshotSessionTests
{
    private string path;
    private bool oldIgnore;

    [SetUp]
    public void SetUp()
    {
        path = Path.Combine(Path.GetTempPath(), "fps-snapshot-test-" + Guid.NewGuid().ToString("N") + ".json");
        oldIgnore = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (File.Exists(path)) File.Delete(path);
        Time.timeScale = 1;
        var empty = SceneManager.CreateScene("SnapshotCleanup-" + Guid.NewGuid().ToString("N"));
        SceneManager.SetActiveScene(empty);
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene != empty) yield return SceneManager.UnloadSceneAsync(scene);
        }
        LogAssert.ignoreFailingMessages = oldIgnore;
    }

    [UnityTest]
    public IEnumerator SaveReloadAndNewGameHaveDistinctEndToEndBehavior()
    {
        yield return SceneManager.LoadSceneAsync("Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
        yield return WaitReady();
        var adapter = Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
        var upgrades = adapter.GetComponent<PlayerUpgradeController>();
        var snapshot = adapter.Capture();
        snapshot.Seed = 504242;
        snapshot.Health = 55;
        snapshot.Armor = 12;
        var vitality = upgrades.AvailableUpgrades.First(definition => definition.EffectType == UpgradeEffectType.MaximumHealth);
        snapshot.Upgrades.Add(new UpgradeLevelSnapshot { UpgradeId = vitality.StableId, Level = 1 });
        snapshot.UpgradeSelectionHistory.Add(vitality.StableId);
        snapshot.PlayerEffects.Add(new GameplayEffectSnapshot
        {
            EffectId = vitality.GameplayEffect.StableId,
            SourceId = vitality.StableId,
            SourceKey = "upgrade:" + vitality.StableId,
            DurationPolicy = 0
        });
        snapshot.CurrentWeaponId = snapshot.Weapons.Last().WeaponId;
        foreach (var ammo in snapshot.Weapons) { ammo.Magazine = 2; ammo.Reserve = 15; }
        Assert.That(adapter.TryRestore(snapshot, out string error), Is.True, error);
        var menu = adapter.GetComponent<RunSnapshotMenu>();
        RunSnapshot expected = adapter.Capture();
        Assert.That(menu.SaveTo(path), Is.True, menu.StatusMessage);
        Assert.That(menu.LoadFrom(path), Is.True, menu.StatusMessage);
        yield return null;
        yield return WaitReady(adapter);
        var restored = Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
        RunSnapshot actual = restored.Capture();
        Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
        Assert.That(actual.Health, Is.EqualTo(expected.Health));
        Assert.That(actual.Armor, Is.EqualTo(expected.Armor));
        Assert.That(actual.CurrentWeaponId, Is.EqualTo(expected.CurrentWeaponId));
        Assert.That(
            actual.UpgradeSelectionHistory,
            Is.EqualTo(expected.UpgradeSelectionHistory));
        Assert.That(
            actual.Weapons.Select(value =>
                (value.WeaponId, value.Magazine, value.Reserve)),
            Is.EqualTo(expected.Weapons.Select(value =>
                (value.WeaponId, value.Magazine, value.Reserve))));
        Assert.That(restored.GetComponent<PlayerUpgradeController>().PendingChoiceCount, Is.Zero);
        Assert.That(restored.GetComponent<PlayerRunProgression>().RewardedKillCount, Is.Zero);
        Assert.That(RunSnapshotSession.LastMessage, Does.Contain("读取成功"));
        RunSnapshotSession.NewGame();
        yield return null;
        yield return WaitReady(restored);
        var fresh = Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>().Capture();
        Assert.That(fresh.Seed, Is.Not.EqualTo(snapshot.Seed));
        Assert.That(fresh.Upgrades, Is.Empty);
        Assert.That(fresh.Health, Is.EqualTo(100));
        Assert.That(File.Exists(path), Is.True, "新游戏不删除已有存档。");
    }

    [UnityTest]
    public IEnumerator MissingSaveLeavesCurrentSceneAndPlayerUntouched()
    {
        yield return SceneManager.LoadSceneAsync("Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
        yield return WaitReady();
        var adapter = Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
        var menu = adapter.GetComponent<RunSnapshotMenu>();
        string before = SnapshotChecksum.Compute(adapter.Capture());
        var scene = SceneManager.GetActiveScene().handle;
        Assert.That(menu.LoadFrom(path), Is.False);
        Assert.That(menu.StatusMessage, Does.Contain("没有"));
        Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(scene));
        Assert.That(SnapshotChecksum.Compute(adapter.Capture()), Is.EqualTo(before));
    }

    [UnityTest]
    public IEnumerator SnapshotActionsShareTheExistingPauseMenuLock()
    {
        yield return SceneManager.LoadSceneAsync("Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
        yield return WaitReady();
        var menu = Object.FindAnyObjectByType<RunSnapshotMenu>();
        var player = menu.GetComponent<PlayerController>();
        var locks = menu.GetComponent<GameplayLockCoordinator>();
        var mission = menu.GetComponent<CityNewMissionController>();

        player.SetPaused(true);
        Assert.That(player.IsPaused, Is.True);
        Assert.That(locks.IsTopmost(GameplayLockReason.PauseMenu), Is.True);
        Assert.That(mission.GetType().GetMethod("SaveRunSnapshot"), Is.Not.Null);
        Assert.That(mission.GetType().GetMethod("LoadRunSnapshot"), Is.Not.Null);
        Assert.That(mission.GetType().GetMethod("StartNewRun"), Is.Not.Null);
        Assert.That(menu.SaveTo(path), Is.True, menu.StatusMessage);
        Assert.That(player.IsPaused, Is.True, "保存不应退出 ESC 暂停菜单。");

        player.SetPaused(false);
        Assert.That(player.IsPaused, Is.False);
        Assert.That(locks.IsLocked, Is.False);

        var inventory = locks.Acquire(GameplayLockReason.Inventory);
        yield return null;
        inventory.Dispose();
        Assert.That(locks.LastModalTransitionFrame, Is.EqualTo(Time.frameCount),
            "ESC 关闭上层模态后，同帧不能穿透到暂停菜单。");
    }

    private static IEnumerator WaitReady(RunSnapshotRuntimeAdapter previous = null)
    {
        float end = Time.realtimeSinceStartup + 35;
        while (Time.realtimeSinceStartup < end)
        {
            var current = Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
            var bootstrap = Object.FindAnyObjectByType<CityNewWaveBootstrap>();
            if (current != null && !ReferenceEquals(current, previous) && bootstrap != null && bootstrap.Director.IsRunning) yield break;
            yield return null;
        }
        Assert.Fail("战局未在时限内就绪：" + RunSnapshotSession.LastMessage);
    }
}
