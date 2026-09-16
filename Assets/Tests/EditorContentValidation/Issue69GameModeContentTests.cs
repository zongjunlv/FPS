using System.Collections.Generic;
using System.IO;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public sealed class Issue69GameModeContentTests
{
    private const string CatalogPath =
        "Assets/Resources/GameModes/GameModeCatalog.asset";

    [Test]
    public void CatalogDefinesExactlyThreeStableRoutes()
    {
        GameModeCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameModeCatalog>(CatalogPath);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryValidate(out string error), Is.True, error);
        Assert.That(catalog.EntryScenePath, Is.EqualTo(GameModeScenePaths.Entry));
        Assert.That(catalog.Modes.Count, Is.EqualTo(3));
        Assert.That(catalog.Modes.Select(route => route.StableId),
            Is.EquivalentTo(new[]
            {
                GameModeIds.Tutorial,
                GameModeIds.SoloBattle,
                GameModeIds.Coop
            }));
        AssertRoute(catalog, GameModeId.Tutorial,
            GameModeStage.Tutorial, GameModeScenePaths.Tutorial);
        AssertRoute(catalog, GameModeId.SoloBattle,
            GameModeStage.BattlePreparation,
            GameModeScenePaths.BattlePreparation);
        AssertRoute(catalog, GameModeId.Coop,
            GameModeStage.CoopLogin, GameModeScenePaths.CoopLogin);
    }

    [Test]
    public void BuildStartsAtModeEntryAndContainsAllRoutes()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        Assert.That(scenes, Is.Not.Empty);
        Assert.That(scenes[0].enabled, Is.True);
        Assert.That(scenes[0].path, Is.EqualTo(GameModeScenePaths.Entry));

        string[] enabled = scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        Assert.That(enabled, Does.Contain(GameModeScenePaths.Tutorial));
        Assert.That(enabled,
            Does.Contain(GameModeScenePaths.BattlePreparation));
        Assert.That(enabled, Does.Contain(GameModeScenePaths.CoopLogin));
        Assert.That(enabled, Does.Contain(GameModeScenePaths.CityNew));
    }

    [Test]
    public void EveryFlowSceneHasOneMatchingModeMarker()
    {
        AssertMarker(GameModeScenePaths.Entry,
            GameModeId.None, GameModeStage.Entry, expectBootstrap: true);
        AssertMarker(GameModeScenePaths.Tutorial,
            GameModeId.Tutorial, GameModeStage.Tutorial,
            expectBootstrap: true);
        AssertMarker(GameModeScenePaths.BattlePreparation,
            GameModeId.SoloBattle, GameModeStage.BattlePreparation,
            expectBootstrap: true);
        AssertMarker(GameModeScenePaths.CoopLogin,
            GameModeId.Coop, GameModeStage.CoopLogin,
            expectBootstrap: true);
        AssertMarker(GameModeScenePaths.CityNew,
            GameModeId.SoloBattle, GameModeStage.Battle,
            expectBootstrap: false);
    }

    [Test]
    public void RuntimeCompositionUsesStableModeContextInsteadOfSceneName()
    {
        string[] guardedFiles =
        {
            "Assets/Scripts/AI/CityNewWaveBootstrap.cs",
            "Assets/Scripts/AI/RuntimeNavMeshBootstrap.cs",
            "Assets/Scripts/AI/EnemySquadCoordinator.cs",
            "Assets/Scripts/AI/CityNewEnemySquadBootstrap.cs",
            "Assets/Scripts/Inventory/CityNewInventoryBootstrap.cs",
            "Assets/Scripts/Interaction/CityNewTerminalMissionBootstrap.cs",
            "Assets/Scripts/World/CityNewModularLayoutBootstrap.cs",
            "Assets/Scripts/Player/CityNewPlayerModeInstaller.cs"
        };

        foreach (string path in guardedFiles)
        {
            string source = File.ReadAllText(path);
            Assert.That(source, Does.Contain("GameModeContext"), path);
            Assert.That(source, Does.Not.Contain(
                "GetActiveScene().name"), path);
        }
    }

    private static void AssertRoute(
        GameModeCatalog catalog,
        GameModeId mode,
        GameModeStage stage,
        string path)
    {
        Assert.That(catalog.TryGet(mode, out GameModeDefinition route),
            Is.True);
        Assert.That(route.StableId, Is.EqualTo(GameModeIds.ToStableId(mode)));
        Assert.That(route.EntryStage, Is.EqualTo(stage));
        Assert.That(route.EntryScenePath, Is.EqualTo(path));
    }

    private static void AssertMarker(
        string path,
        GameModeId mode,
        GameModeStage stage,
        bool expectBootstrap)
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            List<GameModeSceneMarker> markers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    GameModeSceneMarker>(true))
                .ToList();
            Assert.That(markers, Has.Count.EqualTo(1), path);
            Assert.That(markers[0].Mode, Is.EqualTo(mode), path);
            Assert.That(markers[0].Stage, Is.EqualTo(stage), path);
            int bootstraps = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    GameModeSceneBootstrap>(true))
                .Count();
            Assert.That(bootstraps,
                Is.EqualTo(expectBootstrap ? 1 : 0), path);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
