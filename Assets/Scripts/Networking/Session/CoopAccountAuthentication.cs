using System;
using System.Linq;
using System.Threading.Tasks;

namespace FPS.Networking.Session
{
    public enum CoopAccountState
    {
        SignedOut,
        Restoring,
        Registering,
        SigningIn,
        SignedIn,
        SigningOut,
        Failed
    }

    public enum CoopAuthenticationFailure
    {
        InvalidCredentials,
        AccountAlreadyExists,
        NetworkUnavailable,
        RateLimited,
        SessionExpired,
        ProviderUnavailable,
        ServiceUnavailable,
        Unknown
    }

    public sealed class CoopAuthenticationException : Exception
    {
        public CoopAuthenticationFailure Failure { get; }

        public CoopAuthenticationException(CoopAuthenticationFailure failure)
            : base(failure.ToString())
        {
            Failure = failure;
        }
    }

    public interface ICoopAuthenticationGateway
    {
        bool IsSignedIn { get; }
        bool HasCachedSession { get; }
        string PlayerId { get; }
        string Username { get; }

        Task InitializeAsync();
        Task SignUpAsync(string username, string password);
        Task SignInAsync(string username, string password);
        Task RestoreCachedSessionAsync();
        Task SignInAnonymouslyForDevelopmentAsync();
        void SignOut(bool clearCredentials);
    }

    public static class CoopCredentialValidator
    {
        private const string UsernameCharacters = ".-@_";

        public static bool TryValidateUsername(string value, out string error)
        {
            string username = value?.Trim() ?? string.Empty;
            if (username.Length < 3 || username.Length > 20)
            {
                error = "用户名需要包含 3—20 个字符。";
                return false;
            }

            if (username.Any(character =>
                    !char.IsLetterOrDigit(character) &&
                    UsernameCharacters.IndexOf(character) < 0))
            {
                error = "用户名只能使用字母、数字及 . - @ _。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool TryValidatePassword(string value, out string error)
        {
            string password = value ?? string.Empty;
            if (password.Length < 8 || password.Length > 30)
            {
                error = "密码需要包含 8—30 个字符。";
                return false;
            }

            bool letter = password.Any(char.IsLetter);
            bool digit = password.Any(char.IsDigit);
            if (!letter || !digit)
            {
                error = "密码必须至少包含一个字母和一个数字。";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    public sealed class CoopAccountController
    {
        private readonly ICoopAuthenticationGateway gateway;
        private readonly bool allowDevelopmentAnonymous;

        public event Action Changed;

        public CoopAccountState State { get; private set; } =
            CoopAccountState.SignedOut;
        public bool IsBusy => State == CoopAccountState.Restoring ||
                              State == CoopAccountState.Registering ||
                              State == CoopAccountState.SigningIn ||
                              State == CoopAccountState.SigningOut;
        public bool IsSignedIn => gateway.IsSignedIn &&
                                  State == CoopAccountState.SignedIn;
        public bool AllowDevelopmentAnonymous => allowDevelopmentAnonymous;
        public string PlayerId => gateway.IsSignedIn
            ? gateway.PlayerId ?? string.Empty
            : string.Empty;
        public string Username => gateway.IsSignedIn
            ? gateway.Username ?? string.Empty
            : string.Empty;
        public string StatusMessage { get; private set; } = "请输入账号信息。";
        public string LastFailure { get; private set; } = string.Empty;

        public CoopAccountController(ICoopAuthenticationGateway configuredGateway,
            bool configuredDevelopmentAnonymous = false)
        {
            gateway = configuredGateway ?? throw new ArgumentNullException(
                nameof(configuredGateway));
            allowDevelopmentAnonymous = configuredDevelopmentAnonymous;
        }

        public async Task<bool> RestoreAsync()
        {
            if (IsBusy) return false;
            SetState(CoopAccountState.Restoring, "正在恢复登录会话……", string.Empty);
            try
            {
                await gateway.InitializeAsync();
                if (gateway.IsSignedIn)
                {
                    CompleteSignIn("已恢复登录会话。");
                    return true;
                }

                if (!gateway.HasCachedSession)
                {
                    SetState(CoopAccountState.SignedOut, "请输入账号信息。",
                        string.Empty);
                    return false;
                }

                await gateway.RestoreCachedSessionAsync();
                if (!gateway.IsSignedIn)
                    throw new CoopAuthenticationException(
                        CoopAuthenticationFailure.SessionExpired);
                CompleteSignIn("已安全恢复上次登录会话。");
                return true;
            }
            catch (Exception exception)
            {
                bool expired = exception is CoopAuthenticationException auth &&
                               auth.Failure == CoopAuthenticationFailure.SessionExpired;
                if (expired) gateway.SignOut(true);
                // 网络暂时不可用时保留缓存令牌，允许下次启动再次恢复。
                string message = expired
                    ? "登录会话已失效，请重新登录。"
                    : "暂时无法连接账号服务，可稍后重试恢复登录。";
                SetState(CoopAccountState.SignedOut, message, message);
                return false;
            }
        }

        public async Task<bool> RegisterAsync(string username, string password,
            string confirmation)
        {
            if (IsBusy) return false;
            string normalized = username?.Trim() ?? string.Empty;
            if (!ValidateCredentials(normalized, password, out string error))
                return ValidationFailure(error);
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                return ValidationFailure("两次输入的密码不一致。");

            return await RunAuthenticationAsync(CoopAccountState.Registering,
                "正在创建账号……", normalized, password,
                gateway.SignUpAsync, "注册并登录成功。");
        }

        public async Task<bool> SignInAsync(string username, string password)
        {
            if (IsBusy) return false;
            string normalized = username?.Trim() ?? string.Empty;
            if (!ValidateCredentials(normalized, password, out string error))
                return ValidationFailure(error);

            return await RunAuthenticationAsync(CoopAccountState.SigningIn,
                "正在登录……", normalized, password,
                gateway.SignInAsync, "登录成功。");
        }

        public async Task<bool> SignInAnonymouslyForDevelopmentAsync()
        {
            if (IsBusy) return false;
            if (!allowDevelopmentAnonymous)
                return ValidationFailure("匿名登录只允许在开发调试版本中显式使用。");

            SetState(CoopAccountState.SigningIn, "正在使用开发调试身份……",
                string.Empty);
            try
            {
                await gateway.InitializeAsync();
                await gateway.SignInAnonymouslyForDevelopmentAsync();
                if (!gateway.IsSignedIn)
                    throw new CoopAuthenticationException(
                        CoopAuthenticationFailure.ServiceUnavailable);
                CompleteSignIn("开发调试身份登录成功。");
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        public bool SignOut()
        {
            if (IsBusy) return false;
            SetState(CoopAccountState.SigningOut, "正在注销……", string.Empty);
            gateway.SignOut(true);
            SetState(CoopAccountState.SignedOut,
                "已注销并清除本地登录会话。", string.Empty);
            return true;
        }

        private async Task<bool> RunAuthenticationAsync(
            CoopAccountState pendingState,
            string pendingMessage,
            string username,
            string password,
            Func<string, string, Task> operation,
            string successMessage)
        {
            SetState(pendingState, pendingMessage, string.Empty);
            try
            {
                await gateway.InitializeAsync();
                await operation(username, password);
                if (!gateway.IsSignedIn)
                    throw new CoopAuthenticationException(
                        CoopAuthenticationFailure.ServiceUnavailable);
                CompleteSignIn(successMessage);
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
        }

        private static bool ValidateCredentials(string username, string password,
            out string error)
        {
            if (!CoopCredentialValidator.TryValidateUsername(username, out error))
                return false;
            return CoopCredentialValidator.TryValidatePassword(password, out error);
        }

        private bool ValidationFailure(string message)
        {
            SetState(CoopAccountState.Failed, message, message);
            return false;
        }

        private void CompleteSignIn(string message)
        {
            LastFailure = string.Empty;
            string identity = string.IsNullOrWhiteSpace(gateway.Username)
                ? gateway.PlayerId
                : gateway.Username;
            string suffix = string.IsNullOrWhiteSpace(identity)
                ? string.Empty
                : $" 当前账号：{identity}";
            SetState(CoopAccountState.SignedIn, message + suffix, string.Empty);
        }

        private void Fail(Exception exception)
        {
            string message = Describe(exception);
            SetState(CoopAccountState.Failed, message, message);
        }

        private static string Describe(Exception exception)
        {
            if (exception is CoopAuthenticationException authentication)
            {
                return authentication.Failure switch
                {
                    CoopAuthenticationFailure.InvalidCredentials =>
                        "用户名或密码错误，请检查后重试。",
                    CoopAuthenticationFailure.AccountAlreadyExists =>
                        "该用户名已被使用，请更换用户名或直接登录。",
                    CoopAuthenticationFailure.NetworkUnavailable =>
                        "无法连接账号服务，请检查网络后重试。",
                    CoopAuthenticationFailure.RateLimited =>
                        "操作过于频繁，请稍后重试。",
                    CoopAuthenticationFailure.SessionExpired =>
                        "登录会话已失效，请重新登录。",
                    CoopAuthenticationFailure.ProviderUnavailable =>
                        "账号服务尚未开放此登录方式，请联系管理员。",
                    CoopAuthenticationFailure.ServiceUnavailable =>
                        "账号服务暂时不可用，请稍后重试。",
                    _ => "账号操作失败，请稍后重试。"
                };
            }

            return "账号服务暂时不可用，请稍后重试。";
        }

        private void SetState(CoopAccountState state, string status,
            string failure)
        {
            State = state;
            StatusMessage = status;
            LastFailure = failure;
            Changed?.Invoke();
        }
    }
}
