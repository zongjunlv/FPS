using System.Collections;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue74TutorialLocomotionTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator JumpRequiresTakeoffAirborneAndLandingCycle()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorialAtStep(4);
                TutorialFlowController flow = FindFlow();
                TutorialLocomotionEvidenceTracker tracker = FindTracker();
                PlayerController player = flow.Environment.PlayerRig.Player;

                float takeoffTimeout = Time.realtimeSinceStartup + 2f;
                while (tracker.JumpPhase != TutorialJumpPhase.Airborne &&
                       Time.realtimeSinceStartup < takeoffTimeout)
                {
                    if (player.IsGrounded)
                    {
                        Press(keyboard.spaceKey);
                        yield return null;
                        Release(keyboard.spaceKey);
                    }
                    yield return null;
                }

                Assert.That(tracker.JumpPhase,
                    Is.EqualTo(TutorialJumpPhase.Airborne));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(4));

                Press(keyboard.spaceKey);
                yield return null;
                Release(keyboard.spaceKey);
                yield return null;
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(4),
                    "空中重复按键不能提前完成跳跃步骤。");

                float landingTimeout = Time.realtimeSinceStartup + 4f;
                while (flow.Progression.CurrentStepIndex == 4 &&
                       Time.realtimeSinceStartup < landingTimeout)
                {
                    yield return null;
                }

                float groundedTimeout = Time.realtimeSinceStartup + 1f;
                while (!player.IsGrounded &&
                       Time.realtimeSinceStartup < groundedTimeout)
                {
                    yield return null;
                }
                Assert.That(player.IsGrounded, Is.True);
                yield return null;
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(5),
                    $"跳跃证据未结算：phase={tracker.JumpPhase}, " +
                    $"rise={tracker.JumpRise:0.000}, " +
                    $"airborne={tracker.JumpAirborneDuration:0.000}s, " +
                    $"grounded={player.IsGrounded}/" +
                    $"{tracker.LastGroundedSample}, " +
                    $"vertical={player.VerticalVelocity:0.000}, " +
                    $"height={player.transform.position.y:0.000}");
                Assert.That(flow.Progression.CurrentStep.EvidenceType,
                    Is.EqualTo(TutorialEvidenceType.Sprint));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator SprintRequiresRealSprintStateAndForwardDistance()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorialAtStep(5);
                TutorialFlowController flow = FindFlow();
                PlayerController player = flow.Environment.PlayerRig.Player;
                Vector3 start = player.transform.position;

                Press(keyboard.wKey);
                float walkUntil = Time.realtimeSinceStartup + 0.35f;
                while (Time.realtimeSinceStartup < walkUntil)
                {
                    yield return null;
                }
                Release(keyboard.wKey);
                yield return null;
                Assert.That(flow.Progression.CurrentValue,
                    Is.LessThan(0.05f),
                    "普通步行不能累计冲刺里程。");
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(5),
                    "普通步行不能完成冲刺步骤。");

                InputSystem.QueueStateEvent(
                    keyboard,
                    new KeyboardState(Key.W, Key.LeftShift));
                InputSystem.Update();
                bool observedSprinting = false;
                bool observedSprintInput = false;
                float observedForwardInput = 0f;
                PlayerInputReader input = player.GetComponent<
                    PlayerInputReader>();
                float timeout = Time.realtimeSinceStartup + 3f;
                while (flow.Progression.CurrentStepIndex == 5 &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                    observedSprinting |= player.IsSprinting;
                    observedSprintInput |= input.SprintHeld;
                    observedForwardInput = Mathf.Max(
                        observedForwardInput,
                        input.Move.y);
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                yield return null;

                Vector3 localDisplacement = Quaternion.Inverse(
                    player.transform.rotation) *
                    (player.transform.position - start);
                Assert.That(observedSprinting, Is.True,
                    $"冲刺状态未出现：SprintHeld={observedSprintInput}, " +
                    $"MoveY={observedForwardInput:0.00}, " +
                    $"Step={flow.Progression.CurrentStepIndex}, " +
                    $"Value={flow.Progression.CurrentValue:0.00}");
                Assert.That(localDisplacement.z, Is.GreaterThan(2f));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(6));
                Assert.That(player.IsCrouching, Is.False,
                    "进入下蹲教学前必须恢复站立状态。");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator BlockedStandDoesNotCompleteCrouchStep()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            GameObject ceiling = null;
            try
            {
                yield return EnterTutorialAtStep(6);
                TutorialFlowController flow = FindFlow();
                TutorialLocomotionEvidenceTracker tracker = FindTracker();
                PlayerController player = flow.Environment.PlayerRig.Player;

                Press(keyboard.cKey);
                yield return null;
                Release(keyboard.cKey);
                yield return null;
                Assert.That(player.IsCrouching, Is.True);
                Assert.That(flow.Progression.CurrentValue, Is.EqualTo(1f));
                Assert.That(tracker.CrouchPhase,
                    Is.EqualTo(TutorialCrouchPhase.AwaitingStand));

                ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ceiling.name = "Tutorial Crouch Clearance Blocker";
                ceiling.transform.position =
                    player.transform.position + Vector3.up * 1.45f;
                ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
                Physics.SyncTransforms();

                Press(keyboard.cKey);
                yield return null;
                Release(keyboard.cKey);
                yield return null;
                Assert.That(player.IsCrouching, Is.True);
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(6));
                Assert.That(flow.Progression.CurrentValue, Is.EqualTo(1f),
                    "受阻的站起操作不能计为完成。");

                Object.Destroy(ceiling);
                ceiling = null;
                yield return null;
                Physics.SyncTransforms();

                Press(keyboard.cKey);
                yield return null;
                Release(keyboard.cKey);
                yield return null;
                Assert.That(player.IsCrouching, Is.False);
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(7));
            }
            finally
            {
                if (ceiling != null)
                {
                    Object.Destroy(ceiling);
                }
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private static TutorialFlowController FindFlow()
        {
            return Object.FindAnyObjectByType<TutorialFlowController>();
        }

        private static TutorialLocomotionEvidenceTracker FindTracker()
        {
            return Object.FindAnyObjectByType<
                TutorialLocomotionEvidenceTracker>();
        }

        private static IEnumerator EnterTutorialAtStep(int targetStep)
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            ModeEntryView entry = Object.FindAnyObjectByType<ModeEntryView>();
            entry.GetButton(GameModeId.Tutorial).onClick.Invoke();

            float timeout = Time.realtimeSinceStartup + 20f;
            TutorialFlowController flow = null;
            while (Time.realtimeSinceStartup < timeout)
            {
                flow = FindFlow();
                TutorialLocomotionEvidenceTracker tracker = FindTracker();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    flow != null && flow.IsInitialized &&
                    tracker != null && tracker.enabled)
                {
                    break;
                }
                yield return null;
            }

            Assert.That(flow, Is.Not.Null);
            AdvanceMovementSteps(flow);
            if (targetStep >= 5)
            {
                flow.ReportEvidence(TutorialEvidenceType.Jump);
            }
            if (targetStep >= 6)
            {
                flow.ReportEvidence(TutorialEvidenceType.Sprint, 2f);
            }
            yield return null;
            Assert.That(flow.Progression.CurrentStepIndex,
                Is.EqualTo(targetStep));

            PlayerController player = flow.Environment.PlayerRig.Player;
            float groundedTimeout = Time.realtimeSinceStartup + 2f;
            while (!player.IsGrounded &&
                   Time.realtimeSinceStartup < groundedTimeout)
            {
                yield return null;
            }
            Assert.That(player.IsGrounded, Is.True,
                "教学输入测试开始前，玩家未稳定落在训练场地面。");
            yield return null;
        }

        private static void AdvanceMovementSteps(TutorialFlowController flow)
        {
            flow.ReportEvidence(TutorialEvidenceType.MoveForwardDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.MoveBackwardDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.MoveLeftDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.MoveRightDistance, 1f);
        }
    }
}
