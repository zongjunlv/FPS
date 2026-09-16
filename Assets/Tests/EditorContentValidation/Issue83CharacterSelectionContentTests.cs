using System.IO;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public sealed class Issue83CharacterSelectionContentTests
{
    [Test]
    public void BattlePreparationOwnsSelectionRouteAndThreeValidCharacters()
    {
        PlayerAppearanceCatalog catalog =
            AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
                PlayerAppearanceCatalog.DefaultAssetPath);
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryValidate(out string error), Is.True, error);
        Assert.That(catalog.Definitions.Count, Is.GreaterThanOrEqualTo(3));

        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.BattlePreparation);
        try
        {
            GameModeSceneMarker marker = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    GameModeSceneMarker>(true)).Single();
            Assert.That(marker.Mode, Is.EqualTo(GameModeId.SoloBattle));
            Assert.That(marker.Stage,
                Is.EqualTo(GameModeStage.BattlePreparation));
            Assert.That(scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    GameModeSceneBootstrap>(true)).Count(), Is.EqualTo(1));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void ModeBootstrapAndCityNewInstallerUseStableAppearanceIdFlow()
    {
        string bootstrap = File.ReadAllText(
            "Assets/Scripts/GameModes/GameModeSceneBootstrap.cs");
        string installer = File.ReadAllText(
            "Assets/Scripts/Player/CityNewPlayerModeInstaller.cs");
        Assert.That(bootstrap, Does.Contain("BattleCharacterSelectionView"));
        Assert.That(bootstrap, Does.Contain("GameModeStage.BattlePreparation"));
        Assert.That(installer, Does.Contain("PlayerAppearanceSelection"));
        Assert.That(installer, Does.Contain("PlayerAppearanceHost"));
        Assert.That(installer, Does.Not.Contain("AssetDatabase"));
    }
}
