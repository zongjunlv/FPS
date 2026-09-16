using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FPS.Networking.Netcode
{
    public enum CoopAdmissionFailure
    {
        None = 0,
        LoginRequired = 1,
        InvalidCredential = 2,
        CredentialExpired = 3,
        CredentialReplayed = 4,
        DuplicateAccount = 5,
        ServerFull = 6,
        VersionMismatch = 7
    }

    public readonly struct CoopConnectionClaims
    {
        public CoopConnectionClaims(string accountPlayerId, string version,
            string nonce, long issuedAtUnixSeconds, long expiresAtUnixSeconds)
        {
            AccountPlayerId = accountPlayerId;
            Version = version;
            Nonce = nonce;
            IssuedAtUnixSeconds = issuedAtUnixSeconds;
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        }

        public string AccountPlayerId { get; }
        public string Version { get; }
        public string Nonce { get; }
        public long IssuedAtUnixSeconds { get; }
        public long ExpiresAtUnixSeconds { get; }
    }

    public readonly struct CoopApprovedIdentity
    {
        public CoopApprovedIdentity(ulong clientId, string accountPlayerId,
            int simulationPlayerId)
        {
            ClientId = clientId;
            AccountPlayerId = accountPlayerId;
            SimulationPlayerId = simulationPlayerId;
        }

        public ulong ClientId { get; }
        public string AccountPlayerId { get; }
        public int SimulationPlayerId { get; }
    }

    public readonly struct CoopAdmissionDecision
    {
        private CoopAdmissionDecision(bool approved,
            CoopAdmissionFailure failure, string reason,
            CoopApprovedIdentity identity)
        {
            Approved = approved;
            Failure = failure;
            Reason = reason;
            Identity = identity;
        }

        public bool Approved { get; }
        public CoopAdmissionFailure Failure { get; }
        public string Reason { get; }
        public CoopApprovedIdentity Identity { get; }

        public static CoopAdmissionDecision Allow(CoopApprovedIdentity identity) =>
            new(true, CoopAdmissionFailure.None, string.Empty, identity);

        public static CoopAdmissionDecision Reject(CoopAdmissionFailure failure) =>
            new(false, failure, CoopAdmissionMessages.Describe(failure), default);
    }

    public static class CoopAdmissionMessages
    {
        public static string Describe(CoopAdmissionFailure failure)
        {
            return failure switch
            {
                CoopAdmissionFailure.LoginRequired =>
                    "请先登录账号后再加入服务器。",
                CoopAdmissionFailure.InvalidCredential =>
                    "登录凭证无效，请重新登录后重试。",
                CoopAdmissionFailure.CredentialExpired =>
                    "登录凭证已过期，请重新登录后重试。",
                CoopAdmissionFailure.CredentialReplayed =>
                    "该登录凭证已使用，请重新获取后重试。",
                CoopAdmissionFailure.DuplicateAccount =>
                    "该账号已在当前战局中登录。",
                CoopAdmissionFailure.ServerFull =>
                    "服务器人数已满，请稍后重试。",
                CoopAdmissionFailure.VersionMismatch =>
                    "客户端版本与服务器不一致，请更新后重试。",
                _ => "连接审批失败，请稍后重试。"
            };
        }
    }

    /// <summary>
    /// HMAC signed, short-lived connection ticket. In production the issuer
    /// belongs in a trusted backend. The game client only transports the ticket.
    /// </summary>
    public sealed class CoopConnectionTicketCodec
    {
        public const int MaximumCredentialBytes = 2048;
        public const int MaximumLifetimeSeconds = 15 * 60;
        private const string Prefix = "fps1";
        private readonly byte[] signingKey;

        public CoopConnectionTicketCodec(byte[] key)
        {
            if (key == null || key.Length < 32)
                throw new ArgumentException(
                    "Connection ticket signing keys must contain at least 32 bytes.",
                    nameof(key));
            signingKey = (byte[])key.Clone();
        }

        public string Issue(string accountPlayerId, string version,
            long issuedAtUnixSeconds, int lifetimeSeconds = 120,
            string nonce = null)
        {
            string account = NormalizeField(accountPlayerId,
                nameof(accountPlayerId), 128);
            string normalizedVersion = NormalizeField(version,
                nameof(version), 64);
            if (lifetimeSeconds < 1 ||
                lifetimeSeconds > MaximumLifetimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
            string normalizedNonce = string.IsNullOrWhiteSpace(nonce)
                ? Guid.NewGuid().ToString("N")
                : NormalizeField(nonce, nameof(nonce), 64);
            long expiresAt = checked(issuedAtUnixSeconds + lifetimeSeconds);
            string payload = string.Join("\n", new[]
            {
                EncodeText(account),
                EncodeText(normalizedVersion),
                normalizedNonce,
                issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
                expiresAt.ToString(CultureInfo.InvariantCulture)
            });
            string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            string signedValue = Prefix + "." + encodedPayload;
            byte[] signature = Sign(Encoding.UTF8.GetBytes(signedValue));
            return signedValue + "." + Base64UrlEncode(signature);
        }

        public bool TryValidate(string credential, long nowUnixSeconds,
            out CoopConnectionClaims claims, out CoopAdmissionFailure failure)
        {
            claims = default;
            failure = CoopAdmissionFailure.InvalidCredential;
            if (string.IsNullOrWhiteSpace(credential) ||
                Encoding.UTF8.GetByteCount(credential) > MaximumCredentialBytes)
                return false;

            string[] segments = credential.Split('.');
            if (segments.Length != 3 || segments[0] != Prefix ||
                !TryBase64UrlDecode(segments[1], out byte[] payloadBytes) ||
                !TryBase64UrlDecode(segments[2], out byte[] signature))
                return false;

            byte[] signedBytes = Encoding.UTF8.GetBytes(
                segments[0] + "." + segments[1]);
            byte[] expected = Sign(signedBytes);
            if (!FixedTimeEquals(signature, expected)) return false;

            string[] values = Encoding.UTF8.GetString(payloadBytes).Split('\n');
            if (values.Length != 5 ||
                !TryDecodeText(values[0], out string account) ||
                !TryDecodeText(values[1], out string version) ||
                !IsSafeField(account, 128) || !IsSafeField(version, 64) ||
                !IsSafeField(values[2], 64) ||
                !long.TryParse(values[3], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long issuedAt) ||
                !long.TryParse(values[4], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long expiresAt) ||
                expiresAt <= issuedAt ||
                expiresAt - issuedAt > MaximumLifetimeSeconds ||
                issuedAt > nowUnixSeconds + 30)
                return false;

            if (expiresAt <= nowUnixSeconds)
            {
                failure = CoopAdmissionFailure.CredentialExpired;
                return false;
            }

            claims = new CoopConnectionClaims(account, version, values[2],
                issuedAt, expiresAt);
            failure = CoopAdmissionFailure.None;
            return true;
        }

        private byte[] Sign(byte[] value)
        {
            using var hmac = new HMACSHA256(signingKey);
            return hmac.ComputeHash(value);
        }

        private static string NormalizeField(string value, string parameter,
            int maximumLength)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!IsSafeField(normalized, maximumLength))
                throw new ArgumentException("Identity field is invalid.", parameter);
            return normalized;
        }

        private static bool IsSafeField(string value, int maximumLength) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
            value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0;

        private static string EncodeText(string value) =>
            Base64UrlEncode(Encoding.UTF8.GetBytes(value));

        private static bool TryDecodeText(string value, out string result)
        {
            if (!TryBase64UrlDecode(value, out byte[] bytes))
            {
                result = string.Empty;
                return false;
            }
            result = Encoding.UTF8.GetString(bytes);
            return true;
        }

        private static string Base64UrlEncode(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=')
                .Replace('+', '-').Replace('/', '_');

        private static bool TryBase64UrlDecode(string value, out byte[] result)
        {
            result = Array.Empty<byte>();
            if (string.IsNullOrEmpty(value)) return false;
            string normalized = value.Replace('-', '+').Replace('_', '/');
            int remainder = normalized.Length % 4;
            if (remainder == 1) return false;
            if (remainder > 0) normalized = normalized.PadRight(
                normalized.Length + 4 - remainder, '=');
            try
            {
                result = Convert.FromBase64String(normalized);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }

    /// <summary>
    /// Pure server-side admission registry. Account identity is bound exactly
    /// once to a network client and an authoritative simulation player slot.
    /// </summary>
    public sealed class CoopConnectionAdmissionService
    {
        private readonly CoopConnectionTicketCodec codec;
        private readonly string expectedVersion;
        private readonly int maximumPlayers;
        private readonly Func<long> utcNowSeconds;
        private readonly Dictionary<ulong, CoopApprovedIdentity> byClient = new();
        private readonly Dictionary<string, ulong> clientByAccount = new(
            StringComparer.Ordinal);
        private readonly Dictionary<string, long> usedNonces = new(
            StringComparer.Ordinal);

        public CoopConnectionAdmissionService(CoopConnectionTicketCodec ticketCodec,
            string serverVersion, int playerLimit, Func<long> clock = null)
        {
            codec = ticketCodec ?? throw new ArgumentNullException(
                nameof(ticketCodec));
            expectedVersion = string.IsNullOrWhiteSpace(serverVersion)
                ? throw new ArgumentException("Server version is required.",
                    nameof(serverVersion))
                : serverVersion.Trim();
            if (playerLimit < 1 || playerLimit > 16)
                throw new ArgumentOutOfRangeException(nameof(playerLimit));
            maximumPlayers = playerLimit;
            utcNowSeconds = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        public int ApprovedCount => byClient.Count;

        public CoopAdmissionDecision Approve(ulong clientId, byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.LoginRequired);
            if (payload.Length > CoopConnectionTicketCodec.MaximumCredentialBytes)
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.InvalidCredential);

            long now = utcNowSeconds();
            PruneExpiredNonces(now);
            string credential;
            try
            {
                credential = new UTF8Encoding(false, true).GetString(payload);
            }
            catch (DecoderFallbackException)
            {
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.InvalidCredential);
            }

            if (!codec.TryValidate(credential, now,
                    out CoopConnectionClaims claims,
                    out CoopAdmissionFailure validationFailure))
                return CoopAdmissionDecision.Reject(validationFailure);
            if (!string.Equals(claims.Version, expectedVersion,
                    StringComparison.Ordinal))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.VersionMismatch);
            if (usedNonces.ContainsKey(claims.Nonce))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.CredentialReplayed);
            if (clientByAccount.ContainsKey(claims.AccountPlayerId))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.DuplicateAccount);
            if (byClient.Count >= maximumPlayers)
                return CoopAdmissionDecision.Reject(CoopAdmissionFailure.ServerFull);

            int playerId = ResolveAvailablePlayerId();
            if (playerId <= 0)
                return CoopAdmissionDecision.Reject(CoopAdmissionFailure.ServerFull);
            var identity = new CoopApprovedIdentity(clientId,
                claims.AccountPlayerId, playerId);
            byClient.Add(clientId, identity);
            clientByAccount.Add(claims.AccountPlayerId, clientId);
            usedNonces.Add(claims.Nonce, claims.ExpiresAtUnixSeconds);
            return CoopAdmissionDecision.Allow(identity);
        }

        public bool TryGetIdentity(ulong clientId,
            out CoopApprovedIdentity identity) =>
            byClient.TryGetValue(clientId, out identity);

        public void Release(ulong clientId)
        {
            if (!byClient.Remove(clientId, out CoopApprovedIdentity identity))
                return;
            clientByAccount.Remove(identity.AccountPlayerId);
        }

        private int ResolveAvailablePlayerId()
        {
            for (int candidate = 1; candidate <= maximumPlayers; candidate++)
            {
                bool used = false;
                foreach (CoopApprovedIdentity identity in byClient.Values)
                {
                    if (identity.SimulationPlayerId != candidate) continue;
                    used = true;
                    break;
                }
                if (!used) return candidate;
            }
            return -1;
        }

        private void PruneExpiredNonces(long now)
        {
            if (usedNonces.Count == 0) return;
            var expired = new List<string>();
            foreach (KeyValuePair<string, long> pair in usedNonces)
            {
                if (pair.Value <= now) expired.Add(pair.Key);
            }
            for (int index = 0; index < expired.Count; index++)
                usedNonces.Remove(expired[index]);
        }
    }

    public static class CoopAdmissionEnvironment
    {
        public const string SigningSecretVariable = "FPS_SERVER_AUTH_SECRET";

        public static bool TryCreateCodec(out CoopConnectionTicketCodec codec,
            out string error)
        {
            return TryCreateCodec(Environment.GetEnvironmentVariable, out codec,
                out error);
        }

        public static bool TryCreateCodec(Func<string, string> readEnvironment,
            out CoopConnectionTicketCodec codec, out string error)
        {
            if (readEnvironment == null)
                throw new ArgumentNullException(nameof(readEnvironment));
            string value = readEnvironment(SigningSecretVariable);
            if (string.IsNullOrWhiteSpace(value) ||
                Encoding.UTF8.GetByteCount(value) < 32)
            {
                codec = null;
                error = $"环境变量 {SigningSecretVariable} 必须提供至少 32 字节的服务器签名密钥。";
                return false;
            }

            codec = new CoopConnectionTicketCodec(Encoding.UTF8.GetBytes(value));
            error = string.Empty;
            return true;
        }
    }
}
