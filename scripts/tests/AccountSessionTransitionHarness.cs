using System;
using System.IO;
using System.Threading.Tasks;
using FPS.Networking.Session;

internal static class AccountSessionTransitionHarness
{
    private static async Task<int> Main()
    {
        var gateway = new VerifiedGateway();
        var controller = new CoopAccountController(gateway);
        int failures = 0;

        if (controller.State != CoopAccountState.SignedIn ||
            !controller.IsSignedIn)
        {
            Console.Error.WriteLine(
                "FAIL: 新场景的控制器未同步继承本进程已验证的登录状态。");
            failures++;
        }

        bool restored = await controller.RestoreAsync();
        if (!restored || gateway.InitializeCalls != 0 ||
            gateway.RestoreCalls != 0)
        {
            Console.Error.WriteLine(
                "FAIL: 已验证会话仍重新初始化或发起缓存恢复。");
            failures++;
        }

        failures += await VerifySelfHostedGatewayAcrossScenes();
        failures += await VerifyLogoutWinsPendingRestore();
        failures += await VerifyNewGatewayLoginWinsPendingRestore();
        failures += await VerifyNewGatewayLoginWinsPendingLogin();
        failures += await VerifyRevokedTokenClearsSession();
        failures += await VerifyOrdinaryPermissionDenialKeepsSession();
        failures += await VerifyProbeNetworkFailureKeepsSession();
        failures += await VerifyLateRejectionCannotClearNewLogin();
        failures += await VerifyStaleGatewayCannotSignOutNewOwner();

        if (failures == 0)
            Console.WriteLine("PASS: 会话跨场景复用、撤销判定及并发隔离。");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> VerifySelfHostedGatewayAcrossScenes()
    {
        string sessionDirectory = Path.Combine(Path.GetTempPath(),
            "fps-account-session-harness-" + Guid.NewGuid().ToString("N"));
        UnityEngine.Application.persistentDataPath = sessionDirectory;
        int failures = 0;
        SelfHostedAuthenticationGateway gateway =
            SelfHostedAuthenticationGateway.ForCurrentProcess();
        try
        {
            await gateway.SignUpAsync("VerifiedPlayer", "letters123");
            if (!gateway.IsSignedIn)
            {
                Console.Error.WriteLine(
                    "FAIL: 测试登录未建立进程内已验证会话。");
                return 1;
            }

            int previousChecks = SelfHostedHttpClient.MeCalls;
            SelfHostedAuthenticationGateway nextScene =
                SelfHostedAuthenticationGateway.ForCurrentProcess();
            var nextController = new CoopAccountController(nextScene);
            bool restored = await nextController.RestoreAsync();
            if (!ReferenceEquals(gateway, nextScene) || !restored ||
                !nextController.IsSignedIn ||
                SelfHostedHttpClient.MeCalls != previousChecks)
            {
                Console.Error.WriteLine(
                    "FAIL: 跨场景未复用已验证会话，或重新请求 /v1/auth/me。");
                failures++;
            }

            nextController.SignOut();
            if (gateway.IsSignedIn ||
                !string.IsNullOrEmpty(SelfHostedAuthenticationGateway.AccessToken))
            {
                Console.Error.WriteLine(
                    "FAIL: 显式退出账号后仍暴露已登录状态或令牌。");
                failures++;
            }

            SelfHostedHttpClient.IssuedLifetime = TimeSpan.FromSeconds(12);
            await gateway.SignInAsync("VerifiedPlayer", "letters123");
            if (!gateway.IsSignedIn)
            {
                Console.Error.WriteLine(
                    "FAIL: 有效的短期令牌无法建立登录会话。");
                failures++;
            }
            await Task.Delay(2500);
            if (gateway.IsSignedIn ||
                !string.IsNullOrEmpty(SelfHostedAuthenticationGateway.AccessToken))
            {
                Console.Error.WriteLine(
                    "FAIL: 令牌进入到期保护窗口后仍被当作有效会话。");
                failures++;
            }
        }
        finally
        {
            gateway.SignOut(true);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
        return failures;
    }

    private static async Task<int> VerifyLogoutWinsPendingRestore()
    {
        string sessionDirectory = Path.Combine(Path.GetTempPath(),
            "fps-account-logout-race-" + Guid.NewGuid().ToString("N"));
        UnityEngine.Application.persistentDataPath = sessionDirectory;
        SelfHostedHttpClient.IssuedLifetime = TimeSpan.FromMinutes(5);
        var gateway = new SelfHostedAuthenticationGateway();
        int failures = 0;
        try
        {
            await gateway.SignUpAsync("VerifiedPlayer", "letters123");
            gateway.SignOut(false); // Keep only the cached session to restore.
            SelfHostedHttpClient.PendingMe = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task restoration = gateway.RestoreCachedSessionAsync();
            if (restoration.IsCompleted)
            {
                Console.Error.WriteLine(
                    "FAIL: /v1/auth/me 测试请求没有保持待完成状态。");
                failures++;
            }

            gateway.SignOut(true);
            SelfHostedHttpClient.PendingMe.SetResult(
                "{\"accountId\":\"test-account\"," +
                "\"username\":\"VerifiedPlayer\"}");
            try
            {
                await restoration;
            }
            catch (OperationCanceledException)
            {
                // Cancelling the stale request is an acceptable outcome.
            }

            if (gateway.IsSignedIn ||
                !string.IsNullOrEmpty(SelfHostedAuthenticationGateway.AccessToken))
            {
                Console.Error.WriteLine(
                    "FAIL: 显式注销后旧 /v1/auth/me 响应重新激活了账号。");
                failures++;
            }
        }
        finally
        {
            SelfHostedHttpClient.PendingMe = null;
            gateway.SignOut(true);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
        return failures;
    }

    private static async Task<int> VerifyNewGatewayLoginWinsPendingRestore()
    {
        string sessionDirectory = Path.Combine(Path.GetTempPath(),
            "fps-account-cross-gateway-restore-" + Guid.NewGuid().ToString("N"));
        UnityEngine.Application.persistentDataPath = sessionDirectory;
        SelfHostedHttpClient.IssuedLifetime = TimeSpan.FromMinutes(5);
        SelfHostedHttpClient.MeHandler = null;
        var previousGateway = new SelfHostedAuthenticationGateway();
        var currentGateway = new SelfHostedAuthenticationGateway();
        const string currentToken = "cross-gateway-current-token";
        string sessionFile = Path.Combine(sessionDirectory,
            "fps-account-session.json");
        try
        {
            SelfHostedHttpClient.IssuedToken =
                "cross-gateway-previous-token";
            await previousGateway.SignUpAsync("VerifiedPlayer", "letters123");
            previousGateway.SignOut(false);

            SelfHostedHttpClient.PendingMe = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task previousRestore = previousGateway.RestoreCachedSessionAsync();
            if (previousRestore.IsCompleted)
            {
                Console.Error.WriteLine(
                    "FAIL: 旧网关的 /v1/auth/me 请求未保持待完成状态。");
                return 1;
            }

            SelfHostedHttpClient.IssuedToken = currentToken;
            await currentGateway.SignInAsync("VerifiedPlayer", "letters123");
            SelfHostedHttpClient.PendingMe.SetResult(
                "{\"accountId\":\"test-account\"," +
                "\"username\":\"VerifiedPlayer\"}");
            try
            {
                await previousRestore;
            }
            catch (OperationCanceledException)
            {
                // 旧请求被取消是预期行为。
            }

            int previousMeCalls = SelfHostedHttpClient.MeCalls;
            bool staleGatewayRestoreBlocked = false;
            try
            {
                await previousGateway.RestoreCachedSessionAsync();
            }
            catch (OperationCanceledException)
            {
                staleGatewayRestoreBlocked = true;
            }

            bool cacheStillOwnedByCurrent = File.Exists(sessionFile) &&
                UnityEngine.JsonUtility.FromJson<SelfHostedCachedSession>(
                    File.ReadAllText(sessionFile))?.accessToken == currentToken;
            if (currentGateway.IsSignedIn && !previousGateway.IsSignedIn &&
                SelfHostedAuthenticationGateway.AccessToken == currentToken &&
                cacheStillOwnedByCurrent && staleGatewayRestoreBlocked &&
                SelfHostedHttpClient.MeCalls == previousMeCalls)
                return 0;
            Console.Error.WriteLine(
                "FAIL: 旧网关迟到的 /v1/auth/me 响应或后续恢复覆盖了新网关会话。");
            return 1;
        }
        finally
        {
            SelfHostedHttpClient.PendingMe = null;
            currentGateway.SignOut(true);
            previousGateway.SignOut(true);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    private static async Task<int> VerifyNewGatewayLoginWinsPendingLogin()
    {
        string sessionDirectory = Path.Combine(Path.GetTempPath(),
            "fps-account-cross-gateway-login-" + Guid.NewGuid().ToString("N"));
        UnityEngine.Application.persistentDataPath = sessionDirectory;
        SelfHostedHttpClient.IssuedLifetime = TimeSpan.FromMinutes(5);
        var previousGateway = new SelfHostedAuthenticationGateway();
        var currentGateway = new SelfHostedAuthenticationGateway();
        const string currentToken = "cross-gateway-current-login-token";
        string sessionFile = Path.Combine(sessionDirectory,
            "fps-account-session.json");
        var pendingPreviousLogin = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            SelfHostedHttpClient.AuthenticationHandler =
                _ => pendingPreviousLogin.Task;
            Task previousLogin = previousGateway.SignInAsync(
                "VerifiedPlayer", "letters123");
            if (previousLogin.IsCompleted)
            {
                Console.Error.WriteLine(
                    "FAIL: 旧网关的登录请求未保持待完成状态。");
                return 1;
            }

            SelfHostedHttpClient.AuthenticationHandler = null;
            SelfHostedHttpClient.IssuedToken = currentToken;
            await currentGateway.SignInAsync("VerifiedPlayer", "letters123");
            string expiration = DateTimeOffset.UtcNow.AddMinutes(5)
                .ToString("O");
            pendingPreviousLogin.SetResult(
                "{\"accountId\":\"test-account\"," +
                "\"username\":\"VerifiedPlayer\"," +
                "\"accessToken\":\"cross-gateway-previous-login-token\"," +
                "\"expiresAtUtc\":\"" + expiration + "\"}");
            try
            {
                await previousLogin;
            }
            catch (OperationCanceledException)
            {
                // 旧登录请求被取消是预期行为。
            }

            bool cacheStillOwnedByCurrent = File.Exists(sessionFile) &&
                UnityEngine.JsonUtility.FromJson<SelfHostedCachedSession>(
                    File.ReadAllText(sessionFile))?.accessToken == currentToken;
            if (currentGateway.IsSignedIn && !previousGateway.IsSignedIn &&
                SelfHostedAuthenticationGateway.AccessToken == currentToken &&
                cacheStillOwnedByCurrent)
                return 0;
            Console.Error.WriteLine(
                "FAIL: 旧网关迟到的登录响应覆盖了新网关会话。");
            return 1;
        }
        finally
        {
            SelfHostedHttpClient.AuthenticationHandler = null;
            currentGateway.SignOut(true);
            previousGateway.SignOut(true);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    private static Task<int> VerifyRevokedTokenClearsSession() =>
        WithSignedInSession("revoked", async gateway =>
        {
            string token = SelfHostedAuthenticationGateway.AccessToken;
            SelfHostedHttpClient.MeHandler = _ => Task.FromException<string>(
                new SelfHostedHttpException(401, "unauthorized"));
            bool invalidated = await SelfHostedAuthenticationGateway
                .VerifyCurrentSessionAfterAuthorizationFailureAsync(token,
                    new CoopDedicatedServerSettings());
            if (invalidated && !gateway.IsSignedIn &&
                string.IsNullOrEmpty(SelfHostedAuthenticationGateway.AccessToken))
                return 0;
            Console.Error.WriteLine(
                "FAIL: /me 同样拒绝已撤销令牌后，没有清除当前会话。");
            return 1;
        });

    private static Task<int> VerifyOrdinaryPermissionDenialKeepsSession() =>
        WithSignedInSession("permission", async gateway =>
        {
            string token = SelfHostedAuthenticationGateway.AccessToken;
            SelfHostedHttpClient.MeHandler = _ => Task.FromResult(
                "{\"accountId\":\"test-account\"," +
                "\"username\":\"VerifiedPlayer\"}");
            bool invalidated = await SelfHostedAuthenticationGateway
                .VerifyCurrentSessionAfterAuthorizationFailureAsync(token,
                    new CoopDedicatedServerSettings());
            if (!invalidated && gateway.IsSignedIn &&
                SelfHostedAuthenticationGateway.AccessToken == token)
                return 0;
            Console.Error.WriteLine(
                "FAIL: 普通房间权限 403 被误判为账号令牌撤销。");
            return 1;
        });

    private static Task<int> VerifyProbeNetworkFailureKeepsSession() =>
        WithSignedInSession("network", async gateway =>
        {
            string token = SelfHostedAuthenticationGateway.AccessToken;
            SelfHostedHttpClient.MeHandler = _ => Task.FromException<string>(
                new SelfHostedHttpException(0, "network_unavailable"));
            bool invalidated = await SelfHostedAuthenticationGateway
                .VerifyCurrentSessionAfterAuthorizationFailureAsync(token,
                    new CoopDedicatedServerSettings());
            if (!invalidated && gateway.IsSignedIn &&
                SelfHostedAuthenticationGateway.AccessToken == token)
                return 0;
            Console.Error.WriteLine(
                "FAIL: /me 网络故障导致仍有效账号被注销。");
            return 1;
        });

    private static Task<int> VerifyLateRejectionCannotClearNewLogin() =>
        WithSignedInSession("old", async gateway =>
        {
            string oldToken = SelfHostedAuthenticationGateway.AccessToken;
            var pending = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SelfHostedHttpClient.MeHandler = _ => pending.Task;
            Task<bool> verification = SelfHostedAuthenticationGateway
                .VerifyCurrentSessionAfterAuthorizationFailureAsync(oldToken,
                    new CoopDedicatedServerSettings());
            if (verification.IsCompleted)
            {
                Console.Error.WriteLine(
                    "FAIL: 旧令牌探针没有处于待完成状态。");
                return 1;
            }

            SelfHostedHttpClient.IssuedToken = "new-session-token";
            await gateway.SignInAsync("VerifiedPlayer", "letters123");
            pending.SetException(new SelfHostedHttpException(
                403, "unauthorized"));
            bool invalidated = await verification;
            if (!invalidated && gateway.IsSignedIn &&
                SelfHostedAuthenticationGateway.AccessToken ==
                "new-session-token")
                return 0;
            Console.Error.WriteLine(
                "FAIL: 旧请求迟到的 403 注销了新登录会话。");
            return 1;
        });

    private static async Task<int> WithSignedInSession(string scenario,
        Func<SelfHostedAuthenticationGateway, Task<int>> assertion)
    {
        string sessionDirectory = Path.Combine(Path.GetTempPath(),
            "fps-account-revocation-" + Guid.NewGuid().ToString("N"));
        UnityEngine.Application.persistentDataPath = sessionDirectory;
        SelfHostedHttpClient.IssuedLifetime = TimeSpan.FromMinutes(5);
        SelfHostedHttpClient.IssuedToken = scenario + "-session-token";
        SelfHostedHttpClient.MeHandler = null;
        var gateway = SelfHostedAuthenticationGateway.ForCurrentProcess();
        gateway.SignOut(true);
        try
        {
            await gateway.SignInAsync("VerifiedPlayer", "letters123");
            if (!gateway.IsSignedIn)
            {
                Console.Error.WriteLine(
                    "FAIL: 无法建立本地 " + scenario + " 测试会话。");
                return 1;
            }
            return await assertion(gateway);
        }
        finally
        {
            SelfHostedHttpClient.MeHandler = null;
            gateway.SignOut(true);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    private static async Task<int> VerifyStaleGatewayCannotSignOutNewOwner()
    {
        string sessionDirectory = Path.Combine(Path.GetTempPath(),
            "fps-account-owner-" + Guid.NewGuid().ToString("N"));
        UnityEngine.Application.persistentDataPath = sessionDirectory;
        SelfHostedHttpClient.IssuedLifetime = TimeSpan.FromMinutes(5);
        SelfHostedHttpClient.MeHandler = null;
        var previousGateway = new SelfHostedAuthenticationGateway();
        SelfHostedAuthenticationGateway currentGateway =
            SelfHostedAuthenticationGateway.ForCurrentProcess();
        const string currentToken = "owner-b-session-token";
        string sessionFile = Path.Combine(sessionDirectory,
            "fps-account-session.json");
        try
        {
            SelfHostedHttpClient.IssuedToken = "owner-a-session-token";
            await previousGateway.SignUpAsync("VerifiedPlayer", "letters123");
            SelfHostedHttpClient.IssuedToken = currentToken;
            await currentGateway.SignInAsync("VerifiedPlayer", "letters123");
            if (!currentGateway.IsSignedIn ||
                SelfHostedAuthenticationGateway.AccessToken != currentToken ||
                !File.Exists(sessionFile))
            {
                Console.Error.WriteLine(
                    "FAIL: 无法建立旧网关 A 与现网关 B 的测试会话。");
                return 1;
            }

            previousGateway.SignOut(true);
            bool cacheStillOwnedByCurrent = File.Exists(sessionFile) &&
                UnityEngine.JsonUtility.FromJson<SelfHostedCachedSession>(
                    File.ReadAllText(sessionFile))?.accessToken == currentToken;
            if (currentGateway.IsSignedIn &&
                SelfHostedAuthenticationGateway.AccessToken == currentToken &&
                cacheStillOwnedByCurrent)
                return 0;
            Console.Error.WriteLine(
                "FAIL: 旧网关注销清掉了新网关的登录态或磁盘缓存。");
            return 1;
        }
        finally
        {
            currentGateway.SignOut(true);
            previousGateway.SignOut(true);
            if (Directory.Exists(sessionDirectory))
                Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    private sealed class VerifiedGateway : ICoopAuthenticationGateway
    {
        private bool signedIn = true;
        public int InitializeCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public bool IsSignedIn => signedIn;
        public bool HasCachedSession => true;
        public string PlayerId => signedIn ? "verified-player" : string.Empty;
        public string Username => signedIn ? "VerifiedPlayer" : string.Empty;

        public Task InitializeAsync()
        {
            InitializeCalls++;
            return Task.CompletedTask;
        }

        public Task SignUpAsync(string username, string password) =>
            Task.CompletedTask;
        public Task SignInAsync(string username, string password) =>
            Task.CompletedTask;
        public Task RestoreCachedSessionAsync()
        {
            RestoreCalls++;
            return Task.CompletedTask;
        }

        public Task SignInAnonymouslyForDevelopmentAsync() =>
            Task.CompletedTask;
        public void SignOut(bool clearCredentials) => signedIn = false;
    }
}
