using System;
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

        private ISession activeSession;
        private OptionalNetworkBootstrap networkBootstrap;
        private bool operationInProgress;
        private bool directSession;

        public event Action<CoopSessionState> StateChanged;

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

        private void OnDestroy()
        {
            UnbindSession();
        }

        public async Task<bool> HostAsync(string profile = null)
        {
            if (!CanBegin()) return false;
            operationInProgress = true;
            SetState(CoopSessionState.Initializing);

            try
            {
                EnsureNetworkPrerequisite();
                await EnsureServicesAsync(profile);
                SetState(CoopSessionState.Hosting);
                SessionOptions options = new SessionOptions
                {
                    Name = "FPS 双人合作切片",
                    MaxPlayers = MaximumPlayers,
                    IsPrivate = true
                }.WithRelayNetwork();
                BindSession(await MultiplayerService.Instance
                    .CreateSessionAsync(options));
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
            SetState(CoopSessionState.Initializing);
            try
            {
                EnsureNetworkPrerequisite();
                await EnsureServicesAsync(profile);
                SetState(CoopSessionState.Joining);
                BindSession(await MultiplayerService.Instance
                    .JoinSessionByCodeAsync(normalized));
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

        private bool CanBegin()
        {
            if (operationInProgress || activeSession != null || directSession)
            {
                LastFailure = "已有合作战局或网络操作正在进行。";
                return false;
            }

            return true;
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
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
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
            activeSession.Deleted += HandleSessionEnded;
            activeSession.RemovedFromSession += HandleSessionEnded;
        }

        private void UnbindSession()
        {
            if (activeSession == null) return;
            activeSession.Changed -= HandleSessionChanged;
            activeSession.Deleted -= HandleSessionEnded;
            activeSession.RemovedFromSession -= HandleSessionEnded;
        }

        private void HandleSessionChanged()
        {
            StateChanged?.Invoke(State);
        }

        private void HandleSessionEnded()
        {
            UnbindSession();
            activeSession = null;
            SetState(CoopSessionState.Offline);
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
