using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Diagnostics
{
    public enum Issue100ProcessRole
    {
        DedicatedServer = 0,
        ClientA = 1,
        ClientB = 2
    }

    public static class Issue100AcceptanceSteps
    {
        public const string AccountRegister = "account.register";
        public const string AccountLogin = "account.login";
        public const string RoomCreate = "room.create";
        public const string RoomJoin = "room.join";
        public const string CharacterSelect = "character.select";
        public const string LobbyReady = "lobby.ready";
        public const string SceneLoad = "scene.load";
        public const string Walk = "movement.walk";
        public const string Crouch = "movement.crouch";
        public const string Jump = "movement.jump";
        public const string Sprint = "movement.sprint";
        public const string Aim = "combat.aim";
        public const string Fire = "combat.fire";
        public const string Reload = "combat.reload";
        public const string WaveComplete = "wave.complete";
        public const string DropSpawn = "drop.spawn";
        public const string InventoryPickup = "inventory.pickup";
        public const string InventoryUse = "inventory.use";
        public const string UpgradeSelect = "upgrade.select";
        public const string MissionTerminal = "mission.terminal";
        public const string ReconnectRestore = "reconnect.restore";
        public const string MissionExtraction = "mission.extraction";
        public const string MatchSettlement = "match.settlement";

        private static readonly string[] Values =
        {
            AccountRegister,
            AccountLogin,
            RoomCreate,
            RoomJoin,
            CharacterSelect,
            LobbyReady,
            SceneLoad,
            Walk,
            Crouch,
            Jump,
            Sprint,
            Aim,
            Fire,
            Reload,
            WaveComplete,
            DropSpawn,
            InventoryPickup,
            InventoryUse,
            UpgradeSelect,
            MissionTerminal,
            ReconnectRestore,
            MissionExtraction,
            MatchSettlement
        };

        public static IReadOnlyList<string> Required => Values;
    }

    public sealed class Issue100ProcessEvidence
    {
        public Issue100ProcessEvidence(Issue100ProcessRole role, int processId,
            long startedUnixMilliseconds, long endedUnixMilliseconds,
            string logPath, string snapshotPath)
        {
            Role = role;
            ProcessId = processId;
            StartedUnixMilliseconds = startedUnixMilliseconds;
            EndedUnixMilliseconds = endedUnixMilliseconds;
            LogPath = Required(logPath, nameof(logPath));
            SnapshotPath = Required(snapshotPath, nameof(snapshotPath));
        }

        public Issue100ProcessRole Role { get; }
        public int ProcessId { get; }
        public long StartedUnixMilliseconds { get; }
        public long EndedUnixMilliseconds { get; }
        public string LogPath { get; }
        public string SnapshotPath { get; }

        public bool HasValidLifetime => ProcessId > 0 &&
            StartedUnixMilliseconds > 0 &&
            EndedUnixMilliseconds >= StartedUnixMilliseconds;

        private static string Required(string value, string parameter)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new ArgumentException("Value is required.", parameter);
        }
    }

    public sealed class Issue100StepEvidence
    {
        public Issue100StepEvidence(string stepId, Issue100ProcessRole role,
            bool passed, long authoritativeTick, string detail = "")
        {
            StepId = !string.IsNullOrWhiteSpace(stepId)
                ? stepId.Trim()
                : throw new ArgumentException("Step id is required.",
                    nameof(stepId));
            Role = role;
            Passed = passed;
            AuthoritativeTick = Math.Max(0L, authoritativeTick);
            Detail = detail?.Trim() ?? string.Empty;
        }

        public string StepId { get; }
        public Issue100ProcessRole Role { get; }
        public bool Passed { get; }
        public long AuthoritativeTick { get; }
        public string Detail { get; }
    }

    public sealed class Issue100ScenarioEvidence
    {
        public Issue100ScenarioEvidence(string stableId,
            IEnumerable<Issue100ProcessEvidence> processes,
            NetworkDiagnosticScenarioResult metrics,
            IEnumerable<Issue100StepEvidence> steps,
            string timelinePath)
        {
            StableId = !string.IsNullOrWhiteSpace(stableId)
                ? stableId.Trim()
                : throw new ArgumentException("Stable id is required.",
                    nameof(stableId));
            Processes = Array.AsReadOnly((processes ??
                throw new ArgumentNullException(nameof(processes))).ToArray());
            Metrics = metrics ?? throw new ArgumentNullException(
                nameof(metrics));
            Steps = Array.AsReadOnly((steps ?? Array.Empty<
                Issue100StepEvidence>()).ToArray());
            TimelinePath = !string.IsNullOrWhiteSpace(timelinePath)
                ? timelinePath.Trim()
                : throw new ArgumentException("Timeline path is required.",
                    nameof(timelinePath));
        }

        public string StableId { get; }
        public IReadOnlyList<Issue100ProcessEvidence> Processes { get; }
        public NetworkDiagnosticScenarioResult Metrics { get; }
        public IReadOnlyList<Issue100StepEvidence> Steps { get; }
        public string TimelinePath { get; }
    }

    public sealed class Issue100AcceptanceDecision
    {
        public Issue100AcceptanceDecision(IEnumerable<string> failures)
        {
            Failures = Array.AsReadOnly((failures ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        }

        public bool Passed => Failures.Count == 0;
        public IReadOnlyList<string> Failures { get; }
    }

    public sealed class Issue100AcceptanceReport
    {
        public Issue100AcceptanceReport(string runId, string commit,
            string buildHash, string unityVersion,
            IEnumerable<Issue100ScenarioEvidence> scenarios,
            Issue100AcceptanceDecision decision,
            string videoPath = "")
        {
            RunId = Required(runId, nameof(runId));
            Commit = Required(commit, nameof(commit));
            BuildHash = Required(buildHash, nameof(buildHash));
            UnityVersion = Required(unityVersion, nameof(unityVersion));
            Scenarios = Array.AsReadOnly((scenarios ??
                    throw new ArgumentNullException(nameof(scenarios)))
                .OrderBy(value => value.StableId, StringComparer.Ordinal)
                .ToArray());
            Decision = decision ?? throw new ArgumentNullException(
                nameof(decision));
            VideoPath = videoPath?.Trim() ?? string.Empty;
        }

        public string RunId { get; }
        public string Commit { get; }
        public string BuildHash { get; }
        public string UnityVersion { get; }
        public IReadOnlyList<Issue100ScenarioEvidence> Scenarios { get; }
        public Issue100AcceptanceDecision Decision { get; }
        public string VideoPath { get; }

        private static string Required(string value, string parameter)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new ArgumentException("Value is required.", parameter);
        }
    }

    public static class Issue100AcceptanceGate
    {
        private static readonly Issue100ProcessRole[] RequiredRoles =
        {
            Issue100ProcessRole.DedicatedServer,
            Issue100ProcessRole.ClientA,
            Issue100ProcessRole.ClientB
        };

        public static Issue100AcceptanceDecision Evaluate(
            IEnumerable<Issue100ScenarioEvidence> scenarios)
        {
            Issue100ScenarioEvidence[] values = (scenarios ??
                    throw new ArgumentNullException(nameof(scenarios)))
                .OrderBy(value => value.StableId, StringComparer.Ordinal)
                .ToArray();
            var failures = new List<string>();

            ValidateScenarioMatrix(values, failures);
            for (int index = 0; index < values.Length; index++)
                ValidateScenario(values[index], failures);
            ValidateFlow(values, failures);
            ValidateMetrics(values, failures);
            return new Issue100AcceptanceDecision(failures);
        }

        private static void ValidateScenarioMatrix(
            IReadOnlyList<Issue100ScenarioEvidence> scenarios,
            ICollection<string> failures)
        {
            string[] expected = Issue65NetworkConditionMatrix.Required
                .Select(value => value.StableId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] actual = scenarios.Select(value => value.StableId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                failures.Add("matrix:requires-exact-four-network-scenarios");
        }

        private static void ValidateScenario(Issue100ScenarioEvidence scenario,
            ICollection<string> failures)
        {
            string prefix = scenario.StableId + ":";
            if (!string.Equals(scenario.Metrics.Scenario.StableId,
                    scenario.StableId, StringComparison.Ordinal))
                failures.Add(prefix + "metrics-scenario-mismatch");
            if (scenario.Processes.Count != RequiredRoles.Length)
                failures.Add(prefix + "requires-exactly-three-processes");

            foreach (Issue100ProcessRole role in RequiredRoles)
            {
                int count = scenario.Processes.Count(value =>
                    value.Role == role);
                if (count != 1)
                    failures.Add(prefix + "requires-role-" + role);
            }

            if (scenario.Processes.Select(value => value.ProcessId)
                    .Distinct().Count() != scenario.Processes.Count)
                failures.Add(prefix + "process-ids-must-be-distinct");
            if (scenario.Processes.Any(value => !value.HasValidLifetime))
                failures.Add(prefix + "invalid-process-lifetime");

            if (scenario.Processes.Count > 0)
            {
                long latestStart = scenario.Processes.Max(value =>
                    value.StartedUnixMilliseconds);
                long earliestEnd = scenario.Processes.Min(value =>
                    value.EndedUnixMilliseconds);
                if (latestStart >= earliestEnd)
                    failures.Add(prefix + "process-lifetimes-do-not-overlap");
            }

            if (string.IsNullOrWhiteSpace(scenario.TimelinePath))
                failures.Add(prefix + "missing-timeline");
        }

        private static void ValidateFlow(
            IReadOnlyList<Issue100ScenarioEvidence> scenarios,
            ICollection<string> failures)
        {
            var passed = new HashSet<string>(
                scenarios.SelectMany(value => value.Steps)
                    .Where(value => value.Passed)
                    .Select(value => value.StepId),
                StringComparer.Ordinal);
            foreach (string step in Issue100AcceptanceSteps.Required)
            {
                if (!passed.Contains(step))
                    failures.Add("flow:missing-" + step);
            }

            foreach (Issue100StepEvidence failed in scenarios
                         .SelectMany(value => value.Steps)
                         .Where(value => !value.Passed))
                failures.Add("flow:failed-" + failed.StepId);
        }

        private static void ValidateMetrics(
            IReadOnlyList<Issue100ScenarioEvidence> scenarios,
            ICollection<string> failures)
        {
            var thresholds = new NetworkDiagnosticsGateThresholds();
            for (int index = 0; index < scenarios.Count; index++)
            {
                NetworkDiagnosticScenarioResult value =
                    scenarios[index].Metrics;
                string prefix = "metrics:" + value.Scenario.StableId + ":";
                if (value.DurationSeconds <= 0d)
                    failures.Add(prefix + "empty-duration");
                if (value.HitFeedbackSampleCount <
                    thresholds.MinimumHitFeedbackSamples)
                    failures.Add(prefix + "insufficient-hit-feedback-samples");
                double hitBudget =
                    value.Scenario.RoundTripLatencyMilliseconds +
                    thresholds.HitFeedbackP95BudgetAboveRttMilliseconds;
                if (value.Scenario.PacketLossBasisPoints > 0)
                    hitBudget += value.Scenario.RoundTripLatencyMilliseconds;
                if (value.P95HitFeedbackMilliseconds > hitBudget)
                    failures.Add(prefix + "hit-feedback-p95-over-budget");
                if (value.CorrectionsPerMinute >
                    thresholds.MaximumCorrectionsPerMinute)
                    failures.Add(prefix + "correction-rate-over-budget");
                if (value.CorrectionCount >= 20 &&
                        value.P95CorrectionMagnitude >
                        thresholds.MaximumCorrectionP95Magnitude + 0.001d ||
                    value.MaximumCorrectionMagnitude >
                    thresholds.MaximumCorrectionMagnitude)
                    failures.Add(prefix + "correction-magnitude-over-budget");
                if (value.StateComparisonCount <= 0)
                    failures.Add(prefix + "missing-state-comparisons");
                if (value.StateDivergenceRate >
                    thresholds.MaximumStateDivergenceRate ||
                    value.MaximumStateDivergenceMagnitude >
                    thresholds.MaximumStateDivergenceMagnitude ||
                    value.MaximumStateDivergenceDurationMilliseconds >
                    thresholds.MaximumStateDivergenceDurationMilliseconds)
                    failures.Add(prefix + "state-divergence-over-budget");
                if (value.UplinkBytes <= 0 || value.DownlinkBytes <= 0)
                    failures.Add(prefix + "missing-directional-traffic");
                if (value.UplinkBytesPerSecond >
                    thresholds.MaximumUplinkBytesPerSecond ||
                    value.DownlinkBytesPerSecond >
                    thresholds.MaximumDownlinkBytesPerSecond)
                    failures.Add(prefix + "bandwidth-over-budget");
                int accounted = value.AcceptedCommandCount +
                    value.DroppedCommandCount + value.RejectedCommandCount;
                if (value.SentCommandCount <= 0 ||
                    accounted != value.SentCommandCount)
                    failures.Add(prefix + "command-accounting-mismatch");
                if (value.RejectionCount(
                        NetworkCommandRejectionReason.Unknown) > 0)
                    failures.Add(prefix + "unknown-rejection-reason");
            }
        }
    }
}
