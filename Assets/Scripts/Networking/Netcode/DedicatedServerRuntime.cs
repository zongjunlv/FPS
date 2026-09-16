using System;
using System.Collections;
using System.IO;
using System.Threading;
using Unity.Netcode;
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
        public uint tickRate;
        public double uptimeSeconds;
        public int connectedClients;
        public long authoritativeTick;
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
            Application.targetFrameRate = (int)configuration.TickRate;
            QualitySettings.vSyncCount = 0;
            Log("INITIALIZING", $"map={configuration.MapName} " +
                $"port={configuration.Port} match={configuration.MatchId} " +
                $"players={configuration.MaximumPlayers} seed={configuration.Seed} " +
                $"tick={configuration.TickRate} version={configuration.Version}");

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
            installer.ConfigureScenario(BuildPlayers(configuration.MaximumPlayers),
                BuildTargets(configuration.Seed), 1);
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
            for (int index = 0; index < count; index++)
            {
                result[index] = new CoopPlayerSpawnDefinition
                {
                    PlayerId = index + 1,
                    Position = new Vector3((index - center) * 2.5f, 0f, 0f),
                    Health = 100f
                };
            }
            return result;
        }

        private static CoopTargetSpawnDefinition[] BuildTargets(int seed)
        {
            var random = new System.Random(seed);
            float offset = (float)(random.NextDouble() * 8d - 4d);
            return new[]
            {
                new CoopTargetSpawnDefinition
                {
                    TargetId = 1,
                    Position = new Vector3(offset, 0f, 15f),
                    Radius = 1.25f,
                    Health = 68f,
                    DropDefinitionId = "medkit"
                }
            };
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

        private void OnApplicationQuit()
        {
            SaveDiagnostics(IsReady ? "stopped" : "not-ready", shutdownReason);
            network?.Shutdown();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Application.wantsToQuit -= HandleWantsToQuit;
            Console.CancelKeyPress -= HandleCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= HandleProcessExit;
            IsActive = false;
        }

        private void SaveDiagnostics(string status, string reason)
        {
            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.DiagnosticsPath)) return;
            try
            {
                NetworkCoopSessionAuthority authority =
                    installer != null ? installer.SessionAuthority : null;
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
                    tickRate = configuration.TickRate,
                    uptimeSeconds = Time.realtimeSinceStartup - startedAt,
                    connectedClients = network?.NetworkManager != null
                        ? network.NetworkManager.ConnectedClientsIds.Count
                        : 0,
                    authoritativeTick = authority?.LastAuthoritativeSnapshot?.Tick ?? 0,
                    timestampUtc = DateTime.UtcNow.ToString("O")
                };
                string path = configuration.DiagnosticsPath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix}[DIAGNOSTICS_FAILED] " +
                               exception.GetType().Name, this);
            }
        }

        private static void Log(string state, string message)
        {
            Debug.Log($"{LogPrefix}[{state}] {message}");
        }
    }
}
