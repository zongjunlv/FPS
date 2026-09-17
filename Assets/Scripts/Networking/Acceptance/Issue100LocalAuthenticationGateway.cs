using System.Collections.Generic;
using System.Threading.Tasks;
using FPS.Networking.Session;

namespace FPS.Networking.Acceptance
{
    internal sealed class Issue100LocalAuthenticationGateway :
        ICoopAuthenticationGateway
    {
        private readonly Dictionary<string, string> accounts = new();

        public bool IsSignedIn { get; private set; }
        public bool HasCachedSession => false;
        public string PlayerId { get; private set; } = string.Empty;
        public string Username { get; private set; } = string.Empty;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task SignUpAsync(string username, string password)
        {
            if (accounts.ContainsKey(username))
                throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.AccountAlreadyExists);
            accounts.Add(username, password);
            SignIn(username);
            return Task.CompletedTask;
        }

        public Task SignInAsync(string username, string password)
        {
            if (!accounts.TryGetValue(username, out string expected) ||
                expected != password)
                throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.InvalidCredentials);
            SignIn(username);
            return Task.CompletedTask;
        }

        public Task RestoreCachedSessionAsync() => Task.CompletedTask;

        public Task SignInAnonymouslyForDevelopmentAsync()
        {
            SignIn("acceptance-anonymous");
            return Task.CompletedTask;
        }

        public void SignOut(bool clearCredentials)
        {
            IsSignedIn = false;
            PlayerId = string.Empty;
            Username = string.Empty;
        }

        private void SignIn(string username)
        {
            Username = username;
            PlayerId = "local-acceptance-" + username;
            IsSignedIn = true;
        }
    }
}
