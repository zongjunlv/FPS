using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace UnityEngine
{
    internal enum RuntimeInitializeLoadType
    {
        SubsystemRegistration
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(
            RuntimeInitializeLoadType loadType) { }
    }

    internal static class Application
    {
        public static string persistentDataPath { get; set; }
    }

    internal static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            IncludeFields = true
        };

        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options);

        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, Options);
    }
}

namespace FPS.Networking.Session
{
    public sealed class CoopDedicatedServerSettings { }

    public sealed class SelfHostedHttpException : Exception
    {
        public long StatusCode { get; }
        public string ErrorCode { get; }

        public SelfHostedHttpException(long statusCode, string errorCode)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }

    internal static class SelfHostedHttpClient
    {
        public static int MeCalls { get; private set; }
        public static TimeSpan IssuedLifetime { get; set; } =
            TimeSpan.FromMinutes(5);
        public static string IssuedToken { get; set; } = "test-token";
        public static TaskCompletionSource<string> PendingMe { get; set; }
        public static Func<string, Task<string>> MeHandler { get; set; }
        public static Func<string, Task<string>> AuthenticationHandler { get; set; }

        public static Task<string> SendAsync(string method, string path,
            string json = null, string bearerToken = null)
        {
            switch (path)
            {
                case "/v1/auth/register":
                case "/v1/auth/login":
                    if (AuthenticationHandler != null)
                        return AuthenticationHandler(path);
                    return Task.FromResult(AccountResponse());
                case "/v1/auth/me":
                    MeCalls++;
                    if (MeHandler != null) return MeHandler(bearerToken);
                    if (PendingMe != null) return PendingMe.Task;
                    return Task.FromResult(
                        "{\"accountId\":\"test-account\"," +
                        "\"username\":\"VerifiedPlayer\"}");
                case "/v1/auth/logout":
                    return Task.FromResult("{}");
                default:
                    throw new InvalidOperationException(
                        "测试不允许调用未知或公网服务接口：" + path);
            }
        }

        public static Task<string> SendAsync(string method, string path,
            string json, string bearerToken,
            CoopDedicatedServerSettings settings) =>
            SendAsync(method, path, json, bearerToken);

        private static string AccountResponse()
        {
            string expiration = DateTimeOffset.UtcNow.Add(IssuedLifetime)
                .ToString("O");
            return "{\"accountId\":\"test-account\"," +
                   "\"username\":\"VerifiedPlayer\"," +
                   "\"accessToken\":\"" + IssuedToken + "\"," +
                   "\"expiresAtUtc\":\"" + expiration + "\"}";
        }
    }
}
