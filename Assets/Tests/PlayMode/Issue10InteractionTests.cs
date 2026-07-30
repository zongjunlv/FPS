using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue10InteractionTests : InputTestFixture
    {
        [Test]
        public void TerminalHoldCompletesExactlyOnce()
        {
            Type stateMachineType = Type.GetType(
                "TerminalInteractionStateMachine, Assembly-CSharp");
            Assert.That(stateMachineType, Is.Not.Null);
            object stateMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(stateMachine, new object[]
                {
                    1f,
                    false
                });

            Assert.That(
                stateMachineType.GetMethod("TryBegin")
                    .Invoke(stateMachine, null),
                Is.EqualTo(true));
            Assert.That(
                stateMachineType.GetMethod("Advance")
                    .Invoke(stateMachine, new object[] { 0.4f }),
                Is.EqualTo(false));
            Assert.That(
                stateMachineType.GetProperty("ProgressNormalized")
                    .GetValue(stateMachine),
                Is.EqualTo(0.4f).Within(0.001f));

            Assert.That(
                stateMachineType.GetMethod("Advance")
                    .Invoke(stateMachine, new object[] { 0.6f }),
                Is.EqualTo(true));
            Assert.That(
                stateMachineType.GetProperty("State")
                    .GetValue(stateMachine)
                    .ToString(),
                Is.EqualTo("Completed"));

            Assert.That(
                stateMachineType.GetMethod("TryBegin")
                    .Invoke(stateMachine, null),
                Is.EqualTo(false));
            Assert.That(
                stateMachineType.GetMethod("Advance")
                    .Invoke(stateMachine, new object[] { 1f }),
                Is.EqualTo(false),
                "已完成终端不能重复提交完成事件。");
        }

        [Test]
        public void TerminalInterruptUsesConfiguredProgressRule()
        {
            Type stateMachineType = Type.GetType(
                "TerminalInteractionStateMachine, Assembly-CSharp");

            object resetMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(resetMachine, new object[] { 1f, false });
            stateMachineType.GetMethod("TryBegin")
                .Invoke(resetMachine, null);
            stateMachineType.GetMethod("Advance")
                .Invoke(resetMachine, new object[] { 0.45f });
            stateMachineType.GetMethod("Cancel")
                .Invoke(resetMachine, null);
            Assert.That(
                stateMachineType.GetProperty("ProgressNormalized")
                    .GetValue(resetMachine),
                Is.EqualTo(0f));

            object preserveMachine =
                Activator.CreateInstance(stateMachineType);
            stateMachineType.GetMethod("Configure")
                .Invoke(preserveMachine, new object[] { 1f, true });
            stateMachineType.GetMethod("TryBegin")
                .Invoke(preserveMachine, null);
            stateMachineType.GetMethod("Advance")
                .Invoke(preserveMachine, new object[] { 0.45f });
            stateMachineType.GetMethod("Cancel")
                .Invoke(preserveMachine, null);
            Assert.That(
                stateMachineType.GetProperty("ProgressNormalized")
                    .GetValue(preserveMachine),
                Is.EqualTo(0.45f).Within(0.001f));
        }

        [Test]
        public void AlarmTerminalPublishesOneDecoupledSoundEvent()
        {
            Type terminalType = Type.GetType(
                "TerminalInteractable, Assembly-CSharp");
            Type completionModeType = Type.GetType(
                "TerminalCompletionMode, Assembly-CSharp");
            Type progressModeType = Type.GetType(
                "TerminalInterruptionProgressMode, Assembly-CSharp");
            Type channelType = Type.GetType(
                "CombatSoundEventChannel, Assembly-CSharp");
            Type interactionContractType = Type.GetType(
                "IInteractable, Assembly-CSharp");
            Assert.That(terminalType, Is.Not.Null);
            Assert.That(
                interactionContractType.IsAssignableFrom(terminalType),
                Is.EqualTo(true),
                "终端必须实现统一可交互对象契约。");
            GameObject terminalObject = new GameObject(
                "Alarm Terminal");
            GameObject actor = new GameObject("Interactor");

            try
            {
                Component terminal =
                    terminalObject.AddComponent(terminalType);
                object resetMode = Enum.Parse(
                    progressModeType,
                    "Reset");
                object alarmMode = Enum.Parse(
                    completionModeType,
                    "AreaAlarm");
                terminalType.GetMethod("Configure")
                    .Invoke(terminal, new object[]
                    {
                        0.1f,
                        resetMode,
                        alarmMode,
                        32f,
                        1f
                    });
                UnityEngine.Object channel = Resources.Load(
                    "CombatSoundEvents",
                    channelType);
                int before = (int)channelType
                    .GetProperty("PublishCount")
                    .GetValue(channel);

                Assert.That(
                    terminalType.GetMethod("TryBegin")
                        .Invoke(terminal, new object[]
                        {
                            actor
                        }),
                    Is.EqualTo(true));
                Assert.That(
                    terminalType.GetMethod("Advance")
                        .Invoke(terminal, new object[]
                        {
                            actor,
                            0.1f
                        }),
                    Is.EqualTo(true));
                Assert.That(
                    channelType.GetProperty("PublishCount")
                        .GetValue(channel),
                    Is.EqualTo(before + 1));
                Assert.That(
                    terminalType.GetProperty("CompletionCount")
                        .GetValue(terminal),
                    Is.EqualTo(1));

                terminalType.GetMethod("Advance")
                    .Invoke(terminal, new object[]
                    {
                        actor,
                        1f
                    });
                Assert.That(
                    channelType.GetProperty("PublishCount")
                        .GetValue(channel),
                    Is.EqualTo(before + 1),
                    "终端完成后不得重复触发区域警报。");
                Assert.That(
                    terminalType.GetFields(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic),
                    Has.None.Matches<FieldInfo>(
                        field => field.FieldType.Name.Contains(
                            "Enemy")),
                    "终端不应直接引用具体敌人。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
                UnityEngine.Object.DestroyImmediate(terminalObject);
            }
        }

        [UnityTest]
        public IEnumerator CityNewBootstrapsExistingControlUnitMission()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type terminalType = Type.GetType(
                "TerminalInteractable, Assembly-CSharp");
            Type interactionType = Type.GetType(
                "PlayerInteractionController, Assembly-CSharp");
            Type missionHudType = Type.GetType(
                "TerminalMissionHudPresenter, Assembly-CSharp");
            GameObject terminalObject = GameObject.Find("controlunit");
            GameObject player =
                GameObject.FindGameObjectWithTag("Player");

            Assert.That(terminalObject, Is.Not.Null);
            Assert.That(
                terminalObject.GetComponent(terminalType),
                Is.Not.Null,
                "应复用 CityNew 现有 controlunit 作为任务终端。");
            Assert.That(
                player.GetComponent(interactionType),
                Is.Not.Null);
            Component missionHud =
                player.GetComponent(missionHudType);
            Assert.That(missionHud, Is.Not.Null);
            Assert.That(
                missionHudType.GetProperty("ObjectiveCompleted")
                    .GetValue(missionHud),
                Is.EqualTo(false));

            Component terminal =
                terminalObject.GetComponent(terminalType);
            Type channelType = Type.GetType(
                "CombatSoundEventChannel, Assembly-CSharp");
            UnityEngine.Object channel = Resources.Load(
                "CombatSoundEvents",
                channelType);
            int soundCountBefore = (int)channelType
                .GetProperty("PublishCount")
                .GetValue(channel);
            terminalType.GetMethod("Configure")
                .Invoke(terminal, new object[]
                {
                    0.1f,
                    Enum.Parse(
                        Type.GetType(
                            "TerminalInterruptionProgressMode, " +
                            "Assembly-CSharp"),
                        "Reset"),
                    Enum.Parse(
                        Type.GetType(
                            "TerminalCompletionMode, Assembly-CSharp"),
                        "Silent"),
                    32f,
                    1f
                });
            terminalType.GetMethod("TryBegin")
                .Invoke(terminal, new object[] { player });
            terminalType.GetMethod("Advance")
                .Invoke(terminal, new object[] { player, 0.1f });
            Assert.That(
                channelType.GetProperty("PublishCount")
                    .GetValue(channel),
                Is.EqualTo(soundCountBefore),
                "Silent 终端完成时不能发布区域警报。");
            Assert.That(
                missionHudType.GetProperty("ObjectiveCompleted")
                    .GetValue(missionHud),
                Is.EqualTo(true),
                "完成终端交互后任务 HUD 应立即更新为 1/1。");
        }

        [UnityTest]
        public IEnumerator InteractionRayHonorsConfiguredRange()
        {
            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type interactionType = Type.GetType(
                "PlayerInteractionController, Assembly-CSharp");
            Type terminalType = Type.GetType(
                "TerminalInteractable, Assembly-CSharp");
            GameObject player =
                GameObject.FindGameObjectWithTag("Player");
            Component interaction =
                player.GetComponent(interactionType);
            Camera camera = Camera.main;
            GameObject terminalObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject wall = null;

            try
            {
                terminalObject.name = "Range Test Terminal";
                terminalObject.transform.position =
                    camera.transform.position +
                    camera.transform.forward * 2f;
                terminalObject.AddComponent(terminalType);
                Physics.SyncTransforms();
                Ray ray = new Ray(
                    camera.transform.position,
                    camera.transform.forward);
                object[] arguments = { ray, null };

                Assert.That(
                    interactionType.GetMethod("TryFindInteractable")
                        .Invoke(interaction, arguments),
                    Is.EqualTo(true));
                Assert.That(arguments[1], Is.Not.Null);

                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Interaction Occlusion";
                wall.transform.position =
                    camera.transform.position +
                    camera.transform.forward * 1f;
                Physics.SyncTransforms();
                arguments = new object[] { ray, null };
                Assert.That(
                    interactionType.GetMethod("TryFindInteractable")
                        .Invoke(interaction, arguments),
                    Is.EqualTo(false),
                    "墙体必须阻断其后的终端交互。");
                UnityEngine.Object.DestroyImmediate(wall);
                wall = null;

                terminalObject.transform.position =
                    camera.transform.position +
                    camera.transform.forward * 4f;
                Physics.SyncTransforms();
                arguments = new object[] { ray, null };
                Assert.That(
                    interactionType.GetMethod("TryFindInteractable")
                        .Invoke(interaction, arguments),
                    Is.EqualTo(false),
                    "超出交互距离的终端不能显示有效提示。");
            }
            finally
            {
                if (wall != null)
                {
                    UnityEngine.Object.DestroyImmediate(wall);
                }

                UnityEngine.Object.DestroyImmediate(terminalObject);
            }
        }

        [UnityTest]
        public IEnumerator ReleaseAndRangeLossCancelInteraction()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type interactionType = Type.GetType(
                "PlayerInteractionController, Assembly-CSharp");
            Type terminalType = Type.GetType(
                "TerminalInteractable, Assembly-CSharp");
            Type progressModeType = Type.GetType(
                "TerminalInterruptionProgressMode, Assembly-CSharp");
            Type completionModeType = Type.GetType(
                "TerminalCompletionMode, Assembly-CSharp");
            GameObject player =
                GameObject.FindGameObjectWithTag("Player");
            Component interaction =
                player.GetComponent(interactionType);
            Camera camera = Camera.main;
            GameObject terminalObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                Component terminal =
                    terminalObject.AddComponent(terminalType);
                terminalType.GetMethod("Configure")
                    .Invoke(terminal, new object[]
                    {
                        1f,
                        Enum.Parse(progressModeType, "Reset"),
                        Enum.Parse(completionModeType, "Silent"),
                        32f,
                        1f
                    });
                terminalObject.transform.position =
                    camera.transform.position +
                    camera.transform.forward * 2f;
                Physics.SyncTransforms();

                Press(keyboard.eKey);
                yield return null;
                Assert.That(
                    interactionType.GetProperty("IsInteracting")
                        .GetValue(interaction),
                    Is.EqualTo(true));

                Release(keyboard.eKey);
                yield return null;
                Assert.That(
                    interactionType.GetProperty("IsInteracting")
                        .GetValue(interaction),
                    Is.EqualTo(false),
                    "松开交互键必须中断终端接入。");

                Press(keyboard.eKey);
                yield return null;
                Assert.That(
                    interactionType.GetProperty("IsInteracting")
                        .GetValue(interaction),
                    Is.EqualTo(true));

                terminalObject.transform.position =
                    camera.transform.position +
                    camera.transform.forward * 4f;
                Physics.SyncTransforms();
                yield return null;
                Assert.That(
                    interactionType.GetProperty("IsInteracting")
                        .GetValue(interaction),
                    Is.EqualTo(false),
                    "交互目标离开有效范围必须中断接入。");
            }
            finally
            {
                Release(keyboard.eKey);
                InputSystem.RemoveDevice(keyboard);
                UnityEngine.Object.DestroyImmediate(terminalObject);
            }
        }

        [UnityTest]
        public IEnumerator TakingDamageInterruptsActiveInteraction()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

            yield return SceneManager.LoadSceneAsync(
                "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type interactionType = Type.GetType(
                "PlayerInteractionController, Assembly-CSharp");
            Type terminalType = Type.GetType(
                "TerminalInteractable, Assembly-CSharp");
            Type progressModeType = Type.GetType(
                "TerminalInterruptionProgressMode, Assembly-CSharp");
            Type completionModeType = Type.GetType(
                "TerminalCompletionMode, Assembly-CSharp");
            GameObject player =
                GameObject.FindGameObjectWithTag("Player");
            Component interaction =
                player.GetComponent(interactionType);
            Component health = player.GetComponent("Health");
            Camera camera = Camera.main;
            GameObject terminalObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                terminalObject.transform.position =
                    camera.transform.position +
                    camera.transform.forward * 2f;
                Component terminal =
                    terminalObject.AddComponent(terminalType);
                terminalType.GetMethod("Configure")
                    .Invoke(terminal, new object[]
                    {
                        1f,
                        Enum.Parse(progressModeType, "Reset"),
                        Enum.Parse(completionModeType, "Silent"),
                        32f,
                        1f
                    });
                Physics.SyncTransforms();

                Press(keyboard.eKey);
                yield return null;
                Assert.That(
                    interactionType.GetProperty("IsInteracting")
                        .GetValue(interaction),
                    Is.EqualTo(true));

                Type damageInfoType = Type.GetType(
                    "DamageInfo, Assembly-CSharp");
                object damage = Activator.CreateInstance(
                    damageInfoType,
                    new object[]
                    {
                        1f,
                        player.transform.position,
                        Vector3.back,
                        null
                    });
                health.GetType().GetMethod("ApplyDamage")
                    .Invoke(health, new[] { damage });
                yield return null;

                Assert.That(
                    interactionType.GetProperty("IsInteracting")
                        .GetValue(interaction),
                    Is.EqualTo(false));
                Assert.That(
                    terminalType.GetProperty("State")
                        .GetValue(terminal)
                        .ToString(),
                    Is.EqualTo("Inactive"));
                Assert.That(
                    terminalType.GetProperty("ProgressNormalized")
                        .GetValue(terminal),
                    Is.EqualTo(0f),
                    "受击中断后 Reset 终端必须清空进度。");
            }
            finally
            {
                Release(keyboard.eKey);
                InputSystem.RemoveDevice(keyboard);
                UnityEngine.Object.DestroyImmediate(terminalObject);
            }
        }
    }
}
