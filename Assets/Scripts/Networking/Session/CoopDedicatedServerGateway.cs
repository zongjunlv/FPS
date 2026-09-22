using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.Networking;

namespace FPS.Networking.Session
{
    [Serializable]
    public sealed class CoopDedicatedServerSettings
    {
        public string brokerUrl = string.Empty;
        public string pinnedCertificateSha256 = string.Empty;
        public string applicationVersion = "0.1.0";
        public string protocolVersion = "1";
        public string contentVersion = "citynew-v1";
        public int requestTimeoutSeconds = 100;

        public CoopBuildCompatibility Compatibility =>
            new(applicationVersion, protocolVersion, contentVersion);

        public static bool TryLoad(out CoopDedicatedServerSettings settings,
            out string error)
        {
            TextAsset asset = Resources.Load<TextAsset>(
                "Networking/CoopDedicatedServerSettings");
            if (asset == null)
            {
                settings = null;
                error = "缺少专用服务器连接配置。";
                return false;
            }
            try
            {
                settings = JsonUtility.FromJson<CoopDedicatedServerSettings>(
                    asset.text);
                if (settings == null ||
                    !Uri.TryCreate(settings.brokerUrl, UriKind.Absolute,
                        out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    NormalizeFingerprint(settings.pinnedCertificateSha256)
                        .Length != 64)
                {
                    settings = null;
                    error = "专用服务器配置缺少 HTTPS 地址或证书指纹。";
                    return false;
                }
                settings.brokerUrl = settings.brokerUrl.TrimEnd('/');
                settings.pinnedCertificateSha256 = NormalizeFingerprint(
                    settings.pinnedCertificateSha256);
                settings.requestTimeoutSeconds = Mathf.Clamp(
                    settings.requestTimeoutSeconds, 5, 120);
                _ = settings.Compatibility;
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException)
            {
                settings = null;
                error = "专用服务器配置无效：" + exception.Message;
                return false;
            }
        }

        public static string NormalizeFingerprint(string value)
        {
            var builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (Uri.IsHexDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }
            return builder.ToString();
        }
    }

    [Serializable]
    public sealed class CoopBrokerPlayer
    {
        public string accountId;
        public string appearanceId;
    }

    [Serializable]
    internal sealed class CoopBrokerAllocateRequest
    {
        public string sessionId;
        public int seed;
        public string applicationVersion;
        public string protocolVersion;
        public string contentVersion;
        public CoopBrokerPlayer[] players;
    }

    [Serializable]
    internal sealed class CoopBrokerJoinRequest
    {
        public string sessionId;
        public string matchId;
        public string applicationVersion;
        public string protocolVersion;
        public string contentVersion;
    }

    [Serializable]
    public sealed class CoopDedicatedServerAllocation
    {
        public RemoteMatchConnectionInfo connection;
        public string connectionTicket;
    }

    [Serializable]
    internal sealed class CoopBrokerError
    {
        public string error;
    }

    public sealed class CoopDedicatedServerGateway
    {
        private readonly CoopDedicatedServerSettings settings;

        public CoopDedicatedServerGateway(
            CoopDedicatedServerSettings configuredSettings)
        {
            settings = configuredSettings ?? throw new ArgumentNullException(
                nameof(configuredSettings));
        }

        public CoopBuildCompatibility Compatibility => settings.Compatibility;

        public async Task<CoopDedicatedServerAllocation> AllocateAsync(
            string sessionId, int seed,
            IReadOnlyList<CoopLobbyMemberSnapshot> members)
        {
            if (members == null || members.Count < 1 || members.Count > 2)
                throw new InvalidOperationException(
                    "专用服务器战局需要 1—2 名房间成员。 ");
            var players = new CoopBrokerPlayer[members.Count];
            for (int index = 0; index < members.Count; index++)
            {
                players[index] = new CoopBrokerPlayer
                {
                    accountId = members[index].AccountPlayerId,
                    appearanceId = members[index].AppearanceId
                };
            }
            var request = new CoopBrokerAllocateRequest
            {
                sessionId = sessionId,
                seed = seed,
                applicationVersion = settings.applicationVersion,
                protocolVersion = settings.protocolVersion,
                contentVersion = settings.contentVersion,
                players = players
            };
            return await PostAsync("/v1/matches/allocate", request);
        }

        public async Task<CoopDedicatedServerAllocation> JoinAsync(
            string sessionId, RemoteMatchConnectionInfo connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            var request = new CoopBrokerJoinRequest
            {
                sessionId = sessionId,
                matchId = connection.matchId,
                applicationVersion = settings.applicationVersion,
                protocolVersion = settings.protocolVersion,
                contentVersion = settings.contentVersion
            };
            return await PostAsync("/v1/matches/join", request);
        }

        public async Task ReleaseAsync(string sessionId,
            RemoteMatchConnectionInfo connection)
        {
            if (connection == null) return;
            var request = new CoopBrokerJoinRequest
            {
                sessionId = sessionId,
                matchId = connection.matchId,
                applicationVersion = settings.applicationVersion,
                protocolVersion = settings.protocolVersion,
                contentVersion = settings.contentVersion
            };
            await SendAsync("/v1/matches/release",
                JsonUtility.ToJson(request));
        }

        private async Task<CoopDedicatedServerAllocation> PostAsync<T>(
            string path, T payload)
        {
            string responseText = await SendAsync(path,
                JsonUtility.ToJson(payload));
            CoopDedicatedServerAllocation allocation =
                JsonUtility.FromJson<CoopDedicatedServerAllocation>(
                    responseText);
            if (allocation?.connection == null ||
                string.IsNullOrWhiteSpace(allocation.connectionTicket))
                throw new InvalidOperationException(
                    "专用服务器返回了不完整的连接信息。 ");
            CoopCompatibilityDecision decision =
                RemoteMatchCompatibilityValidator.Validate(
                    Compatibility, allocation.connection);
            if (!decision.Compatible)
                throw new InvalidOperationException(decision.Message);
            return allocation;
        }

        private async Task<string> SendAsync(string path, string json)
        {
            if (!AuthenticationService.Instance.IsAuthorized ||
                string.IsNullOrWhiteSpace(
                    AuthenticationService.Instance.AccessToken))
                throw new InvalidOperationException(
                    "账号登录已过期，请重新登录后再进入联机战斗。 ");
            byte[] body = Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(
                settings.brokerUrl + path, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                certificateHandler = new PinnedCertificateHandler(
                    settings.pinnedCertificateSha256),
                timeout = settings.requestTimeoutSeconds,
                disposeCertificateHandlerOnDispose = true
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " +
                AuthenticationService.Instance.AccessToken);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            if (request.result != UnityWebRequest.Result.Success)
            {
                CoopBrokerError response = null;
                try
                {
                    response = JsonUtility.FromJson<CoopBrokerError>(
                        request.downloadHandler.text);
                }
                catch (ArgumentException)
                {
                    // Fall through to the transport message.
                }
                string message = response?.error;
                if (string.IsNullOrWhiteSpace(message))
                    message = string.IsNullOrWhiteSpace(request.error)
                        ? "专用服务器分配失败。"
                        : request.error;
                throw new InvalidOperationException(message);
            }
            return request.downloadHandler.text;
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
