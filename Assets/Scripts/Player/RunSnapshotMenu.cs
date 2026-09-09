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
    private bool requiresRecoveryChoice;
    private string preservedCorruptFilePath;

    public bool IsBusy => busy;
    public string StatusMessage => message;
    public bool RequiresRecoveryChoice => requiresRecoveryChoice;
    public string PreservedCorruptFilePath => preservedCorruptFilePath;

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
            preservedCorruptFilePath = result.CorruptFilePath;
            requiresRecoveryChoice = result.Status == SnapshotStatus.InvalidData;
            message = "未读取存档：" + result.Message;
            if (requiresRecoveryChoice)
            {
                message += string.IsNullOrEmpty(preservedCorruptFilePath)
                    ? " 存档未被覆盖，请选择再次尝试读取或开始新战局。"
                    : " 损坏文件已单独保留，请选择再次尝试读取或开始新战局。";
            }
            return false;
        }

        string error;
        bool validForRuntime = result.OriginalSchemaVersion == 1
            ? adapter.ValidateLegacyV1Snapshot(result.Snapshot, out error)
            : adapter.ValidateSnapshot(result.Snapshot, out error);
        if (!validForRuntime)
        {
            message = "存档无法恢复：" + error;
            return false;
        }

        requiresRecoveryChoice = false;
        preservedCorruptFilePath = result.CorruptFilePath;
        busy = true;
        PrepareSceneTransition();
        RunSnapshotSession.ReloadSnapshot(
            result.Snapshot,
            result.Message,
            result.OriginalSchemaVersion);
        return true;
    }

    public bool StartNewGame()
    {
        if (busy) return false;

        requiresRecoveryChoice = false;
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
