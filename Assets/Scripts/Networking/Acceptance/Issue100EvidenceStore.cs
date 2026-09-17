using System;
using System.IO;
using System.Text;
using FPS.Networking.Diagnostics;
using UnityEngine;

namespace FPS.Networking.Acceptance
{
    [Serializable]
    internal sealed class Issue100TimelineRecord
    {
        public string schemaVersion = "issue100-timeline-v1";
        public string runId;
        public string scenario;
        public string role;
        public int processId;
        public string timestampUtc;
        public double elapsedSeconds;
        public string step;
        public string state;
        public long authoritativeTick;
        public string detail;
    }

    [Serializable]
    internal sealed class Issue100ProcessRecord
    {
        public string schemaVersion = "issue100-process-v1";
        public string runId;
        public string scenario;
        public string role;
        public int processId;
        public long startedUnixMilliseconds;
        public long endedUnixMilliseconds;
        public string startedUtc;
        public string endedUtc;
        public string status;
        public string failure;
        public string logPath;
        public string snapshotPath;
        public string timelinePath;
    }

    [Serializable]
    internal sealed class Issue100RuntimeSnapshot
    {
        public string schemaVersion = "issue100-snapshot-v1";
        public string runId;
        public string scenario;
        public string role;
        public int processId;
        public string timestampUtc;
        public string currentStep;
        public string scene;
        public bool connected;
        public bool presentationReady;
        public long serverTick;
        public int simulationPlayerId;
        public string missionPhase;
        public string waveStatus;
        public int remainingEnemies;
        public int worldDrops;
        public int inventorySlots;
        public int playerLevel;
        public float positionX;
        public float positionY;
        public float positionZ;
        public float health;
        public float armor;
        public int magazineAmmo;
        public int reserveAmmo;
        public ulong transportRxBytes;
        public ulong transportTxBytes;
        public ulong measuredRttMilliseconds;
        public int predictionSamples;
        public int correctionCount;
        public double maximumPredictionError;
        public string failure;
    }

    [Serializable]
    internal sealed class Issue100ClientMetricsRecord
    {
        public string schemaVersion = "issue100-client-metrics-v1";
        public string runId;
        public string scenario;
        public string role;
        public int processId;
        public int configuredRttMilliseconds;
        public int configuredJitterMilliseconds;
        public int configuredPacketLossBasisPoints;
        public double durationSeconds;
        public int hitFeedbackSampleCount;
        public double meanHitFeedbackMilliseconds;
        public double p95HitFeedbackMilliseconds;
        public double p99HitFeedbackMilliseconds;
        public int correctionCount;
        public double correctionsPerMinute;
        public double meanCorrectionMagnitude;
        public double p95CorrectionMagnitude;
        public double maximumCorrectionMagnitude;
        public long uplinkBytes;
        public long downlinkBytes;
        public double uplinkBytesPerSecond;
        public double downlinkBytesPerSecond;
        public int stateComparisonCount;
        public int stateDivergenceCount;
        public double stateDivergenceRate;
        public double maximumStateDivergenceMagnitude;
        public double maximumStateDivergenceDurationMilliseconds;
        public int sentCommandCount;
        public int acceptedCommandCount;
        public int droppedCommandCount;
        public int rejectedCommandCount;
        public double meanTransportRttMilliseconds;
        public double maximumTransportRttMilliseconds;
    }

    internal sealed class Issue100EvidenceStore
    {
        private readonly Issue100RuntimeArguments options;
        private readonly string processPath;
        private readonly string timelinePath;
        private readonly string snapshotPath;
        private readonly int processId;
        private readonly DateTimeOffset started;
        private readonly double startedRealtime;
        private string currentStep = "process.start";
        private string failure = string.Empty;
        private bool completed;

        public Issue100EvidenceStore(Issue100RuntimeArguments options)
        {
            this.options = options ?? throw new ArgumentNullException(
                nameof(options));
            Directory.CreateDirectory(options.OutputDirectory);
            processPath = Path.Combine(options.OutputDirectory, "process.json");
            timelinePath = Path.Combine(options.OutputDirectory,
                "timeline.ndjson");
            snapshotPath = Path.Combine(options.OutputDirectory,
                "latest-snapshot.json");
            processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            started = DateTimeOffset.UtcNow;
            startedRealtime = Time.realtimeSinceStartupAsDouble;
            WriteProcess("running", string.Empty, 0L);
            Record("process.start", "passed", 0L,
                $"pid={processId};topology=dedicated-server-two-clients");
        }

        public string CurrentStep => currentStep;
        public string Failure => failure;
        public int ProcessId => processId;
        public string TimelinePath => timelinePath;
        public string SnapshotPath => snapshotPath;

        public void Started(string step, long tick = 0L, string detail = "") =>
            Record(step, "started", tick, detail);

        public void Passed(string step, long tick = 0L, string detail = "") =>
            Record(step, "passed", tick, detail);

        public void Failed(string step, string reason, long tick = 0L)
        {
            failure = string.IsNullOrWhiteSpace(reason)
                ? "unknown failure"
                : reason.Trim();
            Record(step, "failed", tick, failure);
            WriteProcess("failed", failure,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public void WriteSnapshot(Issue100RuntimeSnapshot snapshot)
        {
            if (snapshot == null) return;
            snapshot.schemaVersion = "issue100-snapshot-v1";
            snapshot.runId = options.RunId;
            snapshot.scenario = options.Scenario.StableId;
            snapshot.role = RoleName(options.Role);
            snapshot.processId = processId;
            snapshot.timestampUtc = DateTime.UtcNow.ToString("O");
            snapshot.currentStep = currentStep;
            snapshot.failure = failure;
            WriteAtomic(snapshotPath, JsonUtility.ToJson(snapshot, true));
        }

        public void WriteMetrics(Issue100ClientMetricsRecord metrics)
        {
            if (metrics == null) return;
            metrics.runId = options.RunId;
            metrics.scenario = options.Scenario.StableId;
            metrics.role = RoleName(options.Role);
            metrics.processId = processId;
            WriteAtomic(Path.Combine(options.OutputDirectory,
                "metrics.json"), JsonUtility.ToJson(metrics, true));
        }

        public void Complete(string status = "passed")
        {
            if (completed) return;
            completed = true;
            long ended = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Record("process.stop", status, 0L, failure);
            WriteProcess(status, failure, ended);
        }

        private void Record(string step, string state, long tick,
            string detail)
        {
            currentStep = string.IsNullOrWhiteSpace(step)
                ? currentStep
                : step.Trim();
            var record = new Issue100TimelineRecord
            {
                runId = options.RunId,
                scenario = options.Scenario.StableId,
                role = RoleName(options.Role),
                processId = processId,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                elapsedSeconds = Math.Max(0d,
                    Time.realtimeSinceStartupAsDouble - startedRealtime),
                step = currentStep,
                state = state ?? string.Empty,
                authoritativeTick = Math.Max(0L, tick),
                detail = detail ?? string.Empty
            };
            File.AppendAllText(timelinePath,
                JsonUtility.ToJson(record, false) + "\n",
                new UTF8Encoding(false));
        }

        private void WriteProcess(string status, string reason,
            long endedMilliseconds)
        {
            var record = new Issue100ProcessRecord
            {
                runId = options.RunId,
                scenario = options.Scenario.StableId,
                role = RoleName(options.Role),
                processId = processId,
                startedUnixMilliseconds = started.ToUnixTimeMilliseconds(),
                endedUnixMilliseconds = endedMilliseconds,
                startedUtc = started.UtcDateTime.ToString("O"),
                endedUtc = endedMilliseconds > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(endedMilliseconds)
                        .UtcDateTime.ToString("O")
                    : string.Empty,
                status = status,
                failure = reason ?? string.Empty,
                logPath = Path.Combine(options.OutputDirectory, "player.log"),
                snapshotPath = snapshotPath,
                timelinePath = timelinePath
            };
            WriteAtomic(processPath, JsonUtility.ToJson(record, true));
        }

        private static void WriteAtomic(string path, string contents)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                string backup = path + ".bak";
                try
                {
                    File.Replace(temporary, path, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    File.Delete(path);
                }
            }
            File.Move(temporary, path);
        }

        public static string RoleName(Issue100ProcessRole role) => role switch
        {
            Issue100ProcessRole.DedicatedServer => "server",
            Issue100ProcessRole.ClientA => "client-a",
            Issue100ProcessRole.ClientB => "client-b",
            _ => "unknown"
        };
    }
}
