using System;
using System.Collections;
using System.Reflection;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode
{
    public abstract class TutorialInputTestFixture : InputTestFixture
    {
        [SetUp]
        public override void Setup()
        {
            // A preceding fixture may leave actions alive across scene tests.
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                action.Disable();
            }

            base.Setup();
            // InputSystemUIInputModule caches one default action asset in a
            // static field; that asset still points at the pre-reset manager.
            FieldInfo defaults = typeof(InputSystemUIInputModule).GetField(
                "defaultActions",
                BindingFlags.Static | BindingFlags.NonPublic);
            (defaults?.GetValue(null) as IDisposable)?.Dispose();
            defaults?.SetValue(null, null);
        }

        [TearDown]
        public override void TearDown()
        {
            // Live tutorial rigs retain actions until the next scene load. Disable
            // them before InputTestFixture restores the native input state.
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                action.Disable();
            }

            base.TearDown();
        }
    }

    public sealed class Issue73TutorialMovementTests : TutorialInputTestFixture
    {
        [UnityTest]
        public IEnumerator WsadStepsCompleteFromActualLocalDisplacement()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorial();
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();
                PlayerController player = flow.Environment.PlayerRig.Player;

                yield return HoldUntilStepAdvances(
                    keyboard.wKey, flow, player, 0, Vector3.forward);
                yield return HoldUntilStepAdvances(
                    keyboard.sKey, flow, player, 1, Vector3.back);
                yield return HoldUntilStepAdvances(
                    keyboard.aKey, flow, player, 2, Vector3.left);
                yield return HoldUntilStepAdvances(
                    keyboard.dKey, flow, player, 3, Vector3.right);

                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(4));
                Assert.That(flow.Progression.CurrentStep.EvidenceType,
                    Is.EqualTo(TutorialEvidenceType.Jump));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator WrongDirectionMovesPlayerShowsProgressAndCannotComplete()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorial();
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();
                TutorialMovementEvidenceTracker tracker =
                    Object.FindAnyObjectByType<TutorialMovementEvidenceTracker>();
                PlayerController player = flow.Environment.PlayerRig.Player;
                Vector3 start = player.transform.position;

                Press(keyboard.sKey);
                float timeout = Time.realtimeSinceStartup + 0.45f;
                while (Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }
                Release(keyboard.sKey);
                yield return null;

                Assert.That(Vector3.Distance(start, player.transform.position),
                    Is.GreaterThan(0.3f));
                Assert.That(flow.Progression.CurrentStepIndex, Is.Zero);
                Assert.That(flow.Progression.CurrentValue,
                    Is.LessThan(0.05f));
                Assert.That(tracker.WrongDirectionDistance,
                    Is.GreaterThan(0.3f));
                Assert.That(flow.Hud.FeedbackText.gameObject.activeSelf,
                    Is.True);
                StringAssert.Contains("方向不符", flow.Hud.FeedbackText.text);
                StringAssert.Contains("m", flow.Hud.FeedbackText.text);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator DiagonalAndHeldInputCannotSkipFutureStep()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorial();
                TutorialFlowController flow =
                    Object.FindAnyObjectByType<TutorialFlowController>();

                Press(keyboard.wKey);
                Press(keyboard.dKey);
                float timeout = Time.realtimeSinceStartup + 3f;
                while (flow.Progression.CurrentStepIndex == 0 &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(1));
                float holdUntil = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < holdUntil)
                {
                    yield return null;
                }
                Release(keyboard.wKey);
                Release(keyboard.dKey);
                yield return null;

                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(1));
                Assert.That(flow.Progression.CurrentStep.EvidenceType,
                    Is.EqualTo(TutorialEvidenceType.MoveBackwardDistance));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private IEnumerator HoldUntilStepAdvances(
            KeyControl key,
            TutorialFlowController flow,
            PlayerController player,
            int expectedStep,
            Vector3 expectedLocalDirection)
        {
            Vector3 start = player.transform.position;
            Quaternion startRotation = player.transform.rotation;
            Press(key);
            float timeout = Time.realtimeSinceStartup + 3f;
            while (flow.Progression.CurrentStepIndex == expectedStep &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            Release(key);
            yield return null;

            Vector3 localDisplacement = Quaternion.Inverse(startRotation) *
                                        (player.transform.position - start);
            Assert.That(flow.Progression.CurrentStepIndex,
                Is.EqualTo(expectedStep + 1),
                $"步骤 {expectedStep + 1} 未由实际位移完成。");
            Assert.That(Vector3.Dot(localDisplacement, expectedLocalDirection),
                Is.GreaterThan(0.8f));
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
                TutorialMovementEvidenceTracker tracker =
                    Object.FindAnyObjectByType<TutorialMovementEvidenceTracker>();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    flow != null && flow.IsInitialized &&
                    tracker != null && tracker.enabled &&
                    tracker.IsTrackingMovementStep)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("WASD 教学未在时限内完成初始化。");
        }
    }
}
