using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    public enum OptionalNetworkState
    {
        Offline = 0,
        StartingHost = 1,
        Hosting = 2,
        StartingClient = 3,
        Client = 4,
        Stopping = 5,
        Failed = 6
    }

    [Serializable]
    public struct NetworkEndpointSettings
    {
        public const ushort DefaultPort = 7777;

        public string Address;
        public string ListenAddress;
        public ushort Port;
        public uint TickRate;

        public static NetworkEndpointSettings Localhost => new()
        {
            Address = "127.0.0.1",
            ListenAddress = "0.0.0.0",
            Port = DefaultPort,
            TickRate = 60
        };

        public NetworkEndpointSettings Sanitized()
        {
            NetworkEndpointSettings value = this;
            value.Address = string.IsNullOrWhiteSpace(value.Address)
                ? "127.0.0.1"
                : value.Address.Trim();
            value.ListenAddress = string.IsNullOrWhiteSpace(value.ListenAddress)
                ? "0.0.0.0"
                : value.ListenAddress.Trim();
            value.Port = value.Port == 0 ? DefaultPort : value.Port;
            value.TickRate = value.TickRate == 0 ? 60u : value.TickRate;
            return value;
        }
    }

    /// <summary>
    /// Opt-in NGO entry point. Merely loading a normal single-player scene never
    /// starts a transport; a caller must explicitly choose host or client.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class OptionalNetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private bool autoStart;
        [SerializeField] private bool autoStartAsHost = true;
        [SerializeField] private NetworkEndpointSettings endpoint;

        private NetworkManager networkManager;
        private UnityTransport transport;
        private bool callbacksBound;

        public event Action<OptionalNetworkState> StateChanged;
        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;

        public bool AutoStart
        {
            get => autoStart;
            set => autoStart = value;
        }

        public NetworkManager NetworkManager => networkManager;
        public UnityTransport Transport => transport;
        public OptionalNetworkState State { get; private set; } =
            OptionalNetworkState.Offline;
        public string LastFailure { get; private set; } = string.Empty;
        public bool IsListening => networkManager != null &&
            networkManager.IsListening;
        public bool IsOffline => !IsListening && State == OptionalNetworkState.Offline;

        public static OptionalNetworkBootstrap CreateRuntime(
            NetworkEndpointSettings settings,
            string objectName = "Optional Network Bootstrap")
        {
            GameObject runtime = new GameObject(objectName);
            runtime.SetActive(false);
            NetworkManager manager = runtime.AddComponent<NetworkManager>();
            UnityTransport runtimeTransport = runtime.AddComponent<UnityTransport>();
            OptionalNetworkBootstrap bootstrap =
                runtime.AddComponent<OptionalNetworkBootstrap>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = runtimeTransport,
                EnableSceneManagement = false,
                ForceSamePrefabs = false,
                TickRate = settings.Sanitized().TickRate
            };
            bootstrap.Configure(settings);
            runtime.SetActive(true);
            return bootstrap;
        }

        private void Awake()
        {
            ResolveComponents();
            endpoint = endpoint.Port == 0
                ? NetworkEndpointSettings.Localhost
                : endpoint.Sanitized();
            EnsureNetworkConfig();
        }

        private void OnEnable()
        {
            BindCallbacks();
        }

        private void Start()
        {
            if (!autoStart)
            {
                return;
            }

            if (autoStartAsHost)
            {
                StartHost();
            }
            else
            {
                StartClient();
            }
        }

        private void OnDisable()
        {
            UnbindCallbacks();
        }

        private void OnDestroy()
        {
            SafeShutdown(discardMessageQueue: true);
        }

        public void Configure(NetworkEndpointSettings settings)
        {
            if (IsListening)
            {
                throw new InvalidOperationException(
                    "Cannot reconfigure an active network session.");
            }

            ResolveComponents();
            endpoint = settings.Sanitized();
            EnsureNetworkConfig();
            networkManager.NetworkConfig.TickRate = endpoint.TickRate;
            transport.SetConnectionData(
                endpoint.Address,
                endpoint.Port,
                endpoint.ListenAddress);
            LastFailure = string.Empty;
            SetState(OptionalNetworkState.Offline);
        }

        public void RegisterNetworkPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            ResolveComponents();
            networkManager.AddNetworkPrefab(prefab);
        }

        public bool StartHost()
        {
            return TryStart(OptionalNetworkState.StartingHost, () =>
                networkManager.StartHost(), OptionalNetworkState.Hosting);
        }

        public bool StartClient()
        {
            return TryStart(OptionalNetworkState.StartingClient, () =>
                networkManager.StartClient(), OptionalNetworkState.Client);
        }

        public void Shutdown()
        {
            SafeShutdown(discardMessageQueue: false);
        }

        /// <summary>Immediate shutdown hook intended for test teardown.</summary>
        public void ResetForTests()
        {
            SafeShutdown(discardMessageQueue: true);
            LastFailure = string.Empty;
        }

        private bool TryStart(
            OptionalNetworkState startingState,
            Func<bool> start,
            OptionalNetworkState runningState)
        {
            ResolveComponents();
            EnsureNetworkConfig();
            if (networkManager.IsListening || networkManager.ShutdownInProgress)
            {
                LastFailure = "A network session is already active or stopping.";
                return false;
            }

            Configure(endpoint);
            SetState(startingState);
            try
            {
                if (start())
                {
                    LastFailure = string.Empty;
                    SetState(runningState);
                    return true;
                }

                Fail("NGO failed to start the requested network role.");
                return false;
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
                return false;
            }
        }

        private void SafeShutdown(bool discardMessageQueue)
        {
            if (networkManager == null)
            {
                SetState(OptionalNetworkState.Offline);
                return;
            }

            if (networkManager.IsServer || networkManager.IsClient)
            {
                SetState(OptionalNetworkState.Stopping);
                networkManager.Shutdown(discardMessageQueue);
            }

            SetState(OptionalNetworkState.Offline);
        }

        private void ResolveComponents()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (transport == null)
            {
                transport = GetComponent<UnityTransport>();
            }
        }

        private void EnsureNetworkConfig()
        {
            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkManager.NetworkConfig.ForceSamePrefabs = false;
        }

        private void BindCallbacks()
        {
            ResolveComponents();
            if (callbacksBound || networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            networkManager.OnTransportFailure += HandleTransportFailure;
            callbacksBound = true;
        }

        private void UnbindCallbacks()
        {
            if (!callbacksBound || networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            networkManager.OnTransportFailure -= HandleTransportFailure;
            callbacksBound = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            ClientConnected?.Invoke(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            ClientDisconnected?.Invoke(clientId);
            if (networkManager != null && !networkManager.IsServer &&
                clientId == networkManager.LocalClientId)
            {
                SetState(OptionalNetworkState.Offline);
            }
        }

        private void HandleTransportFailure()
        {
            Fail("Unity Transport reported a connection failure.");
            SafeShutdown(discardMessageQueue: true);
        }

        private void Fail(string failure)
        {
            LastFailure = string.IsNullOrWhiteSpace(failure)
                ? "Unknown network failure."
                : failure;
            SetState(OptionalNetworkState.Failed);
        }

        private void SetState(OptionalNetworkState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
