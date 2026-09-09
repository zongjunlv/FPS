using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class Issue52SnapshotRecoveryMenuTests
{
    private string path;
    private GameObject player;

    [SetUp]
    public void SetUp()
    {
        path = Path.Combine(
            Path.GetTempPath(),
            "fps-snapshot-menu-" + Guid.NewGuid().ToString("N") + ".json");
        player = new GameObject("Snapshot Menu Test Player");
        player.AddComponent<RunSnapshotRuntimeAdapter>();
        player.AddComponent<RunSnapshotMenu>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (player != null) Object.Destroy(player);
        DeleteIfExists(path);
        DeleteIfExists(path + ".bak");
        DeleteIfExists(path + ".tmp");
        string directory = Path.GetDirectoryName(path);
        string prefix = Path.GetFileName(path) + ".corrupt-";
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            foreach (string file in Directory.GetFiles(directory, prefix + "*"))
                DeleteIfExists(file);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator UnrecoverableSaveIsPreservedAndOffersAnExplicitChoice()
    {
        File.WriteAllText(path, "broken snapshot");
        yield return null;

        RunSnapshotMenu menu = player.GetComponent<RunSnapshotMenu>();
        Assert.That(menu.LoadFrom(path), Is.False);
        Assert.That(menu.RequiresRecoveryChoice, Is.True);
        Assert.That(menu.PreservedCorruptFilePath, Is.Not.Empty);
        Assert.That(File.Exists(menu.PreservedCorruptFilePath), Is.True);
        Assert.That(menu.StatusMessage, Does.Contain("损坏文件已单独保留"));
        Assert.That(menu.StatusMessage, Does.Contain("开始新战局"));
    }

    private static void DeleteIfExists(string file)
    {
        if (File.Exists(file)) File.Delete(file);
    }
}
