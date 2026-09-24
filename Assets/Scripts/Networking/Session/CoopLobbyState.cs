using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Session
{
    public enum CoopLobbyConnectionState
    {
        Connected = 0,
        Reconnecting = 1,
        Disconnected = 2
    }

    public enum CoopLobbyFailure
    {
        None = 0,
        InvalidAccount = 1,
        DuplicateAccount = 2,
        RoomFull = 3,
        MemberNotFound = 4,
        InvalidAppearance = 5,
        AppearanceRequired = 6,
        HostOnly = 7,
        PlayersNotReady = 9,
        RoomAlreadyStarting = 10
    }

    public sealed class CoopLobbyMemberSnapshot
    {
        public CoopLobbyMemberSnapshot(string accountPlayerId, bool isHost,
            string appearanceId, bool isReady,
            CoopLobbyConnectionState connectionState)
        {
            AccountPlayerId = accountPlayerId ?? string.Empty;
            IsHost = isHost;
            AppearanceId = appearanceId ?? string.Empty;
            IsReady = isReady;
            ConnectionState = connectionState;
        }

        public string AccountPlayerId { get; }
        public bool IsHost { get; }
        public string AppearanceId { get; }
        public bool IsReady { get; }
        public CoopLobbyConnectionState ConnectionState { get; }
    }

    public static class CoopLobbyMessages
    {
        public static string Describe(CoopLobbyFailure failure)
        {
            return failure switch
            {
                CoopLobbyFailure.InvalidAccount => "账号身份无效，请重新登录。",
                CoopLobbyFailure.DuplicateAccount => "该账号已在房间中。",
                CoopLobbyFailure.RoomFull => "房间人数已满。",
                CoopLobbyFailure.MemberNotFound => "玩家不在当前房间中。",
                CoopLobbyFailure.InvalidAppearance => "角色 ID 无效，已拒绝选择。",
                CoopLobbyFailure.AppearanceRequired => "请先确认角色再准备。",
                CoopLobbyFailure.HostOnly => "只有房主可以开始战局。",
                CoopLobbyFailure.PlayersNotReady => "所有已加入玩家确认角色并准备后才能开始。",
                CoopLobbyFailure.RoomAlreadyStarting => "战局已经开始加载。",
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// Deterministic room rules independent from the transport adapter.
    /// </summary>
    public sealed class CoopLobbyRoster
    {
        private sealed class Member
        {
            public string AccountPlayerId;
            public bool IsHost;
            public string AppearanceId;
            public bool IsReady;
            public CoopLobbyConnectionState ConnectionState;
        }

        private readonly HashSet<string> allowedAppearances;
        private readonly Dictionary<string, Member> members = new(
            StringComparer.Ordinal);
        private readonly int maximumPlayers;

        public CoopLobbyRoster(IEnumerable<string> validAppearanceIds,
            int playerLimit = CoopSessionController.MaximumPlayers)
        {
            allowedAppearances = new HashSet<string>(
                (validAppearanceIds ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()),
                StringComparer.Ordinal);
            if (allowedAppearances.Count == 0)
                throw new ArgumentException(
                    "At least one valid appearance is required.",
                    nameof(validAppearanceIds));
            if (playerLimit < 1 || playerLimit > 16)
                throw new ArgumentOutOfRangeException(nameof(playerLimit));
            maximumPlayers = playerLimit;
        }

        public bool IsStarting { get; private set; }
        public int Count => members.Count;
        public bool IsFull => Count >= maximumPlayers;
        public IReadOnlyList<CoopLobbyMemberSnapshot> Members => members.Values
            .OrderByDescending(value => value.IsHost)
            .ThenBy(value => value.AccountPlayerId, StringComparer.Ordinal)
            .Select(ToSnapshot).ToArray();

        public bool IsAppearanceAllowed(string appearanceId) =>
            !string.IsNullOrWhiteSpace(appearanceId) &&
            allowedAppearances.Contains(appearanceId.Trim());

        public bool TryJoin(string accountPlayerId, bool isHost,
            string appearanceId, out CoopLobbyFailure failure)
        {
            string account = accountPlayerId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(account))
            {
                failure = CoopLobbyFailure.InvalidAccount;
                return false;
            }
            if (members.ContainsKey(account))
            {
                failure = CoopLobbyFailure.DuplicateAccount;
                return false;
            }
            if (IsFull)
            {
                failure = CoopLobbyFailure.RoomFull;
                return false;
            }
            string appearance = appearanceId?.Trim() ?? string.Empty;
            if (!IsAppearanceAllowed(appearance))
            {
                failure = CoopLobbyFailure.InvalidAppearance;
                return false;
            }
            if (isHost && members.Values.Any(value => value.IsHost))
            {
                failure = CoopLobbyFailure.HostOnly;
                return false;
            }

            members.Add(account, new Member
            {
                AccountPlayerId = account,
                IsHost = isHost,
                AppearanceId = appearance,
                IsReady = false,
                ConnectionState = CoopLobbyConnectionState.Connected
            });
            failure = CoopLobbyFailure.None;
            return true;
        }

        public bool TrySelectAppearance(string accountPlayerId,
            string appearanceId, out CoopLobbyFailure failure)
        {
            if (!TryMember(accountPlayerId, out Member member, out failure))
                return false;
            string appearance = appearanceId?.Trim() ?? string.Empty;
            if (!IsAppearanceAllowed(appearance))
            {
                failure = CoopLobbyFailure.InvalidAppearance;
                return false;
            }

            if (!string.Equals(member.AppearanceId, appearance,
                    StringComparison.Ordinal))
            {
                member.AppearanceId = appearance;
                member.IsReady = false;
            }
            failure = CoopLobbyFailure.None;
            return true;
        }

        public bool TrySetReady(string accountPlayerId, bool ready,
            out CoopLobbyFailure failure)
        {
            if (!TryMember(accountPlayerId, out Member member, out failure))
                return false;
            if (ready && !IsAppearanceAllowed(member.AppearanceId))
            {
                failure = CoopLobbyFailure.AppearanceRequired;
                return false;
            }
            if (member.ConnectionState != CoopLobbyConnectionState.Connected)
            {
                failure = CoopLobbyFailure.MemberNotFound;
                return false;
            }
            member.IsReady = ready;
            failure = CoopLobbyFailure.None;
            return true;
        }

        public bool TrySetConnection(string accountPlayerId,
            CoopLobbyConnectionState state, out CoopLobbyFailure failure)
        {
            if (!TryMember(accountPlayerId, out Member member, out failure))
                return false;
            member.ConnectionState = state;
            if (state != CoopLobbyConnectionState.Connected)
                member.IsReady = false;
            failure = CoopLobbyFailure.None;
            return true;
        }

        public bool TryLeave(string accountPlayerId,
            out CoopLobbyFailure failure)
        {
            if (!members.Remove(accountPlayerId?.Trim() ?? string.Empty))
            {
                failure = CoopLobbyFailure.MemberNotFound;
                return false;
            }
            IsStarting = false;
            failure = CoopLobbyFailure.None;
            return true;
        }

        public bool CanStart(string requesterAccountId,
            out CoopLobbyFailure failure)
        {
            if (IsStarting)
            {
                failure = CoopLobbyFailure.RoomAlreadyStarting;
                return false;
            }
            if (!TryMember(requesterAccountId, out Member requester,
                    out failure))
                return false;
            if (!requester.IsHost)
            {
                failure = CoopLobbyFailure.HostOnly;
                return false;
            }
            if (members.Values.Any(value => !value.IsReady ||
                    value.ConnectionState != CoopLobbyConnectionState.Connected ||
                    !IsAppearanceAllowed(value.AppearanceId)))
            {
                failure = CoopLobbyFailure.PlayersNotReady;
                return false;
            }
            failure = CoopLobbyFailure.None;
            return true;
        }

        public bool TryStart(string requesterAccountId,
            out CoopLobbyFailure failure)
        {
            if (!CanStart(requesterAccountId, out failure)) return false;
            IsStarting = true;
            return true;
        }

        public bool TryGet(string accountPlayerId,
            out CoopLobbyMemberSnapshot snapshot)
        {
            if (members.TryGetValue(accountPlayerId?.Trim() ?? string.Empty,
                    out Member member))
            {
                snapshot = ToSnapshot(member);
                return true;
            }
            snapshot = null;
            return false;
        }

        public void Reset(IEnumerable<CoopLobbyMemberSnapshot> snapshots,
            bool isStarting)
        {
            members.Clear();
            IsStarting = isStarting;
            if (snapshots == null) return;
            foreach (CoopLobbyMemberSnapshot snapshot in snapshots)
            {
                if (snapshot == null || members.Count >= maximumPlayers ||
                    string.IsNullOrWhiteSpace(snapshot.AccountPlayerId) ||
                    members.ContainsKey(snapshot.AccountPlayerId) ||
                    !IsAppearanceAllowed(snapshot.AppearanceId))
                    continue;
                members.Add(snapshot.AccountPlayerId, new Member
                {
                    AccountPlayerId = snapshot.AccountPlayerId,
                    IsHost = snapshot.IsHost,
                    AppearanceId = snapshot.AppearanceId,
                    IsReady = snapshot.IsReady,
                    ConnectionState = snapshot.ConnectionState
                });
            }
        }

        private bool TryMember(string accountPlayerId, out Member member,
            out CoopLobbyFailure failure)
        {
            if (!members.TryGetValue(accountPlayerId?.Trim() ?? string.Empty,
                    out member))
            {
                failure = CoopLobbyFailure.MemberNotFound;
                return false;
            }
            failure = CoopLobbyFailure.None;
            return true;
        }

        private static CoopLobbyMemberSnapshot ToSnapshot(Member member) =>
            new(member.AccountPlayerId, member.IsHost, member.AppearanceId,
                member.IsReady, member.ConnectionState);
    }
}
