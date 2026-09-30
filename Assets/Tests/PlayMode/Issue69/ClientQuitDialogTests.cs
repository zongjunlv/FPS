using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode.Issue69
{
    /// <summary>
    /// Runs in an empty scene with local-only account and exit substitutes.
    /// No test contacts the account service or calls Application.Quit.
    /// </summary>
    public sealed class ClientQuitDialogTests
    {
        private readonly List<GameObject> ownedObjects = new();
        private readonly List<GameObject> suspendedRoots = new();
        private readonly List<Behaviour> suspendedRunnerInput = new();
        private readonly List<InputAction> suspendedExternalActions = new();
        private Keyboard keyboard;
        private Keyboard previousKeyboard;
        private InputSettings previousInputSettings;
        private InputSettings isolatedInputSettings;
        private bool previousRunInBackground;
        private Scene hostScene;
        private Scene isolatedScene;

        [SetUp]
        public void Setup()
        {
            previousKeyboard = Keyboard.current;
            try
            {
                // Keep the Editor's InputManager and native device state intact.
                // Only suspend unrelated scene owners for this local UI test.
                SuspendOtherRoots();
                foreach (InputAction action in InputSystem.ListEnabledActions())
                {
                    suspendedExternalActions.Add(action);
                    action.Disable();
                }
                if (Application.isBatchMode)
                {
                    // A headless Editor has no focused Game View. Route only
                    // this fixture's synthetic events through player updates;
                    // never edit or save the project's real settings asset.
                    previousInputSettings = InputSystem.settings;
                    previousRunInBackground = Application.runInBackground;
                    Application.runInBackground = true;
                    isolatedInputSettings = Object.Instantiate(previousInputSettings);
                    isolatedInputSettings.hideFlags = HideFlags.HideAndDontSave;
                    isolatedInputSettings.backgroundBehavior =
                        InputSettings.BackgroundBehavior.IgnoreFocus;
                    isolatedInputSettings.editorInputBehaviorInPlayMode =
                        InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings = isolatedInputSettings;
                }
            }
            catch
            {
                RestoreInputSettings();
                ResumeOtherRoots();
                throw;
            }
        }

        [UnitySetUp]
        public IEnumerator PrepareIsolatedScene()
        {
            ClientQuitDialog.ResetForTests();
            Time.timeScale = 1f;
            CoopUiInputGate.PauseMenuVisible = false;
            CoopUiInputGate.EconomyModalVisible = false;
            // The initial scene can own PlaymodeTestsController. Never unload
            // it: destroying the runner would prevent RunFinished callbacks.
            hostScene = SceneManager.GetActiveScene();
            isolatedScene = SceneManager.CreateScene(
                "ClientQuitDialogTests-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(isolatedScene);
            GameModeContext.ResetForTests();
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            if (Application.isBatchMode)
            {
                InputSystem.EnableDevice(keyboard);
                Assert.That(keyboard.enabled, Is.True,
                    "后台测试必须启用自身的合成键盘，不可依赖 Game View 焦点。");
            }
            // ModeUiFactory must find this owned module rather than reuse
            // a native module, or create a root that survives our teardown.
            Own(new GameObject("Quit Test EventSystem",
                typeof(EventSystem), typeof(InputSystemUIInputModule)));
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreGlobalState()
        {
            ClientQuitDialog.ResetForTests();
            foreach (GameObject owned in ownedObjects)
            {
                if (owned != null) Object.Destroy(owned);
            }
            ownedObjects.Clear();
            CoopUiInputGate.PauseMenuVisible = false;
            CoopUiInputGate.EconomyModalVisible = false;
            GameModeContext.ResetForTests();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
            yield return null;
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (isolatedScene.IsValid() && isolatedScene.isLoaded &&
                hostScene.IsValid() && hostScene.isLoaded)
            {
                // Normally the runner stays in hostScene. Preserve it even if
                // the framework created an extra controller in our active scene.
                foreach (GameObject root in isolatedScene.GetRootGameObjects())
                {
                    if (IsTestRunnerRoot(root))
                        SceneManager.MoveGameObjectToScene(root, hostScene);
                }
                SceneManager.SetActiveScene(hostScene);
                yield return SceneManager.UnloadSceneAsync(isolatedScene);
            }
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                // Also clean up our device if UnitySetUp/UnityTearDown failed.
                // Never reset or dispose the Editor's global InputManager.
                if (keyboard != null && keyboard.added)
                    InputSystem.RemoveDevice(keyboard);
                keyboard = null;
                if (previousKeyboard != null && previousKeyboard.added)
                    previousKeyboard.MakeCurrent();
                previousKeyboard = null;
            }
            finally
            {
                RestoreInputSettings();
                ResumeOtherRoots();
            }
        }

        private void RestoreInputSettings()
        {
            if (previousInputSettings != null)
            {
                InputSystem.settings = previousInputSettings;
                Application.runInBackground = previousRunInBackground;
            }
            previousInputSettings = null;
            if (isolatedInputSettings != null)
                Object.DestroyImmediate(isolatedInputSettings);
            isolatedInputSettings = null;
        }

        [UnityTest]
        public IEnumerator LoginEscapeShowsConfirmationWithUsableCursor()
        {
            Activate(GameModeId.Coop, GameModeStage.CoopLogin);
            CoopAccountView account = CreateLocalAccount();
            account.UsernameInput.text = "Player_Quit";
            account.PasswordInput.text = "Password1";
            yield return null;
            ClientQuitDialog.Ensure();

            yield return PressEscape();

            ClientQuitDialog dialog = ClientQuitDialog.Instance;
            Assert.That(dialog, Is.Not.Null);
            Assert.That(dialog.IsVisible, Is.True);
            Assert.That(ClientQuitDialog.IsBlockingInput, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(dialog.CancelButton.interactable, Is.True);
            Assert.That(dialog.ConfirmButton.interactable, Is.True);
            Assert.That(account.UsernameInput.text, Is.EqualTo("Player_Quit"));
            Assert.That(account.PasswordInput.text, Is.EqualTo("Password1"));
            Assert.That(GameModeContext.CurrentStage,
                Is.EqualTo(GameModeStage.CoopLogin));
        }

        [UnityTest]
        public IEnumerator EscapeWorksInModeHallAndCharacterSelection()
        {
            ClientQuitDialog dialog = ClientQuitDialog.Ensure();
            GameModeId[] modes =
            {
                GameModeId.None, GameModeId.SoloBattle, GameModeId.Coop
            };
            GameModeStage[] stages =
            {
                GameModeStage.Entry, GameModeStage.BattlePreparation,
                GameModeStage.CoopLobby
            };
            for (int index = 0; index < stages.Length; index++)
            {
                Activate(modes[index], stages[index]);
                yield return PressEscape();
                Assert.That(dialog.IsVisible, Is.True, stages[index].ToString());
                Assert.That(ClientQuitDialog.IsBlockingInput, Is.True);
                dialog.Cancel();
                yield return null;
                Assert.That(dialog.IsVisible, Is.False);
                Assert.That(GameModeContext.CurrentStage,
                    Is.EqualTo(stages[index]), "取消退出不应切换模式或场景。");
            }
        }

        [UnityTest]
        public IEnumerator CancelRestoresLoginInputFocusAndKeepsCredentials()
        {
            Activate(GameModeId.Coop, GameModeStage.CoopLogin);
            CoopAccountView account = CreateLocalAccount();
            account.UsernameInput.text = "Player_Quit";
            account.PasswordInput.text = "Password1";
            yield return null;
            account.PasswordInput.Select();
            account.PasswordInput.ActivateInputField();
            yield return null;
            Assert.That(account.PasswordInput.isFocused, Is.True);

            ClientQuitDialog.RequestQuitConfirmation();
            yield return null;
            ClientQuitDialog.Instance.CancelButton.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.That(ClientQuitDialog.Instance.IsVisible, Is.False);
            Assert.That(ClientQuitDialog.IsBlockingInput, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(account.PasswordInput.gameObject));
            Assert.That(account.PasswordInput.isFocused, Is.True,
                "取消退出后应恢复之前的输入框与闪烁光标。");
            Assert.That(account.UsernameInput.text, Is.EqualTo("Player_Quit"));
            Assert.That(account.PasswordInput.text, Is.EqualTo("Password1"));
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
        }

        [UnityTest]
        public IEnumerator SecondEscapeCancelsAndCannotReopenInTheSameFrame()
        {
            ClientQuitDialog.RequestQuitConfirmation();
            yield return null;
            ClientQuitDialog dialog = ClientQuitDialog.Instance;

            Assert.That(dialog.HandleEscape(), Is.True);
            Assert.That(dialog.IsVisible, Is.False);
            Assert.That(ClientQuitDialog.EscapeHandledThisFrame, Is.True);
            dialog.HandleEscape();

            Assert.That(dialog.IsVisible, Is.False,
                "同一帧的 ESC 不得关闭确认窗口后又重新打开。");
            Assert.That(ClientQuitDialog.IsBlockingInput, Is.False);
            yield return null;
            Assert.That(dialog.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator RepeatedEnsureAndRequestsUseOneDialogInstance()
        {
            ClientQuitDialog first = ClientQuitDialog.Ensure();
            ClientQuitDialog second = ClientQuitDialog.Ensure();
            ClientQuitDialog.RequestQuitConfirmation();
            ClientQuitDialog.RequestQuitConfirmation();
            yield return null;

            Assert.That(second, Is.SameAs(first));
            Assert.That(ClientQuitDialog.Instance, Is.SameAs(first));
            Assert.That(Object.FindObjectsByType<ClientQuitDialog>(
                FindObjectsInactive.Include, FindObjectsSortMode.None),
                Has.Length.EqualTo(1));
            Assert.That(first.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator CancelPreservesPausedTimeAndOriginalCursorState()
        {
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            CursorLockMode previousLock = Cursor.lockState;
            bool previousVisibility = Cursor.visible;

            ClientQuitDialog.RequestQuitConfirmation();
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            ClientQuitDialog.Instance.Cancel();
            yield return null;

            Assert.That(Time.timeScale, Is.Zero,
                "关闭退出确认不应擅自恢复底层暂停中的游戏。");
            Assert.That(Cursor.lockState, Is.EqualTo(previousLock));
            Assert.That(Cursor.visible, Is.EqualTo(previousVisibility));
        }

        [UnityTest]
        public IEnumerator ConfirmWaitsForCleanupAndTerminatesOnlyOnce()
        {
            var cleanup = new TaskCompletionSource<bool>();
            int cleanupCalls = 0;
            int terminateCalls = 0;
            ClientQuitDialog dialog = ClientQuitDialog.Ensure();
            dialog.ConfigureExitForTests(() =>
            {
                cleanupCalls++;
                return cleanup.Task;
            }, () => terminateCalls++);
            ClientQuitDialog.RequestQuitConfirmation();

            dialog.Confirm();
            dialog.Confirm();
            dialog.Cancel();
            yield return null;

            Assert.That(cleanupCalls, Is.EqualTo(1));
            Assert.That(terminateCalls, Is.Zero,
                "正常清理尚未结束时不能提前终止客户端。");
            Assert.That(dialog.IsQuitting, Is.True);
            Assert.That(ClientQuitDialog.IsBlockingInput, Is.True);
            cleanup.SetResult(true);
            yield return WaitFor(() => terminateCalls == 1);
            dialog.Confirm();
            yield return null;
            Assert.That(cleanupCalls, Is.EqualTo(1));
            Assert.That(terminateCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CleanupTimeoutStillExitsWhenGameTimeIsPaused()
        {
            var neverCompleted = new TaskCompletionSource<bool>();
            int terminateCalls = 0;
            ClientQuitDialog dialog = ClientQuitDialog.Ensure();
            dialog.ConfigureExitForTests(() => neverCompleted.Task,
                () => terminateCalls++, 0.05f);
            Time.timeScale = 0f;
            ClientQuitDialog.RequestQuitConfirmation();

            dialog.Confirm();
            yield return WaitFor(() => terminateCalls == 1);

            Assert.That(terminateCalls, Is.EqualTo(1));
            Assert.That(neverCompleted.Task.IsCompleted, Is.False,
                "测试必须真实覆盖清理挂起，不使用已完成任务假装超时。");
            Assert.That(dialog.IsQuitting, Is.True);
        }

        [UnityTest]
        public IEnumerator TutorialExitButtonCancellationKeepsPauseMenu()
        {
            Activate(GameModeId.Tutorial, GameModeStage.Tutorial);
            GameObject owner = Own(new GameObject("Quit Tutorial Test"));
            TutorialPauseView pause = TutorialPauseView.Create(owner.transform, null);
            pause.SetVisible(true);
            Time.timeScale = 0f;

            Assert.That(pause.ExitClientButton, Is.Not.Null);
            pause.ExitClientButton.onClick.Invoke();
            yield return null;
            Assert.That(ClientQuitDialog.Instance.IsVisible, Is.True);
            ClientQuitDialog.Instance.Cancel();
            yield return null;

            Assert.That(pause.IsVisible, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(pause.ContinueButton.interactable, Is.True);
            Assert.That(pause.ReturnButton.interactable, Is.True);
            Assert.That(pause.ExitClientButton.interactable, Is.True);
            Assert.That(Cursor.visible, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(pause.ContinueButton.gameObject));
        }

        [UnityTest]
        public IEnumerator TutorialRepeatedVisibleUpdatesCannotStealModalFocus()
        {
            Activate(GameModeId.Tutorial, GameModeStage.Tutorial);
            GameObject owner = Own(new GameObject("Quit Tutorial Focus Test"));
            TutorialPauseView pause = TutorialPauseView.Create(owner.transform, null);
            pause.SetVisible(true);
            Time.timeScale = 0f;
            pause.ExitClientButton.onClick.Invoke();
            yield return null;
            ClientQuitDialog dialog = ClientQuitDialog.Instance;
            EventSystem.current.SetSelectedGameObject(dialog.ConfirmButton.gameObject);

            for (int frame = 0; frame < 3; frame++)
            {
                // TutorialFlowController calls this from every LateUpdate.
                pause.SetVisible(true);
                yield return null;
                Assert.That(dialog.IsVisible, Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject,
                    Is.EqualTo(dialog.ConfirmButton.gameObject),
                    "重复刷新底层教学暂停菜单不能将焦点抢回继续教学按钮。");
            }

            Assert.That(pause.IsVisible, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            dialog.Cancel();
            yield return null;
            Assert.That(pause.IsVisible, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CoopExitButtonCancellationKeepsUnderlyingInputGates()
        {
            Activate(GameModeId.Coop, GameModeStage.CoopBattle);
            GameObject owner = Own(new GameObject("Quit Co-op Test"));
            CoopBattlePauseView pause = CoopBattlePauseView.Create(owner.transform, null);
            pause.SetVisible(true);
            CoopUiInputGate.PauseMenuVisible = true;
            CoopUiInputGate.EconomyModalVisible = true;

            Assert.That(pause.ExitClientButton, Is.Not.Null);
            pause.ExitClientButton.onClick.Invoke();
            yield return null;
            Assert.That(ClientQuitDialog.Instance.IsVisible, Is.True);
            ClientQuitDialog.Instance.Cancel();
            yield return null;

            Assert.That(pause.IsVisible, Is.True);
            Assert.That(CoopUiInputGate.PauseMenuVisible, Is.True);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.True);
            Assert.That(CoopUiInputGate.GameplayInputSuppressed, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "联机菜单不能暂停服务器战局，也不应改写本机时间比例。");
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(pause.ContinueButton.gameObject));
        }

        [UnityTest]
        public IEnumerator ModalTabCannotSwitchUnderlyingLoginFieldsAndHasIndependentGate()
        {
            Activate(GameModeId.Coop, GameModeStage.CoopLogin);
            CoopAccountView account = CreateLocalAccount();
            account.UsernameInput.text = "Player_Quit";
            account.PasswordInput.text = "Password1";
            yield return null;
            account.UsernameInput.Select();
            account.UsernameInput.ActivateInputField();
            yield return null;
            ClientQuitDialog.RequestQuitConfirmation();
            yield return null;

            Assert.That(CoopUiInputGate.QuitConfirmationVisible, Is.True);
            Assert.That(CoopUiInputGate.QuitConfirmationInputSuppressed, Is.True);
            Assert.That(CoopUiInputGate.PauseMenuVisible, Is.False);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.False);
            Press(keyboard.tabKey);
            yield return null;
            Release(keyboard.tabKey);
            yield return null;

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            ClientQuitDialog dialog = ClientQuitDialog.Instance;
            Assert.That(selected == dialog.CancelButton.gameObject ||
                        selected == dialog.ConfirmButton.gameObject, Is.True,
                "退出确认打开时 Tab 只能停留在确认窗口，不能切到底层账号字段。");
            Assert.That(account.UsernameInput.isFocused, Is.False);
            Assert.That(account.PasswordInput.isFocused, Is.False);
            Assert.That(account.UsernameInput.text, Is.EqualTo("Player_Quit"));
            Assert.That(account.PasswordInput.text, Is.EqualTo("Password1"));
            Assert.That(CoopUiInputGate.PauseMenuVisible, Is.False);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.False);
            dialog.Cancel();
            Assert.That(CoopUiInputGate.QuitConfirmationVisible, Is.False);
            Assert.That(CoopUiInputGate.QuitConfirmationInputSuppressed, Is.True,
                "关闭当帧仍须屏蔽已消费输入，避免它落到底层菜单或联机控制。");
            yield return null;
            yield return null;
            Assert.That(CoopUiInputGate.QuitConfirmationInputSuppressed, Is.False);
            Assert.That(CoopUiInputGate.GameplayInputSuppressed, Is.False);
        }

        [UnityTest]
        public IEnumerator AllPauseButtonsRemainInsidePanelWithoutOverlap()
        {
            GameObject owner = Own(new GameObject("Quit Layout Test"));
            TutorialPauseView tutorial = TutorialPauseView.Create(owner.transform, null);
            CoopBattlePauseView coop = CoopBattlePauseView.Create(owner.transform, null);
            tutorial.SetVisible(true);
            coop.SetVisible(true);
            yield return null;
            Canvas.ForceUpdateCanvases();

            AssertButtonLayout(tutorial.ContinueButton, tutorial.ReturnButton,
                tutorial.ExitClientButton);
            AssertButtonLayout(coop.ContinueButton, coop.ReturnButton,
                coop.ExitClientButton);
        }

        private CoopAccountView CreateLocalAccount()
        {
            CoopAccountView view = CoopAccountView.CreateAuthenticationGate(
                null, new LocalAuthenticationGateway(), () => { });
            Own(view.gameObject);
            return view;
        }

        private GameObject Own(GameObject value)
        {
            ownedObjects.Add(value);
            return value;
        }

        private static bool IsTestRunnerRoot(GameObject root)
        {
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                Type type = behaviour.GetType();
                if (type.Name.Contains("PlaymodeTest") ||
                    type.Namespace != null && type.Namespace.Contains("TestRunner") &&
                    type.Name.Contains("Controller"))
                    return true;
            }
            return false;
        }

        private void SuspendOtherRoots()
        {
            var roots = new HashSet<GameObject>();
            foreach (Transform transform in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                roots.Add(transform.root.gameObject);
            foreach (GameObject root in roots)
            {
                if (!root.scene.IsValid() || !root.activeSelf) continue;
                if (IsTestRunnerRoot(root))
                {
                    // The runner itself must stay alive. Only suspend any
                    // input components sharing its hierarchy, if present.
                    foreach (InputSystemUIInputModule module in
                             root.GetComponentsInChildren<InputSystemUIInputModule>(true))
                        SuspendRunnerInput(module);
                    foreach (PlayerInput input in
                             root.GetComponentsInChildren<PlayerInput>(true))
                        SuspendRunnerInput(input);
                    continue;
                }
                suspendedRoots.Add(root);
                root.SetActive(false);
            }
        }

        private void SuspendRunnerInput(Behaviour behaviour)
        {
            if (!behaviour.enabled) return;
            suspendedRunnerInput.Add(behaviour);
            behaviour.enabled = false;
        }

        private void ResumeOtherRoots()
        {
            foreach (GameObject root in suspendedRoots)
            {
                if (root != null) root.SetActive(true);
            }
            suspendedRoots.Clear();
            foreach (Behaviour behaviour in suspendedRunnerInput)
            {
                if (behaviour != null) behaviour.enabled = true;
            }
            suspendedRunnerInput.Clear();
            foreach (InputAction action in suspendedExternalActions)
                action.Enable();
            suspendedExternalActions.Clear();
        }

        private IEnumerator PressEscape()
        {
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
        }

        private static void Press(ButtonControl control)
        {
            QueueButtonState(control, 1f);
        }

        private static void Release(ButtonControl control)
        {
            QueueButtonState(control, 0f);
        }

        private static void QueueButtonState(ButtonControl control, float value)
        {
            // Keyboard keys are bitfields, so QueueDeltaStateEvent(control,
            // float) is not valid. Use the official bitfield-aware event path.
            using (DeltaStateEvent.From(control, out InputEventPtr eventPtr))
            {
                control.WriteValueIntoEvent(value, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static void Activate(GameModeId mode, GameModeStage stage)
        {
            Assert.That(GameModeContext.TryActivate(mode, stage, out string error),
                Is.True, error);
        }

        private static IEnumerator WaitFor(Func<bool> completed)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!completed() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(completed(), Is.True,
                "退出任务未在真实时间期限内完成；不能依赖暂停中的游戏时钟。");
        }

        private static void AssertButtonLayout(params UnityEngine.UI.Button[] buttons)
        {
            Assert.That(buttons, Has.Length.EqualTo(3));
            RectTransform panel = (RectTransform)buttons[0].transform.parent;
            Bounds? previous = null;
            foreach (UnityEngine.UI.Button button in buttons)
            {
                Bounds bounds = RectTransformUtility
                    .CalculateRelativeRectTransformBounds(panel, button.transform);
                Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(panel.rect.xMin));
                Assert.That(bounds.max.x, Is.LessThanOrEqualTo(panel.rect.xMax));
                Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(panel.rect.yMin));
                Assert.That(bounds.max.y, Is.LessThanOrEqualTo(panel.rect.yMax));
                if (previous.HasValue)
                    Assert.That(bounds.max.y, Is.LessThan(previous.Value.min.y),
                        "新增退出按钮不能压到继续或返回大厅按钮。");
                previous = bounds;
            }
        }

        private sealed class LocalAuthenticationGateway : ICoopAuthenticationGateway
        {
            public bool IsSignedIn => false;
            public bool HasCachedSession => false;
            public string PlayerId => string.Empty;
            public string Username => string.Empty;
            public Task InitializeAsync() => Task.CompletedTask;
            public Task SignUpAsync(string username, string password) =>
                Task.CompletedTask;
            public Task SignInAsync(string username, string password) =>
                Task.CompletedTask;
            public Task RestoreCachedSessionAsync() => Task.CompletedTask;
            public Task SignInAnonymouslyForDevelopmentAsync() => Task.CompletedTask;
            public void SignOut(bool clearCredentials) { }
        }
    }
}
