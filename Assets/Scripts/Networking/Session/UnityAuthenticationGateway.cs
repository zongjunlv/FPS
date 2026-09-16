using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace FPS.Networking.Session
{
    public sealed class UnityAuthenticationGateway : ICoopAuthenticationGateway
    {
        private static bool IsInitialized =>
            UnityServices.State == ServicesInitializationState.Initialized;

        public bool IsSignedIn => IsInitialized &&
                                  AuthenticationService.Instance.IsSignedIn;
        public bool HasCachedSession =>
            IsInitialized && AuthenticationService.Instance.SessionTokenExists;
        public string PlayerId => IsInitialized
            ? AuthenticationService.Instance.PlayerId
            : string.Empty;
        public string Username => IsInitialized
            ? AuthenticationService.Instance.PlayerInfo?.Username ?? string.Empty
            : string.Empty;

        public async Task InitializeAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
        }

        public Task SignUpAsync(string username, string password)
        {
            return Guard(() => AuthenticationService.Instance
                .SignUpWithUsernamePasswordAsync(username, password));
        }

        public Task SignInAsync(string username, string password)
        {
            return Guard(() => AuthenticationService.Instance
                .SignInWithUsernamePasswordAsync(username, password));
        }

        public Task RestoreCachedSessionAsync()
        {
            if (!HasCachedSession)
                throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.SessionExpired);

            // Unity Authentication uses the cached session token when this API
            // is called. No new anonymous identity is created while the token exists.
            return Guard(() => AuthenticationService.Instance
                .SignInAnonymouslyAsync());
        }

        public Task SignInAnonymouslyForDevelopmentAsync()
        {
            return Guard(() => AuthenticationService.Instance
                .SignInAnonymouslyAsync());
        }

        public void SignOut(bool clearCredentials)
        {
            if (!IsInitialized) return;
            if (AuthenticationService.Instance.IsSignedIn ||
                AuthenticationService.Instance.SessionTokenExists)
                AuthenticationService.Instance.SignOut(clearCredentials);
        }

        private static async Task Guard(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (AuthenticationException exception)
            {
                throw Map(exception);
            }
            catch (RequestFailedException exception)
            {
                throw Map(exception);
            }
        }

        private static CoopAuthenticationException Map(
            RequestFailedException exception)
        {
            if (exception.ErrorCode == AuthenticationErrorCodes.InvalidSessionToken)
                return new CoopAuthenticationException(
                    CoopAuthenticationFailure.SessionExpired);
            if (exception.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
                return new CoopAuthenticationException(
                    CoopAuthenticationFailure.InvalidCredentials);
            if (exception.ErrorCode == CommonErrorCodes.TransportError)
                return new CoopAuthenticationException(
                    CoopAuthenticationFailure.NetworkUnavailable);
            if (exception.ErrorCode == CommonErrorCodes.TooManyRequests)
                return new CoopAuthenticationException(
                    CoopAuthenticationFailure.RateLimited);

            string message = exception.Message?.ToLowerInvariant() ?? string.Empty;
            if (message.Contains("already") || message.Contains("conflict"))
                return new CoopAuthenticationException(
                    CoopAuthenticationFailure.AccountAlreadyExists);
            if (message.Contains("invalid") || message.Contains("unauthorized"))
                return new CoopAuthenticationException(
                    CoopAuthenticationFailure.InvalidCredentials);
            return new CoopAuthenticationException(
                CoopAuthenticationFailure.ServiceUnavailable);
        }
    }
}
