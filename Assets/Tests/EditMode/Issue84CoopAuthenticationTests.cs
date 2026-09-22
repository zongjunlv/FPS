using System;
using System.Threading.Tasks;
using FPS.Networking.Session;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue84CoopAuthenticationTests
    {
        private const string ValidUsername = "Player_01";
        private const string ValidPassword = "simple123";

        [TestCase("ab", false)]
        [TestCase("player name", false)]
        [TestCase("Player_01", true)]
        [TestCase("unit.test@fps", true)]
        public void UsernameRulesMatchUnityAuthentication(string value,
            bool expected)
        {
            Assert.That(CoopCredentialValidator.TryValidateUsername(value,
                out _), Is.EqualTo(expected));
        }

        [TestCase("letters123", true)]
        [TestCase("LETTERS123", true)]
        [TestCase("Letters12!", true)]
        [TestCase("onlyletters", false)]
        [TestCase("12345678", false)]
        [TestCase("ab12", false)]
        public void PasswordRulesRequireLetterAndNumber(string value,
            bool expected)
        {
            Assert.That(CoopCredentialValidator.TryValidatePassword(value,
                out _), Is.EqualTo(expected));
        }

        [Test]
        public void RelaxedPasswordIsAdaptedForUnityWithoutBreakingOldAccounts()
        {
            const string relaxed = "simple123";
            string first = UnityAuthenticationGateway.PrepareServicePassword(
                relaxed);
            string second = UnityAuthenticationGateway.PrepareServicePassword(
                relaxed);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Length.EqualTo(30));
            Assert.That(first, Is.Not.EqualTo(relaxed));
            Assert.That(UnityAuthenticationGateway.PrepareServicePassword(
                "StrongPass1!"), Is.EqualTo("StrongPass1!"));
        }

        [Test]
        public async Task InvalidRegistrationNeverReachesGateway()
        {
            var gateway = new FakeAuthenticationGateway();
            var controller = new CoopAccountController(gateway);

            bool result = await controller.RegisterAsync("bad user", "weak",
                "weak");

            Assert.That(result, Is.False);
            Assert.That(gateway.SignUpCalls, Is.Zero);
            Assert.That(controller.State, Is.EqualTo(CoopAccountState.Failed));
            Assert.That(controller.LastFailure, Is.Not.Empty);
        }

        [Test]
        public async Task RegistrationSignsInWithoutRetainingPasswordState()
        {
            var gateway = new FakeAuthenticationGateway();
            gateway.SignUpHandler = (_, _) =>
            {
                gateway.SignedIn = true;
                gateway.CurrentUsername = ValidUsername;
                gateway.CurrentPlayerId = "player-84";
                return Task.CompletedTask;
            };
            var controller = new CoopAccountController(gateway);

            bool result = await controller.RegisterAsync(ValidUsername,
                ValidPassword, ValidPassword);

            Assert.That(result, Is.True);
            Assert.That(gateway.SignUpCalls, Is.EqualTo(1));
            Assert.That(controller.State, Is.EqualTo(CoopAccountState.SignedIn));
            Assert.That(controller.Username, Is.EqualTo(ValidUsername));
            Assert.That(controller.StatusMessage, Does.Not.Contain(ValidPassword));
        }

        [Test]
        public async Task FailedLoginShowsChineseMessageAndCanRetry()
        {
            var gateway = new FakeAuthenticationGateway();
            gateway.SignInHandler = (_, _) => throw
                new CoopAuthenticationException(
                    CoopAuthenticationFailure.InvalidCredentials);
            var controller = new CoopAccountController(gateway);

            Assert.That(await controller.SignInAsync(ValidUsername,
                ValidPassword), Is.False);
            Assert.That(controller.LastFailure, Does.Contain("用户名或密码错误"));

            gateway.SignInHandler = (_, _) =>
            {
                gateway.SignedIn = true;
                gateway.CurrentUsername = ValidUsername;
                return Task.CompletedTask;
            };
            Assert.That(await controller.SignInAsync(ValidUsername,
                ValidPassword), Is.True);
            Assert.That(controller.LastFailure, Is.Empty);
            Assert.That(gateway.SignInCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task BusyControllerRejectsDuplicateSubmission()
        {
            var gateway = new FakeAuthenticationGateway();
            var release = new TaskCompletionSource<bool>();
            gateway.SignInHandler = async (_, _) =>
            {
                await release.Task;
                gateway.SignedIn = true;
            };
            var controller = new CoopAccountController(gateway);

            Task<bool> first = controller.SignInAsync(ValidUsername,
                ValidPassword);
            bool duplicate = await controller.SignInAsync(ValidUsername,
                ValidPassword);
            Assert.That(duplicate, Is.False);
            Assert.That(gateway.SignInCalls, Is.EqualTo(1));

            release.SetResult(true);
            Assert.That(await first, Is.True);
        }

        [Test]
        public async Task CachedSessionRestoresExistingIdentity()
        {
            var gateway = new FakeAuthenticationGateway
            {
                CachedSession = true
            };
            gateway.RestoreHandler = () =>
            {
                gateway.SignedIn = true;
                gateway.CurrentPlayerId = "restored-player";
                return Task.CompletedTask;
            };
            var controller = new CoopAccountController(gateway);

            Assert.That(await controller.RestoreAsync(), Is.True);
            Assert.That(controller.State, Is.EqualTo(CoopAccountState.SignedIn));
            Assert.That(controller.PlayerId, Is.EqualTo("restored-player"));
            Assert.That(gateway.RestoreCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ExpiredSessionIsClearedAndReturnsToLogin()
        {
            var gateway = new FakeAuthenticationGateway
            {
                CachedSession = true,
                RestoreHandler = () => throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.SessionExpired)
            };
            var controller = new CoopAccountController(gateway);

            Assert.That(await controller.RestoreAsync(), Is.False);
            Assert.That(controller.State, Is.EqualTo(CoopAccountState.SignedOut));
            Assert.That(controller.LastFailure, Does.Contain("失效"));
            Assert.That(gateway.SignOutCalls, Is.EqualTo(1));
            Assert.That(gateway.LastClearCredentials, Is.True);
        }

        [Test]
        public async Task LogoutClearsLocalSessionAndIdentity()
        {
            var gateway = new FakeAuthenticationGateway
            {
                SignedIn = true,
                CachedSession = true,
                CurrentUsername = ValidUsername
            };
            var controller = new CoopAccountController(gateway);
            await controller.RestoreAsync();

            Assert.That(controller.SignOut(), Is.True);
            Assert.That(controller.State, Is.EqualTo(CoopAccountState.SignedOut));
            Assert.That(gateway.LastClearCredentials, Is.True);
            Assert.That(controller.Username, Is.Empty);
        }

        [Test]
        public async Task AnonymousIdentityRequiresExplicitDevelopmentOptIn()
        {
            var gateway = new FakeAuthenticationGateway();
            var releaseController = new CoopAccountController(gateway, false);
            Assert.That(await releaseController
                .SignInAnonymouslyForDevelopmentAsync(), Is.False);
            Assert.That(gateway.AnonymousCalls, Is.Zero);

            gateway.AnonymousHandler = () =>
            {
                gateway.SignedIn = true;
                return Task.CompletedTask;
            };
            var developmentController = new CoopAccountController(gateway, true);
            Assert.That(await developmentController
                .SignInAnonymouslyForDevelopmentAsync(), Is.True);
            Assert.That(gateway.AnonymousCalls, Is.EqualTo(1));
        }

        private sealed class FakeAuthenticationGateway :
            ICoopAuthenticationGateway
        {
            public bool SignedIn;
            public bool CachedSession;
            public string CurrentPlayerId = string.Empty;
            public string CurrentUsername = string.Empty;
            public int SignUpCalls;
            public int SignInCalls;
            public int RestoreCalls;
            public int AnonymousCalls;
            public int SignOutCalls;
            public bool LastClearCredentials;
            public Func<string, string, Task> SignUpHandler = (_, _) =>
                Task.CompletedTask;
            public Func<string, string, Task> SignInHandler = (_, _) =>
                Task.CompletedTask;
            public Func<Task> RestoreHandler = () => Task.CompletedTask;
            public Func<Task> AnonymousHandler = () => Task.CompletedTask;

            public bool IsSignedIn => SignedIn;
            public bool HasCachedSession => CachedSession;
            public string PlayerId => CurrentPlayerId;
            public string Username => CurrentUsername;

            public Task InitializeAsync() => Task.CompletedTask;

            public Task SignUpAsync(string username, string password)
            {
                SignUpCalls++;
                return SignUpHandler(username, password);
            }

            public Task SignInAsync(string username, string password)
            {
                SignInCalls++;
                return SignInHandler(username, password);
            }

            public Task RestoreCachedSessionAsync()
            {
                RestoreCalls++;
                return RestoreHandler();
            }

            public Task SignInAnonymouslyForDevelopmentAsync()
            {
                AnonymousCalls++;
                return AnonymousHandler();
            }

            public void SignOut(bool clearCredentials)
            {
                SignOutCalls++;
                LastClearCredentials = clearCredentials;
                SignedIn = false;
                if (clearCredentials) CachedSession = false;
                CurrentPlayerId = string.Empty;
                CurrentUsername = string.Empty;
            }
        }
    }
}
