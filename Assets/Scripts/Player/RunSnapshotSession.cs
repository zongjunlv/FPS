using System;
using System.IO;
using FPS.SaveGame;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RunSnapshotSession
{
    private static RunSnapshot pending;
    private static int? newSeed;
    public static string LastMessage { get; private set; } = "新战局已开始；按 ESC 打开暂停与存档菜单。";
    public static string DefaultPath => Path.Combine(Application.persistentDataPath, "run-snapshot.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        pending = null;
        newSeed = null;
        LastMessage = "新战局已开始；按 ESC 打开暂停与存档菜单。";
    }

    public static bool InitializePlayer(GameObject player, out string error)
    {
        var composition = player.GetComponent<PlayerCombatCompositionRoot>();
        if (composition == null || !composition.TryInitialize())
        {
            pending = null;
            error = "玩家组件尚未准备完成，无法恢复快照。";
            return false;
        }
        var adapter = player.GetComponent<RunSnapshotRuntimeAdapter>() ?? player.AddComponent<RunSnapshotRuntimeAdapter>();
        if (player.GetComponent<RunSnapshotMenu>() == null) player.AddComponent<RunSnapshotMenu>();
        if (newSeed.HasValue)
        {
            var upgrades = player.GetComponent<PlayerUpgradeController>();
            upgrades.ConfigureRun(newSeed.Value, upgrades.AvailableUpgrades);
            newSeed = null;
        }
        RunSnapshot snapshot = pending;
        pending = null;
        error = string.Empty;
        if (snapshot == null)
        {
            LastMessage = "新战局已开始，已有存档未删除。";
            return true;
        }
        bool restored = adapter.TryRestore(snapshot, out error);
        LastMessage = restored ? "读取成功：已恢复玩家状态，波次与任务重新开始。" : "读取失败：" + error;
        return restored;
    }

    public static void ReloadSnapshot(RunSnapshot snapshot)
    {
        pending = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        newSeed = null;
        Time.timeScale = 1f;
        SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
    }

    public static void NewGame()
    {
        pending = null;
        newSeed = Guid.NewGuid().GetHashCode();
        LastMessage = "新战局已开始，原存档未删除。";
        Time.timeScale = 1f;
        SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
    }
}
