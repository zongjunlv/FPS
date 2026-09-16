using System.Collections;
using System.Linq;
using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue69
{
    public sealed class Issue69GameModeFlowTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator EntryBuildsThreeNavigableModesWithoutGameplay()
        {
            yield return LoadAndSettle(GameModeScenePaths.Entry);

            ModeEntryView view = Object.FindFirstObjectByType<ModeEntryView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.Buttons, Has.Count.EqualTo(3));
            Assert.That(view.Buttons.Select(button =>
                    button.GetComponent<GameModeEntryButton>().Mode),
                Is.EquivalentTo(new[]
                {
                    GameModeId.Tutorial,
                    GameModeId.SoloBattle,
                    GameModeId.Coop
                }));
            Assert.That(Object.FindObjectsByType<EventSystem>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None),
                Has.Length.EqualTo(1));
            Assert.That(EventSystem.current.GetComponent<
                    InputSystemUIInputModule>(),
                Is.Not.Null);
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(view.Buttons[0].gameObject));
            Assert.That(Object.FindFirstObjectByType<PlayerController>(),
                Is.Null);
            Assert.That(Object.FindFirstObjectByType<UnifiedGameHud>(),
                Is.Null);
            Assert.That(GameModeContext.CurrentMode, Is.EqualTo(GameModeId.None));
            Assert.That(GameModeContext.CurrentStage,
                Is.EqualTo(GameModeStage.Entry));
        }

        [UnityTest]
        public IEnumerator KeyboardNavigationAndSubmitEnterBattlePreparation()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return LoadAndSettle(GameModeScenePaths.Entry);
                ModeEntryView view =
                    Object.FindFirstObjectByType<ModeEntryView>();

                Press(keyboard.downArrowKey);
                yield return null;
                Release(keyboard.downArrowKey);
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject,
                    Is.EqualTo(view.Buttons[1].gameObject));

                Press(keyboard.enterKey);
                yield return null;
                Release(keyboard.enterKey);
                yield return WaitForScene(
                    GameModeScenePaths.BattlePreparation);
                Assert.That(GameModeContext.IsActive(
                    GameModeId.SoloBattle,
                    GameModeStage.BattlePreparation), Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator AllConfiguredRoutesLoadAndCanReturnToEntry()
        {
            GameModeId[] modes =
            {
                GameModeId.Tutorial,
                GameModeId.SoloBattle,
                GameModeId.Coop
            };
            string[] paths =
            {
                GameModeScenePaths.Tutorial,
                GameModeScenePaths.BattlePreparation,
                GameModeScenePaths.CoopLogin
            };
            GameModeStage[] stages =
            {
                GameModeStage.Tutorial,
                GameModeStage.BattlePreparation,
                GameModeStage.CoopLogin
            };

            for (int index = 0; index < modes.Length; index++)
            {
                yield return LoadAndSettle(GameModeScenePaths.Entry);
                ModeEntryView entry =
                    Object.FindFirstObjectByType<ModeEntryView>();
                entry.GetButton(modes[index]).onClick.Invoke();
                yield return WaitForScene(paths[index]);

                Assert.That(GameModeContext.IsActive(modes[index], stages[index]),
                    Is.True, paths[index]);
                ModeDestinationView destination = Object.FindFirstObjectByType<
                    ModeDestinationView>();
                Assert.That(destination, Is.Not.Null, paths[index]);
                Assert.That(Object.FindFirstObjectByType<PlayerController>(),
                    Is.Null, paths[index]);

                destination.ReturnButton.onClick.Invoke();
                yield return WaitForScene(GameModeScenePaths.Entry);
                Assert.That(GameModeContext.IsActive(
                    GameModeId.None, GameModeStage.Entry), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator Issue70GameplayRouteRejectsEntryBypass()
        {
            yield return LoadAndSettle(GameModeScenePaths.Entry);
            GameModeFlowController flow = GameModeFlowController.Instance;

            Assert.That(flow.TryEnterGameplay(GameModeId.SoloBattle), Is.False);
            yield return null;

            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo(GameModeScenePaths.Entry));
            Assert.That(GameModeContext.IsActive(
                GameModeId.None, GameModeStage.Entry), Is.True);
            Assert.That(flow.FailureMessage, Does.Contain("准备流程"));
            Assert.That(Object.FindFirstObjectByType<ModeEntryView>()
                .VisibleStatus, Does.Contain("准备流程"));
        }

        [UnityTest]
        public IEnumerator MissingSceneProducesVisibleFailureWithoutLeavingMenu()
        {
            yield return LoadAndSettle(GameModeScenePaths.Entry);
            GameModeFlowController flow = GameModeFlowController.Instance;
            var catalog = ScriptableObject.CreateInstance<GameModeCatalog>();
            catalog.Configure(GameModeScenePaths.Entry, new[]
            {
                Definition(GameModeId.Tutorial,
                    "Assets/Scenes/Modes/MissingTutorial.unity",
                    GameModeStage.Tutorial),
                Definition(GameModeId.SoloBattle,
                    GameModeScenePaths.BattlePreparation,
                    GameModeStage.BattlePreparation,
                    GameModeScenePaths.CityNew,
                    GameModeStage.Battle),
                Definition(GameModeId.Coop,
                    GameModeScenePaths.CoopLogin,
                    GameModeStage.CoopLogin)
            });
            flow.Configure(catalog);

            Assert.That(flow.TryEnterMode(GameModeId.Tutorial), Is.False);
            yield return null;

            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo(GameModeScenePaths.Entry));
            Assert.That(flow.FailureMessage, Does.Contain("目标场景不可用"));
            Assert.That(Object.FindFirstObjectByType<ModeEntryView>()
                .VisibleStatus, Does.Contain("目标场景不可用"));
            Object.Destroy(catalog);
        }

        [UnityTest]
        public IEnumerator ReturningFromConnectedCoopReleasesNetworkRuntime()
        {
            yield return LoadAndSettle(GameModeScenePaths.CoopLogin);
            CoopSessionController session =
                Object.FindFirstObjectByType<CoopSessionController>();
            Assert.That(session, Is.Not.Null);
            var endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = 17969;
            Assert.That(session.StartDirect(true, endpoint), Is.True,
                session.LastFailure);
            yield return null;
            Assert.That(Object.FindFirstObjectByType<NetworkManager>()
                .IsListening, Is.True);

            Object.FindFirstObjectByType<ModeDestinationView>()
                .ReturnButton.onClick.Invoke();
            yield return WaitForScene(GameModeScenePaths.Entry);
            yield return null;

            Assert.That(Object.FindFirstObjectByType<NetworkManager>(), Is.Null);
            Assert.That(Object.FindFirstObjectByType<CoopSessionController>(),
                Is.Null);
            Assert.That(Object.FindFirstObjectByType<CoopSessionOverlay>(),
                Is.Null);
        }

        [UnityTest]
        public IEnumerator CityNewActivatesSoloBattleBeforeCompositionStarts()
        {
            yield return LoadAndSettle(GameModeScenePaths.CityNew, 4);

            Assert.That(GameModeContext.IsActive(
                GameModeId.SoloBattle, GameModeStage.Battle), Is.True);
            Assert.That(Object.FindFirstObjectByType<GameModeSceneMarker>(),
                Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<PlayerGameplayRig>(),
                Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<
                CityNewPlayerModeInstaller>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<UnifiedGameHud>(),
                Is.Not.Null);
        }

        private static GameModeDefinition Definition(
            GameModeId mode,
            string path,
            GameModeStage stage,
            string gameplayPath = null,
            GameModeStage gameplayStage = GameModeStage.Entry)
        {
            var definition = new GameModeDefinition();
            definition.Configure(
                mode,
                mode.ToString(),
                "test",
                path,
                stage,
                gameplayPath,
                gameplayStage);
            return definition;
        }

        private static IEnumerator LoadAndSettle(string path, int frames = 2)
        {
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
            for (int index = 0; index < frames; index++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForScene(string path)
        {
            for (int frame = 0; frame < 600; frame++)
            {
                if (SceneManager.GetActiveScene().path == path &&
                    !GameModeContext.IsTransitioning)
                {
                    yield return null;
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Timed out loading mode scene: " + path);
        }
    }
}
