using System;
using FPS.SaveGame;
using UnityEngine;

/// <summary>
/// 战局快照服务。界面和输入统一由 ESC 暂停菜单负责。
/// </summary>
public sealed class RunSnapshotMenu : MonoBehaviour
{
    private RunSnapshotRuntimeAdapter adapter;
    private bool busy;
    private string message;

    public bool IsBusy => busy;
    public string StatusMessage => message;

    private void Awake()
    {
        adapter = GetComponent<RunSnapshotRuntimeAdapter>();
        message = RunSnapshotSession.LastMessage;
    }

    public bool SaveTo(string path)
    {
        if (busy) return false;

        try
        {
            var result = new RunSnapshotStore(path).Save(adapter.Capture());
            message = result.Success ? "保存成功。" : "保存失败：" + result.Message;
            return result.Success;
        }
        catch (Exception exception)
        {
            message = "保存失败：" + exception.Message;
            return false;
        }
    }

    public bool LoadFrom(string path)
    {
        if (busy) return false;

        var result = new RunSnapshotStore(path).Load();
        if (!result.Success)
        {
            message = "未读取存档：" + result.Message;
            return false;
        }

        if (!adapter.ValidateSnapshot(result.Snapshot, out string error))
        {
            message = "存档无法恢复：" + error;
            return false;
        }

        busy = true;
        PrepareSceneTransition();
        RunSnapshotSession.ReloadSnapshot(result.Snapshot);
        return true;
    }

    public bool StartNewGame()
    {
        if (busy) return false;

        busy = true;
        PrepareSceneTransition();
        RunSnapshotSession.NewGame();
        return true;
    }

    private void PrepareSceneTransition()
    {
        GetComponent<GameplayLockCoordinator>()?.ResetForSceneTransition();
        Time.timeScale = 1f;
    }
}
