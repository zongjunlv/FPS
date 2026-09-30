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
        private static SelfHostedAuthenticationGateway currentProcessGateway;
        private static SelfHostedAuthenticationGateway currentSessionOwner;
        private static string currentAccessToken = string.Empty;
        private static string currentAccountId = string.Empty;
        private static string validatedToken = string.Empty;
        private static string validatedAccountId = string.Empty;
        private static string validatedExpiresAtUtc = string.Empty;
        private static long processSessionGeneration;
        private SelfHostedCachedSession cached;
        private bool initialized;
        private bool signedIn;
        private int sessionGeneration;
        private string accountId = string.Empty;
        private string username = string.Empty;

        // Entry 和联机大厅使用同一份已验证的进程内会话；独立 new 仍可用于
        // 测试和需要主动重新读取磁盘缓存的入口。
        public static SelfHostedAuthenticationGateway ForCurrentProcess()
        {
            return currentProcessGateway ??=
                new SelfHostedAuthenticationGateway();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            processSessionGeneration++;
            // Editor 关闭 Domain Reload 时，静态字段不会自动随 Play 重置。
            if (currentProcessGateway != null)
            {
                currentProcessGateway.sessionGeneration++;
                currentProcessGateway.signedIn = false;
                currentProcessGateway.accountId = string.Empty;
                currentProcessGateway.username = string.Empty;
            }
            if (currentSessionOwner != null &&
                !ReferenceEquals(currentSessionOwner, currentProcessGateway))
            {
                currentSessionOwner.sessionGeneration++;
                currentSessionOwner.signedIn = false;
                currentSessionOwner.accountId = string.Empty;
                currentSessionOwner.username = string.Empty;
            }
            currentProcessGateway = null;
            currentSessionOwner = null;
            currentAccessToken = string.Empty;
            currentAccountId = string.Empty;
            ClearValidatedExpiry();
        }

        // 仅在当前进程通过登录或 /me 验证后暴露令牌。不会打印至日志。
        public static string AccessToken => CurrentValidatedTokenExpired
            ? string.Empty
            : currentAccessToken;
        public static string AccountId => CurrentValidatedTokenExpired
            ? string.Empty
            : currentAccountId;
        private static bool CurrentValidatedTokenExpired =>
            !string.IsNullOrEmpty(validatedToken) &&
            string.Equals(validatedToken, currentAccessToken,
                StringComparison.Ordinal) &&
            string.Equals(validatedAccountId, currentAccountId,
                StringComparison.Ordinal) &&
            IsExpired(validatedExpiresAtUtc);
        public bool IsSignedIn => signedIn && cached != null &&
                                  !IsExpired(cached.expiresAtUtc) &&
                                  !string.IsNullOrEmpty(accountId) &&
                                  string.Equals(accountId, currentAccountId,
                                      StringComparison.Ordinal) &&
                                  string.Equals(cached.accessToken,
                                      currentAccessToken,
                                      StringComparison.Ordinal);
        public bool HasCachedSession => cached != null &&
                                        !string.IsNullOrWhiteSpace(
                                            cached.accessToken);
        public string PlayerId => IsSignedIn ? accountId : string.Empty;
        public string Username => IsSignedIn ? username : string.Empty;

        public Task InitializeAsync()
        {
            if (initialized) return Task.CompletedTask;
            initialized = true;
            // 新实例只能验证自己的磁盘缓存，不能清掉另一实例已验证的
            // 当前会话；首启时没有 owner，仍从空令牌开始恢复。
            if (currentSessionOwner == null)
            {
                currentAccessToken = string.Empty;
                currentAccountId = string.Empty;
                ClearValidatedExpiry();
            }
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
            if (IsSignedIn) return;
            // 旧场景的网关不能以自己的磁盘快照覆盖另一个网关已验证的会话。
            if (currentSessionOwner != null &&
                !ReferenceEquals(currentSessionOwner, this) &&
                currentSessionOwner.IsSignedIn)
                throw new OperationCanceledException("登录会话已更改。");
            SelfHostedCachedSession restoring = cached;
            if (restoring == null ||
                string.IsNullOrWhiteSpace(restoring.accessToken) ||
                IsExpired(restoring.expiresAtUtc))
                throw new CoopAuthenticationException(
                    CoopAuthenticationFailure.SessionExpired);
            int expectedGeneration = sessionGeneration;
            long expectedProcessGeneration = processSessionGeneration;
            try
            {
                string json = await SelfHostedHttpClient.SendAsync("GET",
                    "/v1/auth/me", bearerToken: restoring.accessToken);
                if (expectedGeneration != sessionGeneration ||
                    expectedProcessGeneration != processSessionGeneration ||
                    !ReferenceEquals(restoring, cached))
                    throw new OperationCanceledException("登录会话已更改。");
                SelfHostedAccountResponse response =
                    JsonUtility.FromJson<SelfHostedAccountResponse>(json);
                if (response == null ||
                    string.IsNullOrWhiteSpace(response.accountId) ||
                    string.IsNullOrWhiteSpace(response.username) ||
                    !string.Equals(response.accountId, restoring.accountId,
                        StringComparison.Ordinal))
                    throw new CoopAuthenticationException(
                        CoopAuthenticationFailure.SessionExpired);
                accountId = response.accountId;
                username = response.username;
                currentAccessToken = restoring.accessToken;
                currentAccountId = accountId;
                SetValidatedExpiry(restoring);
                signedIn = true;
                currentSessionOwner = this;
                processSessionGeneration++;
            }
            catch (SelfHostedHttpException exception)
            {
                // 退出账号或重新登录后，即使旧 /me 返回 401，也不能让旧
                // Controller 误判为当前会话过期，进而注销新会话。
                if (expectedGeneration != sessionGeneration ||
                    expectedProcessGeneration != processSessionGeneration ||
                    !ReferenceEquals(restoring, cached))
                    throw new OperationCanceledException("登录会话已更改。");
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
            ClearSession(clearCredentials, revokeServer: true);
        }

        // 房间/分配接口的 403 也可能只是“非房主/非成员”，不能直接
        // 当成令牌失效。仅在同一入口的 /me 也拒绝同一令牌时清会话。
        public static async Task<bool> VerifyCurrentSessionAfterAuthorizationFailureAsync(
            string requestToken, CoopDedicatedServerSettings settings)
        {
            SelfHostedAuthenticationGateway owner = currentSessionOwner;
            if (owner == null || string.IsNullOrWhiteSpace(requestToken) ||
                !owner.IsSignedIn ||
                !string.Equals(requestToken, currentAccessToken,
                    StringComparison.Ordinal))
                return false;

            int expectedGeneration = owner.sessionGeneration;
            try
            {
                await SelfHostedHttpClient.SendAsync("GET", "/v1/auth/me",
                    null, requestToken, settings);
                return false;
            }
            catch (SelfHostedHttpException exception) when (
                exception.StatusCode == 401 || exception.StatusCode == 403)
            {
                if (!ReferenceEquals(owner, currentSessionOwner) ||
                    owner.sessionGeneration != expectedGeneration ||
                    !owner.IsSignedIn ||
                    !string.Equals(requestToken, currentAccessToken,
                        StringComparison.Ordinal))
                    return false;

                // 服务端已经撤销此令牌，无需再发送 /logout。
                owner.ClearSession(clearCredentials: true,
                    revokeServer: false);
                return true;
            }
            catch (Exception)
            {
                // 探针超时、TLS/网络故障及配置错误不能作为注销依据。
                return false;
            }
        }

        private void ClearSession(bool clearCredentials, bool revokeServer)
        {
            bool ownsCurrent = ReferenceEquals(currentSessionOwner, this);
            bool noCurrentSession = currentSessionOwner == null &&
                                    string.IsNullOrEmpty(currentAccessToken);
            string token = ownsCurrent ? currentAccessToken : string.Empty;
            string cachedToken = cached?.accessToken;
            sessionGeneration++;
            signedIn = false;
            accountId = string.Empty;
            username = string.Empty;
            if (ownsCurrent)
            {
                processSessionGeneration++;
                currentSessionOwner = null;
                currentAccessToken = string.Empty;
                currentAccountId = string.Empty;
                ClearValidatedExpiry();
            }
            if (!clearCredentials) return;
            cached = null;
            // 旧场景的 Controller 可能晚于新账号登出；非当前所有者绝不能
            // 删除新账号的共享缓存文件或撤销其令牌。
            if (!ownsCurrent && !noCurrentSession) return;
            try
            {
                string path = GetSessionPath();
                if (File.Exists(path) && ownsCurrent)
                    File.Delete(path);
                else if (File.Exists(path) &&
                         !string.IsNullOrWhiteSpace(cachedToken))
                {
                    SelfHostedCachedSession disk =
                        JsonUtility.FromJson<SelfHostedCachedSession>(
                            File.ReadAllText(path));
                    if (string.Equals(disk?.accessToken, cachedToken,
                            StringComparison.Ordinal))
                        File.Delete(path);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    ArgumentException)
            {
                // 本地文件清理尽力而为；服务器仍会尝试撤销令牌。
            }
            if (revokeServer && !string.IsNullOrWhiteSpace(token))
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
            int expectedGeneration = sessionGeneration;
            long expectedProcessGeneration = processSessionGeneration;
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

            if (expectedGeneration != sessionGeneration ||
                expectedProcessGeneration != processSessionGeneration)
            {
                if (!string.Equals(response.accessToken, currentAccessToken,
                        StringComparison.Ordinal))
                    RevokeBestEffortAsync(response.accessToken);
                throw new OperationCanceledException("登录会话已更改。");
            }

            sessionGeneration++;
            processSessionGeneration++;
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
            SetValidatedExpiry(cached);
            signedIn = true;
            currentSessionOwner = this;
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

        private static void SetValidatedExpiry(SelfHostedCachedSession session)
        {
            validatedToken = session.accessToken;
            validatedAccountId = session.accountId;
            validatedExpiresAtUtc = session.expiresAtUtc;
        }

        private static void ClearValidatedExpiry()
        {
            validatedToken = string.Empty;
            validatedAccountId = string.Empty;
            validatedExpiresAtUtc = string.Empty;
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
