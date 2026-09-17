using System;
using System.Collections;
using System.IO;
using System.Threading;
using FPS.Networking.Domain;
using Unity.Netcode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Analytics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.Networking.Netcode
{
    public readonly struct DedicatedServerPresentationResult
    {
        public DedicatedServerPresentationResult(int cameras, int listeners,
            int audioSources, int canvases, int renderers, int lights,
            int particles)
        {
            Cameras = cameras;
            Listeners = listeners;
            AudioSources = audioSources;
            Canvases = canvases;
            Renderers = renderers;
            Lights = lights;
            Particles = particles;
        }

        public int Cameras { get; }
        public int Listeners { get; }
        public int AudioSources { get; }
        public int Canvases { get; }
        public int Renderers { get; }
        public int Lights { get; }
        public int Particles { get; }
    }

    public static class DedicatedServerPresentationStripper
    {
        public static DedicatedServerPresentationResult Strip()
        {
            int cameras = Disable<Camera>();
            int listeners = Disable<AudioListener>();
            int audioSources = Disable<AudioSource>();
            int canvases = Disable<Canvas>();
            int renderers = DisableRenderers();
            int lights = Disable<Light>();
            ParticleSystem[] particles = UnityEngine.Object.FindObjectsByType<
                ParticleSystem>(FindObjectsInactive.Include);
            for (int index = 0; index < particles.Length; index++)
            {
                particles[index].Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            return new DedicatedServerPresentationResult(cameras, listeners,
                audioSources, canvases, renderers, lights, particles.Length);
        }

        private static int Disable<T>() where T : Behaviour
        {
            T[] components = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include);
            for (int index = 0; index < components.Length; index++)
                components[index].enabled = false;
            return components.Length;
        }

        private static int DisableRenderers()
        {
            Renderer[] components = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include);
            for (int index = 0; index < components.Length; index++)
                components[index].enabled = false;
            return components.Length;
        }
    }

    [Serializable]
    internal sealed class DedicatedServerDiagnostics
    {
        public string status;
        public string reason;
        public string map;
        public ushort port;
        public string matchId;
        public int maximumPlayers;
        public int seed;
        public string version;
        public string protocolVersion;
        public string contentVersion;
        public uint tickRate;
        public int idleTimeoutSeconds;
        public double uptimeSeconds;
        public int connectedClients;
        public long authoritativeTick;
        public string missionPhase;
        public string waveStatus;
        public ulong receivedBytes;
        public ulong transmittedBytes;
        public double receivedBytesPerSecond;
        public double transmittedBytesPerSecond;
        public int rejectedConnections;
        public string lastAdmissionFailure;
        public string lastRuntimeException;
        public string timestampUtc;
    }

    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class DedicatedServerRuntime : MonoBehaviour
    {
        private const string LogPrefix = "[DEDICATED_SERVER]";
        private OptionalNetworkBootstrap network;
        private CoopNetworkRuntimeInstaller installer;
        private DedicatedServerConfiguration configuration;
        private float startedAt;
        private volatile bool shutdownRequested;
        private string shutdownReason = "unknown";
        private bool shutdownStarted;
        private bool allowQuit;
        private int exitCode;
        private DedicatedServerIdlePolicy idlePolicy;
        private float nextDiagnosticsAt;
        private DriverStatistics previousTraffic;
        private float previousTrafficAt;
        private bool hasTrafficSample;
        private double receivedBytesPerSecond;
        private double transmittedBytesPerSecond;
        private int rejectedConnections;
        private string lastAdmissionFailure = string.Empty;
        private string lastRuntimeException = string.Empty;
        private bool diagnosticsWriteInProgress;

        public static bool IsActive { get; private set; }
        public DedicatedServerConfiguration Configuration => configuration;
        public OptionalNetworkBootstrap Network => network;
        public bool IsReady { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            bool serverBuild = false;
#if UNITY_SERVER
            serverBuild = true;
#endif
            string[] arguments = Environment.GetCommandLineArgs();
            if (!serverBuild &&
                !DedicatedServerConfiguration.IsRequested(arguments))
                return;
            if (UnityEngine.Object.FindAnyObjectByType<DedicatedServerRuntime>() !=
                null) return;

            var runtime = new GameObject("FPS Dedicated Server");
            DontDestroyOnLoad(runtime);
            runtime.AddComponent<DedicatedServerRuntime>();
        }

        private void Awake()
        {
            IsActive = true;
            startedAt = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Application.wantsToQuit += HandleWantsToQuit;
            Console.CancelKeyPress += HandleCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += HandleProcessExit;
            AppDomain.CurrentDomain.UnhandledException +=
                HandleUnhandledException;
            Application.logMessageReceived += HandleLogMessage;
        }

        private IEnumerator Start()
        {
            if (!DedicatedServerConfiguration.TryParse(
                    Environment.GetCommandLineArgs(), Application.version,
                    Application.persistentDataPath, out configuration,
                    out string error))
            {
                Fail(error, 2);
                yield break;
            }

            Time.fixedDeltaTime = 1f / configuration.TickRate;
            idlePolicy = new DedicatedServerIdlePolicy(
                configuration.IdleTimeoutSeconds, startedAt);
            Application.targetFrameRate = (int)configuration.TickRate;
            QualitySettings.vSyncCount = 0;
            Log("INITIALIZING", $"map={configuration.MapName} " +
                $"port={configuration.Port} match={configuration.MatchId} " +
                $"players={configuration.MaximumPlayers} seed={configuration.Seed} " +
                $"tick={configuration.TickRate} version={configuration.Version} " +
                $"protocol={configuration.ProtocolVersion} " +
                $"content={configuration.ContentVersion} " +
                $"idle={configuration.IdleTimeoutSeconds}s");

            if (SceneManager.GetActiveScene().path != configuration.MapScenePath)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(
                    configuration.MapScenePath, LoadSceneMode.Single);
                if (load == null)
                {
                    Fail($"无法加载服务器地图：{configuration.MapScenePath}", 3);
                    yield break;
                }
                yield return load;
            }
            yield return null;

            DedicatedServerPresentationResult stripped =
                DedicatedServerPresentationStripper.Strip();
            Log("HEADLESS", $"camera={stripped.Cameras} canvas={stripped.Canvases} " +
                $"renderer={stripped.Renderers} audio={stripped.AudioSources}");

            if (!TryConfigureNetwork(out error))
            {
                Fail(error, 4);
                yield break;
            }

            if (!network.StartServer())
            {
                Fail(network.LastFailure, 5);
                yield break;
            }
            Log("LISTENING", $"0.0.0.0:{configuration.Port}");
            yield return null;

            if (installer.SessionAuthority == null &&
                !installer.SpawnAuthoritativeSlice())
            {
                Fail(installer.LastFailure, 6);
                yield break;
            }

            IsReady = true;
            Log("READY", $"match={configuration.MatchId} " +
                $"authoritativeTick={configuration.TickRate}/s");
            SaveDiagnostics("ready", "listening");
        }

        private void Update()
        {
            if (IsReady && network?.NetworkManager != null)
            {
                float now = Time.realtimeSinceStartup;
                int connected = network.NetworkManager.ConnectedClientsIds.Count;
                if (!shutdownRequested && idlePolicy != null &&
                    idlePolicy.ShouldRecycle(now, connected))
                    RequestShutdown("idle-timeout");
                if (now >= nextDiagnosticsAt)
                {
                    nextDiagnosticsAt = now + 5f;
                    SaveDiagnostics("ready", "heartbeat");
                }
            }
            if (shutdownRequested && !shutdownStarted)
                StartCoroutine(ShutdownRoutine());
        }

        private bool TryConfigureNetwork(out string error)
        {
            var endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = configuration.Port;
            endpoint.TickRate = configuration.TickRate;
            network = OptionalNetworkBootstrap.CreateRuntime(endpoint,
                "Dedicated Network Runtime");
            DontDestroyOnLoad(network.gameObject);
            network.ConnectionAdmissionEvaluated += HandleAdmissionEvaluated;
            network.StateChanged += HandleNetworkStateChanged;
            if (!CoopAdmissionEnvironment.TryCreateCodec(
                    out CoopConnectionTicketCodec ticketCodec,
                    out error))
                return false;
            network.ConfigureServerAdmission(
                new CoopConnectionAdmissionService(ticketCodec,
                    new CoopBuildCompatibility(configuration.Version,
                        configuration.ProtocolVersion,
                        configuration.ContentVersion),
                    configuration.MaximumPlayers,
                    matchId: configuration.MatchId));
            installer = network.gameObject.AddComponent<
                CoopNetworkRuntimeInstaller>();

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
                error = "专用服务器网络 Prefab 缺失。";
                return false;
            }

            installer.ConfigurePrefabs(authorityPrefab, replicaPrefab);
            CoopTargetSpawnDefinition[] targets =
                BuildTargets(configuration.Seed);
            installer.ConfigureScenario(BuildPlayers(configuration.MaximumPlayers),
                targets, targets.Length);
            installer.ConfigureMaximumPlayers(configuration.MaximumPlayers);
            if (!installer.RegisterConfiguredPrefabs())
            {
                error = installer.LastFailure;
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static CoopPlayerSpawnDefinition[] BuildPlayers(int count)
        {
            var result = new CoopPlayerSpawnDefinition[count];
            float center = (count - 1) * 0.5f;
            Vector3 cityNewOrigin = new(49.761f, 0.16f, 59.719f);
            for (int index = 0; index < count; index++)
            {
                result[index] = new CoopPlayerSpawnDefinition
                {
                    PlayerId = index + 1,
                    Position = cityNewOrigin +
                        Vector3.right * ((index - center) * 2.5f),
                    Health = 100f
                };
            }
            return result;
        }

        private static CoopTargetSpawnDefinition[] BuildTargets(int seed)
        {
            var random = new System.Random(seed);
            var result = new CoopTargetSpawnDefinition[6];
            Vector3 cityNewWaveCenter = new(49.761f, 0.16f, 74.719f);
            for (int index = 0; index < result.Length; index++)
            {
                double angle = random.NextDouble() * Math.PI * 2d;
                float radius = 12f + (float)random.NextDouble() * 6f;
                result[index] = new CoopTargetSpawnDefinition
                {
                    TargetId = index + 1,
                    Position = cityNewWaveCenter + new Vector3(
                        Mathf.Sin((float)angle) * radius,
                        0f,
                        Mathf.Cos((float)angle) * radius),
                    Radius = index == 5 ? 1.45f : 1.1f,
                    Health = index == 5 ? 135f : 68f,
                    HeadOffset = new Vector3(0f,
                        index == 5 ? 1.05f : 0.85f, 0f),
                    HeadRadius = index == 5 ? 0.38f : 0.32f,
                    DropDefinitionId = index % 3 == 0
                        ? "medkit"
                        : string.Empty,
                    Role = index == 5
                        ? AuthoritativeEnemyRole.Elite
                        : (AuthoritativeEnemyRole)(index % 4),
                    SpawnTick = index * 45L,
                    MoveSpeed = index % 2 == 0 ? 2.4f : 2.9f,
                    AttackRange = 1.8f,
                    AttackDamage = index == 5 ? 9f : 6f,
                    AttackIntervalTicks = index == 5 ? 64 : 60,
                    RewardExperience = index == 5 ? 150 : 100,
                    DropQuantity = 1
                };
            }
            return result;
        }

        public void RequestShutdown(string reason, int requestedExitCode = 0)
        {
            shutdownReason = string.IsNullOrWhiteSpace(reason)
                ? "requested"
                : reason;
            exitCode = requestedExitCode;
            shutdownRequested = true;
        }

        private IEnumerator ShutdownRoutine()
        {
            shutdownStarted = true;
            IsReady = false;
            Log("SHUTDOWN", shutdownReason);
            SaveDiagnostics("stopped", shutdownReason);
            network?.Shutdown();
            yield return null;
            allowQuit = true;
            Application.Quit(exitCode);
        }

        private void Fail(string reason, int code)
        {
            IsReady = false;
            Debug.LogError($"{LogPrefix}[FAILED] {reason}", this);
            SaveDiagnostics("failed", reason);
            RequestShutdown(reason, code);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (configuration != null)
                DedicatedServerPresentationStripper.Strip();
        }

        private bool HandleWantsToQuit()
        {
            if (allowQuit || shutdownStarted) return true;
            RequestShutdown("application-quit");
            return false;
        }

        private void HandleCancelKeyPress(object sender,
            ConsoleCancelEventArgs arguments)
        {
            arguments.Cancel = true;
            RequestShutdown("console-signal");
        }

        private void HandleProcessExit(object sender, EventArgs arguments)
        {
            shutdownReason = "process-exit";
            shutdownRequested = true;
        }

        private void HandleUnhandledException(object sender,
            UnhandledExceptionEventArgs arguments)
        {
            object value = arguments.ExceptionObject;
            lastRuntimeException = value == null
                ? "UnknownUnhandledException"
                : value.GetType().Name;
            shutdownReason = "unhandled-exception";
            exitCode = 5;
            shutdownRequested = true;
        }

        private void HandleLogMessage(string condition, string stackTrace,
            LogType type)
        {
            if (type != LogType.Exception) return;
            lastRuntimeException = string.IsNullOrWhiteSpace(condition)
                ? "UnityException"
                : condition.Split('\n')[0];
            SaveDiagnostics("failed", "unhandled-exception");
        }

        private void HandleAdmissionEvaluated(ulong clientId,
            CoopAdmissionDecision decision)
        {
            if (decision.Approved) return;
            rejectedConnections++;
            lastAdmissionFailure = decision.Failure + ": " + decision.Reason;
            Log("AUTH_REJECTED", $"client={clientId} " +
                $"failure={decision.Failure}");
            SaveDiagnostics("ready", "authentication-rejected");
        }

        private void HandleNetworkStateChanged(OptionalNetworkState state)
        {
            if (state != OptionalNetworkState.Failed) return;
            lastRuntimeException = string.IsNullOrWhiteSpace(
                network?.LastFailure)
                ? "TransportFailure"
                : network.LastFailure;
            SaveDiagnostics("failed", "transport-failure");
            RequestShutdown("transport-failure", 4);
        }

        private void OnApplicationQuit()
        {
            // ShutdownRoutine already captured the final authority/transport
            // snapshot before NGO teardown. Do not overwrite it with zeros
            // after NetworkManager and the authority have been destroyed.
            if (!shutdownStarted)
                SaveDiagnostics(IsReady ? "stopped" : "not-ready",
                    shutdownReason);
            network?.Shutdown();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Application.wantsToQuit -= HandleWantsToQuit;
            Console.CancelKeyPress -= HandleCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= HandleProcessExit;
            AppDomain.CurrentDomain.UnhandledException -=
                HandleUnhandledException;
            Application.logMessageReceived -= HandleLogMessage;
            if (network != null)
            {
                network.ConnectionAdmissionEvaluated -=
                    HandleAdmissionEvaluated;
                network.StateChanged -= HandleNetworkStateChanged;
            }
            IsActive = false;
        }

        private void SaveDiagnostics(string status, string reason)
        {
            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.DiagnosticsPath)) return;
            if (diagnosticsWriteInProgress) return;
            diagnosticsWriteInProgress = true;
            try
            {
                NetworkCoopSessionAuthority authority =
                    installer != null ? installer.SessionAuthority : null;
                DriverStatistics traffic = ReadTraffic();
                float now = Time.realtimeSinceStartup;
                if (hasTrafficSample)
                {
                    double elapsed = Math.Max(0.001d, now - previousTrafficAt);
                    receivedBytesPerSecond = Delta(traffic.RxTotalBytes,
                        previousTraffic.RxTotalBytes) / elapsed;
                    transmittedBytesPerSecond = Delta(traffic.TxTotalBytes,
                        previousTraffic.TxTotalBytes) / elapsed;
                }
                previousTraffic = traffic;
                previousTrafficAt = now;
                hasTrafficSample = true;
                NetcodeWorldState world = authority?.WorldState ?? default;
                var report = new DedicatedServerDiagnostics
                {
                    status = status,
                    reason = reason,
                    map = configuration.MapName,
                    port = configuration.Port,
                    matchId = configuration.MatchId,
                    maximumPlayers = configuration.MaximumPlayers,
                    seed = configuration.Seed,
                    version = configuration.Version,
                    protocolVersion = configuration.ProtocolVersion,
                    contentVersion = configuration.ContentVersion,
                    tickRate = configuration.TickRate,
                    idleTimeoutSeconds = configuration.IdleTimeoutSeconds,
                    uptimeSeconds = Time.realtimeSinceStartup - startedAt,
                    connectedClients = network?.NetworkManager != null
                        ? network.NetworkManager.ConnectedClientsIds.Count
                        : 0,
                    authoritativeTick = authority?.LastAuthoritativeSnapshot?.Tick ?? 0,
                    missionPhase = authority == null
                        ? "NotInitialized"
                        : world.MissionPhase.ToString(),
                    waveStatus = authority == null
                        ? "NotInitialized"
                        : world.WaveStatus.ToString(),
                    receivedBytes = traffic.RxTotalBytes,
                    transmittedBytes = traffic.TxTotalBytes,
                    receivedBytesPerSecond = receivedBytesPerSecond,
                    transmittedBytesPerSecond = transmittedBytesPerSecond,
                    rejectedConnections = rejectedConnections,
                    lastAdmissionFailure = lastAdmissionFailure,
                    lastRuntimeException = lastRuntimeException,
                    timestampUtc = DateTime.UtcNow.ToString("O")
                };
                string path = configuration.DiagnosticsPath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary,
                    JsonUtility.ToJson(report, true));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix}[DIAGNOSTICS_FAILED] " +
                               exception.GetType().Name, this);
            }
            finally
            {
                diagnosticsWriteInProgress = false;
            }
        }

        private DriverStatistics ReadTraffic()
        {
            if (network?.Transport == null) return default;
            ref NetworkDriver driver = ref network.Transport.GetNetworkDriver();
            return driver.IsCreated ? driver.GetStatistics() : default;
        }

        private static ulong Delta(ulong current, ulong previous) =>
            current >= previous ? current - previous : current;

        private static void Log(string state, string message)
        {
            Debug.Log($"{LogPrefix}[{state}] {message}");
        }
    }
}
