using System.Collections;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue71TutorialRuntimeTests
    {
        [UnityTest]
        public IEnumerator TutorialEntryCreatesOnePlayableRigAndNoEnemies()
        {
            yield return EnterTutorial();

            TutorialTrainingEnvironment environment =
                Object.FindAnyObjectByType<TutorialTrainingEnvironment>();
            PlayerGameplayRig rig = environment.PlayerRig;
            Assert.That(GameModeContext.IsActive(
                GameModeId.Tutorial,
                GameModeStage.Tutorial), Is.True);
            Assert.That(Object.FindObjectsByType<PlayerGameplayRig>(
                FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(rig.TryValidate(out string error), Is.True, error);
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.Combat.IsInitialized, Is.True);
            Assert.That(rig.Weapons.Count, Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<UnifiedGameHud>(
                FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindAnyObjectByType<ModeDestinationView>(),
                Is.Null);
            if (!Application.isBatchMode)
            {
                Assert.That(Cursor.lockState,
                    Is.EqualTo(CursorLockMode.Locked));
            }
            AssertNoBattleRuntime();

            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
            }

            AssertNoBattleRuntime();
        }

        [UnityTest]
        public IEnumerator SafetyBoundariesCloseArenaAndRecoveryReturnsPlayer()
        {
            yield return EnterTutorial();

            TutorialTrainingEnvironment environment =
                Object.FindAnyObjectByType<TutorialTrainingEnvironment>();
            PlayerController player = environment.PlayerRig.Player;
            Vector3[] directions =
            {
                Vector3.left,
                Vector3.right,
                Vector3.back,
                Vector3.forward
            };
            Vector3[] origins =
            {
                new Vector3(-5f, 1f, 0f),
                new Vector3(5f, 1f, 0f),
                new Vector3(10f, 1f, -5f),
                new Vector3(-10f, 1f, 0f)
            };
            Collider[] guards = environment.SafetyBoundaries.ToArray();
            for (int index = 0; index < directions.Length; index++)
            {
                Assert.That(Physics.Raycast(
                        origins[index],
                        directions[index],
                        out RaycastHit hit,
                        30f,
                        Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore),
                    Is.True,
                    $"安全边界 {index + 1} 未封闭。");
                Assert.That(guards, Does.Contain(hit.collider));
            }

            Assert.That(player.TryRestoreSnapshotPose(
                    new Vector3(0f, -7f, 0f),
                    Quaternion.identity,
                    0f,
                    false,
                    out string poseError),
                Is.True,
                poseError);
            Assert.That(environment.RecoveryVolume.Recover(player), Is.True,
                environment.RecoveryVolume.LastRecoveryError);
            yield return null;

            Assert.That(player.transform.position.x,
                Is.EqualTo(environment.SpawnPoint.position.x).Within(0.05f));
            Assert.That(player.transform.position.z,
                Is.EqualTo(environment.SpawnPoint.position.z).Within(0.05f));
            Assert.That(environment.ContainsPlayablePoint(
                player.transform.position), Is.True);
            Assert.That(environment.RecoveryVolume.RecoveryCount,
                Is.GreaterThanOrEqualTo(1));
        }

        private static IEnumerator EnterTutorial()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            ModeEntryView entry = Object.FindAnyObjectByType<ModeEntryView>();
            Assert.That(entry, Is.Not.Null);
            entry.GetButton(GameModeId.Tutorial).onClick.Invoke();

            float timeout = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < timeout)
            {
                TutorialTrainingEnvironment environment =
                    Object.FindAnyObjectByType<TutorialTrainingEnvironment>();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    environment != null && environment.IsReady &&
                    environment.PlayerRig.CompositionRoot.IsInitialized &&
                    environment.PlayerRig.CompositionRoot.HudBootstrap != null &&
                    environment.PlayerRig.CompositionRoot.HudBootstrap
                        .IsInitialized)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("教学模式未在时限内完成初始化。");
        }

        private static void AssertNoBattleRuntime()
        {
            Assert.That(Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include), Is.Empty);
            Assert.That(Object.FindAnyObjectByType<CityNewWaveBootstrap>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<WaveDirector>(), Is.Null);
            Assert.That(WaveDirector.Active, Is.Null);
            Assert.That(Object.FindAnyObjectByType<EncounterRuntimeController>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<
                CityNewTerminalMissionBootstrap>(), Is.Null);
            Assert.That(Object.FindAnyObjectByType<CityNewMissionController>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<TerminalInteractable>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<EnemyPerceptionController>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<EnemyPerceptionScheduler>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<EnemySquadCoordinator>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<EnemySpatialIndexService>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<PooledEnemyFactory>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<AddressableEnemyFactory>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<RuntimeNavMeshBootstrap>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<CityNewPlayerModeInstaller>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<CityNewInventoryBootstrap>(),
                Is.Null);
        }
    }
}
