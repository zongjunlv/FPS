using System.Collections;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue69
{
    public sealed class Issue84CoopAccountViewTests
    {
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
