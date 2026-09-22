using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using FPS.Networking.Session;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue69
{
    public sealed class Issue87CoopLobbyViewTests
    {
        [UnityTest]
        public IEnumerator SignedInAccountSeesRoomEntryAndCharacterControls()
        {
            yield return SceneManager.LoadSceneAsync(GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            GameModeFlowController flow = GameModeFlowController.Instance;
            var sessionObject = new GameObject("Issue87 Session");
            CoopSessionController session =
                sessionObject.AddComponent<CoopSessionController>();
            PlayerAppearanceCatalog catalog =
                Resources.Load<PlayerAppearanceCatalog>(
                    PlayerAppearanceCatalog.ResourcesPath);
            Assert.That(catalog, Is.Not.Null);
            var gateway = new FakeGateway();
            CoopAccountView view = CoopAccountView.Create(flow, gateway, false,
                session, catalog);
            yield return null;

            view.UsernameInput.text = "Player_87";
            view.PasswordInput.text = "StrongPass1!";
            view.ConfirmationInput.text = "StrongPass1!";
            view.RegisterButton.onClick.Invoke();
            yield return null;

            Assert.That(view.Controller.IsSignedIn, Is.True);
            Assert.That(view.AuthenticationPanel.activeSelf, Is.False);
            Assert.That(view.LobbyPanel.activeSelf, Is.True);
            Assert.That(view.CreateRoomButton.gameObject.activeSelf, Is.True);
            Assert.That(view.JoinRoomButton.gameObject.activeSelf, Is.True);
            Assert.That(view.RefreshRoomsButton.gameObject.activeSelf, Is.True);
            Assert.That(view.JoinSelectedRoomButton.gameObject.activeSelf,
                Is.True);
            Assert.That(view.PublicRoomsContent, Is.Not.Null);
            Assert.That(view.JoinSelectedRoomButton.interactable, Is.False);
            Assert.That(view.ReadyButton.gameObject.activeSelf, Is.False);
            Assert.That(view.StartRoomButton.gameObject.activeSelf, Is.False);
            Assert.That(view.JoinRoomButton.interactable, Is.False);
            Assert.That(view.LobbyPanel
                .GetComponentsInChildren<UnityEngine.UI.Image>(true)
                .Any(image => image.gameObject.name == "大厅图标" &&
                              image.sprite != null), Is.True);

            view.JoinCodeInput.text = "ABC123";
            yield return null;
            Assert.That(view.JoinRoomButton.interactable, Is.True);

            string[] visibleCopy = view.LobbyPanel
                .GetComponentsInChildren<TMP_Text>()
                .Select(value => value.text)
                .ToArray();
            Assert.That(visibleCopy, Does.Contain("行动整备大厅"));
            Assert.That(visibleCopy.Any(value =>
                value.Contains("玩家席位 1") &&
                value.Contains("玩家席位 2")), Is.True);

            Object.Destroy(view.gameObject);
            Object.Destroy(sessionObject);
            yield return null;
        }

        private sealed class FakeGateway : ICoopAuthenticationGateway
        {
            public bool SignedIn;
            public bool IsSignedIn => SignedIn;
            public bool HasCachedSession => false;
            public string PlayerId => SignedIn ? "player-87" : string.Empty;
            public string Username => SignedIn ? "Player_87" : string.Empty;
            public Task InitializeAsync() => Task.CompletedTask;

            public Task SignUpAsync(string username, string password)
            {
                SignedIn = true;
                return Task.CompletedTask;
            }

            public Task SignInAsync(string username, string password)
            {
                SignedIn = true;
                return Task.CompletedTask;
            }

            public Task RestoreCachedSessionAsync() => Task.CompletedTask;
            public Task SignInAnonymouslyForDevelopmentAsync() =>
                Task.CompletedTask;
            public void SignOut(bool clearCredentials) => SignedIn = false;
        }
    }
}
