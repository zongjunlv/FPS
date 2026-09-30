using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue69
{
    public sealed class Issue84CoopAccountViewTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator AuthenticationSurfaceSeparatesModesAndShowsInputFocus()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            GameModeFlowController flow = GameModeFlowController.Instance;
            var gateway = new FakeGateway();
            CoopAccountView view = CoopAccountView.Create(flow, gateway, false);
            yield return null;

            Assert.That(view.AuthenticationPanel.activeSelf, Is.True);
            Assert.That(view.LobbyPanel.activeSelf, Is.False);
            Assert.That(view.IsRegisterMode, Is.False);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.True);
            Assert.That(view.LoginButton.interactable, Is.False,
                "空账号与密码不应允许提交登录。");
            Assert.That(view.RegisterButton.gameObject.activeSelf, Is.False);
            Assert.That(view.ConfirmationInput.gameObject.activeSelf, Is.False);
            Assert.That(view.ReturnButton.gameObject.activeSelf, Is.False,
                "未登录页面不应出现返回模式选择。");
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(view.UsernameInput.gameObject));
            Assert.That(view.UsernameInput.customCaretColor, Is.True);
            Assert.That(view.UsernameInput.caretWidth, Is.GreaterThanOrEqualTo(3));
            Assert.That(view.UsernameInput.isFocused, Is.True,
                "首次打开登录页时用户名输入框应真正激活光标。");
            Assert.That(view.AuthenticationPanel
                .GetComponentsInChildren<UnityEngine.UI.Image>(true)
                .Any(image => image.gameObject.name == "字段图标" &&
                              image.sprite != null), Is.True);

            view.RegisterTabButton.onClick.Invoke();
            Assert.That(view.IsRegisterMode, Is.True);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.False);
            Assert.That(view.RegisterButton.gameObject.activeSelf, Is.True);
            Assert.That(view.RegisterButton.interactable, Is.False,
                "空注册表单不应允许提交。");
            Assert.That(view.ConfirmationInput.gameObject.activeSelf, Is.True);

            Assert.That(view.PasswordInput.contentType,
                Is.EqualTo(TMP_InputField.ContentType.Password));
            view.PasswordVisibilityButton.onClick.Invoke();
            Assert.That(view.PasswordInput.contentType,
                Is.EqualTo(TMP_InputField.ContentType.Standard));
            view.PasswordVisibilityButton.onClick.Invoke();
            Assert.That(view.PasswordInput.contentType,
                Is.EqualTo(TMP_InputField.ContentType.Password));

            Object.Destroy(view.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CredentialsEnableSubmitAndTabMovesBetweenFields()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return SceneManager.LoadSceneAsync(
                    GameModeScenePaths.Entry, LoadSceneMode.Single);
                yield return null;
                CoopAccountView view = CoopAccountView.Create(
                    GameModeFlowController.Instance, new FakeGateway(), false);
                yield return null;

                Assert.That(view.LoginButton.interactable, Is.False);
                view.UsernameInput.text = "Player_84";
                Assert.That(view.LoginButton.interactable, Is.False);
                view.PasswordInput.text = "StrongPass1";
                Assert.That(view.LoginButton.interactable, Is.True);
                view.PasswordInput.text = string.Empty;
                Assert.That(view.LoginButton.interactable, Is.False);

                view.UsernameInput.Select();
                view.UsernameInput.ActivateInputField();
                yield return null;
                Press(keyboard.tabKey);
                yield return null;
                Release(keyboard.tabKey);
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject,
                    Is.EqualTo(view.PasswordInput.gameObject),
                    "Tab 应从用户名切到密码。");
                Assert.That(view.PasswordInput.isFocused, Is.True);

                view.RegisterTabButton.onClick.Invoke();
                view.UsernameInput.text = "Player_84";
                view.PasswordInput.text = "StrongPass1";
                view.PasswordInput.Select();
                view.PasswordInput.ActivateInputField();
                yield return null;
                Press(keyboard.tabKey);
                yield return null;
                Release(keyboard.tabKey);
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject,
                    Is.EqualTo(view.ConfirmationInput.gameObject),
                    "注册页 Tab 应能到确认密码。");
                Assert.That(view.RegisterButton.interactable, Is.False);
                view.ConfirmationInput.text = "StrongPass1";
                Assert.That(view.RegisterButton.interactable, Is.True);

                Press(keyboard.leftShiftKey);
                Press(keyboard.tabKey);
                yield return null;
                Release(keyboard.tabKey);
                Release(keyboard.leftShiftKey);
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject,
                    Is.EqualTo(view.PasswordInput.gameObject),
                    "Shift+Tab 应反向回到密码。");

                Object.Destroy(view.gameObject);
                yield return null;
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator InitialAndPostLogoutAuthenticationFocusIsConsistent()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry, LoadSceneMode.Single);
            yield return null;
            CoopAccountView view = CoopAccountView.Create(
                GameModeFlowController.Instance, new FakeGateway(), false);
            yield return null;
            yield return null;

            Assert.That(view.UsernameInput.isFocused, Is.True,
                "首次登录应与退出账号后的输入光标一致。");
            Assert.That(view.ReturnButton.gameObject.activeSelf, Is.False);

            view.RegisterTabButton.onClick.Invoke();
            view.UsernameInput.text = "Player_84";
            view.PasswordInput.text = "StrongPass1";
            view.ConfirmationInput.text = "StrongPass1";
            view.RegisterButton.onClick.Invoke();
            yield return null;
            Assert.That(view.LogoutButton.gameObject.activeSelf, Is.False,
                "退出账号入口应放在模式大厅，而非联机房间页。");

            Object.Destroy(view.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthenticationGateRestoresUsernameFocusAfterAsyncSessionCheck()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry, LoadSceneMode.Single);
            yield return null;
            var gateway = new FakeGateway
            {
                InitWaiter = new TaskCompletionSource<bool>()
            };
            CoopAccountView gate = CoopAccountView.CreateAuthenticationGate(
                GameModeFlowController.Instance, gateway, () => { });
            yield return null;
            gateway.InitWaiter.SetResult(true);
            yield return new WaitUntil(() => !gate.Controller.IsBusy);
            yield return null;

            Assert.That(gate.AuthenticationPanel.activeSelf, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(gate.UsernameInput.gameObject));
            Assert.That(gate.UsernameInput.isFocused, Is.True,
                "首次完成异步会话检查后输入框应有光标。");
            Object.Destroy(gate.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RegisterAndLogoutFlowClearsSensitiveFields()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            GameModeFlowController flow = GameModeFlowController.Instance;
            var gateway = new FakeGateway();
            CoopAccountView view = CoopAccountView.Create(flow, gateway, false);
            yield return null;

            view.UsernameInput.text = "Player_84";
            view.PasswordInput.text = "StrongPass1!";
            view.ConfirmationInput.text = "StrongPass1!";
            view.RegisterButton.onClick.Invoke();
            yield return null;

            Assert.That(view.Controller.State,
                Is.EqualTo(CoopAccountState.SignedIn));
            Assert.That(gateway.SignUpCalls, Is.EqualTo(1));
            Assert.That(view.PasswordInput.text, Is.Empty);
            Assert.That(view.ConfirmationInput.text, Is.Empty);
            Assert.That(view.LogoutButton.gameObject.activeSelf, Is.False);
            Assert.That(view.ReturnButton.gameObject.activeSelf, Is.True);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.False);

            view.LogoutButton.onClick.Invoke();
            Assert.That(view.Controller.State,
                Is.EqualTo(CoopAccountState.SignedOut));
            Assert.That(gateway.ClearedCredentials, Is.True);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.True);
            Assert.That(view.ReturnButton.gameObject.activeSelf, Is.False);
            Object.Destroy(view.gameObject);
        }

        [UnityTest]
        public IEnumerator ModeGateSignOutReopensAuthenticationForm()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry, LoadSceneMode.Single);
            yield return null;
            var gateway = new FakeGateway();
            ModeEntryView mode = Object.FindFirstObjectByType<ModeEntryView>();
            CoopAccountView gate = CoopAccountView.CreateAuthenticationGate(
                GameModeFlowController.Instance, gateway, () => { });
            mode.SetAuthenticationUnlocked(false);
            mode.ConfigureAccountSignOut(() =>
            {
                if (gate.SignOutToAuthentication())
                    mode.SetAuthenticationUnlocked(false);
            });
            yield return null;
            gate.RegisterTabButton.onClick.Invoke();
            gate.UsernameInput.text = "Player_84";
            gate.PasswordInput.text = "StrongPass1";
            gate.ConfirmationInput.text = "StrongPass1";
            gate.RegisterButton.onClick.Invoke();
            yield return null;

            Assert.That(gate.gameObject.activeSelf, Is.False);
            mode.SetAuthenticationUnlocked(true);
            UnityEngine.UI.Button logout = mode
                .GetComponentsInChildren<UnityEngine.UI.Button>(true)
                .Single(button => button.gameObject.name == "退出当前账号");
            Assert.That(logout.interactable, Is.True);
            logout.onClick.Invoke();
            yield return null;
            Assert.That(gate.Controller.IsSignedIn, Is.False);
            Assert.That(mode.GetButton(GameModeId.Coop).interactable, Is.False);
            Assert.That(gate.gameObject.activeSelf, Is.True);
            Assert.That(gate.AuthenticationPanel.activeSelf, Is.True);
            Assert.That(gate.UsernameInput.isFocused, Is.True);
            Assert.That(gate.ReturnButton.gameObject.activeSelf, Is.False);
            Object.Destroy(gate.gameObject);
        }

        [UnityTest]
        public IEnumerator AlreadySignedInAccountEntersCoopWithoutLoginFlashOrRevalidation()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry, LoadSceneMode.Single);
            yield return null;

            var gateway = new FakeGateway();
            GameModeFlowController flow = GameModeFlowController.Instance;
            CoopAccountView gate = CoopAccountView.CreateAuthenticationGate(
                flow, gateway, () => { });
            CoopAccountView lobby = null;
            try
            {
                yield return null;
                gate.RegisterTabButton.onClick.Invoke();
                gate.UsernameInput.text = "Player_Gate";
                gate.PasswordInput.text = "StrongPass1";
                gate.ConfirmationInput.text = "StrongPass1";
                gate.RegisterButton.onClick.Invoke();
                yield return null;
                Assert.That(gate.Controller.IsSignedIn, Is.True);
                Assert.That(gate.gameObject.activeSelf, Is.False);

                // A slow account endpoint must not send an already signed-in
                // player back through the login form on a mode transition.
                gateway.InitWaiter = new TaskCompletionSource<bool>();
                int initializationCount = gateway.InitializeCalls;
                lobby = CoopAccountView.Create(flow, gateway, false);

                Assert.That(lobby.AuthenticationPanel.activeSelf, Is.False,
                    "已登录用户进入联机大厅时，登录表单不能闪现。");
                Assert.That(lobby.LobbyPanel.activeSelf, Is.True,
                    "已登录用户应立即看到联机大厅。");
                Assert.That(gateway.InitializeCalls,
                    Is.EqualTo(initializationCount),
                    "场景切换不应重新等待账号服务验证已登录会话。");
                Assert.That(gateway.RestoreCachedSessionCalls, Is.Zero,
                    "同一进程内已验证的账号不应重复恢复缓存令牌。");

                Assert.That(gate.SignOutToAuthentication(), Is.True);
                Assert.That(gate.AuthenticationPanel.activeSelf, Is.True,
                    "只有显式退出账号才应再次显示登录表单。");
            }
            finally
            {
                gateway.InitWaiter?.TrySetResult(true);
                if (lobby != null) Object.Destroy(lobby.gameObject);
                if (gate != null) Object.Destroy(gate.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReturningFromCoopToModesDoesNotReopenLoginGate()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry, LoadSceneMode.Single);
            yield return null;

            var gateway = new FakeGateway
            {
                SignedIn = true,
                InitWaiter = new TaskCompletionSource<bool>()
            };
            int authenticationCompleted = 0;
            CoopAccountView gate = null;
            try
            {
                gate = CoopAccountView.CreateAuthenticationGate(
                    GameModeFlowController.Instance, gateway,
                    () => authenticationCompleted++);

                Assert.That(authenticationCompleted, Is.EqualTo(1),
                    "从联机返回模式大厅时，已验证账号应立即解锁模式。");
                Assert.That(gate.gameObject.activeSelf, Is.False,
                    "返回模式大厅不应短暂显示登录页。");
                Assert.That(gateway.InitializeCalls, Is.Zero,
                    "返回模式大厅不应重新请求账号服务。");
                Assert.That(gateway.RestoreCachedSessionCalls, Is.Zero);

                Assert.That(gate.SignOutToAuthentication(), Is.True);
                Assert.That(gate.AuthenticationPanel.activeSelf, Is.True,
                    "显式退出账号仍应回到登录表单。");
            }
            finally
            {
                gateway.InitWaiter.TrySetResult(true);
                if (gate != null) Object.Destroy(gate.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RevokedSessionReopensLoginOnlyAfterGatewayLosesIdentity()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry, LoadSceneMode.Single);
            yield return null;

            var gateway = new FakeGateway { SignedIn = true };
            CoopAccountView lobby = CoopAccountView.Create(
                GameModeFlowController.Instance, gateway, false);
            try
            {
                Assert.That(lobby.LobbyPanel.activeSelf, Is.True);
                Assert.That(lobby.AuthenticationPanel.activeSelf, Is.False);

                gateway.SignedIn = false;
                yield return null;

                Assert.That(lobby.Controller.State,
                    Is.EqualTo(CoopAccountState.SignedOut));
                Assert.That(lobby.AuthenticationPanel.activeSelf, Is.True,
                    "网关确认会话失效后，应提示重新登录。");
                Assert.That(lobby.LobbyPanel.activeSelf, Is.False);
                Assert.That(lobby.VisibleStatus, Does.Contain("会话已失效"));
            }
            finally
            {
                Object.Destroy(lobby.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthenticationGateUnlocksModesOnlyAfterSignIn()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            GameModeFlowController flow = GameModeFlowController.Instance;
            var gateway = new FakeGateway();
            bool completed = false;
            CoopAccountView gate = CoopAccountView.CreateAuthenticationGate(
                flow, gateway, () => completed = true);
            yield return null;

            Assert.That(completed, Is.False);
            Assert.That(gate.gameObject.activeSelf, Is.True);
            Assert.That(gate.AuthenticationPanel.activeSelf, Is.True);
            gate.RegisterTabButton.onClick.Invoke();
            gate.UsernameInput.text = "Player_Gate";
            gate.PasswordInput.text = "StrongPass1!";
            gate.ConfirmationInput.text = "StrongPass1!";
            gate.RegisterButton.onClick.Invoke();
            yield return null;

            Assert.That(completed, Is.True);
            Assert.That(gate.Controller.IsSignedIn, Is.True);
            Assert.That(gate.gameObject.activeSelf, Is.False);
            Object.Destroy(gate.gameObject);
        }

        private sealed class FakeGateway : ICoopAuthenticationGateway
        {
            public bool SignedIn;
            public int SignUpCalls;
            public int InitializeCalls;
            public int RestoreCachedSessionCalls;
            public bool ClearedCredentials;
            public TaskCompletionSource<bool> InitWaiter;

            public bool IsSignedIn => SignedIn;
            public bool HasCachedSession => false;
            public string PlayerId => SignedIn ? "player-84" : string.Empty;
            public string Username => SignedIn ? "Player_84" : string.Empty;
            public Task InitializeAsync()
            {
                InitializeCalls++;
                return InitWaiter?.Task ?? Task.CompletedTask;
            }

            public Task SignUpAsync(string username, string password)
            {
                SignUpCalls++;
                SignedIn = true;
                return Task.CompletedTask;
            }

            public Task SignInAsync(string username, string password) =>
                Task.CompletedTask;
            public Task RestoreCachedSessionAsync()
            {
                RestoreCachedSessionCalls++;
                return Task.CompletedTask;
            }
            public Task SignInAnonymouslyForDevelopmentAsync() =>
                Task.CompletedTask;

            public void SignOut(bool clearCredentials)
            {
                SignedIn = false;
                ClearedCredentials = clearCredentials;
            }
        }
    }
}
