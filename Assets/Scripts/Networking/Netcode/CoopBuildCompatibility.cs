using System;
using System.Text;

namespace FPS.Networking.Netcode
{
    public enum CoopCompatibilityFailure
    {
        None = 0,
        InvalidServerMetadata = 1,
        ApplicationVersionMismatch = 2,
        ProtocolVersionMismatch = 3,
        ContentVersionMismatch = 4
    }

    public readonly struct CoopCompatibilityDecision
    {
        private CoopCompatibilityDecision(bool compatible,
            CoopCompatibilityFailure failure, string message)
        {
            Compatible = compatible;
            Failure = failure;
            Message = message ?? string.Empty;
        }

        public bool Compatible { get; }
        public CoopCompatibilityFailure Failure { get; }
        public string Message { get; }

        public static CoopCompatibilityDecision Allow() =>
            new(true, CoopCompatibilityFailure.None, string.Empty);

        public static CoopCompatibilityDecision Reject(
            CoopCompatibilityFailure failure, string message) =>
            new(false, failure, message);
    }

    /// <summary>
    /// Immutable build identity shared by allocation responses, connection
    /// tickets and the dedicated server. It intentionally excludes secrets.
    /// </summary>
    public readonly struct CoopBuildCompatibility : IEquatable<
        CoopBuildCompatibility>
    {
        private const string TicketPrefix = "fps2";

        public CoopBuildCompatibility(string applicationVersion,
            string protocolVersion, string contentVersion)
        {
            ApplicationVersion = Normalize(applicationVersion,
                nameof(applicationVersion));
            ProtocolVersion = Normalize(protocolVersion,
                nameof(protocolVersion));
            ContentVersion = Normalize(contentVersion,
                nameof(contentVersion));
        }

        public string ApplicationVersion { get; }
        public string ProtocolVersion { get; }
        public string ContentVersion { get; }

        public string ToTicketValue() => string.Join("|", new[]
        {
            TicketPrefix,
            Encode(ApplicationVersion),
            Encode(ProtocolVersion),
            Encode(ContentVersion)
        });

        public static bool TryParseTicketValue(string value,
            out CoopBuildCompatibility compatibility)
        {
            compatibility = default;
            string[] fields = value?.Split('|') ?? Array.Empty<string>();
            if (fields.Length != 4 || fields[0] != TicketPrefix ||
                !TryDecode(fields[1], out string application) ||
                !TryDecode(fields[2], out string protocol) ||
                !TryDecode(fields[3], out string content)) return false;
            try
            {
                compatibility = new CoopBuildCompatibility(application,
                    protocol, content);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static CoopCompatibilityDecision Compare(
            CoopBuildCompatibility client,
            CoopBuildCompatibility server)
        {
            if (!string.Equals(client.ApplicationVersion,
                    server.ApplicationVersion, StringComparison.Ordinal))
                return CoopCompatibilityDecision.Reject(
                    CoopCompatibilityFailure.ApplicationVersionMismatch,
                    $"客户端版本 {client.ApplicationVersion} 与服务器版本 " +
                    $"{server.ApplicationVersion} 不一致，请更新后重试。");
            if (!string.Equals(client.ProtocolVersion,
                    server.ProtocolVersion, StringComparison.Ordinal))
                return CoopCompatibilityDecision.Reject(
                    CoopCompatibilityFailure.ProtocolVersionMismatch,
                    $"网络协议版本不兼容：客户端 {client.ProtocolVersion}，" +
                    $"服务器 {server.ProtocolVersion}。");
            if (!string.Equals(client.ContentVersion,
                    server.ContentVersion, StringComparison.Ordinal))
                return CoopCompatibilityDecision.Reject(
                    CoopCompatibilityFailure.ContentVersionMismatch,
                    $"游戏内容版本不兼容：客户端 {client.ContentVersion}，" +
                    $"服务器 {server.ContentVersion}。");
            return CoopCompatibilityDecision.Allow();
        }

        public bool Equals(CoopBuildCompatibility other) =>
            string.Equals(ApplicationVersion, other.ApplicationVersion,
                StringComparison.Ordinal) &&
            string.Equals(ProtocolVersion, other.ProtocolVersion,
                StringComparison.Ordinal) &&
            string.Equals(ContentVersion, other.ContentVersion,
                StringComparison.Ordinal);

        public override bool Equals(object value) =>
            value is CoopBuildCompatibility other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            ApplicationVersion, ProtocolVersion, ContentVersion);

        private static string Normalize(string value, string parameter)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length < 1 || normalized.Length > 64 ||
                normalized.IndexOf('\n') >= 0 ||
                normalized.IndexOf('\r') >= 0 ||
                normalized.IndexOf('|') >= 0)
                throw new ArgumentException(
                    "版本标识必须为 1—64 个安全字符。", parameter);
            return normalized;
        }

        private static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static bool TryDecode(string value, out string result)
        {
            result = string.Empty;
            if (string.IsNullOrEmpty(value)) return false;
            string normalized = value.Replace('-', '+').Replace('_', '/');
            int remainder = normalized.Length % 4;
            if (remainder == 1) return false;
            if (remainder > 0)
                normalized = normalized.PadRight(
                    normalized.Length + 4 - remainder, '=');
            try
            {
                result = Encoding.UTF8.GetString(
                    Convert.FromBase64String(normalized));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    [Serializable]
    public sealed class RemoteMatchConnectionInfo
    {
        public string schemaVersion = "fps-remote-match-v1";
        public string host = string.Empty;
        public ushort port = NetworkEndpointSettings.DefaultPort;
        public string matchId = string.Empty;
        public string applicationVersion = string.Empty;
        public string protocolVersion = string.Empty;
        public string contentVersion = string.Empty;
        public int maximumPlayers;
        public string expiresAtUtc = string.Empty;

        public bool TryGetCompatibility(
            out CoopBuildCompatibility compatibility,
            out string error)
        {
            try
            {
                compatibility = new CoopBuildCompatibility(
                    applicationVersion, protocolVersion, contentVersion);
                error = string.Empty;
                return true;
            }
            catch (ArgumentException)
            {
                compatibility = default;
                error = "服务器返回的版本信息无效，请重新申请战局。";
                return false;
            }
        }
    }

    public static class RemoteMatchCompatibilityValidator
    {
        public static CoopCompatibilityDecision Validate(
            CoopBuildCompatibility local, RemoteMatchConnectionInfo remote)
        {
            string error = string.Empty;
            CoopBuildCompatibility server = default;
            if (remote == null || string.IsNullOrWhiteSpace(remote.host) ||
                remote.port == 0 || string.IsNullOrWhiteSpace(remote.matchId) ||
                !remote.TryGetCompatibility(out server, out error))
                return CoopCompatibilityDecision.Reject(
                    CoopCompatibilityFailure.InvalidServerMetadata,
                    string.IsNullOrWhiteSpace(error)
                        ? "服务器连接信息不完整，请重新申请战局。"
                        : error);
            return CoopBuildCompatibility.Compare(local, server);
        }
    }
}
