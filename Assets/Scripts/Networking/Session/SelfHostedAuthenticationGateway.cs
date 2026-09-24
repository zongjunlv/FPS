using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace FPS.Networking.Session
{
    [Serializable]
    internal sealed class SelfHostedCredentialRequest
    {
        public string username;
        public string password;
    }

    [Serializable]
    internal sealed class SelfHostedAccountResponse
    {
        public string accountId;
        public string username;
        public string accessToken;
        public string expiresAtUtc;
    }

    [Serializable]
    internal sealed class SelfHostedCachedSession
    {
        public string accountId;
        public string username;
        public string accessToken;
        public string expiresAtUtc;
    }

    public sealed class SelfHostedAuthenticationGateway :
        ICoopAuthenticationGateway
    {
        private const string SessionFileName = "fps-account-session.json";
        private static string currentAccessToken = string.Empty;
        private static string currentAccountId = string.Empty;
        private SelfHostedCachedSession cached;
        private bool initialized;
        private bool signedIn;
        private string accountId = string.Empty;
        private string username = string.Empty;

        // 仅在当前进程通过登录或 /me 验证后暴露令牌。不会打印至日志。
        public static string AccessToken => currentAccessToken;
        public static string AccountId => currentAccountId;
        public bool IsSignedIn => signedIn && !string.IsNullOrEmpty(accountId) &&
                                  !string.IsNullOrEmpty(currentAccessToken);
        public bool HasCachedSession => cached != null &&
                                        !string.IsNullOrWhiteSpace(
                                            cached.accessToken);
        public string PlayerId => IsSignedIn ? accountId : string.Empty;
        public string Username => IsSignedIn ? username : string.Empty;

        public Task InitializeAsync()
        {
            if (initialized) return Task.CompletedTask;
            initialized = true;
            // 新场景的账号网关必须重新向自建服务确认本地缓存，而不是沿用
            // 旧场景中未经本实例验证的静态令牌。
            currentAccessToken = string.Empty;
            currentAccountId = string.Empty;
            try
            {
                string path = GetSessionPath();
                if (PrepareSessionDirectory() && File.Exists(path))
                    cached = JsonUtility.FromJson<SelfHostedCachedSession>(
                        File.ReadAllText(path));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    ArgumentException)
            {
                // 本地会话损坏或不可读时回退到显式登录，不读取或输出凭据。
                cached = null;
            }
            return Task.CompletedTask;
        }

        public Task SignUpAsync(string value, string password)
        {
            return AuthenticateAsync("/v1/auth/register", value, password);
        }

        public Task SignInAsync(string value, string password)
        {
            return AuthenticateAsync("/v1/auth/login", value, password);
        }

        public async Task RestoreCachedSessionAsync()
        {
            await InitializeAsync();
            if (!HasCachedSession || IsExpired(cached.expiresAtUtc))
                throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.SessionExpired);
            try
            {
                string json = await SelfHostedHttpClient.SendAsync("GET",
                    "/v1/auth/me", bearerToken: cached.accessToken);
                SelfHostedAccountResponse response =
                    JsonUtility.FromJson<SelfHostedAccountResponse>(json);
                if (response == null ||
                    string.IsNullOrWhiteSpace(response.accountId) ||
                    string.IsNullOrWhiteSpace(response.username) ||
                    !string.Equals(response.accountId, cached.accountId,
                        StringComparison.Ordinal))
                    throw new CoopAuthenticationException(
                        CoopAuthenticationFailure.SessionExpired);
                accountId = response.accountId;
                username = response.username;
                currentAccessToken = cached.accessToken;
                currentAccountId = accountId;
                signedIn = true;
            }
            catch (SelfHostedHttpException exception)
            {
                throw new CoopAuthenticationException(
                    ClassifyFailure(exception, restoring: true));
            }
        }

        public Task SignInAnonymouslyForDevelopmentAsync()
        {
            throw new CoopAuthenticationException(
                CoopAuthenticationFailure.ProviderUnavailable);
        }

        public void SignOut(bool clearCredentials)
        {
            string token = currentAccessToken;
            signedIn = false;
            accountId = string.Empty;
            username = string.Empty;
            currentAccessToken = string.Empty;
            currentAccountId = string.Empty;
            if (!clearCredentials) return;
            cached = null;
            try
            {
                string path = GetSessionPath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // 本地文件清理尽力而为；服务器仍会尝试撤销令牌。
            }
            if (!string.IsNullOrWhiteSpace(token))
                RevokeBestEffortAsync(token);
        }

        public static CoopAuthenticationFailure ClassifyFailure(
            SelfHostedHttpException exception, bool restoring = false)
        {
            if (exception == null) return CoopAuthenticationFailure.Unknown;
            if (exception.StatusCode == 401 || exception.StatusCode == 403)
                return restoring
                    ? CoopAuthenticationFailure.SessionExpired
                    : CoopAuthenticationFailure.InvalidCredentials;
            if (exception.StatusCode == 409 ||
                exception.ErrorCode == "username_taken")
                return CoopAuthenticationFailure.AccountAlreadyExists;
            if (exception.StatusCode == 429)
                return CoopAuthenticationFailure.RateLimited;
            if (exception.StatusCode == 400 || exception.StatusCode == 422)
                return CoopAuthenticationFailure.InvalidCredentials;
            if (exception.StatusCode <= 0 &&
                exception.ErrorCode != "client_config")
                return CoopAuthenticationFailure.NetworkUnavailable;
            return CoopAuthenticationFailure.ServiceUnavailable;
        }

        public static bool IsExpired(string expiresAtUtc)
        {
            return !DateTimeOffset.TryParse(expiresAtUtc,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal,
                       out DateTimeOffset expiry) ||
                   expiry <= DateTimeOffset.UtcNow.AddSeconds(10);
        }

        private async Task AuthenticateAsync(string path, string value,
            string password)
        {
            await InitializeAsync();
            string requestJson = JsonUtility.ToJson(
                new SelfHostedCredentialRequest
                {
                    username = value,
                    password = password
                });
            string json;
            try
            {
                json = await SelfHostedHttpClient.SendAsync("POST", path,
                    requestJson);
            }
            catch (SelfHostedHttpException exception)
            {
                throw new CoopAuthenticationException(
                    ClassifyFailure(exception));
            }
            SelfHostedAccountResponse response;
            try
            {
                response = JsonUtility.FromJson<SelfHostedAccountResponse>(
                    json);
            }
            catch (ArgumentException)
            {
                response = null;
            }
            if (response == null ||
                string.IsNullOrWhiteSpace(response.accountId) ||
                string.IsNullOrWhiteSpace(response.username) ||
                string.IsNullOrWhiteSpace(response.accessToken) ||
                IsExpired(response.expiresAtUtc))
                throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.ServiceUnavailable);

            cached = new SelfHostedCachedSession
            {
                accountId = response.accountId,
                username = response.username,
                accessToken = response.accessToken,
                expiresAtUtc = response.expiresAtUtc
            };
            accountId = cached.accountId;
            username = cached.username;
            currentAccessToken = cached.accessToken;
            currentAccountId = accountId;
            signedIn = true;
            try
            {
                if (!PrepareSessionDirectory()) return;
                // 只缓存短期访问令牌，不存密码或长期刷新令牌。该文件不能抵御
                // 同一系统账号下的恶意程序；正式发行可换平台安全凭据仓库。
                File.WriteAllText(GetSessionPath(),
                    JsonUtility.ToJson(cached));
                RestrictSessionFile();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // 缓存写入失败不阻止本次登录，只影响下次自动恢复。
            }
        }

        private static string GetSessionPath()
        {
            return Path.Combine(Application.persistentDataPath,
                SessionFileName);
        }

        private static bool PrepareSessionDirectory()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            // Create/restrict the parent before writing the bearer token, so
            // a default 0644 file mode cannot expose it to another OS user.
            return Chmod(Application.persistentDataPath, 0x1C0u) == 0; // 0700
#else
            // On Windows persistentDataPath sits under the user's profile ACL.
            return true;
#endif
        }

        private static void RestrictSessionFile()
        {
#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            if (Chmod(GetSessionPath(), 0x180u) != 0) // 0600
                File.Delete(GetSessionPath());
#endif
        }

#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string path, uint mode);
#endif

        private static async void RevokeBestEffortAsync(string token)
        {
            try
            {
                await SelfHostedHttpClient.SendAsync("POST", "/v1/auth/logout",
                    "{}", token);
            }
            catch (Exception)
            {
                // 当前同步 UI 接口无法等待网络；服务器端还需强制令牌过期。
            }
        }
    }
}
