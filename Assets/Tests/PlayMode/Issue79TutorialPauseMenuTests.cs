using System.Collections;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue79TutorialPauseMenuTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator EscapePauseCanContinueCurrentTutorialStep()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorial();
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();
                int stepIndex = flow.Progression.CurrentStepIndex;

                Press(keyboard.escapeKey);
                yield return null;
                Release(keyboard.escapeKey);
                yield return null;

                Button continueButton = FindVisibleButton("继续教学");
                Assert.That(continueButton, Is.Not.Null);
                continueButton.onClick.Invoke();
                yield return null;

                Assert.That(flow.Environment.PlayerRig.Player.IsPaused,
                    Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(flow.Progression.CurrentStepIndex,
                    Is.EqualTo(stepIndex));
                Assert.That(FindVisibleButton("继续教学"), Is.Null);
            }
            finally
            {
                if (keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                Time.timeScale = 1f;
            }
        }

        [UnityTest]
        public IEnumerator EscapePauseCanExitTutorialBeforeCompletion()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorial();
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();
                Assert.That(flow.Progression.IsComplete, Is.False);

                Press(keyboard.escapeKey);
                yield return null;
                Release(keyboard.escapeKey);
                yield return null;

                PlayerController player = flow.Environment.PlayerRig.Player;
                Assert.That(player.IsPaused, Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(0f));

                Button exitButton = FindVisibleButton(
                    "退出教学并返回大厅");
                Assert.That(exitButton, Is.Not.Null,
                    "教学未完成时按 ESC 也必须提供返回模式大厅的入口。");
                exitButton.onClick.Invoke();
                yield return WaitForScene(GameModeScenePaths.Entry);

                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(GameModeContext.IsActive(
                    GameModeId.None, GameModeStage.Entry), Is.True);
                Assert.That(Object.FindAnyObjectByType<ModeEntryView>(),
                    Is.Not.Null);
                Assert.That(Object.FindAnyObjectByType<TutorialFlowController>(),
                    Is.Null);
                Assert.That(Object.FindAnyObjectByType<PlayerController>(),
                    Is.Null);
            }
            finally
            {
                if (keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                Time.timeScale = 1f;
            }
        }

        private static Button FindVisibleButton(string label)
        {
            return Object.FindObjectsByType<Button>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None)
                .FirstOrDefault(button =>
                    button.GetComponentInChildren<TMP_Text>(true)?.text ==
                    label);
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

            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    GameModeFlowController.Instance != null &&
                    !GameModeFlowController.Instance.IsLoading &&
                    flow != null && flow.IsInitialized)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("教学场景未在期限内完成初始化。");
        }

        private static IEnumerator WaitForScene(string scenePath)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().path == scenePath &&
                    !GameModeContext.IsTransitioning)
                {
                    yield return null;
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"场景加载超时：{scenePath}");
        }
    }
}
