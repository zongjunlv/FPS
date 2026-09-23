using System;
using System.Collections;
using System.IO;
using System.Linq;
using FPS.Core.GameModes;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class Issue51RunSnapshotContinuationTests
{
    private string path;
    private bool oldIgnoreLogs;

    [SetUp]
    public void SetUp()
    {
        path = Path.Combine(
            Path.GetTempPath(),
            "fps-issue51-" + Guid.NewGuid().ToString("N") + ".json");
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
            "Issue51Cleanup-" + Guid.NewGuid().ToString("N"));
        SceneManager.SetActiveScene(cleanup);
        for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene != cleanup)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }
        LogAssert.ignoreFailingMessages = oldIgnoreLogs;
    }

    [UnityTest]
    public IEnumerator Issue70BattleEntryMidWaveSaveLoadsTwiceExactlyOnce()
    {
        yield return LoadCityAndWaitReady();
        RunSnapshotRuntimeAdapter adapter =
            Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
        PlayerController player = adapter.GetComponent<PlayerController>();
        PlayerInventoryController inventory =
            adapter.GetComponent<PlayerInventoryController>();
        CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
        ItemDefinition medkit = catalog.Items.First(value =>
            value.StableId == "medical_kit");
        Assert.That(inventory.TryAdd(medkit, 3), Is.True);
        Assert.That(inventory.BindQuickSlot(0, medkit.StableId), Is.True);

        WaveDirector director = WaveDirector.Active;
        EnemySpawnHandle target = FirstEnemy(director);
        Health targetHealth = target.Controller.GetComponent<Health>();
        targetHealth.ApplyDamage(new DamageInfo(
            7f,
            target.Controller.transform.position,
            Vector3.up,
            player.gameObject,
            DamageType.Hitscan));
        EnemyBurnEffectController burn =
            target.Controller.GetComponent<EnemyBurnEffectController>();
        Assert.That(burn.ApplyBurn(player.gameObject), Is.True);

        player.SetPaused(true);
        Vector3 savedPlayerPosition = player.transform.position +
                                      new Vector3(0.65f, 0f, 0.45f);
        Assert.That(player.TryRestoreSnapshotPose(
                savedPlayerPosition,
                Quaternion.Euler(0f, 37f, 0f),
                -21f,
                true,
                out string poseError),
            Is.True,
            poseError);
        RunSnapshot expected = adapter.Capture();
        Assert.That(expected.Enemies, Is.Not.Empty);
        Assert.That(
            expected.Enemies.First(value => value.SpawnId == target.SpawnId)
                .Effects,
            Is.Not.Empty);
        RunSnapshotMenu menu = adapter.GetComponent<RunSnapshotMenu>();
        Assert.That(menu.SaveTo(path), Is.True, menu.StatusMessage);

        for (int loadIndex = 0; loadIndex < 2; loadIndex++)
        {
            RunSnapshotRuntimeAdapter previous = adapter;
            Assert.That(menu.LoadFrom(path), Is.True, menu.StatusMessage);
            yield return null;
            yield return WaitReady(previous);

            Assert.That(GameModeContext.IsActive(
                GameModeId.SoloBattle, GameModeStage.Battle), Is.True);
            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo(GameModeScenePaths.CityNew));

            adapter = Object.FindAnyObjectByType<RunSnapshotRuntimeAdapter>();
            player = adapter.GetComponent<PlayerController>();
            player.SetPaused(true);
            RunSnapshot actual = adapter.Capture();
            AssertPlayerAndWorldState(expected, actual);

            director = WaveDirector.Active;
            Assert.That(TryGetEnemy(
                    director,
                    target.SpawnId,
                    out EnemySpawnHandle restoredEnemy),
                Is.True);
            int settledBefore = director.CurrentProgress.SettledCount;
            int rewardedBefore = adapter
                .GetComponent<PlayerRunProgression>()
                .RewardedKillCount;
            int lootBefore = adapter
                .GetComponent<PlayerLootRewardController>()
                .EnemySettlementCount;
            restoredEnemy.Controller.GetComponent<Health>().ApplyDamage(
                new DamageInfo(
                    99999f,
                    restoredEnemy.Controller.transform.position,
                    Vector3.up,
                    player.gameObject,
                    DamageType.Hitscan));
            Assert.That(
                director.CurrentProgress.SettledCount,
                Is.EqualTo(settledBefore + 1));
            Assert.That(
                adapter.GetComponent<PlayerRunProgression>()
                    .RewardedKillCount,
                Is.EqualTo(rewardedBefore + 1));
            Assert.That(
                adapter.GetComponent<PlayerLootRewardController>()
                    .EnemySettlementCount,
                Is.EqualTo(lootBefore + 1));

            menu = adapter.GetComponent<RunSnapshotMenu>();
        }
    }

    private static void AssertPlayerAndWorldState(
        RunSnapshot expected,
        RunSnapshot actual)
    {
        Assert.That(actual.InventorySlots.Select(value =>
            (value.ItemId, value.Quantity)), Is.EqualTo(
            expected.InventorySlots.Select(value =>
                (value.ItemId, value.Quantity))));
        Assert.That(actual.QuickSlots.Select(value => value.ItemId),
            Is.EqualTo(expected.QuickSlots.Select(value => value.ItemId)));
        Assert.That(actual.Wave.CurrentWave,
            Is.EqualTo(expected.Wave.CurrentWave));
        Assert.That(actual.Wave.SpawnedIds,
            Is.EqualTo(expected.Wave.SpawnedIds));
        Assert.That(actual.Wave.ActiveIds,
            Is.EqualTo(expected.Wave.ActiveIds));
        Assert.That(actual.Wave.SettledIds,
            Is.EqualTo(expected.Wave.SettledIds));
        Assert.That(actual.Mission.Phase,
            Is.EqualTo(expected.Mission.Phase));
        Assert.That(actual.Mission.TerminalProgressNormalized,
            Is.EqualTo(expected.Mission.TerminalProgressNormalized)
                .Within(0.001f));
        Assert.That(Vector3.Distance(
                new Vector3(
                    actual.PlayerPosition.X,
                    actual.PlayerPosition.Y,
                    actual.PlayerPosition.Z),
                new Vector3(
                    expected.PlayerPosition.X,
                    expected.PlayerPosition.Y,
                    expected.PlayerPosition.Z)),
            Is.LessThan(0.01f));
        Assert.That(
            Quaternion.Angle(
                new Quaternion(
                    actual.PlayerRotation.X,
                    actual.PlayerRotation.Y,
                    actual.PlayerRotation.Z,
                    actual.PlayerRotation.W),
                new Quaternion(
                    expected.PlayerRotation.X,
                    expected.PlayerRotation.Y,
                    expected.PlayerRotation.Z,
                    expected.PlayerRotation.W)),
            Is.LessThan(0.01f));
        Assert.That(actual.CameraPitch,
            Is.EqualTo(expected.CameraPitch).Within(0.01f));
        Assert.That(actual.PlayerCrouching,
            Is.EqualTo(expected.PlayerCrouching));
        foreach (EnemySnapshot enemy in expected.Enemies)
        {
            EnemySnapshot restored = actual.Enemies.First(value =>
                value.SpawnId == enemy.SpawnId);
            Assert.That(restored.Health,
                Is.EqualTo(enemy.Health).Within(0.01f));
            Assert.That(restored.Effects.Count,
                Is.EqualTo(enemy.Effects.Count));
            if (enemy.Effects.Count > 0)
            {
                Assert.That(restored.Effects[0].Stacks.Count,
                    Is.EqualTo(enemy.Effects[0].Stacks.Count));
                Assert.That(restored.Effects[0].Stacks[0].RemainingDuration,
                    Is.LessThanOrEqualTo(
                        enemy.Effects[0].Stacks[0].RemainingDuration));
            }
        }
    }

    private static IEnumerator LoadCityAndWaitReady()
    {
        yield return SceneManager.LoadSceneAsync(
            GameModeScenePaths.Entry);
        yield return null;
        ModeEntryView entry = Object.FindAnyObjectByType<ModeEntryView>();
        Assert.That(entry, Is.Not.Null);
        entry.GetButton(GameModeId.SoloBattle).onClick.Invoke();

        float routeTimeout = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < routeTimeout &&
               (SceneManager.GetActiveScene().path !=
                    GameModeScenePaths.BattlePreparation ||
                GameModeContext.IsTransitioning ||
                GameModeFlowController.Instance.IsLoading))
        {
            yield return null;
        }

        BattleCharacterSelectionView preparation =
            Object.FindAnyObjectByType<BattleCharacterSelectionView>();
        Assert.That(preparation, Is.Not.Null);
        Assert.That(preparation.ConfirmButton, Is.Not.Null);
        preparation.ConfirmButton.onClick.Invoke();
        yield return WaitReady(null);
        Assert.That(GameModeContext.IsActive(
            GameModeId.SoloBattle, GameModeStage.Battle), Is.True);
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
                (bootstrap.Director.ActiveEnemies.Count > 0 ||
                 bootstrap.Director.EncounterEnemies.Count > 0) &&
                !RunSnapshotSession.HasPendingWorldRestore)
            {
                yield break;
            }
            yield return null;
        }
        Assert.Fail("Issue51 战局未在时限内恢复完成：" +
                    RunSnapshotSession.LastMessage);
    }

    private static EnemySpawnHandle FirstEnemy(WaveDirector director)
    {
        return director.ActiveEnemies.Values
            .Concat(director.EncounterEnemies.Values)
            .First();
    }

    private static bool TryGetEnemy(
        WaveDirector director,
        int spawnId,
        out EnemySpawnHandle enemy)
    {
        return director.ActiveEnemies.TryGetValue(spawnId, out enemy) ||
               director.EncounterEnemies.TryGetValue(spawnId, out enemy);
    }
}
