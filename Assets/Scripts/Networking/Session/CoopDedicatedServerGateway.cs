using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using UnityEngine;
using System.IO;

namespace FPS.Networking.Session
{
    [Serializable]
    public sealed class CoopDedicatedServerSettings
    {
        // 打包默认值位于 Resources；迁移服务器时可在 persistentDataPath 放置
        // CoopDedicatedServerSettings.json 覆盖 brokerUrl / 证书指纹，无需重新打包。
        // 示例：{"brokerUrl":"https://new.example.com",
        //        "pinnedCertificateSha256":""}
        // 空指纹表示使用系统 CA 校验；若配置 64 位十六进制证书指纹，换证时
        // 必须同步更新。覆盖文件可以只写需更改的字段，其余继承打包默认值。
        // 账号与房间数据仍必须由服务端数据库备份、迁移；本文件不承载数据。
        public const string OverrideFileName = "CoopDedicatedServerSettings.json";
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
                string overridePath = Path.Combine(
                    Application.persistentDataPath, OverrideFileName);
                string overrideJson = File.Exists(overridePath)
                    ? File.ReadAllText(overridePath)
                    : null;
                return TryParse(asset.text, overrideJson, out settings,
                    out error);
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException or IOException or
                    UnauthorizedAccessException)
            {
                settings = null;
                error = "专用服务器配置无法读取：" + exception.Message;
                return false;
            }
        }

        public static bool TryParse(string bundledJson, string overrideJson,
            out CoopDedicatedServerSettings settings, out string error)
        {
            try
            {
                if (overrideJson != null &&
                    string.IsNullOrWhiteSpace(overrideJson))
                {
                    settings = null;
                    error = "外部服务器配置为空，请修正或移除覆盖文件。";
                    return false;
                }
                settings = JsonUtility.FromJson<CoopDedicatedServerSettings>(
                    bundledJson);
                if (settings == null)
                    throw new FormatException("打包配置为空。");
                if (overrideJson != null)
                    JsonUtility.FromJsonOverwrite(overrideJson, settings);
                if (!Uri.TryCreate(settings.brokerUrl, UriKind.Absolute,
                        out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment))
                {
                    settings = null;
                    error = "账号与房间服务必须使用有效 HTTPS 地址。";
                    return false;
                }

                string fingerprint = NormalizeFingerprint(
                    settings.pinnedCertificateSha256);
                if (!string.IsNullOrWhiteSpace(
                        settings.pinnedCertificateSha256) &&
                    fingerprint.Length != 64)
                {
                    settings = null;
                    error = "证书指纹必须是完整的 SHA-256 值。";
                    return false;
                }
                settings.brokerUrl = settings.brokerUrl.TrimEnd('/');
                settings.pinnedCertificateSha256 = fingerprint;
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
            string token = SelfHostedAuthenticationGateway.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "账号登录已过期，请重新登录后再进入联机战斗。 ");
            try
            {
                return await SelfHostedHttpClient.SendAsync("POST", path, json,
                    token, settings);
            }
            catch (SelfHostedHttpException exception)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(exception.SafeMessage)
                        ? "专用服务器分配失败，请稍后重试。"
                        : exception.SafeMessage);
            }
        }
    }
}
