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
        VersionMismatch = 7,
        SessionMismatch = 8,
        ReconnectWindowExpired = 9,
        ProtocolMismatch = 10,
        ContentMismatch = 11,
        NotInMatchRoster = 12
    }

    public readonly struct CoopConnectionClaims
    {
        public CoopConnectionClaims(string accountPlayerId, string version,
            string nonce, long issuedAtUnixSeconds, long expiresAtUnixSeconds,
            string matchId = "")
        {
            AccountPlayerId = accountPlayerId;
            Version = version;
            Nonce = nonce;
            IssuedAtUnixSeconds = issuedAtUnixSeconds;
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
            MatchId = matchId ?? string.Empty;
        }

        public string AccountPlayerId { get; }
        public string Version { get; }
        public string Nonce { get; }
        public long IssuedAtUnixSeconds { get; }
        public long ExpiresAtUnixSeconds { get; }
        public string MatchId { get; }
    }

    public readonly struct CoopApprovedIdentity
    {
        public CoopApprovedIdentity(ulong clientId, string accountPlayerId,
            int simulationPlayerId, int connectionGeneration = 1,
            bool isReconnection = false)
        {
            ClientId = clientId;
            AccountPlayerId = accountPlayerId;
            SimulationPlayerId = simulationPlayerId;
            ConnectionGeneration = Math.Max(1, connectionGeneration);
            IsReconnection = isReconnection;
        }

        public ulong ClientId { get; }
        public string AccountPlayerId { get; }
        public int SimulationPlayerId { get; }
        public int ConnectionGeneration { get; }
        public bool IsReconnection { get; }
    }

    public readonly struct CoopReconnectReservation
    {
        public CoopReconnectReservation(string accountPlayerId,
            int simulationPlayerId, int connectionGeneration,
            long expiresAtUnixSeconds)
        {
            AccountPlayerId = accountPlayerId ?? string.Empty;
            SimulationPlayerId = simulationPlayerId;
            ConnectionGeneration = Math.Max(1, connectionGeneration);
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        }

        public string AccountPlayerId { get; }
        public int SimulationPlayerId { get; }
        public int ConnectionGeneration { get; }
        public long ExpiresAtUnixSeconds { get; }
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
                CoopAdmissionFailure.ProtocolMismatch =>
                    "客户端网络协议与服务器不兼容，请更新后重试。",
                CoopAdmissionFailure.ContentMismatch =>
                    "客户端游戏内容与服务器不兼容，请更新后重试。",
                CoopAdmissionFailure.SessionMismatch =>
                    "连接凭证不属于当前战局，请重新进入房间。",
                CoopAdmissionFailure.ReconnectWindowExpired =>
                    "重连时间已结束，请返回房间等待下一局。",
                CoopAdmissionFailure.NotInMatchRoster =>
                    "当前账号不属于这场合作战局，请返回房间重新加入。",
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
            string nonce = null, string matchId = "")
        {
            string account = NormalizeField(accountPlayerId,
                nameof(accountPlayerId), 128);
            string normalizedVersion = NormalizeField(version,
                nameof(version), 256);
            if (lifetimeSeconds < 1 ||
                lifetimeSeconds > MaximumLifetimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
            string normalizedNonce = string.IsNullOrWhiteSpace(nonce)
                ? Guid.NewGuid().ToString("N")
                : NormalizeField(nonce, nameof(nonce), 64);
            string normalizedMatch = string.IsNullOrWhiteSpace(matchId)
                ? string.Empty
                : NormalizeField(matchId, nameof(matchId), 128);
            long expiresAt = checked(issuedAtUnixSeconds + lifetimeSeconds);
            string payload = string.Join("\n", new[]
            {
                EncodeText(account),
                EncodeText(normalizedVersion),
                normalizedNonce,
                issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
                expiresAt.ToString(CultureInfo.InvariantCulture),
                EncodeText(normalizedMatch)
            });
            string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            string signedValue = Prefix + "." + encodedPayload;
            byte[] signature = Sign(Encoding.UTF8.GetBytes(signedValue));
            return signedValue + "." + Base64UrlEncode(signature);
        }

        public string Issue(string accountPlayerId,
            CoopBuildCompatibility compatibility,
            long issuedAtUnixSeconds, int lifetimeSeconds = 120,
            string nonce = null, string matchId = "") => Issue(
            accountPlayerId, compatibility.ToTicketValue(),
            issuedAtUnixSeconds, lifetimeSeconds, nonce, matchId);

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
            if (values.Length != 5 && values.Length != 6 ||
                !TryDecodeText(values[0], out string account) ||
                !TryDecodeText(values[1], out string version) ||
                !IsSafeField(account, 128) || !IsSafeField(version, 256) ||
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

            string matchId = string.Empty;
            if (values.Length == 6)
            {
                if (values[5].Length == 0)
                    matchId = string.Empty;
                else if (!TryDecodeText(values[5], out matchId) ||
                         matchId.Length > 128 || matchId.IndexOf('\n') >= 0 ||
                         matchId.IndexOf('\r') >= 0)
                    return false;
            }
            claims = new CoopConnectionClaims(account, version, values[2],
                issuedAt, expiresAt, matchId);
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
        private readonly CoopBuildCompatibility? expectedCompatibility;
        private readonly int maximumPlayers;
        private readonly Func<long> utcNowSeconds;
        private readonly int reconnectGraceSeconds;
        private readonly string expectedMatchId;
        private readonly Dictionary<string, int> playerSlotByAccount;
        private readonly Dictionary<ulong, CoopApprovedIdentity> byClient = new();
        private readonly Dictionary<string, ulong> clientByAccount = new(
            StringComparer.Ordinal);
        private readonly Dictionary<string, long> usedNonces = new(
            StringComparer.Ordinal);
        private readonly Dictionary<string, CoopReconnectReservation>
            reservations = new(StringComparer.Ordinal);
        private readonly HashSet<string> expiredReconnectAccounts = new(
            StringComparer.Ordinal);
        private readonly Queue<CoopReconnectReservation>
            expiredReservations = new();
        private readonly Dictionary<string, int> generationByAccount = new(
            StringComparer.Ordinal);

        public CoopConnectionAdmissionService(CoopConnectionTicketCodec ticketCodec,
            string serverVersion, int playerLimit, Func<long> clock = null,
            int reconnectWindowSeconds = 30, string matchId = "",
            IReadOnlyDictionary<string, int> matchRoster = null)
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
            if (reconnectWindowSeconds < 1 || reconnectWindowSeconds > 300)
                throw new ArgumentOutOfRangeException(
                    nameof(reconnectWindowSeconds));
            reconnectGraceSeconds = reconnectWindowSeconds;
            expectedMatchId = matchId?.Trim() ?? string.Empty;
            playerSlotByAccount = CopyMatchRoster(matchRoster, playerLimit);
        }

        public CoopConnectionAdmissionService(
            CoopConnectionTicketCodec ticketCodec,
            CoopBuildCompatibility compatibility,
            int playerLimit, Func<long> clock = null,
            int reconnectWindowSeconds = 30, string matchId = "",
            IReadOnlyDictionary<string, int> matchRoster = null) : this(
            ticketCodec, compatibility.ToTicketValue(), playerLimit, clock,
            reconnectWindowSeconds, matchId, matchRoster)
        {
            expectedCompatibility = compatibility;
        }

        public int ApprovedCount => byClient.Count;
        public int ReservedCount
        {
            get
            {
                PruneExpiredReservations(utcNowSeconds());
                return reservations.Count;
            }
        }

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
            PruneExpiredReservations(now);
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
            CoopAdmissionFailure compatibilityFailure =
                ResolveCompatibilityFailure(claims.Version);
            if (compatibilityFailure != CoopAdmissionFailure.None)
                return CoopAdmissionDecision.Reject(compatibilityFailure);
            if (!string.IsNullOrEmpty(expectedMatchId) &&
                !string.Equals(claims.MatchId, expectedMatchId,
                    StringComparison.Ordinal))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.SessionMismatch);
            if (playerSlotByAccount.Count > 0 &&
                !playerSlotByAccount.ContainsKey(claims.AccountPlayerId))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.NotInMatchRoster);
            if (usedNonces.ContainsKey(claims.Nonce))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.CredentialReplayed);
            if (clientByAccount.ContainsKey(claims.AccountPlayerId))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.DuplicateAccount);
            if (expiredReconnectAccounts.Contains(claims.AccountPlayerId))
                return CoopAdmissionDecision.Reject(
                    CoopAdmissionFailure.ReconnectWindowExpired);

            bool reconnecting = reservations.Remove(
                claims.AccountPlayerId, out CoopReconnectReservation reserved);
            if (!reconnecting && byClient.Count + reservations.Count >=
                    maximumPlayers)
                return CoopAdmissionDecision.Reject(CoopAdmissionFailure.ServerFull);

            int playerId = reconnecting
                ? reserved.SimulationPlayerId
                : ResolveAvailablePlayerId(claims.AccountPlayerId);
            if (playerId <= 0)
                return CoopAdmissionDecision.Reject(CoopAdmissionFailure.ServerFull);
            int generation = generationByAccount.TryGetValue(
                claims.AccountPlayerId, out int previousGeneration)
                ? previousGeneration + 1
                : 1;
            generationByAccount[claims.AccountPlayerId] = generation;
            var identity = new CoopApprovedIdentity(clientId,
                claims.AccountPlayerId, playerId, generation, reconnecting);
            byClient.Add(clientId, identity);
            clientByAccount.Add(claims.AccountPlayerId, clientId);
            usedNonces.Add(claims.Nonce, claims.ExpiresAtUnixSeconds);
            return CoopAdmissionDecision.Allow(identity);
        }

        private CoopAdmissionFailure ResolveCompatibilityFailure(
            string ticketVersion)
        {
            if (!expectedCompatibility.HasValue)
                return string.Equals(ticketVersion, expectedVersion,
                    StringComparison.Ordinal)
                    ? CoopAdmissionFailure.None
                    : CoopAdmissionFailure.VersionMismatch;
            if (!CoopBuildCompatibility.TryParseTicketValue(ticketVersion,
                    out CoopBuildCompatibility client))
                return CoopAdmissionFailure.VersionMismatch;
            CoopBuildCompatibility server = expectedCompatibility.Value;
            if (!string.Equals(client.ApplicationVersion,
                    server.ApplicationVersion, StringComparison.Ordinal))
                return CoopAdmissionFailure.VersionMismatch;
            if (!string.Equals(client.ProtocolVersion,
                    server.ProtocolVersion, StringComparison.Ordinal))
                return CoopAdmissionFailure.ProtocolMismatch;
            if (!string.Equals(client.ContentVersion,
                    server.ContentVersion, StringComparison.Ordinal))
                return CoopAdmissionFailure.ContentMismatch;
            return CoopAdmissionFailure.None;
        }

        public bool TryGetIdentity(ulong clientId,
            out CoopApprovedIdentity identity) =>
            byClient.TryGetValue(clientId, out identity);

        public void Release(ulong clientId)
        {
            if (!byClient.Remove(clientId, out CoopApprovedIdentity identity))
                return;
            clientByAccount.Remove(identity.AccountPlayerId);
            reservations[identity.AccountPlayerId] =
                new CoopReconnectReservation(
                    identity.AccountPlayerId,
                    identity.SimulationPlayerId,
                    identity.ConnectionGeneration,
                    checked(utcNowSeconds() + reconnectGraceSeconds));
        }

        public void ReleaseImmediately(ulong clientId)
        {
            if (!byClient.Remove(clientId, out CoopApprovedIdentity identity))
                return;
            clientByAccount.Remove(identity.AccountPlayerId);
            reservations.Remove(identity.AccountPlayerId);
            expiredReconnectAccounts.Remove(identity.AccountPlayerId);
        }

        public bool TryGetReservation(string accountPlayerId,
            out CoopReconnectReservation reservation)
        {
            PruneExpiredReservations(utcNowSeconds());
            return reservations.TryGetValue(accountPlayerId ?? string.Empty,
                out reservation);
        }

        public bool TryDequeueExpiredReservation(
            out CoopReconnectReservation reservation)
        {
            PruneExpiredReservations(utcNowSeconds());
            if (expiredReservations.Count > 0)
            {
                reservation = expiredReservations.Dequeue();
                return true;
            }
            reservation = default;
            return false;
        }

        private int ResolveAvailablePlayerId(string accountPlayerId)
        {
            if (playerSlotByAccount.TryGetValue(accountPlayerId,
                    out int configuredSlot))
                return IsPlayerSlotAvailable(configuredSlot)
                    ? configuredSlot
                    : -1;
            for (int candidate = 1; candidate <= maximumPlayers; candidate++)
            {
                if (IsPlayerSlotAvailable(candidate)) return candidate;
            }
            return -1;
        }

        private bool IsPlayerSlotAvailable(int candidate)
        {
            foreach (CoopApprovedIdentity identity in byClient.Values)
            {
                if (identity.SimulationPlayerId == candidate) return false;
            }
            foreach (CoopReconnectReservation reservation in
                     reservations.Values)
            {
                if (reservation.SimulationPlayerId == candidate) return false;
            }
            return true;
        }

        private static Dictionary<string, int> CopyMatchRoster(
            IReadOnlyDictionary<string, int> source, int playerLimit)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (source == null) return result;
            var usedSlots = new HashSet<int>();
            foreach (KeyValuePair<string, int> pair in source)
            {
                string account = pair.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(account) ||
                    pair.Value < 1 || pair.Value > playerLimit ||
                    !usedSlots.Add(pair.Value))
                    throw new ArgumentException(
                        "Match roster must contain unique accounts and player slots.",
                        nameof(source));
                result.Add(account, pair.Value);
            }
            return result;
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

        private void PruneExpiredReservations(long now)
        {
            if (reservations.Count == 0) return;
            var expired = new List<string>();
            foreach (KeyValuePair<string, CoopReconnectReservation> pair in
                     reservations)
            {
                if (pair.Value.ExpiresAtUnixSeconds > now) continue;
                expired.Add(pair.Key);
                expiredReconnectAccounts.Add(pair.Key);
                expiredReservations.Enqueue(pair.Value);
            }
            for (int index = 0; index < expired.Count; index++)
                reservations.Remove(expired[index]);
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
