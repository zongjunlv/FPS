using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using Unity.Services.Multiplayer;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Networking.Session
{
    public enum CoopSessionState
    {
        Offline,
        Initializing,
        Hosting,
        Joining,
        Connected,
        Reconnecting,
        Leaving,
        Failed
    }

    public readonly struct CoopPublicRoomSnapshot
    {
        public CoopPublicRoomSnapshot(string sessionId, string displayName,
            string mapId, int playerCount, int maximumPlayers,
            DateTime lastUpdated)
        {
            SessionId = sessionId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "未命名小队"
                : displayName.Trim();
            MapId = string.IsNullOrWhiteSpace(mapId)
                ? CoopSessionController.DefaultMapId
                : mapId.Trim();
            PlayerCount = Math.Max(0, playerCount);
            MaximumPlayers = Math.Max(1, maximumPlayers);
            LastUpdated = lastUpdated;
        }

        public string SessionId { get; }
        public string DisplayName { get; }
        public string MapId { get; }
        public int PlayerCount { get; }
        public int MaximumPlayers { get; }
        public DateTime LastUpdated { get; }
    }

    /// <summary>
    /// Explicit, opt-in two-player session entry point. The object can exist in
    /// a single-player run without starting services or a transport.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopSessionController : MonoBehaviour
    {
        public const int MaximumPlayers = 2;
        public const string DefaultMapId = "CityNew";
        public const string AppearanceProperty = "appearance";
        public const string ReadyProperty = "ready";
        public const string ConnectionProperty = "connection";
        public const string MapProperty = "map";
        public const string PhaseProperty = "phase";
        public const string PhaseLobby = "lobby";
        public const string PhaseStarting = "starting";
        public const string PhaseLoading = "loading";
        public const string PhaseBattle = "battle";
        public const string PhaseCancelled = "cancelled";
        public const string SeedProperty = "seed";
        public const string LoadEpochProperty = "load_epoch";
        public const string ReadyEpochProperty = "ready_epoch";
        public const string LoadFailureProperty = "load_failure";
        public const string ServerAllocationProperty = "server_allocation";

        private SelfHostedRoomSnapshot activeRoom;
        private SelfHostedRoomGateway roomGateway;
        private OptionalNetworkBootstrap networkBootstrap;
        private bool operationInProgress;
        private bool directSession;
        private bool modeExitRequested;
        private int operationGeneration;
        private string pendingConnectionCredential = string.Empty;
        private string pendingAppearanceId = "operative-alpha";
        private string lobbyMapId = DefaultMapId;
        private CoopLobbyRoster lobbyRoster = new(new[] { "operative-alpha" });
        private bool lobbyOperationInProgress;
        private bool lobbyStartEventRaised;
        private readonly CoopSceneLoadBarrier sceneLoadBarrier = new();
        private CoopNetworkRuntimeInstaller networkInstaller;
        private string sceneLoadFailure = string.Empty;
        private string observedLoadEpoch = string.Empty;
        private bool sceneBarrierEvaluationInProgress;
        private bool sceneCancellationInProgress;
        private bool battleReadyEventRaised;
        private bool sceneCancellationEventRaised;
        private bool returnToLobbyInProgress;
        private string lastObservedPhase = PhaseLobby;
        private readonly List<CoopPublicRoomSnapshot> publicRooms = new();
        private bool publicRoomQueryInProgress;
        private int pendingScenarioSeed = 18018;
        private CoopDedicatedServerGateway dedicatedServerGateway;
        private RemoteMatchConnectionInfo activeRemoteConnection;
        private string activeRemoteTicket = string.Empty;
        private bool dedicatedTransportOperationInProgress;
        private bool dedicatedTransportConnected;
        private bool suppressDedicatedReconnect;
        private bool roomPollInProgress;
        private float nextRoomPollAt;
        private int consecutiveRoomPollFailures;
        private bool recoveringBattleScene;

        public event Action<CoopSessionState> StateChanged;
        public event Action LobbyChanged;
        public event Action LobbyStartRequested;
        public event Action PublicRoomsChanged;
        public event Action BattleSceneReady;
        public event Action<string> SceneLoadCancelled;
        public event Action ReturnedToLobby;

        public CoopSessionState State { get; private set; } =
            CoopSessionState.Offline;
        public string JoinCode => activeRoom?.joinCode ?? string.Empty;
        public int PlayerCount => activeRoom?.players?.Length ??
            ResolveDirectPlayerCount();
        public bool IsHost => activeRoom != null
            ? string.Equals(activeRoom.hostId, LocalPlayerId,
                StringComparison.Ordinal)
            : directSession && networkBootstrap != null &&
              networkBootstrap.NetworkManager.IsHost;
        public bool IsConnected => (activeRoom != null || directSession) &&
            State == CoopSessionState.Connected;
        /// <summary>
        /// True only after the local NGO client has completed the dedicated
        /// gameplay-server handshake. A connected room is control-plane
        /// state and must never be mistaken for a playable battle connection.
        /// </summary>
        public bool IsBattleTransportConnected
        {
            get
            {
                NetworkManager manager = networkBootstrap?.NetworkManager;
                if (manager == null || !manager.IsConnectedClient)
                    return false;
                return directSession || dedicatedTransportConnected;
            }
        }
        public bool IsBattleTransportConnecting =>
            dedicatedTransportOperationInProgress;
        public string LastFailure { get; private set; } = string.Empty;
        public SelfHostedRoomSnapshot ActiveSession => activeRoom;
        public bool HasActiveSession => activeRoom != null;
        public IReadOnlyList<CoopLobbyMemberSnapshot> LobbyMembers =>
            lobbyRoster.Members;
        public string LobbyMapId => lobbyMapId;
        public string LocalPlayerId =>
            SelfHostedAuthenticationGateway.AccountId ?? string.Empty;
        public string PendingAppearanceId => pendingAppearanceId;
        public bool IsLobbyBusy => lobbyOperationInProgress;
        public bool IsLobbyStarting => lobbyRoster.IsStarting;
        public bool IsLocalReady => lobbyRoster.TryGet(LocalPlayerId,
            out CoopLobbyMemberSnapshot member) && member.IsReady;
        public bool CanHostStart => activeRoom != null && IsHost &&
            lobbyRoster.CanStart(LocalPlayerId, out _);
        public string LobbyFailureMessage { get; private set; } = string.Empty;
        public IReadOnlyList<CoopPublicRoomSnapshot> PublicRooms => publicRooms;
        public bool IsPublicRoomQueryInProgress => publicRoomQueryInProgress;
        public string PublicRoomBrowserFailure { get; private set; } =
            string.Empty;
        public string SceneLoadEpoch => activeRoom?.loadEpoch ?? string.Empty;
        public int SceneSeed => activeRoom?.seed ?? 0;
        public string LobbyPhase => activeRoom?.phase ?? PhaseLobby;
        public string SceneLoadFailure => sceneLoadFailure;

        /// <summary>
        /// Idempotent battle-scene watchdog. Session change notifications can
        /// be delayed across a Single scene load, so the scene coordinator also
        /// calls this once per frame until the data-plane connection starts.
        /// </summary>
        public void EnsureBattleTransportForCurrentPhase()
        {
            if (activeRoom == null || recoveringBattleScene ||
                dedicatedTransportConnected ||
                dedicatedTransportOperationInProgress ||
                State == CoopSessionState.Failed ||
                !string.Equals(LobbyPhase, PhaseBattle,
                    StringComparison.Ordinal))
                return;
            _ = EnsureDedicatedBattleTransportAsync();
        }

        public async Task<bool> RetryBattleTransportAsync()
        {
            if (activeRoom == null || recoveringBattleScene ||
                !string.Equals(LobbyPhase, PhaseBattle,
                    StringComparison.Ordinal) ||
                dedicatedTransportOperationInProgress)
                return false;

            if (networkBootstrap?.IsListening == true)
            {
                networkBootstrap.Shutdown();
                NetworkManager manager = networkBootstrap.NetworkManager;
                while (manager != null && manager.ShutdownInProgress)
                    await Task.Yield();
            }
            dedicatedTransportConnected = false;
            activeRemoteTicket = string.Empty;
            LastFailure = string.Empty;
            LobbyFailureMessage = string.Empty;
            SetState(CoopSessionState.Connected);
            await EnsureDedicatedBattleTransportAsync();
            return IsBattleTransportConnected;
        }

        public void ConfigureLobby(IEnumerable<string> allowedAppearanceIds,
            string defaultAppearanceId, string mapId = DefaultMapId)
        {
            if (activeRoom != null)
                throw new InvalidOperationException(
                    "房间已创建，不能再替换角色白名单。");
            string[] allowed = (allowedAppearanceIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (allowed.Length == 0)
                throw new ArgumentException("角色白名单不能为空。",
                    nameof(allowedAppearanceIds));
            lobbyRoster = new CoopLobbyRoster(allowed, MaximumPlayers);
            pendingAppearanceId = allowed.Contains(defaultAppearanceId,
                StringComparer.Ordinal)
                ? defaultAppearanceId
                : allowed[0];
            lobbyMapId = string.IsNullOrWhiteSpace(mapId)
                ? DefaultMapId
                : mapId.Trim();
            LobbyFailureMessage = string.Empty;
            NotifyLobbyChanged();
        }

        public bool SetPendingLobbyAppearance(string appearanceId)
        {
            if (!lobbyRoster.IsAppearanceAllowed(appearanceId))
            {
                LobbyFailureMessage = CoopLobbyMessages.Describe(
                    CoopLobbyFailure.InvalidAppearance);
                NotifyLobbyChanged();
                return false;
            }
            pendingAppearanceId = appearanceId.Trim();
            LobbyFailureMessage = string.Empty;
            NotifyLobbyChanged();
            return true;
        }

        /// <summary>
        /// Receives a short-lived ticket from the trusted account backend.
        /// Passwords and long-lived authentication tokens must never be supplied.
        /// </summary>
        public void ConfigureConnectionCredential(string credential)
        {
            if (networkBootstrap != null && networkBootstrap.IsListening)
                throw new InvalidOperationException(
                    "连接已开始，不能再替换身份凭证。");
            pendingConnectionCredential = credential?.Trim() ?? string.Empty;
            networkBootstrap?.ConfigureClientCredential(
                pendingConnectionCredential);
        }

        /// <summary>
        /// Validates an allocation response before opening the transport. This
        /// keeps incompatible clients out of the NGO handshake and provides a
        /// precise, user-facing reason instead of a generic disconnect.
        /// </summary>
        public bool StartRemote(RemoteMatchConnectionInfo connection,
            CoopBuildCompatibility localCompatibility, string credential)
        {
            CoopCompatibilityDecision decision =
                RemoteMatchCompatibilityValidator.Validate(
                    localCompatibility, connection);
            if (!decision.Compatible)
            {
                LastFailure = decision.Message;
                SetState(CoopSessionState.Failed);
                return false;
            }
            if (string.IsNullOrWhiteSpace(credential))
            {
                LastFailure = "远程战局缺少短期连接凭证，请重新申请战局。";
                SetState(CoopSessionState.Failed);
                return false;
            }

            ConfigureConnectionCredential(credential);
            var endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Address = connection.host.Trim();
            endpoint.Port = connection.port;
            return StartDirect(false, endpoint);
        }

        /// <summary>
        /// Restarts only the client transport with a newly issued one-time
        /// credential while retaining the current lobby and battle context.
        /// </summary>
        public bool ReconnectWithCredential(string credential)
        {
            if (operationInProgress || networkBootstrap == null ||
                networkBootstrap.IsListening ||
                networkBootstrap.NetworkManager.IsHost ||
                activeRoom == null && !directSession)
            {
                LastFailure = "当前会话不能执行重连。";
                return false;
            }
            string normalized = credential?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(normalized))
            {
                LastFailure = "重连需要新的短期身份凭证。";
                return false;
            }
            operationInProgress = true;
            SetState(CoopSessionState.Reconnecting);
            try
            {
                pendingConnectionCredential = normalized;
                networkBootstrap.ConfigureClientCredential(normalized);
                if (!networkBootstrap.StartClient())
                {
                    LastFailure = networkBootstrap.LastFailure;
                    SetState(CoopSessionState.Failed);
                    return false;
                }
                LastFailure = string.Empty;
                return true;
            }
            finally
            {
                operationInProgress = false;
            }
        }

        private void OnDestroy()
        {
            modeExitRequested = true;
            operationGeneration++;
            activeRoom = null;
            directSession = false;
            suppressDedicatedReconnect = true;
            if (networkBootstrap != null)
            {
                networkBootstrap.ClientConnected -=
                    HandleReconnectableClientConnected;
                networkBootstrap.ClientDisconnected -=
                    HandleReconnectableClientDisconnect;
            }
            networkBootstrap?.Shutdown();
        }

        private void Update()
        {
            if (activeRoom == null || modeExitRequested ||
                roomPollInProgress || Time.unscaledTime < nextRoomPollAt)
                return;
            nextRoomPollAt = Time.unscaledTime + 0.75f;
            _ = PollRoomAsync(activeRoom.id, operationGeneration);
        }

        public async Task<bool> HostAsync(string profile = null,
            string roomName = null)
        {
            if (!CanBegin()) return false;
            modeExitRequested = false;
            int generation = ++operationGeneration;
            operationInProgress = true;
            SetState(CoopSessionState.Initializing);

            try
            {
                pendingScenarioSeed = CreateRunSeed();
                EnsureNetworkPrerequisite(deferPlayerSpawn: true);
                EnsureSignedIn();
                if (!IsCurrentOperation(generation)) return false;
                SetState(CoopSessionState.Hosting);
                SelfHostedRoomSnapshot created = await EnsureRoomGateway()
                    .CreateAsync(NormalizeRoomName(roomName), lobbyMapId,
                        pendingScenarioSeed, pendingAppearanceId);
                if (!IsCurrentOperation(generation))
                {
                    await LeaveStaleRoomAsync(created);
                    return false;
                }

                BindRoom(created);
                LastFailure = string.Empty;
                SetState(CoopSessionState.Connected);
                return true;
            }
            catch (Exception exception)
            {
                if (IsCurrentOperation(generation))
                {
                    Fail(exception);
                }
                return false;
            }
            finally
            {
                if (generation == operationGeneration)
                {
                    operationInProgress = false;
                }
            }
        }

        public async Task<bool> JoinAsync(
            string joinCode,
            string profile = null)
        {
            if (!CanBegin()) return false;
            string normalized = NormalizeJoinCode(joinCode);
            if (string.IsNullOrEmpty(normalized))
            {
                LastFailure = "请输入有效的加入码。";
                SetState(CoopSessionState.Failed);
                return false;
            }

            operationInProgress = true;
            modeExitRequested = false;
            int generation = ++operationGeneration;
            SetState(CoopSessionState.Initializing);
            try
            {
                EnsureNetworkPrerequisite(deferPlayerSpawn: true);
                EnsureSignedIn();
                if (!IsCurrentOperation(generation)) return false;
                SetState(CoopSessionState.Joining);
                SelfHostedRoomSnapshot joined = await EnsureRoomGateway()
                    .JoinByCodeAsync(normalized, pendingAppearanceId);
                if (!IsCurrentOperation(generation))
                {
                    await LeaveStaleRoomAsync(joined);
                    return false;
                }

                BindRoom(joined);
                LastFailure = string.Empty;
                SetState(CoopSessionState.Connected);
                return true;
            }
            catch (Exception exception)
            {
                if (IsCurrentOperation(generation))
                {
                    Fail(exception);
                }
                return false;
            }
            finally
            {
                if (generation == operationGeneration)
                {
                    operationInProgress = false;
                }
            }
        }

        public async Task<bool> JoinPublicRoomAsync(string sessionId,
            string profile = null)
        {
            if (!CanBegin()) return false;
            string normalized = sessionId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(normalized))
            {
                LastFailure = "请先选择一个仍可加入的公开房间。";
                SetState(CoopSessionState.Failed);
                return false;
            }

            operationInProgress = true;
            modeExitRequested = false;
            int generation = ++operationGeneration;
            SetState(CoopSessionState.Initializing);
            try
            {
                EnsureNetworkPrerequisite(deferPlayerSpawn: true);
                EnsureSignedIn();
                if (!IsCurrentOperation(generation)) return false;
                SetState(CoopSessionState.Joining);
                SelfHostedRoomSnapshot joined = await EnsureRoomGateway()
                    .JoinByIdAsync(normalized, pendingAppearanceId);
                if (!IsCurrentOperation(generation))
                {
                    await LeaveStaleRoomAsync(joined);
                    return false;
                }

                BindRoom(joined);
                LastFailure = string.Empty;
                PublicRoomBrowserFailure = string.Empty;
                SetState(CoopSessionState.Connected);
                return true;
            }
            catch (Exception exception)
            {
                if (IsCurrentOperation(generation)) Fail(exception);
                return false;
            }
            finally
            {
                if (generation == operationGeneration)
                    operationInProgress = false;
            }
        }

        public async Task<bool> RefreshPublicRoomsAsync(string profile = null)
        {
            if (publicRoomQueryInProgress || activeRoom != null ||
                directSession || operationInProgress)
                return false;

            modeExitRequested = false;
            int generation = operationGeneration;
            publicRoomQueryInProgress = true;
            PublicRoomBrowserFailure = string.Empty;
            NotifyPublicRoomsChanged();
            try
            {
                EnsureSignedIn();
                SelfHostedRoomSnapshot current = await EnsureRoomGateway()
                    .GetCurrentAsync();
                if (!IsCurrentOperation(generation)) return false;
                if (current != null)
                {
                    publicRooms.Clear();
                    BindRoom(current);
                    LastFailure = string.Empty;
                    SetState(CoopSessionState.Connected);
                    return true;
                }
                SelfHostedRoomSnapshot[] result =
                    await EnsureRoomGateway().ListAsync();
                if (!IsCurrentOperation(generation)) return false;
                publicRooms.Clear();
                foreach (SelfHostedRoomSnapshot room in result)
                {
                    if (!TryCreatePublicRoomSnapshot(room,
                            out CoopPublicRoomSnapshot snapshot))
                        continue;
                    publicRooms.Add(snapshot);
                }
                publicRooms.Sort((left, right) =>
                    right.LastUpdated.CompareTo(left.LastUpdated));
                return true;
            }
            catch (Exception exception)
            {
                publicRooms.Clear();
                PublicRoomBrowserFailure = string.IsNullOrWhiteSpace(
                    exception.Message)
                    ? "公开房间列表刷新失败，请检查网络后重试。"
                    : exception.Message;
                return false;
            }
            finally
            {
                publicRoomQueryInProgress = false;
                NotifyPublicRoomsChanged();
            }
        }

        // Legacy Issue87 test compatibility only; production room discovery
        // uses SelfHostedRoomGateway and never invokes Unity Multiplayer.
        public static QuerySessionsOptions CreatePublicRoomQueryOptions()
        {
            return new QuerySessionsOptions
            {
                Count = 20,
                FilterOptions = new List<FilterOption>
                {
                    new(FilterField.AvailableSlots, "0",
                        FilterOperation.Greater),
                    new(FilterField.IsLocked, "false",
                        FilterOperation.Equal),
                    new(FilterField.StringIndex1, DefaultMapId,
                        FilterOperation.Equal),
                    new(FilterField.StringIndex2, PhaseLobby,
                        FilterOperation.Equal)
                },
                SortOptions = new List<SortOption>
                {
                    new(SortOrder.Descending, SortField.LastUpdated)
                }
            };
        }

        public static bool TryCreatePublicRoomSnapshot(ISessionInfo info,
            out CoopPublicRoomSnapshot snapshot)
        {
            snapshot = default;
            if (info == null || string.IsNullOrWhiteSpace(info.Id) ||
                info.IsLocked || info.AvailableSlots <= 0 ||
                !string.Equals(Property(info.Properties, MapProperty,
                    string.Empty), DefaultMapId, StringComparison.Ordinal) ||
                !string.Equals(Property(info.Properties, PhaseProperty,
                    string.Empty), PhaseLobby, StringComparison.Ordinal))
                return false;

            int players = Math.Max(0, info.MaxPlayers - info.AvailableSlots);
            snapshot = new CoopPublicRoomSnapshot(info.Id, info.Name,
                Property(info.Properties, MapProperty, DefaultMapId),
                players, info.MaxPlayers, info.LastUpdated);
            return true;
        }

        public static bool TryCreatePublicRoomSnapshot(
            SelfHostedRoomSnapshot room,
            out CoopPublicRoomSnapshot snapshot)
        {
            snapshot = default;
            if (room == null || string.IsNullOrWhiteSpace(room.id) ||
                !string.Equals(room.mapId, DefaultMapId,
                    StringComparison.Ordinal) ||
                !string.Equals(room.phase, PhaseLobby,
                    StringComparison.Ordinal))
                return false;
            int capacity = room.maximumPlayers > 0
                ? room.maximumPlayers : MaximumPlayers;
            int players = room.players?.Length ?? room.playerCount;
            if (players >= capacity) return false;
            DateTime updatedAt = DateTime.TryParse(room.updatedAtUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTime parsed) ? parsed : DateTime.MinValue;
            snapshot = new CoopPublicRoomSnapshot(room.id,
                room.displayName, room.mapId, players, capacity, updatedAt);
            return true;
        }

        public async Task ShutdownForModeExitAsync()
        {
            modeExitRequested = true;
            operationGeneration++;
            operationInProgress = false;
            directSession = false;
            suppressDedicatedReconnect = true;

            SelfHostedRoomSnapshot leaving = activeRoom;
            RemoteMatchConnectionInfo remoteConnection =
                ResolveRemoteConnectionFromSession();
            await ReleaseOwnedDedicatedServerAsync(leaving, remoteConnection);
            activeRoom = null;

            if (leaving != null)
            {
                try
                {
                    await EnsureRoomGateway().LeaveAsync(leaving.id);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"合作战局退出时清理失败：{exception.Message}",
                        this);
                }
            }

            ShutdownDedicatedBattleTransport();
            LastFailure = string.Empty;
            ResetLobbyState();
            SetState(CoopSessionState.Offline);
        }

        public async Task LeaveAsync()
        {
            if (operationInProgress ||
                (activeRoom == null && !directSession)) return;
            modeExitRequested = true;
            operationGeneration++;
            operationInProgress = true;
            SetState(CoopSessionState.Leaving);
            suppressDedicatedReconnect = true;

            if (directSession)
            {
                directSession = false;
                networkBootstrap?.Shutdown();
                LastFailure = string.Empty;
                SetState(CoopSessionState.Offline);
                ResetLobbyState();
                operationInProgress = false;
                return;
            }

            SelfHostedRoomSnapshot leaving = activeRoom;
            RemoteMatchConnectionInfo remoteConnection =
                ResolveRemoteConnectionFromSession();
            await ReleaseOwnedDedicatedServerAsync(leaving, remoteConnection);
            activeRoom = null;

            try
            {
                ShutdownDedicatedBattleTransport();
                await EnsureRoomGateway().LeaveAsync(leaving.id);
                LastFailure = string.Empty;
                SetState(CoopSessionState.Offline);
                ResetLobbyState();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
            finally
            {
                operationInProgress = false;
            }
        }

        public async Task<bool> ReturnToLobbyAfterMatchAsync()
        {
            if (returnToLobbyInProgress) return false;
            if (directSession)
            {
                await LeaveAsync();
                ReturnedToLobby?.Invoke();
                return true;
            }
            // The host must also be able to tear down a match after a transport
            // or allocation failure. The room is still valid even when the
            // gameplay connection has moved this controller into Failed.
            if (activeRoom == null || !IsHost)
                return false;
            returnToLobbyInProgress = true;
            string roomId = activeRoom.id;
            try
            {
                RemoteMatchConnectionInfo finishedConnection =
                    ResolveRemoteConnectionFromSession();
                ShutdownDedicatedBattleTransport();
                await ReleaseOwnedDedicatedServerAsync(
                    activeRoom, finishedConnection);
                if (modeExitRequested || activeRoom == null ||
                    !string.Equals(activeRoom.id, roomId,
                        StringComparison.Ordinal)) return false;
                SelfHostedRoomSnapshot returned = await EnsureRoomGateway()
                    .SetPhaseAsync(roomId, PhaseLobby);
                ApplyRoom(returned);
                LastFailure = string.Empty;
                SetState(CoopSessionState.Connected);
                return true;
            }
            catch (Exception exception)
            {
                return FailLobby(exception);
            }
            finally
            {
                returnToLobbyInProgress = false;
            }
        }

        public bool StartDirect(
            bool asHost,
            NetworkEndpointSettings endpoint)
        {
            if (!CanBegin()) return false;
            modeExitRequested = false;
            operationGeneration++;
            operationInProgress = true;
            try
            {
                EnsureNetworkPrerequisite(deferPlayerSpawn: false);
                networkBootstrap.Configure(endpoint);
                bool started = asHost
                    ? networkBootstrap.StartHost()
                    : networkBootstrap.StartClient();
                if (!started)
                {
                    LastFailure = networkBootstrap.LastFailure;
                    SetState(CoopSessionState.Failed);
                    return false;
                }

                directSession = true;
                LastFailure = string.Empty;
                SetState(CoopSessionState.Connected);
                return true;
            }
            catch (Exception exception)
            {
                Fail(exception);
                return false;
            }
            finally
            {
                operationInProgress = false;
            }
        }

        public static string NormalizeJoinCode(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        public async Task<bool> SelectLobbyCharacterAsync(string appearanceId)
        {
            if (!CanMutateLobby()) return false;
            if (!lobbyRoster.IsAppearanceAllowed(appearanceId))
                return FailLobby(CoopLobbyFailure.InvalidAppearance);
            lobbyOperationInProgress = true;
            try
            {
                SelfHostedRoomSnapshot changed = await EnsureRoomGateway()
                    .SelectAppearanceAsync(activeRoom.id, appearanceId);
                pendingAppearanceId = appearanceId;
                ApplyRoom(changed);
                LobbyFailureMessage = string.Empty;
                NotifyLobbyChanged();
                return true;
            }
            catch (Exception exception)
            {
                return FailLobby(exception);
            }
            finally
            {
                lobbyOperationInProgress = false;
                NotifyLobbyChanged();
            }
        }

        public async Task<bool> SetLobbyReadyAsync(bool ready)
        {
            if (!CanMutateLobby()) return false;
            if (ready && !lobbyRoster.IsAppearanceAllowed(pendingAppearanceId))
                return FailLobby(CoopLobbyFailure.AppearanceRequired);
            lobbyOperationInProgress = true;
            try
            {
                SelfHostedRoomSnapshot changed = await EnsureRoomGateway()
                    .SetReadyAsync(activeRoom.id, ready);
                ApplyRoom(changed);
                LobbyFailureMessage = string.Empty;
                NotifyLobbyChanged();
                return true;
            }
            catch (Exception exception)
            {
                return FailLobby(exception);
            }
            finally
            {
                lobbyOperationInProgress = false;
                NotifyLobbyChanged();
            }
        }

        public async Task<bool> StartLobbyGameAsync()
        {
            if (!CanMutateLobby()) return false;
            RefreshLobbyFromRoom();
            if (!lobbyRoster.TryStart(LocalPlayerId,
                    out CoopLobbyFailure failure))
                return FailLobby(failure);
            lobbyOperationInProgress = true;
            string roomId = activeRoom.id;
            try
            {
                int seed = SceneSeed > 0
                    ? SceneSeed
                    : pendingScenarioSeed;
                CoopDedicatedServerGateway gateway =
                    EnsureDedicatedServerGateway();
                CoopDedicatedServerAllocation allocation =
                    await gateway.AllocateAsync(roomId, seed,
                        lobbyRoster.Members);
                if (modeExitRequested || activeRoom == null ||
                    !string.Equals(activeRoom.id, roomId,
                        StringComparison.Ordinal))
                {
                    // An exit may race with a long server startup. The
                    // backend idle lease remains a safety net if release is
                    // rejected after the room member has already left.
                    try
                    {
                        await gateway.ReleaseAsync(roomId,
                            allocation.connection);
                    }
                    catch (Exception)
                    {
                        // Best effort only: the room may no longer belong to
                        // this account after the exit completed.
                    }
                    return false;
                }
                activeRemoteConnection = allocation.connection;
                activeRemoteTicket = allocation.connectionTicket;
                // Allocation is atomic on the backend: it also moves the room
                // into loading and assigns a server-generated load epoch.
                SelfHostedRoomSnapshot loading = await EnsureRoomGateway()
                    .GetAsync(roomId);
                if (!string.Equals(loading.phase, PhaseLoading,
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(loading.loadEpoch))
                    throw new InvalidOperationException(
                        "服务器已分配战局，但未设置有效加载阶段。 ");
                ApplyRoom(loading);
                LobbyFailureMessage = string.Empty;
                NotifyLobbyChanged();
                return true;
            }
            catch (Exception exception)
            {
                RefreshLobbyFromRoom();
                return FailLobby(exception);
            }
            finally
            {
                lobbyOperationInProgress = false;
                NotifyLobbyChanged();
            }
        }

        public async Task<bool> ReportSceneReadyAsync(string epoch)
        {
            if (!CanMutateLobby()) return false;
            if (!string.Equals(SceneLoadEpoch, epoch,
                    StringComparison.Ordinal))
                return FailSceneLoad("场景就绪回报已过期，服务器已忽略。");
            if (recoveringBattleScene &&
                string.Equals(LobbyPhase, PhaseBattle,
                    StringComparison.Ordinal))
            {
                recoveringBattleScene = false;
                await EnsureDedicatedBattleTransportAsync();
                return IsBattleTransportConnected;
            }
            if (!string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal))
                return FailSceneLoad("场景就绪回报已过期，服务器已忽略。");
            try
            {
                SelfHostedRoomSnapshot changed = await EnsureRoomGateway()
                    .ReportReadyEpochAsync(activeRoom.id, epoch);
                ApplyRoom(changed);
                await EvaluateHostSceneLoadBarrierAsync();
                return true;
            }
            catch (Exception exception)
            {
                return FailSceneLoad(exception.Message);
            }
        }

        public async Task<bool> CancelSceneLoadAsync(string reason)
        {
            if (activeRoom == null || !IsHost) return false;
            if (string.Equals(LobbyPhase, PhaseCancelled,
                    StringComparison.Ordinal)) return true;
            if (sceneCancellationInProgress) return false;
            sceneCancellationInProgress = true;
            sceneLoadBarrier.Cancel();
            string roomId = activeRoom.id;
            sceneLoadFailure = string.IsNullOrWhiteSpace(reason)
                ? "服务器取消了场景加载。"
                : reason.Trim();
            try
            {
                SelfHostedRoomSnapshot changed = await EnsureRoomGateway()
                    .SetPhaseAsync(roomId, PhaseCancelled,
                        sceneLoadFailure);
                ApplyRoom(changed);
                if (modeExitRequested || activeRoom == null) return false;
                await ReleaseCancelledDedicatedServerAsync();
                RaiseSceneLoadCancelledOnce();
                // A cancelled load must not strand the room in a phase that
                // rejects the next allocation. Return both clients to lobby.
                SelfHostedRoomSnapshot lobby = await EnsureRoomGateway()
                    .SetPhaseAsync(roomId, PhaseLobby);
                ApplyRoom(lobby);
                NotifyLobbyChanged();
                return true;
            }
            catch (Exception exception)
            {
                return FailSceneLoad(exception.Message);
            }
            finally
            {
                sceneCancellationInProgress = false;
            }
        }

        public void TickSceneLoadTimeout(double nowSeconds)
        {
            if (activeRoom == null || !IsHost ||
                !string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal)) return;
            EnsureHostSceneLoadBarrier();
            if (sceneLoadBarrier.Tick(nowSeconds))
                _ = CancelSceneLoadAsync("等待成员加载 CityNew 超时，战局已取消。");
        }

        private bool CanBegin()
        {
            if (operationInProgress || activeRoom != null || directSession)
            {
                LastFailure = "已有合作战局或网络操作正在进行。";
                return false;
            }

            return true;
        }

        private bool IsCurrentOperation(int generation)
        {
            return !modeExitRequested &&
                   generation == operationGeneration &&
                   this != null;
        }

        private async Task LeaveStaleRoomAsync(SelfHostedRoomSnapshot room)
        {
            if (room == null) return;
            try
            {
                await EnsureRoomGateway().LeaveAsync(room.id);
            }
            catch (Exception)
            {
                // A newer mode owns the screen now. Cleanup is best effort.
            }
        }

        private int ResolveDirectPlayerCount()
        {
            if (!directSession || networkBootstrap?.NetworkManager == null)
            {
                return 0;
            }

            NetworkManager manager = networkBootstrap.NetworkManager;
            if (manager.IsServer) return manager.ConnectedClientsList.Count;
            return manager.IsConnectedClient ? 2 : 1;
        }

        private void EnsureNetworkPrerequisite(bool deferPlayerSpawn)
        {
            if (networkBootstrap != null)
            {
                networkInstaller?.ConfigurePlayerSpawnBarrier(deferPlayerSpawn);
                return;
            }
            networkBootstrap = OptionalNetworkBootstrap.CreateRuntime(
                NetworkEndpointSettings.Localhost,
                "FPS Coop Network Runtime");
            DontDestroyOnLoad(networkBootstrap.gameObject);
            networkBootstrap.ClientDisconnected +=
                HandleReconnectableClientDisconnect;
            networkBootstrap.ClientConnected +=
                HandleReconnectableClientConnected;
            if (!string.IsNullOrEmpty(pendingConnectionCredential))
                networkBootstrap.ConfigureClientCredential(
                    pendingConnectionCredential);

            GameObject authorityObject = Resources.Load<GameObject>(
                "Networking/CoopSessionAuthority");
            GameObject replicaObject = Resources.Load<GameObject>(
                "Networking/CoopPlayerReplica");
            NetworkObject authorityPrefab = authorityObject != null
                ? authorityObject.GetComponent<NetworkObject>()
                : null;
            NetworkObject replicaPrefab = replicaObject != null
                ? replicaObject.GetComponent<NetworkObject>()
                : null;
            if (authorityPrefab == null || replicaPrefab == null)
            {
                Destroy(networkBootstrap.gameObject);
                networkBootstrap = null;
                throw new InvalidOperationException(
                    "合作战局网络 Prefab 缺失，请重新运行 Issue65 资源构建器。");
            }

            CoopNetworkRuntimeInstaller installer =
                networkBootstrap.gameObject.AddComponent<
                    CoopNetworkRuntimeInstaller>();
            networkInstaller = installer;
            installer.ConfigurePrefabs(authorityPrefab, replicaPrefab);
            if (!CoopScenarioRegistry.TryCreateCityNew(
                    seed: pendingScenarioSeed,
                    maximumPlayers: MaximumPlayers,
                    out CoopScenarioConfiguration scenario,
                    out string scenarioError))
            {
                Destroy(networkBootstrap.gameObject);
                networkBootstrap = null;
                throw new InvalidOperationException(scenarioError);
            }
            installer.ConfigureScenario(
                scenario.Players,
                scenario.Targets,
                scenario.RequiredKills,
                scenario.Mission,
                scenario.Waves);
            installer.ConfigurePlayerSpawnBarrier(deferPlayerSpawn);
            networkBootstrap.gameObject.AddComponent<
                CoopNetworkWorldPresenter>();
            networkBootstrap.gameObject.AddComponent<
                CoopNetworkDropPresenter>();
            if (!installer.RegisterConfiguredPrefabs())
            {
                string failure = installer.LastFailure;
                Destroy(networkBootstrap.gameObject);
                networkBootstrap = null;
                throw new InvalidOperationException(failure);
            }
        }

        private CoopDedicatedServerGateway EnsureDedicatedServerGateway()
        {
            if (dedicatedServerGateway != null) return dedicatedServerGateway;
            if (!CoopDedicatedServerSettings.TryLoad(
                    out CoopDedicatedServerSettings settings,
                    out string error))
                throw new InvalidOperationException(error);
            dedicatedServerGateway = new CoopDedicatedServerGateway(settings);
            return dedicatedServerGateway;
        }

        private async Task EnsureDedicatedBattleTransportAsync()
        {
            if (recoveringBattleScene) return;
            if (dedicatedTransportConnected)
            {
                RaiseBattleReadyOnce();
                return;
            }
            if (dedicatedTransportOperationInProgress || activeRoom == null ||
                !string.Equals(LobbyPhase, PhaseBattle,
                    StringComparison.Ordinal)) return;
            dedicatedTransportOperationInProgress = true;
            try
            {
                Debug.Log("[COOP_DATA_PLANE][CONNECTING] 正在连接专用服务器。",
                    this);
                EnsureNetworkPrerequisite(deferPlayerSpawn: true);
                RemoteMatchConnectionInfo connection =
                    ResolveRemoteConnectionFromSession();
                if (connection == null)
                    throw new InvalidOperationException(
                        "房间没有收到专用服务器连接信息。 ");
                CoopDedicatedServerGateway gateway =
                    EnsureDedicatedServerGateway();
                string ticket = activeRemoteTicket;
                if (activeRemoteConnection == null ||
                    !string.Equals(activeRemoteConnection.matchId,
                        connection.matchId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(ticket))
                {
                    CoopDedicatedServerAllocation allocation =
                        await gateway.JoinAsync(activeRoom.id, connection);
                    connection = allocation.connection;
                    ticket = allocation.connectionTicket;
                }
                CoopCompatibilityDecision decision =
                    RemoteMatchCompatibilityValidator.Validate(
                        gateway.Compatibility, connection);
                if (!decision.Compatible)
                    throw new InvalidOperationException(decision.Message);

                activeRemoteConnection = connection;
                activeRemoteTicket = ticket;
                pendingConnectionCredential = ticket;
                var endpoint = NetworkEndpointSettings.Localhost;
                endpoint.Address = connection.host.Trim();
                endpoint.Port = connection.port;
                suppressDedicatedReconnect = false;
                networkBootstrap.Configure(endpoint);
                networkBootstrap.ConfigureClientCredential(ticket);
                if (!networkBootstrap.StartClient())
                    throw new InvalidOperationException(
                        networkBootstrap.LastFailure);

                float deadline = Time.realtimeSinceStartup + 15f;
                while (networkBootstrap.NetworkManager != null &&
                       !networkBootstrap.NetworkManager.IsConnectedClient &&
                       networkBootstrap.IsListening &&
                       Time.realtimeSinceStartup < deadline)
                    await Task.Yield();
                if (networkBootstrap.NetworkManager == null ||
                    !networkBootstrap.NetworkManager.IsConnectedClient)
                    throw new TimeoutException(
                        "连接专用服务器超时，请检查网络后重试。 ");
                dedicatedTransportConnected = true;
                LastFailure = string.Empty;
                LobbyFailureMessage = string.Empty;
                SetState(CoopSessionState.Connected);
                Debug.Log("[COOP_DATA_PLANE][CONNECTED] 专用服务器连接成功。",
                    this);
                RaiseBattleReadyOnce();
            }
            catch (Exception exception)
            {
                dedicatedTransportConnected = false;
                LastFailure = string.IsNullOrWhiteSpace(exception.Message)
                    ? "连接专用服务器失败。"
                    : exception.Message;
                LobbyFailureMessage = LastFailure;
                SetState(CoopSessionState.Failed);
                Debug.LogError("[COOP_DATA_PLANE][FAILED] " + LastFailure,
                    this);
                NotifyLobbyChanged();
            }
            finally
            {
                dedicatedTransportOperationInProgress = false;
            }
        }

        private RemoteMatchConnectionInfo ResolveRemoteConnectionFromSession()
        {
            if (activeRemoteConnection != null)
                return activeRemoteConnection;
            return activeRoom?.serverAllocation;
        }

        private async Task AttemptDedicatedReconnectAsync()
        {
            if (dedicatedTransportOperationInProgress || activeRoom == null ||
                activeRemoteConnection == null) return;
            dedicatedTransportOperationInProgress = true;
            try
            {
                float shutdownDeadline = Time.realtimeSinceStartup + 3f;
                while (networkBootstrap != null &&
                       networkBootstrap.IsListening &&
                       Time.realtimeSinceStartup < shutdownDeadline)
                    await Task.Yield();
                CoopDedicatedServerAllocation allocation =
                    await EnsureDedicatedServerGateway().JoinAsync(
                        activeRoom.id, activeRemoteConnection);
                activeRemoteConnection = allocation.connection;
                activeRemoteTicket = allocation.connectionTicket;
                if (!ReconnectWithCredential(activeRemoteTicket))
                    throw new InvalidOperationException(LastFailure);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (networkBootstrap.NetworkManager != null &&
                       !networkBootstrap.NetworkManager.IsConnectedClient &&
                       networkBootstrap.IsListening &&
                       Time.realtimeSinceStartup < deadline)
                    await Task.Yield();
                if (networkBootstrap.NetworkManager == null ||
                    !networkBootstrap.NetworkManager.IsConnectedClient)
                    throw new TimeoutException("专用服务器重连超时。 ");
                dedicatedTransportConnected = true;
                LastFailure = string.Empty;
                SetState(CoopSessionState.Connected);
            }
            catch (Exception exception)
            {
                dedicatedTransportConnected = false;
                LastFailure = string.IsNullOrWhiteSpace(exception.Message)
                    ? "专用服务器重连失败。"
                    : exception.Message;
                SetState(CoopSessionState.Failed);
            }
            finally
            {
                dedicatedTransportOperationInProgress = false;
            }
        }

        private void ShutdownDedicatedBattleTransport()
        {
            suppressDedicatedReconnect = true;
            dedicatedTransportConnected = false;
            activeRemoteConnection = null;
            activeRemoteTicket = string.Empty;
            pendingConnectionCredential = string.Empty;
            networkBootstrap?.Shutdown();
        }

        private async Task ReleaseCancelledDedicatedServerAsync()
        {
            if (activeRoom == null || !IsHost ||
                activeRemoteConnection == null) return;
            RemoteMatchConnectionInfo cancelled = activeRemoteConnection;
            try
            {
                await EnsureDedicatedServerGateway().ReleaseAsync(
                    activeRoom.id, cancelled);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "取消战局后释放专用服务器失败，将由空闲租约回收：" +
                    exception.Message, this);
            }
            finally
            {
                ShutdownDedicatedBattleTransport();
            }
        }

        private async Task ReleaseOwnedDedicatedServerAsync(
            SelfHostedRoomSnapshot room,
            RemoteMatchConnectionInfo connection)
        {
            if (room == null || connection == null ||
                !string.Equals(room.hostId, LocalPlayerId,
                    StringComparison.Ordinal)) return;
            try
            {
                await EnsureDedicatedServerGateway().ReleaseAsync(
                    room.id, connection);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "释放专用服务器战局失败，将由空闲租约回收：" +
                    exception.Message, this);
            }
        }

        private void HandleReconnectableClientDisconnect(ulong clientId)
        {
            if (modeExitRequested || networkBootstrap?.NetworkManager == null ||
                networkBootstrap.NetworkManager.IsServer ||
                clientId != networkBootstrap.NetworkManager.LocalClientId ||
                State == CoopSessionState.Leaving ||
                State == CoopSessionState.Offline)
                return;
            if (suppressDedicatedReconnect) return;
            SetState(CoopSessionState.Reconnecting);
            _ = AttemptDedicatedReconnectAsync();
        }

        private void HandleReconnectableClientConnected(ulong clientId)
        {
            if (networkBootstrap?.NetworkManager == null ||
                clientId != networkBootstrap.NetworkManager.LocalClientId ||
                State != CoopSessionState.Reconnecting)
                return;
            LastFailure = string.Empty;
            SetState(CoopSessionState.Connected);
        }

        private static int CreateRunSeed()
        {
            int value = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) &
                        int.MaxValue;
            return value == 0 ? 18018 : value;
        }

        private static void EnsureSignedIn()
        {
            if (string.IsNullOrWhiteSpace(
                    SelfHostedAuthenticationGateway.AccessToken) ||
                string.IsNullOrWhiteSpace(
                    SelfHostedAuthenticationGateway.AccountId))
                throw new InvalidOperationException(
                    "请先在多人合作登录页完成账号登录。 ");
        }

        private SelfHostedRoomGateway EnsureRoomGateway()
        {
            if (roomGateway != null) return roomGateway;
            if (!CoopDedicatedServerSettings.TryLoad(
                    out CoopDedicatedServerSettings settings,
                    out string error))
                throw new InvalidOperationException(error);
            roomGateway = new SelfHostedRoomGateway(settings);
            return roomGateway;
        }

        private void BindRoom(SelfHostedRoomSnapshot room)
        {
            if (room == null) throw new InvalidOperationException(
                "房间服务没有返回有效战局。 ");
            lastObservedPhase = PhaseLobby;
            recoveringBattleScene = string.Equals(room.phase, PhaseBattle,
                StringComparison.Ordinal);
            ApplyRoom(room, force: true);
            nextRoomPollAt = Time.unscaledTime + 0.75f;
        }

        private async Task PollRoomAsync(string roomId, int generation)
        {
            roomPollInProgress = true;
            try
            {
                SelfHostedRoomSnapshot latest = await EnsureRoomGateway()
                    .GetAsync(roomId);
                if (!IsCurrentOperation(generation) || activeRoom == null ||
                    !string.Equals(activeRoom.id, roomId,
                        StringComparison.Ordinal)) return;
                consecutiveRoomPollFailures = 0;
                if (string.Equals(LobbyFailureMessage,
                        "房间同步暂时中断，正在重试。",
                        StringComparison.Ordinal))
                {
                    LobbyFailureMessage = string.Empty;
                    NotifyLobbyChanged();
                }
                if (!latest.HasPlayer(LocalPlayerId))
                {
                    HandleRoomEnded();
                    return;
                }
                ApplyRoom(latest);
            }
            catch (Exception exception)
            {
                if (IsCurrentOperation(generation) && activeRoom != null &&
                    string.Equals(activeRoom.id, roomId,
                        StringComparison.Ordinal))
                {
                    consecutiveRoomPollFailures++;
                    nextRoomPollAt = Time.unscaledTime +
                        Mathf.Min(5f, consecutiveRoomPollFailures);
                    if (consecutiveRoomPollFailures >= 3 &&
                        string.IsNullOrWhiteSpace(LobbyFailureMessage))
                    {
                        LobbyFailureMessage =
                            "房间同步暂时中断，正在重试。";
                        NotifyLobbyChanged();
                    }
                    Debug.LogWarning(
                        $"合作房间同步失败：{exception.Message}", this);
                }
            }
            finally
            {
                roomPollInProgress = false;
            }
        }

        private void ApplyRoom(SelfHostedRoomSnapshot room,
            bool force = false)
        {
            if (room == null || modeExitRequested ||
                !force && (activeRoom == null ||
                    !string.Equals(activeRoom.id, room.id,
                        StringComparison.Ordinal))) return;
            if (!force && activeRoom != null &&
                string.Equals(activeRoom.id, room.id,
                    StringComparison.Ordinal) &&
                room.revision <= activeRoom.revision)
                return;
            string previousPhase = activeRoom == null
                ? lastObservedPhase : LobbyPhase;
            activeRoom = room;
            RefreshLobbyFromRoom();
            string currentPhase = LobbyPhase;
            lastObservedPhase = currentPhase;
            if (HasLoadingPhase() || recoveringBattleScene &&
                string.Equals(currentPhase, PhaseBattle,
                    StringComparison.Ordinal))
                RaiseLobbyStartOnce();
            _ = EvaluateHostSceneLoadBarrierAsync();
            if (!recoveringBattleScene &&
                string.Equals(currentPhase, PhaseBattle,
                    StringComparison.Ordinal))
                _ = EnsureDedicatedBattleTransportAsync();
            if (string.Equals(currentPhase, PhaseCancelled,
                    StringComparison.Ordinal))
                RaiseSceneLoadCancelledOnce();
            if (string.Equals(currentPhase, PhaseLobby,
                    StringComparison.Ordinal) &&
                !string.Equals(previousPhase, PhaseLobby,
                    StringComparison.Ordinal))
            {
                recoveringBattleScene = false;
                ShutdownDedicatedBattleTransport();
                _ = ClearLocalReadyAfterMatchAsync();
                LastFailure = string.Empty;
                SetState(CoopSessionState.Connected);
                ReturnedToLobby?.Invoke();
            }
            StateChanged?.Invoke(State);
        }

        private async Task ClearLocalReadyAfterMatchAsync()
        {
            if (activeRoom == null) return;
            string roomId = activeRoom.id;
            try
            {
                SelfHostedRoomSnapshot changed = await EnsureRoomGateway()
                    .SetReadyAsync(roomId, false);
                if (activeRoom != null && string.Equals(activeRoom.id,
                        roomId, StringComparison.Ordinal))
                    ApplyRoom(changed);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"返回合作房间时清理准备状态失败：{exception.Message}",
                    this);
            }
        }

        private void HandleRoomEnded()
        {
            ShutdownDedicatedBattleTransport();
            activeRoom = null;
            recoveringBattleScene = false;
            ResetLobbyState();
            SetState(CoopSessionState.Offline);
        }

        private bool CanMutateLobby()
        {
            if (lobbyOperationInProgress)
            {
                LobbyFailureMessage = "房间操作正在进行，请稍候。";
                NotifyLobbyChanged();
                return false;
            }
            if (activeRoom == null || !IsConnected)
            {
                LobbyFailureMessage = "请先创建或加入合作房间。";
                NotifyLobbyChanged();
                return false;
            }
            return true;
        }

        private void RefreshLobbyFromRoom()
        {
            if (activeRoom == null)
            {
                lobbyRoster.Reset(Array.Empty<CoopLobbyMemberSnapshot>(), false);
                NotifyLobbyChanged();
                return;
            }

            if (!string.IsNullOrWhiteSpace(activeRoom.mapId))
                lobbyMapId = activeRoom.mapId;
            string phase = LobbyPhase;
            string loadEpoch = SceneLoadEpoch;
            if (string.Equals(phase, PhaseLoading, StringComparison.Ordinal) &&
                !string.Equals(observedLoadEpoch, loadEpoch,
                    StringComparison.Ordinal))
            {
                observedLoadEpoch = loadEpoch;
                sceneLoadFailure = string.Empty;
                battleReadyEventRaised = false;
                sceneCancellationEventRaised = false;
            }
            else if (string.Equals(phase, PhaseCancelled,
                         StringComparison.Ordinal))
            {
                sceneLoadFailure = string.IsNullOrWhiteSpace(
                    activeRoom.loadFailure)
                    ? "服务器取消了场景加载。"
                    : activeRoom.loadFailure;
            }
            bool starting = string.Equals(phase, PhaseStarting,
                StringComparison.Ordinal) ||
                string.Equals(phase, PhaseLoading,
                    StringComparison.Ordinal) ||
                string.Equals(phase, PhaseBattle,
                    StringComparison.Ordinal);
            if (!starting) lobbyStartEventRaised = false;
            var members = new List<CoopLobbyMemberSnapshot>();
            foreach (SelfHostedRoomPlayer player in activeRoom.players ??
                         Array.Empty<SelfHostedRoomPlayer>())
            {
                if (player == null) continue;
                string appearance = player.appearanceId ?? string.Empty;
                members.Add(new CoopLobbyMemberSnapshot(player.accountId,
                    string.Equals(player.accountId, activeRoom.hostId,
                        StringComparison.Ordinal), appearance, player.ready,
                    player.connected
                        ? CoopLobbyConnectionState.Connected
                        : CoopLobbyConnectionState.Disconnected));
                if (string.Equals(player.accountId, LocalPlayerId,
                        StringComparison.Ordinal) &&
                    lobbyRoster.IsAppearanceAllowed(appearance))
                    pendingAppearanceId = appearance;
            }
            lobbyRoster.Reset(members, starting);
            NotifyLobbyChanged();
        }

        private bool HasLoadingPhase() => activeRoom != null &&
            string.Equals(LobbyPhase, PhaseLoading,
                StringComparison.Ordinal);

        private void EnsureHostSceneLoadBarrier()
        {
            if (activeRoom == null || !IsHost ||
                string.Equals(sceneLoadBarrier.Epoch, SceneLoadEpoch,
                    StringComparison.Ordinal) &&
                sceneLoadBarrier.State != CoopSceneLoadState.Idle) return;
            if (!string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(SceneLoadEpoch)) return;
            sceneLoadBarrier.Begin(SceneLoadEpoch, LobbyMapId, SceneSeed,
                (activeRoom.players ?? Array.Empty<SelfHostedRoomPlayer>())
                    .Where(value => value != null)
                    .Select(value => value.accountId),
                Time.realtimeSinceStartupAsDouble, 30d);
        }

        private async Task EvaluateHostSceneLoadBarrierAsync()
        {
            if (activeRoom == null || !IsHost ||
                sceneBarrierEvaluationInProgress ||
                sceneCancellationInProgress ||
                !string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal)) return;
            sceneBarrierEvaluationInProgress = true;
            try
            {
                EnsureHostSceneLoadBarrier();
                if (activeRoom == null ||
                    (activeRoom.players?.Length ?? 0) !=
                    sceneLoadBarrier.ExpectedCount)
                {
                    await CancelSceneLoadAsync(
                        "成员在加载期间离开，服务器已取消本次战局。");
                    return;
                }
                foreach (SelfHostedRoomPlayer player in activeRoom.players ??
                             Array.Empty<SelfHostedRoomPlayer>())
                {
                    if (player == null) continue;
                    string readyEpoch = player.readyEpoch;
                    if (!string.Equals(readyEpoch, SceneLoadEpoch,
                            StringComparison.Ordinal)) continue;
                    sceneLoadBarrier.ReportReady(player.accountId, readyEpoch);
                }
                if (sceneLoadBarrier.State != CoopSceneLoadState.Ready ||
                    activeRoom == null || !IsHost ||
                    !string.Equals(LobbyPhase, PhaseLoading,
                        StringComparison.Ordinal)) return;
                SelfHostedRoomSnapshot battle = await EnsureRoomGateway()
                    .SetPhaseAsync(activeRoom.id, PhaseBattle);
                ApplyRoom(battle);
            }
            catch (Exception exception)
            {
                FailSceneLoad(exception.Message);
            }
            finally
            {
                sceneBarrierEvaluationInProgress = false;
            }
        }

        private bool FailSceneLoad(string message)
        {
            sceneLoadFailure = string.IsNullOrWhiteSpace(message)
                ? "场景加载失败。"
                : message.Trim();
            NotifyLobbyChanged();
            return false;
        }

        private bool FailLobby(CoopLobbyFailure failure)
        {
            LobbyFailureMessage = CoopLobbyMessages.Describe(failure);
            NotifyLobbyChanged();
            return false;
        }

        private bool FailLobby(Exception exception)
        {
            LobbyFailureMessage = string.IsNullOrWhiteSpace(exception?.Message)
                ? "房间操作失败，请稍后重试。"
                : exception.Message;
            NotifyLobbyChanged();
            return false;
        }

        private void ResetLobbyState()
        {
            lobbyRoster.Reset(Array.Empty<CoopLobbyMemberSnapshot>(), false);
            LobbyFailureMessage = string.Empty;
            lobbyStartEventRaised = false;
            sceneLoadFailure = string.Empty;
            observedLoadEpoch = string.Empty;
            sceneBarrierEvaluationInProgress = false;
            sceneCancellationInProgress = false;
            battleReadyEventRaised = false;
            sceneCancellationEventRaised = false;
            activeRemoteConnection = null;
            activeRemoteTicket = string.Empty;
            dedicatedTransportOperationInProgress = false;
            dedicatedTransportConnected = false;
            suppressDedicatedReconnect = false;
            recoveringBattleScene = false;
            NotifyLobbyChanged();
        }

        private void RaiseBattleReadyOnce()
        {
            if (battleReadyEventRaised) return;
            battleReadyEventRaised = true;
            BattleSceneReady?.Invoke();
        }

        private void RaiseSceneLoadCancelledOnce()
        {
            if (sceneCancellationEventRaised) return;
            sceneCancellationEventRaised = true;
            SceneLoadCancelled?.Invoke(sceneLoadFailure);
        }

        private void RaiseLobbyStartOnce()
        {
            if (lobbyStartEventRaised) return;
            lobbyStartEventRaised = true;
            LobbyStartRequested?.Invoke();
        }

        private void NotifyLobbyChanged()
        {
            LobbyChanged?.Invoke();
        }

        private void NotifyPublicRoomsChanged()
        {
            PublicRoomsChanged?.Invoke();
        }

        private static string NormalizeRoomName(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(normalized))
                return "公开合作小队";
            return normalized.Length <= 48
                ? normalized
                : normalized.Substring(0, 48);
        }

        private static string Property(
            IReadOnlyDictionary<string, SessionProperty> properties,
            string key, string fallback)
        {
            return properties != null && properties.TryGetValue(key,
                       out SessionProperty property) && property != null &&
                   !string.IsNullOrWhiteSpace(property.Value)
                ? property.Value
                : fallback;
        }

        private void Fail(Exception exception)
        {
            LastFailure = string.IsNullOrWhiteSpace(exception?.Message)
                ? "合作战局操作失败。"
                : exception.Message;
            SetState(CoopSessionState.Failed);
        }

        private void SetState(CoopSessionState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
