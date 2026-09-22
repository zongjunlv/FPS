using System.Collections;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue69
{
    public sealed class Issue84CoopAccountViewTests
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
            Assert.That(view.RegisterButton.gameObject.activeSelf, Is.False);
            Assert.That(view.ConfirmationInput.gameObject.activeSelf, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject,
                Is.EqualTo(view.UsernameInput.gameObject));
            Assert.That(view.UsernameInput.customCaretColor, Is.True);
            Assert.That(view.UsernameInput.caretWidth, Is.GreaterThanOrEqualTo(3));

            view.RegisterTabButton.onClick.Invoke();
            Assert.That(view.IsRegisterMode, Is.True);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.False);
            Assert.That(view.RegisterButton.gameObject.activeSelf, Is.True);
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
            Assert.That(view.LogoutButton.gameObject.activeSelf, Is.True);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.False);

            view.LogoutButton.onClick.Invoke();
            Assert.That(view.Controller.State,
                Is.EqualTo(CoopAccountState.SignedOut));
            Assert.That(gateway.ClearedCredentials, Is.True);
            Assert.That(view.LoginButton.gameObject.activeSelf, Is.True);
            Object.Destroy(view.gameObject);
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
            public bool ClearedCredentials;

            public bool IsSignedIn => SignedIn;
            public bool HasCachedSession => false;
            public string PlayerId => SignedIn ? "player-84" : string.Empty;
            public string Username => SignedIn ? "Player_84" : string.Empty;
            public Task InitializeAsync() => Task.CompletedTask;

            public Task SignUpAsync(string username, string password)
            {
                SignUpCalls++;
                SignedIn = true;
                return Task.CompletedTask;
            }

            public Task SignInAsync(string username, string password) =>
                Task.CompletedTask;
            public Task RestoreCachedSessionAsync() => Task.CompletedTask;
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
