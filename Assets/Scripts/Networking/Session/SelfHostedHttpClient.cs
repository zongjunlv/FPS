using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FPS.Networking.Session
{
    public sealed class SelfHostedHttpException : Exception
    {
        public long StatusCode { get; }
        public string ErrorCode { get; }
        public string SafeMessage { get; }

        public SelfHostedHttpException(long statusCode, string errorCode,
            string safeMessage) : base(safeMessage)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode ?? string.Empty;
            SafeMessage = safeMessage ?? string.Empty;
        }
    }

    [Serializable]
    internal sealed class SelfHostedErrorResponse
    {
        public string error;
        public string code;
    }

    // 账号、房间和战局分配共享同一可迁移 HTTPS 入口。无 pin 时使用平台默认
    // TLS 证书校验；指定 pin 时仅信任与 SHA-256 指纹完全一致的证书。
    public static class SelfHostedHttpClient
    {
        public static Task<string> SendAsync(string method, string path,
            string json = null, string bearerToken = null)
        {
            return SendAsync(method, path, json, bearerToken, null);
        }

        public static async Task<string> SendAsync(string method, string path,
            string json, string bearerToken,
            CoopDedicatedServerSettings configuredSettings)
        {
            if (string.IsNullOrWhiteSpace(method) ||
                string.IsNullOrWhiteSpace(path) || !path.StartsWith("/",
                    StringComparison.Ordinal) || path.StartsWith("//",
                    StringComparison.Ordinal))
                throw new ArgumentException("无效的服务端请求路径。");

            CoopDedicatedServerSettings settings = configuredSettings;
            if (settings == null && !CoopDedicatedServerSettings.TryLoad(
                    out settings, out string error))
                throw new SelfHostedHttpException(0, "client_config", error);
            if (!Uri.TryCreate(settings.brokerUrl, UriKind.Absolute,
                    out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new SelfHostedHttpException(0, "client_config",
                    "账号与房间服务必须使用 HTTPS 地址。");
            string fingerprint = CoopDedicatedServerSettings.NormalizeFingerprint(
                settings.pinnedCertificateSha256);
            if (!string.IsNullOrWhiteSpace(
                    settings.pinnedCertificateSha256) &&
                fingerprint.Length != 64)
                throw new SelfHostedHttpException(0, "client_config",
                    "服务器证书指纹无效。");

            using var request = new UnityWebRequest(
                settings.brokerUrl + path, method.ToUpperInvariant())
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = settings.requestTimeoutSeconds,
                disposeCertificateHandlerOnDispose = true
            };
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(
                    Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrWhiteSpace(bearerToken))
                request.SetRequestHeader("Authorization", "Bearer " +
                    bearerToken);
            if (!string.IsNullOrWhiteSpace(fingerprint))
                request.certificateHandler = new PinnedCertificateHandler(
                    fingerprint);

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            if (request.result == UnityWebRequest.Result.Success)
                return request.downloadHandler.text;

            SelfHostedErrorResponse body = null;
            try
            {
                body = JsonUtility.FromJson<SelfHostedErrorResponse>(
                    request.downloadHandler.text);
            }
            catch (ArgumentException)
            {
                // 非 JSON 错误页不应把代理或 TLS 内部信息展示给玩家。
            }
            throw new SelfHostedHttpException(request.responseCode,
                body?.code ?? body?.error ?? string.Empty,
                string.IsNullOrWhiteSpace(body?.error)
                    ? "服务器暂时不可用，请稍后重试。"
                    : body.error);
        }

        private sealed class PinnedCertificateHandler : CertificateHandler
        {
            private readonly string expected;

            public PinnedCertificateHandler(string fingerprint)
            {
                expected = CoopDedicatedServerSettings.NormalizeFingerprint(
                    fingerprint);
            }

            protected override bool ValidateCertificate(byte[] certificateData)
            {
                if (certificateData == null || certificateData.Length == 0)
                    return false;
                using SHA256 sha = SHA256.Create();
                string actual = BitConverter.ToString(
                        sha.ComputeHash(certificateData))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
                return string.Equals(actual, expected,
                    StringComparison.Ordinal);
            }
        }
    }
}
