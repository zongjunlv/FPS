using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace FPS.Determinism
{
    [DataContract]
    public sealed class ReplayStateField
    {
        [DataMember(Order = 1)] private string path;
        [DataMember(Order = 2)] private string value;

        private ReplayStateField(string path, string value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("回放状态字段路径不能为空。", nameof(path));
            this.path = path.Trim();
            this.value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Path => path;
        public string Value => value;

        public static ReplayStateField Text(string path, string value) =>
            new ReplayStateField(path, value ?? string.Empty);

        public static ReplayStateField Number(string path, long value) =>
            new ReplayStateField(path, value.ToString(CultureInfo.InvariantCulture));

        public static ReplayStateField Flag(string path, bool value) =>
            new ReplayStateField(path, value ? "true" : "false");
    }

    [DataContract]
    public sealed class ReplayStateSnapshot
    {
        [DataMember(Order = 1)] private List<ReplayStateField> fields;

        private ReplayStateSnapshot(IEnumerable<ReplayStateField> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            fields = source.OrderBy(field => field?.Path, StringComparer.Ordinal).ToList();
            ValidateOrThrow(fields);
        }

        public IReadOnlyList<ReplayStateField> Fields => fields;

        public static ReplayStateSnapshot Create(params ReplayStateField[] fields) =>
            new ReplayStateSnapshot(fields);

        public static ReplayStateSnapshot Create(IEnumerable<ReplayStateField> fields) =>
            new ReplayStateSnapshot(fields);

        internal static bool IsValid(ReplayStateSnapshot snapshot)
        {
            if (snapshot?.fields == null) return false;
            try
            {
                ValidateOrThrow(snapshot.fields);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static void ValidateOrThrow(IReadOnlyList<ReplayStateField> values)
        {
            string previous = null;
            for (int index = 0; index < values.Count; index++)
            {
                ReplayStateField field = values[index];
                if (field == null || string.IsNullOrWhiteSpace(field.Path) || field.Value == null ||
                    (previous != null && string.CompareOrdinal(previous, field.Path) >= 0))
                    throw new ArgumentException("回放状态字段必须非空、唯一并按路径排序。", nameof(values));
                previous = field.Path;
            }
        }
    }

    public static class ReplayStateChecksum
    {
        public static string Compute(ReplayStateSnapshot snapshot)
        {
            if (!ReplayStateSnapshot.IsValid(snapshot))
                throw new ArgumentException("回放状态快照无效。", nameof(snapshot));

            var builder = new StringBuilder();
            foreach (ReplayStateField field in snapshot.Fields)
            {
                builder.Append(field.Path.Length).Append(':').Append(field.Path)
                    .Append('=').Append(field.Value.Length).Append(':').Append(field.Value).Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }
    }

    [DataContract]
    public sealed class RunStateCheckpoint
    {
        [DataMember(Order = 1)] private long tick;
        [DataMember(Order = 2)] private string checksum;
        [DataMember(Order = 3)] private ReplayStateSnapshot state;

        internal RunStateCheckpoint(long tick, ReplayStateSnapshot state)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            this.tick = tick;
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            checksum = ReplayStateChecksum.Compute(state);
        }

        public long Tick => tick;
        public string Checksum => checksum;
        public ReplayStateSnapshot State => state;
    }

    public interface IRunReplayAdapter
    {
        void Begin(long runSeed, IReadOnlyList<RunEvent> configurationEvents);
        void Apply(RunEvent runEvent);
        ReplayStateSnapshot CaptureState();
    }

    public enum RunReplayStatus
    {
        Ready,
        Running,
        Completed,
        Diverged
    }

    public sealed class ReplayDivergence
    {
        internal ReplayDivergence(
            long tick,
            string fieldPath,
            string expected,
            string actual)
        {
            Tick = tick;
            FieldPath = fieldPath ?? "checksum";
            Expected = expected ?? "<missing>";
            Actual = actual ?? "<missing>";
            string[] segments = FieldPath.Split('/');
            System = segments.Length > 0 ? segments[0] : "unknown";
            EntityId = segments.Length > 2 && string.Equals(segments[0], "enemy", StringComparison.Ordinal)
                ? segments[1]
                : string.Empty;
        }

        public long Tick { get; }
        public string System { get; }
        public string EntityId { get; }
        public string FieldPath { get; }
        public string Expected { get; }
        public string Actual { get; }
        public string Summary => $"Tick {Tick}: {FieldPath} 预期 {Expected}，实际 {Actual}";

        public static ReplayDivergence CreateForDiagnostics(
            long tick,
            string fieldPath,
            string expected,
            string actual) =>
            new ReplayDivergence(tick, fieldPath, expected, actual);
    }

    public readonly struct ReplayAdvanceResult
    {
        internal ReplayAdvanceResult(RunReplayStatus status, long tick, ReplayDivergence divergence)
        {
            Status = status;
            Tick = tick;
            Divergence = divergence;
        }

        public RunReplayStatus Status { get; }
        public long Tick { get; }
        public ReplayDivergence Divergence { get; }
    }

    /// <summary>
    /// Replays recorded logical events and validates quantized gameplay state.
    /// It intentionally does not promise deterministic physics or pixel-identical rendering.
    /// </summary>
    public sealed class RunReplayPlayer
    {
        private readonly RunRecord record;
        private readonly IRunReplayAdapter adapter;
        private int nextEvent;
        private int nextCheckpoint;
        private long currentTick = -1;
        private RunReplayStatus status = RunReplayStatus.Ready;
        private ReplayDivergence divergence;

        public RunReplayPlayer(RunRecord record, IRunReplayAdapter adapter)
        {
            RunRecordValidationResult validation = DeterministicRunValidator.Validate(record);
            if (!validation.Success) throw new ArgumentException(validation.Error, nameof(record));
            this.record = record;
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public RunReplayStatus Status => status;
        public long CurrentTick => currentTick;
        public ReplayDivergence Divergence => divergence;

        public ReplayAdvanceResult AdvanceTo(long targetTick)
        {
            if (targetTick < 0 || targetTick < currentTick)
                throw new ArgumentOutOfRangeException(nameof(targetTick));
            if (status == RunReplayStatus.Diverged || status == RunReplayStatus.Completed)
                return Result();

            EnsureStarted();

            long boundedTarget = Math.Min(targetTick, record.CurrentTick);
            while (nextCheckpoint < record.Checkpoints.Count &&
                   record.Checkpoints[nextCheckpoint].Tick <= boundedTarget)
            {
                RunStateCheckpoint expected = record.Checkpoints[nextCheckpoint];
                if (!ApplyEventsThrough(expected.Tick)) return Result();
                if (!VerifyCheckpoint(expected)) return Result();
                nextCheckpoint++;
            }

            if (!ApplyEventsThrough(boundedTarget)) return Result();

            currentTick = boundedTarget;
            if (boundedTarget >= record.CurrentTick && nextEvent >= record.Events.Count &&
                nextCheckpoint >= record.Checkpoints.Count)
                status = RunReplayStatus.Completed;
            return Result();
        }

        public ReplayAdvanceResult DispatchTo(long targetTick)
        {
            if (targetTick < 0 || targetTick < currentTick)
                throw new ArgumentOutOfRangeException(nameof(targetTick));
            if (status == RunReplayStatus.Diverged || status == RunReplayStatus.Completed)
                return Result();
            EnsureStarted();
            long boundedTarget = Math.Min(targetTick, record.CurrentTick);
            if (!ApplyEventsThrough(boundedTarget)) return Result();
            currentTick = boundedTarget;
            if (boundedTarget >= record.CurrentTick && record.Checkpoints.Count == 0)
                status = RunReplayStatus.Completed;
            return Result();
        }

        public ReplayAdvanceResult VerifyThrough(long verifiedTick)
        {
            if (verifiedTick < 0 || verifiedTick > currentTick)
                throw new ArgumentOutOfRangeException(nameof(verifiedTick));
            if (status == RunReplayStatus.Diverged || status == RunReplayStatus.Completed)
                return Result();

            while (nextCheckpoint < record.Checkpoints.Count &&
                   record.Checkpoints[nextCheckpoint].Tick <= verifiedTick)
            {
                if (!VerifyCheckpoint(record.Checkpoints[nextCheckpoint])) return Result();
                nextCheckpoint++;
            }
            if (currentTick >= record.CurrentTick && nextEvent >= record.Events.Count &&
                nextCheckpoint >= record.Checkpoints.Count)
                status = RunReplayStatus.Completed;
            return Result();
        }

        private ReplayAdvanceResult Result() =>
            new ReplayAdvanceResult(status, currentTick, divergence);

        private void EnsureStarted()
        {
            if (status != RunReplayStatus.Ready) return;
            RunEvent[] configuration = record.Events
                .Where(value => value.Tick == 0 && value.Type == RunEventType.WaveGenerated)
                .ToArray();
            adapter.Begin(record.RunSeed, configuration);
            status = RunReplayStatus.Running;
        }

        private bool VerifyCheckpoint(RunStateCheckpoint expected)
        {
            ReplayStateSnapshot actual = adapter.CaptureState();
            string actualChecksum = ReplayStateChecksum.Compute(actual);
            if (string.Equals(expected.Checksum, actualChecksum, StringComparison.Ordinal))
                return true;
            divergence = FindFirstDifference(expected.Tick, expected.State, actual,
                expected.Checksum, actualChecksum);
            status = RunReplayStatus.Diverged;
            currentTick = expected.Tick;
            return false;
        }

        private bool ApplyEventsThrough(long tick)
        {
            while (nextEvent < record.Events.Count && record.Events[nextEvent].Tick <= tick)
            {
                try
                {
                    adapter.Apply(record.Events[nextEvent]);
                }
                catch (Exception exception)
                {
                    RunEvent failed = record.Events[nextEvent];
                    divergence = new ReplayDivergence(failed.Tick,
                        "event/" + failed.Sequence.ToString(CultureInfo.InvariantCulture) + "/apply",
                        failed.Type.ToString(), exception.Message);
                    status = RunReplayStatus.Diverged;
                    currentTick = failed.Tick;
                    return false;
                }
                nextEvent++;
            }
            return true;
        }

        private static ReplayDivergence FindFirstDifference(
            long tick,
            ReplayStateSnapshot expected,
            ReplayStateSnapshot actual,
            string expectedChecksum,
            string actualChecksum)
        {
            int left = 0;
            int right = 0;
            while (left < expected.Fields.Count || right < actual.Fields.Count)
            {
                ReplayStateField expectedField = left < expected.Fields.Count ? expected.Fields[left] : null;
                ReplayStateField actualField = right < actual.Fields.Count ? actual.Fields[right] : null;
                int order = expectedField == null ? 1 : actualField == null ? -1 :
                    string.CompareOrdinal(expectedField.Path, actualField.Path);
                if (order == 0)
                {
                    if (!string.Equals(expectedField.Value, actualField.Value, StringComparison.Ordinal))
                        return new ReplayDivergence(tick, expectedField.Path,
                            expectedField.Value, actualField.Value);
                    left++;
                    right++;
                }
                else if (order < 0)
                {
                    return new ReplayDivergence(tick, expectedField.Path, expectedField.Value, null);
                }
                else
                {
                    return new ReplayDivergence(tick, actualField.Path, null, actualField.Value);
                }
            }
            return new ReplayDivergence(tick, "checksum", expectedChecksum, actualChecksum);
        }
    }
}
