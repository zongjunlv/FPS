using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FPS.Networking.Domain;
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
        public const string PhaseLoading = "loading";
        public const string PhaseBattle = "battle";
        public const string PhaseCancelled = "cancelled";
        public const string SeedProperty = "seed";
        public const string LoadEpochProperty = "load_epoch";
        public const string ReadyEpochProperty = "ready_epoch";
        public const string LoadFailureProperty = "load_failure";

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

        public event Action<CoopSessionState> StateChanged;
        public event Action LobbyChanged;
        public event Action LobbyStartRequested;
        public event Action BattleSceneReady;
        public event Action<string> SceneLoadCancelled;
        public event Action ReturnedToLobby;

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
        public bool HasActiveSession => activeSession != null;
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
        public string SceneLoadEpoch => activeSession == null
            ? string.Empty
            : Property(activeSession.Properties, LoadEpochProperty, string.Empty);
        public int SceneSeed => activeSession != null && int.TryParse(
            Property(activeSession.Properties, SeedProperty, "0"), out int seed)
            ? seed
            : 0;
        public string LobbyPhase => activeSession == null
            ? PhaseLobby
            : Property(activeSession.Properties, PhaseProperty, PhaseLobby);
        public string SceneLoadFailure => sceneLoadFailure;

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
                EnsureNetworkPrerequisite(deferPlayerSpawn: true);
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
                EnsureNetworkPrerequisite(deferPlayerSpawn: true);
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

        public async Task<bool> ReturnToLobbyAfterMatchAsync()
        {
            if (returnToLobbyInProgress) return false;
            if (directSession)
            {
                await LeaveAsync();
                ReturnedToLobby?.Invoke();
                return true;
            }
            if (activeSession == null || !IsConnected || !IsHost)
                return false;
            returnToLobbyInProgress = true;
            try
            {
                IHostSession host = activeSession.AsHost();
                host.IsLocked = false;
                host.SetProperty(PhaseProperty,
                    new SessionProperty(PhaseLobby));
                host.SetProperty(LoadEpochProperty,
                    new SessionProperty(string.Empty));
                host.SetProperty(LoadFailureProperty,
                    new SessionProperty(string.Empty));
                activeSession.CurrentPlayer.SetProperty(ReadyProperty,
                    new PlayerProperty("0"));
                activeSession.CurrentPlayer.SetProperty(ReadyEpochProperty,
                    new PlayerProperty(string.Empty));
                await activeSession.SaveCurrentPlayerDataAsync();
                await host.SavePropertiesAsync();
                lastObservedPhase = PhaseLobby;
                lobbyStartEventRaised = false;
                battleReadyEventRaised = false;
                sceneCancellationEventRaised = false;
                observedLoadEpoch = string.Empty;
                RefreshLobbyFromSession();
                ReturnedToLobby?.Invoke();
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
                string epoch = Guid.NewGuid().ToString("N");
                int seed = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) &
                           int.MaxValue;
                host.SetProperty(MapProperty,
                    new SessionProperty(DefaultMapId));
                host.SetProperty(SeedProperty,
                    new SessionProperty(seed.ToString()));
                host.SetProperty(LoadEpochProperty,
                    new SessionProperty(epoch));
                host.SetProperty(LoadFailureProperty,
                    new SessionProperty(string.Empty));
                host.SetProperty(PhaseProperty,
                    new SessionProperty(PhaseLoading));
                observedLoadEpoch = epoch;
                sceneLoadFailure = string.Empty;
                battleReadyEventRaised = false;
                sceneCancellationEventRaised = false;
                sceneLoadBarrier.Begin(epoch, DefaultMapId, seed,
                    activeSession.Players.Select(value => value.Id),
                    Time.realtimeSinceStartupAsDouble, 30d);
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

        public async Task<bool> ReportSceneReadyAsync(string epoch)
        {
            if (!CanMutateLobby()) return false;
            if (!string.Equals(SceneLoadEpoch, epoch, StringComparison.Ordinal) ||
                !string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal))
                return FailSceneLoad("场景就绪回报已过期，服务器已忽略。");
            try
            {
                activeSession.CurrentPlayer.SetProperty(ReadyEpochProperty,
                    new PlayerProperty(epoch));
                await activeSession.SaveCurrentPlayerDataAsync();
                RefreshLobbyFromSession();
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
            if (activeSession == null || !activeSession.IsHost) return false;
            if (string.Equals(LobbyPhase, PhaseCancelled,
                    StringComparison.Ordinal)) return true;
            if (sceneCancellationInProgress) return false;
            sceneCancellationInProgress = true;
            sceneLoadBarrier.Cancel();
            sceneLoadFailure = string.IsNullOrWhiteSpace(reason)
                ? "服务器取消了场景加载。"
                : reason.Trim();
            try
            {
                IHostSession host = activeSession.AsHost();
                host.IsLocked = false;
                host.SetProperty(LoadFailureProperty,
                    new SessionProperty(sceneLoadFailure));
                host.SetProperty(PhaseProperty,
                    new SessionProperty(PhaseCancelled));
                await host.SavePropertiesAsync();
                RaiseSceneLoadCancelledOnce();
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
            if (activeSession == null || !activeSession.IsHost ||
                !string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal)) return;
            EnsureHostSceneLoadBarrier();
            if (sceneLoadBarrier.Tick(nowSeconds))
                _ = CancelSceneLoadAsync("等待成员加载 CityNew 超时，战局已取消。");
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
            CoopTargetSpawnDefinition[] targetDefinitions = deferPlayerSpawn
                ? BuildCityNewTargetSpawns()
                : BuildTargetSpawns();
            installer.ConfigureScenario(
                deferPlayerSpawn
                    ? BuildCityNewPlayerSpawns()
                    : BuildPlayerSpawns(),
                targetDefinitions,
                deferPlayerSpawn ? targetDefinitions.Length : 1,
                deferPlayerSpawn
                    ? new AuthoritativeMissionDefinition(
                        new NetVector3(51.059917d, 0.766349d, 72.16028d),
                        new NetVector3(48.414d, 0.05d, 41.41d),
                        terminalRadius: 3d,
                        extractionRadius: 5d,
                        reviveRadius: 2.5d,
                        terminalHoldTicks: 150,
                        extractionHoldTicks: 120,
                        reviveHoldTicks: 180,
                        revivedHealth: 40d)
                    : AuthoritativeMissionDefinition.Default);
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

        private static CoopPlayerSpawnDefinition[] BuildCityNewPlayerSpawns()
        {
            Vector3 origin = new(49.761f, 0.16f, 59.719f);
            return new[]
            {
                new CoopPlayerSpawnDefinition
                {
                    PlayerId = 1,
                    Position = origin + Vector3.left * 1.25f,
                    Health = 100f
                },
                new CoopPlayerSpawnDefinition
                {
                    PlayerId = 2,
                    Position = origin + Vector3.right * 1.25f,
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
                    DropDefinitionId = "medical_kit",
                    HeadOffset = new Vector3(0f, 0.85f, 0f),
                    HeadRadius = 0.32f,
                    Role = AuthoritativeEnemyRole.Assault,
                    AttackRange = 1.8f,
                    AttackIntervalTicks = 60,
                    RewardExperience = 40
                }
            };
        }

        private static CoopTargetSpawnDefinition[] BuildCityNewTargetSpawns()
        {
            return new[]
            {
                new CoopTargetSpawnDefinition
                {
                    TargetId = 1,
                    Position = new Vector3(49.761f, 0.16f, 74.719f),
                    Radius = 1.25f,
                    Health = 68f,
                    DropDefinitionId = "medical_kit",
                    HeadOffset = new Vector3(0f, 0.85f, 0f),
                    HeadRadius = 0.32f,
                    Role = AuthoritativeEnemyRole.Assault,
                    MoveSpeed = 2.35f,
                    AttackRange = 1.8f,
                    AttackDamage = 6f,
                    AttackIntervalTicks = 60,
                    RewardExperience = 40
                },
                new CoopTargetSpawnDefinition
                {
                    TargetId = 2,
                    Position = new Vector3(57.5f, 0.16f, 76f),
                    Radius = 1.05f,
                    Health = 54f,
                    HeadOffset = new Vector3(0f, 0.8f, 0f),
                    HeadRadius = 0.3f,
                    Role = AuthoritativeEnemyRole.Raider,
                    SpawnTick = 45,
                    MoveSpeed = 3.1f,
                    AttackRange = 1.55f,
                    AttackDamage = 5f,
                    AttackIntervalTicks = 48,
                    RewardExperience = 40
                },
                new CoopTargetSpawnDefinition
                {
                    TargetId = 3,
                    Position = new Vector3(42.2f, 0.16f, 76f),
                    Radius = 1.15f,
                    Health = 82f,
                    DropDefinitionId = "armor_pack",
                    HeadOffset = new Vector3(0f, 0.9f, 0f),
                    HeadRadius = 0.34f,
                    Role = AuthoritativeEnemyRole.Support,
                    SpawnTick = 90,
                    MoveSpeed = 1.9f,
                    AttackRange = 2.1f,
                    AttackDamage = 5f,
                    AttackIntervalTicks = 72,
                    RewardExperience = 50
                },
                new CoopTargetSpawnDefinition
                {
                    TargetId = 4,
                    Position = new Vector3(61f, 0.16f, 82f),
                    Radius = 1.2f,
                    Health = 76f,
                    HeadOffset = new Vector3(0f, 0.9f, 0f),
                    HeadRadius = 0.34f,
                    Role = AuthoritativeEnemyRole.Suppressor,
                    SpawnTick = 135,
                    MoveSpeed = 1.75f,
                    AttackRange = 2.6f,
                    AttackDamage = 7f,
                    AttackIntervalTicks = 70,
                    RewardExperience = 55
                },
                new CoopTargetSpawnDefinition
                {
                    TargetId = 5,
                    Position = new Vector3(38.5f, 0.16f, 82f),
                    Radius = 1.1f,
                    Health = 64f,
                    HeadOffset = new Vector3(0f, 0.85f, 0f),
                    HeadRadius = 0.32f,
                    Role = AuthoritativeEnemyRole.Raider,
                    SpawnTick = 180,
                    MoveSpeed = 3.2f,
                    AttackRange = 1.55f,
                    AttackDamage = 5f,
                    AttackIntervalTicks = 48,
                    RewardExperience = 40
                },
                new CoopTargetSpawnDefinition
                {
                    TargetId = 6,
                    Position = new Vector3(49.761f, 0.16f, 88f),
                    Radius = 1.45f,
                    Health = 135f,
                    DropDefinitionId = "medical_kit",
                    HeadOffset = new Vector3(0f, 1.05f, 0f),
                    HeadRadius = 0.38f,
                    Role = AuthoritativeEnemyRole.Elite,
                    SpawnTick = 225,
                    MoveSpeed = 2.55f,
                    AttackRange = 2f,
                    AttackDamage = 9f,
                    AttackIntervalTicks = 64,
                    RewardExperience = 80
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
            string previousPhase = lastObservedPhase;
            RefreshLobbyFromSession();
            string currentPhase = LobbyPhase;
            lastObservedPhase = currentPhase;
            _ = EnforceHostLobbyRulesAsync();
            if (HasLoadingPhase()) RaiseLobbyStartOnce();
            _ = EvaluateHostSceneLoadBarrierAsync();
            if (string.Equals(LobbyPhase, PhaseBattle,
                    StringComparison.Ordinal))
                RaiseBattleReadyOnce();
            if (string.Equals(LobbyPhase, PhaseCancelled,
                    StringComparison.Ordinal))
                RaiseSceneLoadCancelledOnce();
            if (string.Equals(currentPhase, PhaseLobby,
                    StringComparison.Ordinal) &&
                !string.Equals(previousPhase, PhaseLobby,
                    StringComparison.Ordinal))
            {
                _ = ClearLocalReadyAfterMatchAsync();
                ReturnedToLobby?.Invoke();
            }
            StateChanged?.Invoke(State);
        }

        private async Task ClearLocalReadyAfterMatchAsync()
        {
            if (activeSession == null) return;
            try
            {
                activeSession.CurrentPlayer.SetProperty(ReadyProperty,
                    new PlayerProperty("0"));
                activeSession.CurrentPlayer.SetProperty(ReadyEpochProperty,
                    new PlayerProperty(string.Empty));
                await activeSession.SaveCurrentPlayerDataAsync();
                RefreshLobbyFromSession();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"返回合作房间时清理准备状态失败：{exception.Message}",
                    this);
            }
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
            string phase = Property(activeSession.Properties,
                PhaseProperty, PhaseLobby);
            string loadEpoch = Property(activeSession.Properties,
                LoadEpochProperty, string.Empty);
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
                sceneLoadFailure = Property(activeSession.Properties,
                    LoadFailureProperty, "服务器取消了场景加载。");
            }
            bool starting = string.Equals(phase, PhaseStarting,
                StringComparison.Ordinal) ||
                string.Equals(phase, PhaseLoading,
                    StringComparison.Ordinal) ||
                string.Equals(phase, PhaseBattle,
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

        private bool HasLoadingPhase() => activeSession != null &&
            string.Equals(Property(activeSession.Properties, PhaseProperty,
                PhaseLobby), PhaseLoading, StringComparison.Ordinal);

        private void EnsureHostSceneLoadBarrier()
        {
            if (activeSession == null || !activeSession.IsHost ||
                string.Equals(sceneLoadBarrier.Epoch, SceneLoadEpoch,
                    StringComparison.Ordinal) &&
                sceneLoadBarrier.State != CoopSceneLoadState.Idle) return;
            if (!string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(SceneLoadEpoch)) return;
            sceneLoadBarrier.Begin(SceneLoadEpoch, LobbyMapId, SceneSeed,
                activeSession.Players.Select(value => value.Id),
                Time.realtimeSinceStartupAsDouble, 30d);
        }

        private async Task EvaluateHostSceneLoadBarrierAsync()
        {
            if (activeSession == null || !activeSession.IsHost ||
                sceneBarrierEvaluationInProgress ||
                sceneCancellationInProgress ||
                !string.Equals(LobbyPhase, PhaseLoading,
                    StringComparison.Ordinal)) return;
            sceneBarrierEvaluationInProgress = true;
            try
            {
                EnsureHostSceneLoadBarrier();
                if (activeSession == null ||
                    activeSession.PlayerCount != MaximumPlayers)
                {
                    await CancelSceneLoadAsync(
                        "成员在加载期间离开，服务器已取消本次战局。");
                    return;
                }
                for (int index = 0; index < activeSession.Players.Count; index++)
                {
                    IReadOnlyPlayer player = activeSession.Players[index];
                    string readyEpoch = Property(player.Properties,
                        ReadyEpochProperty, string.Empty);
                    if (!string.Equals(readyEpoch, SceneLoadEpoch,
                            StringComparison.Ordinal)) continue;
                    sceneLoadBarrier.ReportReady(player.Id, readyEpoch);
                }
                if (sceneLoadBarrier.State != CoopSceneLoadState.Ready ||
                    activeSession == null || !activeSession.IsHost ||
                    !string.Equals(LobbyPhase, PhaseLoading,
                        StringComparison.Ordinal)) return;
                IHostSession host = activeSession.AsHost();
                host.SetProperty(PhaseProperty,
                    new SessionProperty(PhaseBattle));
                await host.SavePropertiesAsync();
                RaiseBattleReadyOnce();
                NotifyLobbyChanged();
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
