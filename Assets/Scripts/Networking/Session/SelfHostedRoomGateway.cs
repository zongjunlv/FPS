using System;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using UnityEngine;

namespace FPS.Networking.Session
{
    [Serializable]
    public sealed class SelfHostedRoomPlayer
    {
        public string accountId;
        public string appearanceId;
        public bool ready;
        public string readyEpoch;
        public bool connected;
    }

    [Serializable]
    public sealed class SelfHostedRoomSnapshot
    {
        public string id;
        public string joinCode;
        public string displayName;
        public string mapId;
        public string hostId;
        public string phase;
        public int seed;
        public string loadEpoch;
        public string loadFailure;
        public RemoteMatchConnectionInfo serverAllocation;
        public SelfHostedRoomPlayer[] players;
        // Public room discovery deliberately omits the private member list.
        public int playerCount;
        public int maximumPlayers;
        public string updatedAtUtc;
        public long revision;

        public bool HasPlayer(string accountId)
        {
            foreach (SelfHostedRoomPlayer player in players ??
                         Array.Empty<SelfHostedRoomPlayer>())
            {
                if (string.Equals(player?.accountId, accountId,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    [Serializable]
    internal sealed class SelfHostedRoomListResponse
    {
        public SelfHostedRoomSnapshot[] rooms;
    }

    [Serializable]
    internal sealed class SelfHostedCreateRoomRequest
    {
        public string displayName;
        public string mapId;
        public int seed;
        public string appearanceId;
    }

    [Serializable]
    internal sealed class SelfHostedJoinRoomRequest
    {
        public string sessionId;
        public string joinCode;
        public string appearanceId;
    }

    [Serializable]
    internal sealed class SelfHostedAppearanceRequest
    {
        public string appearanceId;
        public bool ready;
    }

    [Serializable]
    internal sealed class SelfHostedReadyRequest
    {
        public bool ready;
    }

    [Serializable]
    internal sealed class SelfHostedReadyEpochRequest
    {
        public string readyEpoch;
    }

    [Serializable]
    internal sealed class SelfHostedPhaseRequest
    {
        public string phase;
        public string loadFailure;
    }

    /// <summary>
    /// Thin adapter for the self-hosted room control plane. Gameplay packets
    /// still connect directly to the allocated dedicated Unity server.
    /// </summary>
    public sealed class SelfHostedRoomGateway
    {
        private readonly CoopDedicatedServerSettings settings;

        public SelfHostedRoomGateway(CoopDedicatedServerSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
        }

        public async Task<SelfHostedRoomSnapshot[]> ListAsync()
        {
            string body = await SendAsync("GET", "/v1/rooms");
            SelfHostedRoomListResponse list =
                JsonUtility.FromJson<SelfHostedRoomListResponse>(body);
            return list?.rooms ?? Array.Empty<SelfHostedRoomSnapshot>();
        }

        public Task<SelfHostedRoomSnapshot> CreateAsync(string displayName,
            string mapId, int seed, string appearanceId) => SendRoomAsync(
            "POST", "/v1/rooms", JsonUtility.ToJson(
                new SelfHostedCreateRoomRequest
                {
                    displayName = displayName,
                    mapId = mapId,
                    seed = seed,
                    appearanceId = appearanceId
                }));

        public Task<SelfHostedRoomSnapshot> JoinByCodeAsync(string code,
            string appearanceId) => SendRoomAsync("POST", "/v1/rooms/join",
            JsonUtility.ToJson(new SelfHostedJoinRoomRequest
            {
                joinCode = code,
                appearanceId = appearanceId
            }));

        public Task<SelfHostedRoomSnapshot> JoinByIdAsync(string id,
            string appearanceId) => SendRoomAsync("POST", "/v1/rooms/join",
            JsonUtility.ToJson(new SelfHostedJoinRoomRequest
            {
                sessionId = id,
                appearanceId = appearanceId
            }));

        public Task<SelfHostedRoomSnapshot> GetAsync(string id) =>
            SendRoomAsync("GET", RoomPath(id));

        public async Task<SelfHostedRoomSnapshot> GetCurrentAsync()
        {
            string body = await SendAsync("GET", "/v1/rooms/current");
            return ParseOptionalRoomResponse(body);
        }

        public static SelfHostedRoomSnapshot ParseOptionalRoomResponse(
            string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            SelfHostedRoomSnapshot room =
                JsonUtility.FromJson<SelfHostedRoomSnapshot>(body);
            if (room == null || string.IsNullOrWhiteSpace(room.id) ||
                string.IsNullOrWhiteSpace(room.hostId))
                throw new InvalidOperationException(
                    "房间服务返回了不完整的房间信息。");
            return room;
        }

        public Task<SelfHostedRoomSnapshot> SelectAppearanceAsync(string id,
            string appearanceId) => SendRoomAsync("POST", RoomPath(id) +
            "/player", JsonUtility.ToJson(new SelfHostedAppearanceRequest
            {
                appearanceId = appearanceId,
                ready = false
            }));

        public Task<SelfHostedRoomSnapshot> SetReadyAsync(string id,
            bool ready) => SendRoomAsync("POST", RoomPath(id) + "/player",
            JsonUtility.ToJson(new SelfHostedReadyRequest
            {
                ready = ready
            }));

        public Task<SelfHostedRoomSnapshot> ReportReadyEpochAsync(string id,
            string epoch) => SendRoomAsync("POST", RoomPath(id) + "/player",
            JsonUtility.ToJson(new SelfHostedReadyEpochRequest
            {
                readyEpoch = epoch
            }));

        public Task<SelfHostedRoomSnapshot> SetPhaseAsync(string id,
            string phase, string failure = null) => SendRoomAsync("POST",
            RoomPath(id) + "/phase", JsonUtility.ToJson(
                new SelfHostedPhaseRequest
                {
                    phase = phase,
                    loadFailure = failure ?? string.Empty
                }));

        public async Task LeaveAsync(string id)
        {
            await SendAsync("POST", RoomPath(id) + "/leave", "{}");
        }

        private async Task<SelfHostedRoomSnapshot> SendRoomAsync(
            string method, string path, string json = null)
        {
            string body = await SendAsync(method, path, json);
            return ParseOptionalRoomResponse(body) ??
                   throw new InvalidOperationException(
                       "房间服务没有返回房间信息。");
        }

        private async Task<string> SendAsync(string method, string path,
            string json = null)
        {
            string token = SelfHostedAuthenticationGateway.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "账号登录已过期，请重新登录后再进入联机房间。");
            try
            {
                return await SelfHostedHttpClient.SendAsync(method, path,
                    json, token, settings);
            }
            catch (SelfHostedHttpException exception)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(exception.SafeMessage)
                        ? "房间服务暂时不可用，请稍后重试。"
                        : exception.SafeMessage);
            }
        }

        private static string RoomPath(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("房间 ID 不能为空。", nameof(id));
            return "/v1/rooms/" + Uri.EscapeDataString(id.Trim());
        }
    }
}
