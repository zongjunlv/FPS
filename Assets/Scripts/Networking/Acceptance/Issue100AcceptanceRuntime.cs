using System;
using System.Collections;
using System.Linq;
using FPS.Networking.Diagnostics;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Analytics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.Networking.Acceptance
{
    [DefaultExecutionOrder(-31950)]
    [DisallowMultipleComponent]
    internal sealed class Issue100AcceptanceRuntime : MonoBehaviour
    {
        private Issue100RuntimeArguments options;
        private Issue100EvidenceStore evidence;
        private float nextSnapshotAt;
        private double startedAt;
        private bool finished;
        private bool quitting;

        public Issue100RuntimeArguments Options => options;
        public Issue100EvidenceStore Evidence => evidence;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!Issue100RuntimeArguments.IsRequested(arguments)) return;
            if (FindAnyObjectByType<Issue100AcceptanceRuntime>() != null) return;
            if (!Issue100RuntimeArguments.TryParse(arguments,
                    out Issue100RuntimeArguments parsed, out string error))
            {
                Debug.LogError("[ISSUE100][ARGUMENTS_FAILED] " + error);
                Application.Quit(100);
                return;
            }

            var root = new GameObject("Issue100 Multi-Process Acceptance");
            root.SetActive(false);
            DontDestroyOnLoad(root);
            if (parsed.IsClient)
            {
                NetworkConditionScenario condition = parsed.Scenario;
                NetworkSimulator simulator = root.AddComponent<
                    NetworkSimulator>();
                simulator.ConnectionPreset = NetworkSimulatorPreset.Create(
                    "Issue100 " + condition.StableId,
                    "Deterministic acceptance network condition",
                    packetDelayMs: condition.RoundTripLatencyMilliseconds / 2,
                    packetJitterMs: condition.JitterMilliseconds / 2,
                    packetLossPercent:
                        condition.PacketLossBasisPoints / 100);
            }

            Issue100AcceptanceRuntime runtime = root.AddComponent<
                Issue100AcceptanceRuntime>();
            runtime.options = parsed;
            if (parsed.RecordVideo)
                root.AddComponent<Issue100OffscreenRecorder>();
            root.SetActive(true);
        }

        private void Awake()
        {
            Application.runInBackground = true;
            startedAt = Time.realtimeSinceStartupAsDouble;
            evidence = new Issue100EvidenceStore(options);
        }

        private IEnumerator Start()
        {
            if (options.IsServer)
            {
                yield return RunServerProbe();
                yield break;
            }

            if (!string.Equals(SceneManager.GetActiveScene().name, "CityNew",
                    StringComparison.Ordinal))
            {
                evidence.Started(Issue100AcceptanceSteps.SceneLoad,
                    detail: "loading=CityNew");
                AsyncOperation load = SceneManager.LoadSceneAsync(
                    DedicatedServerConfiguration.CityNewScenePath,
                    LoadSceneMode.Single);
                if (load == null)
                {
                    Fail(Issue100AcceptanceSteps.SceneLoad,
                        "CityNew 场景加载请求失败。");
                    yield break;
                }
                yield return load;
            }

            if (!string.Equals(SceneManager.GetActiveScene().name, "CityNew",
                    StringComparison.Ordinal))
            {
                Fail(Issue100AcceptanceSteps.SceneLoad,
                    "客户端没有进入 CityNew。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.SceneLoad,
                detail: "scene=CityNew");

            Issue100ClientScenarioDriver driver = gameObject.AddComponent<
                Issue100ClientScenarioDriver>();
            driver.Initialize(this);
        }

        private IEnumerator RunServerProbe()
        {
            evidence.Started(Issue100AcceptanceSteps.RoomCreate,
                detail: "controlPlane=local-acceptance");
            while (!DedicatedServerRuntime.IsActive ||
                   FindAnyObjectByType<DedicatedServerRuntime>() == null ||
                   !FindAnyObjectByType<DedicatedServerRuntime>().IsReady)
            {
                if (Expired())
                {
                    Fail(Issue100AcceptanceSteps.RoomCreate,
                        "专用服务器没有在时限内进入 READY。");
                    yield break;
                }
                yield return null;
            }
            DedicatedServerRuntime server = FindAnyObjectByType<
                DedicatedServerRuntime>();
            NetworkCoopSessionAuthority authority = FindAnyObjectByType<
                NetworkCoopSessionAuthority>();
            if (authority != null)
                authority.ServerTickCompleted += LogRejectedCommands;
            evidence.Passed(Issue100AcceptanceSteps.RoomCreate,
                tick: server?.Network?.NetworkManager?.ServerTime.Tick ?? 0,
                detail: $"match={options.MatchId};dataPlane=UnityTransport");
        }

        private static void LogRejectedCommands(
            AuthoritativeTickResult result)
        {
            for (int index = 0; index < result.Commands.Count; index++)
            {
                CommandResolution resolution = result.Commands[index];
                if (resolution.Accepted) continue;
                Debug.LogWarning(
                    "[ISSUE100][COMMAND_REJECTED] " +
                    $"player={resolution.Command.PlayerId};" +
                    $"sequence={resolution.Command.Sequence};" +
                    $"clientTick={resolution.Command.ClientTick};" +
                    $"serverTick={result.Tick};" +
                    $"fire={resolution.Command.Fire};" +
                    $"reason={resolution.RejectionReason}");
            }
        }

        private void Update()
        {
            if (finished || evidence == null) return;
            if (Time.realtimeSinceStartup < nextSnapshotAt) return;
            nextSnapshotAt = Time.realtimeSinceStartup + 1f;
            WriteSnapshot();
            if (Expired())
                Fail(evidence.CurrentStep, "验收进程超过总时限。");
        }

        internal void FinishClient(bool passed, string reason = "")
        {
            if (finished) return;
            if (!passed)
            {
                Fail(evidence.CurrentStep, reason);
                return;
            }
            finished = true;
            WriteSnapshot();
            evidence.Complete("passed");
            StartCoroutine(QuitAfterFrame(0));
        }

        internal void Fail(string step, string reason)
        {
            if (finished) return;
            finished = true;
            WriteSnapshot();
            evidence.Failed(step, reason, ResolveServerTick());
            StartCoroutine(QuitAfterFrame(100));
        }

        private IEnumerator QuitAfterFrame(int code)
        {
            yield return null;
            quitting = true;
            Application.Quit(code);
        }

        private bool Expired() =>
            Time.realtimeSinceStartupAsDouble - startedAt >
            options.TimeoutSeconds;

        private long ResolveServerTick()
        {
            NetworkCoopSessionAuthority authority = FindAnyObjectByType<
                NetworkCoopSessionAuthority>();
            return authority?.WorldState.ServerTick ?? 0L;
        }

        private void WriteSnapshot()
        {
            NetworkCoopSessionAuthority authority = FindAnyObjectByType<
                NetworkCoopSessionAuthority>();
            NetworkPlayerReplica local = FindObjectsByType<NetworkPlayerReplica>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(value => value.IsLocallyControlled);
            OptionalNetworkBootstrap network = FindAnyObjectByType<
                OptionalNetworkBootstrap>();
            NetworkManager manager = network?.NetworkManager;
            UnityTransport transport = network?.Transport;
            DriverStatistics traffic = default;
            ulong rtt = 0UL;
            if (transport != null)
            {
                ref NetworkDriver networkDriver = ref transport.GetNetworkDriver();
                if (networkDriver.IsCreated)
                    traffic = networkDriver.GetStatistics();
                if (manager != null && manager.IsClient &&
                    manager.IsConnectedClient)
                    rtt = transport.GetCurrentRtt(NetworkManager.ServerClientId);
            }

            int inventory = 0;
            int level = 0;
            if (authority != null && local != null)
            {
                for (int index = 0;
                     index < authority.ReplicatedInventorySlotCount;
                     index++)
                {
                    NetcodeInventorySlotState slot =
                        authority.GetReplicatedInventorySlot(index);
                    if (slot.PlayerId == local.PlayerId && slot.Quantity > 0)
                        inventory++;
                }
                if (authority.TryGetProgression(local.PlayerId,
                        out NetcodeProgressionState progression))
                    level = progression.Level;
            }

            NetcodeWorldState world = authority?.WorldState ?? default;
            Vector3 position = local?.PresentedPosition ?? Vector3.zero;
            evidence.WriteSnapshot(new Issue100RuntimeSnapshot
            {
                scene = SceneManager.GetActiveScene().name,
                connected = manager != null &&
                    (manager.IsServer || manager.IsConnectedClient),
                presentationReady = local != null && local.IsPresentationReady,
                serverTick = world.ServerTick,
                simulationPlayerId = local?.PlayerId ?? 0,
                missionPhase = world.MissionPhase.ToString(),
                waveStatus = world.WaveStatus.ToString(),
                remainingEnemies = world.RemainingEnemyCount,
                worldDrops = authority?.ReplicatedWorldDropCount ?? 0,
                inventorySlots = inventory,
                playerLevel = level,
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                health = local?.PresentedHealth ?? 0f,
                armor = local?.PresentedArmor ?? 0f,
                magazineAmmo = local?.PresentedMagazineAmmo ?? 0,
                reserveAmmo = local?.PresentedReserveAmmo ?? 0,
                transportRxBytes = traffic.RxTotalBytes,
                transportTxBytes = traffic.TxTotalBytes,
                measuredRttMilliseconds = rtt,
                predictionSamples = local?.PredictionSampleCount ?? 0,
                correctionCount = local?.PredictionCorrectionCount ?? 0,
                maximumPredictionError = local?.MaximumPredictionError ?? 0d
            });
        }

        private void OnApplicationQuit()
        {
            if (evidence == null) return;
            WriteSnapshot();
            if (!finished)
                evidence.Complete(quitting ? "stopped" : "terminated");
        }
    }
}
