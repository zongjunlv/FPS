using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue79DualModeContentTests
{
    [Test]
    public void TutorialContainsOnlyOneStaticDamageDummyAndNoEnemyAi()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            Assert.That(FindAll<TutorialFlowController>(scene).Length,
                Is.EqualTo(1));
            Assert.That(FindAll<TutorialTrainingEnvironment>(scene).Length,
                Is.EqualTo(1));
            Assert.That(FindAll<TutorialDamageTrainingTarget>(scene).Length,
                Is.EqualTo(1));
            Assert.That(FindAll<EnemyController>(scene), Is.Empty);
            Assert.That(FindAll<EnemyCombatController>(scene), Is.Empty);
            Assert.That(FindAll<EnemyPerceptionController>(scene), Is.Empty);
            Assert.That(FindAll<WaveDirector>(scene), Is.Empty);
            Assert.That(FindAll<CityNewWaveBootstrap>(scene), Is.Empty);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void CityNewHasNoTutorialSceneContent()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.CityNew);
        try
        {
            Assert.That(FindAll<TutorialFlowController>(scene), Is.Empty);
            Assert.That(FindAll<TutorialTrainingEnvironment>(scene), Is.Empty);
            Assert.That(FindAll<TutorialDamageTrainingTarget>(scene), Is.Empty);
            Assert.That(FindAll<TutorialShootingTarget>(scene), Is.Empty);
            Assert.That(FindAll<TutorialTopHud>(scene), Is.Empty);
            Assert.That(FindAll<TutorialCompletionView>(scene), Is.Empty);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void DualModeScenesAreEnabledInBuildSettings()
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(value => value.enabled)
            .Select(value => value.path)
            .ToArray();
        CollectionAssert.Contains(enabledScenes, GameModeScenePaths.Entry);
        CollectionAssert.Contains(
            enabledScenes,
            GameModeScenePaths.Tutorial);
        CollectionAssert.Contains(
            enabledScenes,
            GameModeScenePaths.BattlePreparation);
        CollectionAssert.Contains(enabledScenes, GameModeScenePaths.CityNew);
    }

    private static T[] FindAll<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }
}
