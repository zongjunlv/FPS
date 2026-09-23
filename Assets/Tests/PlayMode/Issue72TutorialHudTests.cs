using System.Collections;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue72TutorialHudTests
    {
        [UnityTearDown]
        public IEnumerator ClearTutorialScene()
        {
            Scene tutorial = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("Issue72 Empty Test Scene");
            SceneManager.SetActiveScene(empty);
            if (tutorial.IsValid() && tutorial.isLoaded)
                yield return SceneManager.UnloadSceneAsync(tutorial);
            GameModeFlowController.ResetRuntimeForTests();
        }

        [UnityTest]
        public IEnumerator TopGuideIsReadableSafeAndDoesNotBlockGameplay()
        {
            yield return EnterTutorial();

            TutorialFlowController flow =
                Object.FindAnyObjectByType<TutorialFlowController>();
            TutorialTopHud hud = flow.Hud;
            CanvasGroup group = hud.GetComponent<CanvasGroup>();

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(hud.RootCanvas.renderMode,
                Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(hud.RootCanvas.sortingOrder, Is.GreaterThan(100));
            Assert.That(hud.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(group.interactable, Is.False);
            Assert.That(hud.GetComponentsInChildren<Graphic>(true)
                .All(graphic => !graphic.raycastTarget), Is.True);
            Assert.That(hud.Panel.anchorMin.y, Is.EqualTo(1f));
            Assert.That(hud.Panel.anchorMax.y, Is.EqualTo(1f));
            Assert.That(hud.Panel.anchoredPosition.y, Is.LessThan(0f));
            Assert.That(hud.SafeArea.GetComponent<SafeAreaFitter>(), Is.Not.Null);
            Assert.That(hud.ChapterText.font.HasCharacter('中', false, true),
                Is.True);
            StringAssert.Contains("第一章", hud.ChapterText.text);
            StringAssert.Contains("W", hud.InstructionText.text);
            StringAssert.Contains("步骤 1/15", hud.ProgressText.text);

            SafeAreaFitter fitter = hud.SafeArea.GetComponent<SafeAreaFitter>();
            fitter.Apply(new Rect(100f, 50f, 1720f, 980f), 1920, 1080);
            Assert.That(hud.SafeArea.anchorMin.x, Is.EqualTo(100f / 1920f)
                .Within(0.0001f));
            Assert.That(hud.SafeArea.anchorMax.y, Is.EqualTo(1030f / 1080f)
                .Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator HudOnlyAdvancesForCurrentStepEvidence()
        {
            yield return EnterTutorial();

            TutorialFlowController flow =
                Object.FindAnyObjectByType<TutorialFlowController>();
            Assert.That(flow.ReportEvidence(TutorialEvidenceType.Jump), Is.False);
            Assert.That(flow.Progression.CurrentStepIndex, Is.Zero);

            Assert.That(flow.ReportEvidence(
                TutorialEvidenceType.MoveForwardDistance, 0.5f, "first"),
                Is.True);
            Assert.That(flow.ReportEvidence(
                TutorialEvidenceType.MoveForwardDistance, 0.5f, "first"),
                Is.False);
            Assert.That(flow.ReportEvidence(
                TutorialEvidenceType.MoveForwardDistance, 0.5f, "second"),
                Is.True);
            yield return null;

            Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(1));
            StringAssert.Contains("向后移动", flow.Hud.StepText.text);
            StringAssert.Contains("步骤 2/15", flow.Hud.ProgressText.text);
            Assert.That(flow.Hud.FeedbackText.gameObject.activeSelf, Is.True);
            StringAssert.Contains("向前移动", flow.Hud.FeedbackText.text);
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
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    flow != null && flow.IsInitialized &&
                    flow.Hud != null && flow.Hud.IsBound)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("教学流程与顶部提示未在时限内完成初始化。");
        }
    }
}
