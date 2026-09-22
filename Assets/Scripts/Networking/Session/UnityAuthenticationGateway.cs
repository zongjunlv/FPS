using System;
using System.Security.Cryptography;
using System.Text;
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
                .SignUpWithUsernamePasswordAsync(username,
                    PrepareServicePassword(password)));
        }

        public Task SignInAsync(string username, string password)
        {
            return Guard(() => AuthenticationService.Instance
                .SignInWithUsernamePasswordAsync(username,
                    PrepareServicePassword(password)));
        }

        public static string PrepareServicePassword(string password)
        {
            string value = password ?? string.Empty;
            if (MeetsUnityPasswordPolicy(value)) return value;

            // Unity Authentication 固定要求大小写、数字和符号。本项目面向玩家
            // 只要求字母与数字，因此仅在缺少服务端字符类别时生成稳定的内部凭据。
            // 该值不落盘、不写日志；旧规则下创建的强密码保持原值，继续兼容登录。
            using SHA256 sha256 = SHA256.Create();
            byte[] digest = sha256.ComputeHash(
                Encoding.UTF8.GetBytes("fps-auth-v1:" + value));
            string encoded = Convert.ToBase64String(digest);
            return "Aa1!" + encoded.Substring(0, 26);
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

        private static bool MeetsUnityPasswordPolicy(string password)
        {
            bool upper = false;
            bool lower = false;
            bool digit = false;
            bool symbol = false;
            for (int index = 0; index < password.Length; index++)
            {
                char character = password[index];
                upper |= char.IsUpper(character);
                lower |= char.IsLower(character);
                digit |= char.IsDigit(character);
                symbol |= !char.IsLetterOrDigit(character);
            }

            return password.Length is >= 8 and <= 30 && upper && lower &&
                   digit && symbol;
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
