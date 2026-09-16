using System;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue71TutorialContentTests
{
    private const string RigPath =
        "Assets/Resources/Player/PlayerGameplayRig.prefab";

    [Test]
    public void TutorialUsesOneReusablePlayableRigAndNoMenuCamera()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            PlayerGameplayRig[] rigs = Components<PlayerGameplayRig>(scene);
            Assert.That(rigs, Has.Length.EqualTo(1));
            Assert.That(rigs[0].TryValidate(out string error), Is.True, error);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    rigs[0].gameObject),
                Is.EqualTo(RigPath));
            Assert.That(Components<Camera>(scene), Has.Length.EqualTo(1));
            Assert.That(Components<AudioListener>(scene), Has.Length.EqualTo(1));
            Assert.That(Components<Camera>(scene).Single().orthographic,
                Is.False);
            Assert.That(Components<Camera>(scene).Single().name,
                Is.Not.EqualTo("Menu Camera"));
            Assert.That(Components<GameModeSceneBootstrap>(scene), Is.Empty);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void TutorialDeclaresSafeMovementShootingAndDamageStations()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialTrainingEnvironment environment =
                Components<TutorialTrainingEnvironment>(scene).Single();
            Assert.That(environment.TryValidate(out string error),
                Is.True,
                error);
            Assert.That(environment.SafetyBoundaries.Count, Is.EqualTo(4));
            Assert.That(environment.MovementFloor.isTrigger, Is.False);
            Assert.That(environment.ShootingWall.isTrigger, Is.False);
            Assert.That(environment.RecoveryVolume.Trigger.isTrigger, Is.True);
            Assert.That(environment.ContainsPlayablePoint(
                environment.SpawnPoint.position), Is.True);
            Assert.That(environment.ContainsPlayablePoint(
                environment.DamageTrainingPoint.position), Is.True);

            Vector3 wallDirection =
                environment.ShootingWall.bounds.center -
                environment.SpawnPoint.position;
            Assert.That(wallDirection.sqrMagnitude, Is.GreaterThan(1f));
            Assert.That(environment.ShootingWall.bounds.size.x,
                Is.GreaterThanOrEqualTo(12f));
            Assert.That(environment.ShootingWall.bounds.size.y,
                Is.GreaterThanOrEqualTo(5f));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void TutorialContainsNoBattleEnemyOrMissionComposition()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            AssertNone<EnemyController>(scene);
            AssertNone<CityNewWaveBootstrap>(scene);
            AssertNone<WaveDirector>(scene);
            AssertNone<EncounterRuntimeController>(scene);
            AssertNone<CityNewMissionController>(scene);
            AssertNone<CityNewTerminalMissionBootstrap>(scene);
            AssertNone<TerminalInteractable>(scene);
            AssertNone<RuntimeNavMeshBootstrap>(scene);
            AssertNone<EnemyPerceptionController>(scene);
            AssertNone<EnemyPerceptionScheduler>(scene);
            AssertNone<EnemySquadCoordinator>(scene);
            AssertNone<EnemySpatialIndexService>(scene);
            AssertNone<PooledEnemyFactory>(scene);
            AssertNone<AddressableEnemyFactory>(scene);
            AssertNone<CityNewPlayerModeInstaller>(scene);
            AssertNone<CityNewInventoryBootstrap>(scene);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }

    private static void AssertNone<T>(Scene scene) where T : Component
    {
        Assert.That(Components<T>(scene), Is.Empty, typeof(T).Name);
    }
}
