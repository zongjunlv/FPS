using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
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
        Leaving,
        Failed
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

        private ISession activeSession;
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
        private bool lobbyEnforcementInProgress;
        private bool lobbyStartEventRaised;

        public event Action<CoopSessionState> StateChanged;
        public event Action LobbyChanged;
        public event Action LobbyStartRequested;

        public CoopSessionState State { get; private set; } =
            CoopSessionState.Offline;
        public string JoinCode => activeSession?.Code ?? string.Empty;
        public int PlayerCount => activeSession?.PlayerCount ??
            ResolveDirectPlayerCount();
        public bool IsHost => activeSession?.IsHost ??
            (directSession && networkBootstrap != null &&
                networkBootstrap.NetworkManager.IsHost);
        public bool IsConnected => (activeSession != null || directSession) &&
            State == CoopSessionState.Connected;
        public string LastFailure { get; private set; } = string.Empty;
        public ISession ActiveSession => activeSession;
        public IReadOnlyList<CoopLobbyMemberSnapshot> LobbyMembers =>
            lobbyRoster.Members;
        public string LobbyMapId => lobbyMapId;
        public string LocalPlayerId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(activeSession?.CurrentPlayer?.Id))
                    return activeSession.CurrentPlayer.Id;
                return UnityServices.State ==
                           ServicesInitializationState.Initialized &&
                       AuthenticationService.Instance.IsSignedIn
                    ? AuthenticationService.Instance.PlayerId
                    : string.Empty;
            }
        }
        public string PendingAppearanceId => pendingAppearanceId;
        public bool IsLobbyBusy => lobbyOperationInProgress;
        public bool IsLobbyStarting => lobbyRoster.IsStarting;
        public bool IsLocalReady => lobbyRoster.TryGet(LocalPlayerId,
            out CoopLobbyMemberSnapshot member) && member.IsReady;
        public bool CanHostStart => activeSession != null && IsHost &&
            lobbyRoster.CanStart(LocalPlayerId, out _);
        public string LobbyFailureMessage { get; private set; } = string.Empty;

        public void ConfigureLobby(IEnumerable<string> allowedAppearanceIds,
            string defaultAppearanceId, string mapId = DefaultMapId)
        {
            if (activeSession != null)
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

        private void OnDestroy()
        {
            modeExitRequested = true;
            operationGeneration++;
            UnbindSession();
            activeSession = null;
            directSession = false;
            networkBootstrap?.Shutdown();
        }

        public async Task<bool> HostAsync(string profile = null)
        {
            if (!CanBegin()) return false;
            modeExitRequested = false;
            int generation = ++operationGeneration;
            operationInProgress = true;
            SetState(CoopSessionState.Initializing);

            try
            {
                EnsureNetworkPrerequisite();
                await EnsureServicesAsync(profile);
                if (!IsCurrentOperation(generation)) return false;
                SetState(CoopSessionState.Hosting);
                SessionOptions options = new SessionOptions
                {
                    Name = "FPS 双人合作切片",
                    MaxPlayers = MaximumPlayers,
                    IsPrivate = true,
                    PlayerProperties = BuildPlayerProperties(
                        pendingAppearanceId, false),
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        [MapProperty] = new SessionProperty(lobbyMapId),
                        [PhaseProperty] = new SessionProperty(PhaseLobby)
                    }
                }.WithRelayNetwork();
                ISession created = await MultiplayerService.Instance
                    .CreateSessionAsync(options);
                if (!IsCurrentOperation(generation))
                {
                    await LeaveStaleSessionAsync(created);
                    return false;
                }

                BindSession(created);
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
                EnsureNetworkPrerequisite();
                await EnsureServicesAsync(profile);
                if (!IsCurrentOperation(generation)) return false;
                SetState(CoopSessionState.Joining);
                var joinOptions = new JoinSessionOptions
                {
                    PlayerProperties = BuildPlayerProperties(
                        pendingAppearanceId, false)
                };
                ISession joined = await MultiplayerService.Instance
                    .JoinSessionByCodeAsync(normalized, joinOptions);
                if (!IsCurrentOperation(generation))
                {
                    await LeaveStaleSessionAsync(joined);
                    return false;
                }

                BindSession(joined);
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

        public async Task ShutdownForModeExitAsync()
        {
            modeExitRequested = true;
            operationGeneration++;
            operationInProgress = false;
            directSession = false;

            ISession leaving = activeSession;
            UnbindSession();
            activeSession = null;

            if (leaving != null)
            {
                try
                {
                    await leaving.LeaveAsync();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"合作战局退出时清理失败：{exception.Message}",
                        this);
                }
            }

            networkBootstrap?.Shutdown();
            LastFailure = string.Empty;
            ResetLobbyState();
            SetState(CoopSessionState.Offline);
        }

        public async Task LeaveAsync()
        {
            if (operationInProgress ||
                (activeSession == null && !directSession)) return;
            operationInProgress = true;
            SetState(CoopSessionState.Leaving);

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

            ISession leaving = activeSession;
            UnbindSession();
            activeSession = null;

            try
            {
                // The session SDK owns transport shutdown. Calling NGO.Shutdown
                // independently here can race its cleanup path.
                await leaving.LeaveAsync();
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
                EnsureNetworkPrerequisite();
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
                activeSession.CurrentPlayer.SetProperty(AppearanceProperty,
                    new PlayerProperty(appearanceId));
                activeSession.CurrentPlayer.SetProperty(ReadyProperty,
                    new PlayerProperty("0"));
                await activeSession.SaveCurrentPlayerDataAsync();
                pendingAppearanceId = appearanceId;
                RefreshLobbyFromSession();
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
                activeSession.CurrentPlayer.SetProperty(ReadyProperty,
                    new PlayerProperty(ready ? "1" : "0"));
                await activeSession.SaveCurrentPlayerDataAsync();
                RefreshLobbyFromSession();
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
            RefreshLobbyFromSession();
            if (!lobbyRoster.TryStart(LocalPlayerId,
                    out CoopLobbyFailure failure))
                return FailLobby(failure);
            lobbyOperationInProgress = true;
            try
            {
                IHostSession host = activeSession.AsHost();
                host.IsLocked = true;
                host.SetProperty(PhaseProperty,
                    new SessionProperty(PhaseStarting));
                await host.SavePropertiesAsync();
                LobbyFailureMessage = string.Empty;
                NotifyLobbyChanged();
                RaiseLobbyStartOnce();
                return true;
            }
            catch (Exception exception)
            {
                RefreshLobbyFromSession();
                return FailLobby(exception);
            }
            finally
            {
                lobbyOperationInProgress = false;
                NotifyLobbyChanged();
            }
        }

        private bool CanBegin()
        {
            if (operationInProgress || activeSession != null || directSession)
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

        private static async Task LeaveStaleSessionAsync(ISession session)
        {
            if (session == null) return;
            try
            {
                await session.LeaveAsync();
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

        private void EnsureNetworkPrerequisite()
        {
            if (networkBootstrap != null) return;
            networkBootstrap = OptionalNetworkBootstrap.CreateRuntime(
                NetworkEndpointSettings.Localhost,
                "FPS Coop Network Runtime");
            DontDestroyOnLoad(networkBootstrap.gameObject);
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
            installer.ConfigurePrefabs(authorityPrefab, replicaPrefab);
            installer.ConfigureScenario(
                BuildPlayerSpawns(),
                BuildTargetSpawns(),
                1);
            networkBootstrap.gameObject.AddComponent<
                CoopNetworkWorldPresenter>();
            if (!installer.RegisterConfiguredPrefabs())
            {
                string failure = installer.LastFailure;
                Destroy(networkBootstrap.gameObject);
                networkBootstrap = null;
                throw new InvalidOperationException(failure);
            }
        }

        private static CoopPlayerSpawnDefinition[] BuildPlayerSpawns()
        {
            Vector3 origin = ResolveArenaOrigin(out Vector3 forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return new[]
            {
                new CoopPlayerSpawnDefinition
                {
                    PlayerId = 1,
                    Position = origin - right * 1.25f,
                    Health = 100f
                },
                new CoopPlayerSpawnDefinition
                {
                    PlayerId = 2,
                    Position = origin + right * 1.25f,
                    Health = 100f
                }
            };
        }

        private static CoopTargetSpawnDefinition[] BuildTargetSpawns()
        {
            Vector3 origin = ResolveArenaOrigin(out Vector3 forward);
            return new[]
            {
                new CoopTargetSpawnDefinition
                {
                    TargetId = 1,
                    Position = origin + forward * 15f,
                    Radius = 1.25f,
                    Health = 68f,
                    DropDefinitionId = "medkit"
                }
            };
        }

        private static Vector3 ResolveArenaOrigin(out Vector3 forward)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                forward = Vector3.forward;
                return Vector3.zero;
            }

            forward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            return camera.transform.position;
        }

        private static async Task EnsureServicesAsync(string profile)
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                string normalizedProfile = NormalizeProfile(profile);
                if (!string.IsNullOrEmpty(normalizedProfile))
                {
                    options.SetProfile(normalizedProfile);
                }

                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                throw new InvalidOperationException(
                    "请先在多人合作登录页完成账号登录。匿名身份仅用于显式开发调试。");
        }

        private static string NormalizeProfile(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile)) return string.Empty;
            string trimmed = profile.Trim();
            return trimmed.Length <= 30 ? trimmed : trimmed.Substring(0, 30);
        }

        private void BindSession(ISession session)
        {
            UnbindSession();
            activeSession = session ?? throw new InvalidOperationException(
                "会话服务没有返回有效战局。 ");
            activeSession.Changed += HandleSessionChanged;
            activeSession.PlayerPropertiesChanged += HandleSessionChanged;
            activeSession.SessionPropertiesChanged += HandleSessionChanged;
            activeSession.PlayerJoined += HandlePlayerMembershipChanged;
            activeSession.PlayerHasLeft += HandlePlayerMembershipChanged;
            activeSession.Deleted += HandleSessionEnded;
            activeSession.RemovedFromSession += HandleSessionEnded;
            RefreshLobbyFromSession();
            _ = EnforceHostLobbyRulesAsync();
        }

        private void UnbindSession()
        {
            if (activeSession == null) return;
            activeSession.Changed -= HandleSessionChanged;
            activeSession.PlayerPropertiesChanged -= HandleSessionChanged;
            activeSession.SessionPropertiesChanged -= HandleSessionChanged;
            activeSession.PlayerJoined -= HandlePlayerMembershipChanged;
            activeSession.PlayerHasLeft -= HandlePlayerMembershipChanged;
            activeSession.Deleted -= HandleSessionEnded;
            activeSession.RemovedFromSession -= HandleSessionEnded;
        }

        private void HandleSessionChanged()
        {
            RefreshLobbyFromSession();
            _ = EnforceHostLobbyRulesAsync();
            if (HasStartingPhase()) RaiseLobbyStartOnce();
            StateChanged?.Invoke(State);
        }

        private void HandlePlayerMembershipChanged(string _)
        {
            HandleSessionChanged();
        }

        private void HandleSessionEnded()
        {
            UnbindSession();
            activeSession = null;
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
            if (activeSession == null || !IsConnected)
            {
                LobbyFailureMessage = "请先创建或加入合作房间。";
                NotifyLobbyChanged();
                return false;
            }
            return true;
        }

        private void RefreshLobbyFromSession()
        {
            if (activeSession == null)
            {
                lobbyRoster.Reset(Array.Empty<CoopLobbyMemberSnapshot>(), false);
                NotifyLobbyChanged();
                return;
            }

            lobbyMapId = Property(activeSession.Properties, MapProperty,
                lobbyMapId);
            bool starting = string.Equals(Property(activeSession.Properties,
                    PhaseProperty, PhaseLobby), PhaseStarting,
                StringComparison.Ordinal);
            if (!starting) lobbyStartEventRaised = false;
            var members = new List<CoopLobbyMemberSnapshot>();
            for (int index = 0; index < activeSession.Players.Count; index++)
            {
                IReadOnlyPlayer player = activeSession.Players[index];
                string appearance = Property(player.Properties,
                    AppearanceProperty, string.Empty);
                bool ready = Property(player.Properties, ReadyProperty, "0") ==
                             "1";
                CoopLobbyConnectionState connection = ParseConnection(
                    Property(player.Properties, ConnectionProperty,
                        "connected"));
                members.Add(new CoopLobbyMemberSnapshot(player.Id,
                    string.Equals(player.Id, activeSession.Host,
                        StringComparison.Ordinal), appearance, ready, connection));
                if (string.Equals(player.Id, LocalPlayerId,
                        StringComparison.Ordinal) &&
                    lobbyRoster.IsAppearanceAllowed(appearance))
                    pendingAppearanceId = appearance;
            }
            lobbyRoster.Reset(members, starting);
            NotifyLobbyChanged();
        }

        private async Task EnforceHostLobbyRulesAsync()
        {
            if (lobbyEnforcementInProgress || activeSession == null ||
                !activeSession.IsHost) return;
            lobbyEnforcementInProgress = true;
            try
            {
                IHostSession host = activeSession.AsHost();
                IReadOnlyList<IReadOnlyPlayer> players = activeSession.Players;
                for (int index = players.Count - 1; index >= 0; index--)
                {
                    IReadOnlyPlayer player = players[index];
                    string appearance = Property(player.Properties,
                        AppearanceProperty, string.Empty);
                    if (lobbyRoster.IsAppearanceAllowed(appearance)) continue;
                    if (string.Equals(player.Id, activeSession.Host,
                            StringComparison.Ordinal)) continue;
                    await host.RemovePlayerAsync(player.Id);
                }
            }
            catch (Exception exception)
            {
                LobbyFailureMessage = string.IsNullOrWhiteSpace(exception.Message)
                    ? "房间成员校验失败。"
                    : exception.Message;
                NotifyLobbyChanged();
            }
            finally
            {
                lobbyEnforcementInProgress = false;
            }
        }

        private bool HasStartingPhase() => activeSession != null &&
            string.Equals(Property(activeSession.Properties, PhaseProperty,
                PhaseLobby), PhaseStarting, StringComparison.Ordinal);

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
            NotifyLobbyChanged();
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

        private static Dictionary<string, PlayerProperty> BuildPlayerProperties(
            string appearanceId, bool ready)
        {
            return new Dictionary<string, PlayerProperty>
            {
                [AppearanceProperty] = new PlayerProperty(appearanceId),
                [ReadyProperty] = new PlayerProperty(ready ? "1" : "0"),
                [ConnectionProperty] = new PlayerProperty("connected")
            };
        }

        private static string Property(
            IReadOnlyDictionary<string, PlayerProperty> properties,
            string key, string fallback)
        {
            return properties != null && properties.TryGetValue(key,
                       out PlayerProperty property) && property != null &&
                   !string.IsNullOrWhiteSpace(property.Value)
                ? property.Value
                : fallback;
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

        private static CoopLobbyConnectionState ParseConnection(string value)
        {
            return value switch
            {
                "reconnecting" => CoopLobbyConnectionState.Reconnecting,
                "disconnected" => CoopLobbyConnectionState.Disconnected,
                _ => CoopLobbyConnectionState.Connected
            };
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
