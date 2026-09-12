using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class Issue51LoadPresentationTests
{
    private const string GateName = "Run Snapshot Presentation Gate";

    private string path;
    private bool oldIgnoreLogs;

    [SetUp]
    public void SetUp()
    {
        path = Path.Combine(
            Path.GetTempPath(),
            "fps-load-presentation-" + Guid.NewGuid().ToString("N") + ".json");
        oldIgnoreLogs = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Time.timeScale = 1f;
        Scene cleanup = SceneManager.CreateScene(
            "LoadPresentationCleanup-" + Guid.NewGuid().ToString("N"));
        SceneManager.SetActiveScene(cleanup);
        for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene != cleanup)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        Object gate = GameObject.Find(GateName);
        if (gate != null)
        {
            Object.Destroy(gate);
        }

        LogAssert.ignoreFailingMessages = oldIgnoreLogs;
    }

    [UnityTest]
    public IEnumerator LoadCoversEveryFrameUntilSavedWorldIsFullyRestored()
    {
        yield return SceneManager.LoadSceneAsync(
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
        yield return WaitReady(null);

        RunSnapshotRuntimeAdapter previous =
            Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
        RunSnapshotMenu menu = previous.GetComponent<RunSnapshotMenu>();
        Assert.That(menu.SaveTo(path), Is.True, menu.StatusMessage);
        Assert.That(menu.LoadFrom(path), Is.True, menu.StatusMessage);

        AssertOpaqueGate("读取操作发起后必须立刻遮住旧场景");

        float timeout = Time.realtimeSinceStartup + 35f;
        while (Time.realtimeSinceStartup < timeout)
        {
            RunSnapshotRuntimeAdapter current =
                Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
            CityNewWaveBootstrap bootstrap =
                Object.FindAnyObjectByType<CityNewWaveBootstrap>();
            bool restored = current != null &&
                            !ReferenceEquals(current, previous) &&
                            bootstrap != null &&
                            bootstrap.Director.IsRunning &&
                            !RunSnapshotSession.HasPendingWorldRestore;
            if (restored)
            {
                break;
            }

            AssertOpaqueGate("玩家、波次和任务尚未全部恢复时不能露出初始画面");
            yield return null;
        }

        Assert.That(
            RunSnapshotSession.HasPendingWorldRestore,
            Is.False,
            RunSnapshotSession.LastMessage);
        for (int frame = 0;
             frame < 5 && GameObject.Find(GateName) != null;
             frame++)
        {
            yield return null;
        }
        Assert.That(GameObject.Find(GateName), Is.Null,
            "完整恢复且 HUD 刷新后应及时移除读取遮罩。");
    }

    private static void AssertOpaqueGate(string reason)
    {
        GameObject gate = GameObject.Find(GateName);
        Assert.That(gate, Is.Not.Null, reason);
        Canvas canvas = gate.GetComponent<Canvas>();
        Image image = gate.GetComponentInChildren<Image>();
        Assert.That(canvas, Is.Not.Null, reason);
        Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(canvas.sortingOrder, Is.GreaterThanOrEqualTo(32000));
        Assert.That(image, Is.Not.Null, reason);
        Assert.That(image.color.a, Is.EqualTo(1f).Within(0.001f));
    }

    private static IEnumerator WaitReady(RunSnapshotRuntimeAdapter previous)
    {
        float timeout = Time.realtimeSinceStartup + 35f;
        while (Time.realtimeSinceStartup < timeout)
        {
            RunSnapshotRuntimeAdapter current =
                Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
            CityNewWaveBootstrap bootstrap =
                Object.FindAnyObjectByType<CityNewWaveBootstrap>();
            if (current != null && !ReferenceEquals(current, previous) &&
                bootstrap != null && bootstrap.Director.IsRunning &&
                !RunSnapshotSession.HasPendingWorldRestore)
            {
                yield break;
            }

            yield return null;
        }

        Assert.Fail("战局未在时限内就绪：" + RunSnapshotSession.LastMessage);
    }
}
