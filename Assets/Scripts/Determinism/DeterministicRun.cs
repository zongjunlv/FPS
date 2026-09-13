using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FPS.Determinism
{
    public enum RunRandomStream
    {
        Upgrade,
        Loot,
        Wave,
        Elite,
        Director
    }

    public enum RunEventType
    {
        InputSampled,
        UpgradeSelected,
        LootGenerated,
        WaveGenerated,
        EliteGenerated,
        EnemySpawned,
        ShotFired,
        EnemyKilled,
        WaveTransition,
        AiDecision,
        CombatDirectorDecision
    }

    public readonly struct RunPayloadField
    {
        private RunPayloadField(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("事件字段名不能为空。", nameof(key));
            Key = key;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Key { get; }
        public string Value { get; }

        public static RunPayloadField Text(string key, string value) => new RunPayloadField(key, value ?? string.Empty);
        public static RunPayloadField Number(string key, long value) => new RunPayloadField(key, value.ToString(CultureInfo.InvariantCulture));
        public static RunPayloadField Flag(string key, bool value) => new RunPayloadField(key, value ? "true" : "false");
    }

    public static class StableEventPayload
    {
        public static string Create(params RunPayloadField[] fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            var ordered = fields.OrderBy(field => field.Key, StringComparer.Ordinal).ToArray();
            for (int index = 1; index < ordered.Length; index++)
            {
                if (string.Equals(ordered[index - 1].Key, ordered[index].Key, StringComparison.Ordinal))
                    throw new ArgumentException("事件字段名不能重复。", nameof(fields));
            }

            return string.Join("&", ordered.Select(field => Escape(field.Key) + "=" + Escape(field.Value)));
        }

        internal static bool IsCanonical(string payload)
        {
            if (payload == null) return false;
            if (payload.Length == 0) return true;

            string[] pairs = payload.Split('&');
            string previousKey = null;
            foreach (string pair in pairs)
            {
                int separator = pair.IndexOf('=');
                if (separator <= 0) return false;
                string key;
                string value;
                try
                {
                    key = Unescape(pair.Substring(0, separator));
                    value = Unescape(pair.Substring(separator + 1));
                }
                catch (FormatException)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(key) ||
                    (previousKey != null && string.CompareOrdinal(previousKey, key) >= 0) ||
                    pair != Escape(key) + "=" + Escape(value))
                    return false;
                previousKey = key;
            }

            return true;
        }

        public static IReadOnlyDictionary<string, string> Parse(string payload)
        {
            if (!IsCanonical(payload))
                throw new FormatException("事件 payload 不是稳定格式。");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (payload.Length == 0) return result;
            foreach (string pair in payload.Split('&'))
            {
                int separator = pair.IndexOf('=');
                result.Add(
                    Unescape(pair.Substring(0, separator)),
                    Unescape(pair.Substring(separator + 1)));
            }
            return result;
        }

        private static string Escape(string value) => Uri.EscapeDataString(value);
        private static string Unescape(string value) => Uri.UnescapeDataString(value);
    }

    public sealed class DeterministicRandom
    {
        private ulong state;

        internal DeterministicRandom(ulong seed)
        {
            state = seed;
        }

        public ulong State => state;

        public void RestoreState(ulong restoredState)
        {
            state = restoredState;
        }

        public uint NextUInt32()
        {
            // SplitMix64 has a fully specified transition and output, unlike System.Random.
            state += 0x9E3779B97F4A7C15UL;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (uint)(value >> 32);
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));

            uint bound = (uint)exclusiveMaximum;
            uint threshold = unchecked((uint)(0U - bound)) % bound;
            uint value;
            do value = NextUInt32(); while (value < threshold);
            return (int)(value % bound);
        }
    }

    public sealed class NamedRandomStreams
    {
        private readonly long runSeed;
        private readonly Dictionary<RunRandomStream, DeterministicRandom> streams =
            new Dictionary<RunRandomStream, DeterministicRandom>();

        public NamedRandomStreams(long runSeed)
        {
            this.runSeed = runSeed;
            foreach (RunRandomStream stream in Enum.GetValues(typeof(RunRandomStream)))
                streams.Add(stream, new DeterministicRandom(Derive(runSeed, stream.ToString().ToLowerInvariant(), null)));
        }

        public DeterministicRandom Get(RunRandomStream stream) => streams[stream];

        public DeterministicRandom Fork(RunRandomStream stream, string contextKey)
        {
            if (string.IsNullOrWhiteSpace(contextKey)) throw new ArgumentException("上下文键不能为空。", nameof(contextKey));
            return new DeterministicRandom(Derive(runSeed, stream.ToString().ToLowerInvariant(), contextKey));
        }

        private static ulong Derive(long seed, string name, string contextKey)
        {
            // FNV-1a over an explicit little-endian seed and UTF-8 stream name.
            ulong hash = 14695981039346656037UL;
            ulong bits = unchecked((ulong)seed);
            for (int index = 0; index < 8; index++)
            {
                hash ^= (byte)(bits >> (index * 8));
                hash *= 1099511628211UL;
            }

            foreach (byte value in Encoding.UTF8.GetBytes(name))
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }

            if (contextKey != null)
            {
                hash ^= 0xff;
                hash *= 1099511628211UL;
                foreach (byte value in Encoding.UTF8.GetBytes(contextKey))
                {
                    hash ^= value;
                    hash *= 1099511628211UL;
                }
            }

            return hash;
        }
    }

    [DataContract]
    public sealed class RunEvent
    {
        [DataMember(Order = 1)] private long tick;
        [DataMember(Order = 2)] private long sequence;
        [DataMember(Order = 3)] private RunEventType type;
        [DataMember(Order = 4)] private string payload;

        internal RunEvent(long tick, long sequence, RunEventType type, string payload)
        {
            this.tick = tick;
            this.sequence = sequence;
            this.type = type;
            this.payload = payload;
        }

        public long Tick => tick;
        public long Sequence => sequence;
        public RunEventType Type => type;
        public string Payload => payload;
    }

    [DataContract]
    public sealed class RunRecord
    {
        public const int CurrentSchemaVersion = 1;

        [DataMember(Order = 1)] private int schemaVersion;
        [DataMember(Order = 2)] private long runSeed;
        [DataMember(Order = 3)] private long currentTick;
        [DataMember(Order = 4)] private List<RunEvent> events;
        [DataMember(Order = 5, EmitDefaultValue = false)] private List<RunStateCheckpoint> checkpoints;

        public RunRecord(long runSeed)
        {
            schemaVersion = CurrentSchemaVersion;
            this.runSeed = runSeed;
            events = new List<RunEvent>();
            checkpoints = new List<RunStateCheckpoint>();
        }

        public int SchemaVersion => schemaVersion;
        public long RunSeed => runSeed;
        public long CurrentTick => currentTick;
        public IReadOnlyList<RunEvent> Events => events;
        public IReadOnlyList<RunStateCheckpoint> Checkpoints =>
            checkpoints ?? (IReadOnlyList<RunStateCheckpoint>)Array.Empty<RunStateCheckpoint>();

        internal void Advance(long ticks)
        {
            if (ticks <= 0) throw new ArgumentOutOfRangeException(nameof(ticks), "逻辑 Tick 增量必须大于零。");
            checked { currentTick += ticks; }
        }

        internal void Append(RunEventType type, string payload)
        {
            if (!Enum.IsDefined(typeof(RunEventType), type)) throw new ArgumentOutOfRangeException(nameof(type));
            if (!StableEventPayload.IsCanonical(payload)) throw new ArgumentException("事件 payload 不是稳定格式。", nameof(payload));
            events.Add(new RunEvent(currentTick, events.Count, type, payload));
        }

        internal void AppendCheckpoint(ReplayStateSnapshot state)
        {
            checkpoints ??= new List<RunStateCheckpoint>();
            if (checkpoints.Count > 0 && checkpoints[checkpoints.Count - 1].Tick >= currentTick)
                throw new InvalidOperationException("同一逻辑 Tick 只能记录一个状态 Checkpoint。");
            checkpoints.Add(new RunStateCheckpoint(currentTick, state));
        }

        internal IList<RunEvent> MutableEvents => events;
        internal IList<RunStateCheckpoint> MutableCheckpoints => checkpoints;
    }

    public sealed class DeterministicRun
    {
        private readonly RunRecord record;

        public DeterministicRun(long runSeed)
        {
            record = new RunRecord(runSeed);
            Random = new NamedRandomStreams(runSeed);
        }

        public NamedRandomStreams Random { get; }
        public RunRecord Record => record;
        public long Tick => record.CurrentTick;

        public void AdvanceTick(long ticks = 1) => record.Advance(ticks);
        public void RecordEvent(RunEventType type, string payload) => record.Append(type, payload);
        public void RecordCheckpoint(ReplayStateSnapshot state) => record.AppendCheckpoint(state);
    }

    public sealed class RunRecordValidationResult
    {
        private RunRecordValidationResult(bool success, int divergenceIndex, string error)
        {
            Success = success;
            DivergenceIndex = divergenceIndex;
            Error = error;
        }

        public bool Success { get; }
        public int DivergenceIndex { get; }
        public string Error { get; }

        internal static RunRecordValidationResult Passed() => new RunRecordValidationResult(true, -1, null);
        internal static RunRecordValidationResult Failed(string error, int index = -1) =>
            new RunRecordValidationResult(false, index, error);
    }

    public static class RunRecordCodec
    {
        public const int MaximumBytes = 4 * 1024 * 1024;

        public static string Export(RunRecord record)
        {
            RunRecordValidationResult validation = DeterministicRunValidator.Validate(record);
            if (!validation.Success) throw new InvalidDataException(validation.Error);

            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RunRecord)).WriteObject(stream, record);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static RunRecord Import(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes)
                throw new InvalidDataException("RunRecord 为空或超出大小限制。");

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var record = (RunRecord)new DataContractJsonSerializer(typeof(RunRecord)).ReadObject(stream);
                    RunRecordValidationResult validation = DeterministicRunValidator.Validate(record);
                    if (!validation.Success) throw new InvalidDataException(validation.Error);
                    return record;
                }
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException("RunRecord 无法解析。", exception);
            }
        }
    }

    public static class DeterministicRunValidator
    {
        public static RunRecordValidationResult Validate(RunRecord record)
        {
            if (record == null) return RunRecordValidationResult.Failed("RunRecord 不能为空。");
            if (record.SchemaVersion != RunRecord.CurrentSchemaVersion)
                return RunRecordValidationResult.Failed("不支持的 RunRecord 版本。");
            if (record.CurrentTick < 0 || record.MutableEvents == null)
                return RunRecordValidationResult.Failed("RunRecord 状态无效。");

            long previousTick = 0;
            for (int index = 0; index < record.MutableEvents.Count; index++)
            {
                RunEvent current = record.MutableEvents[index];
                if (current == null || current.Sequence != index || current.Tick < previousTick ||
                    current.Tick > record.CurrentTick || !Enum.IsDefined(typeof(RunEventType), current.Type) ||
                    !StableEventPayload.IsCanonical(current.Payload))
                    return RunRecordValidationResult.Failed("事件序列结构无效。", index);
                previousTick = current.Tick;
            }

            long previousCheckpointTick = -1;
            IReadOnlyList<RunStateCheckpoint> checkpoints = record.Checkpoints;
            for (int index = 0; index < checkpoints.Count; index++)
            {
                RunStateCheckpoint checkpoint = checkpoints[index];
                if (checkpoint == null || checkpoint.Tick <= previousCheckpointTick ||
                    checkpoint.Tick > record.CurrentTick ||
                    !ReplayStateSnapshot.IsValid(checkpoint.State) ||
                    !string.Equals(checkpoint.Checksum,
                        ReplayStateChecksum.Compute(checkpoint.State), StringComparison.Ordinal))
                    return RunRecordValidationResult.Failed("状态 Checkpoint 结构无效。", index);
                previousCheckpointTick = checkpoint.Tick;
            }

            return RunRecordValidationResult.Passed();
        }

        public static RunRecordValidationResult Compare(RunRecord expected, RunRecord actual)
        {
            RunRecordValidationResult expectedValidation = Validate(expected);
            if (!expectedValidation.Success) return expectedValidation;
            RunRecordValidationResult actualValidation = Validate(actual);
            if (!actualValidation.Success) return actualValidation;
            if (expected.RunSeed != actual.RunSeed)
                return RunRecordValidationResult.Failed("Run seed 不一致。");

            int sharedCount = Math.Min(expected.Events.Count, actual.Events.Count);
            for (int index = 0; index < sharedCount; index++)
            {
                RunEvent left = expected.Events[index];
                RunEvent right = actual.Events[index];
                if (left.Tick != right.Tick)
                    return RunRecordValidationResult.Failed("事件 tick 不一致。", index);
                if (left.Type != right.Type)
                    return RunRecordValidationResult.Failed("事件 type 不一致。", index);
                if (!string.Equals(left.Payload, right.Payload, StringComparison.Ordinal))
                    return RunRecordValidationResult.Failed("事件 payload 不一致。", index);
            }

            if (expected.Events.Count != actual.Events.Count)
                return RunRecordValidationResult.Failed("确定性事件数量不一致。", sharedCount);
            if (expected.CurrentTick != actual.CurrentTick)
                return RunRecordValidationResult.Failed("最终逻辑 Tick 不一致。", sharedCount);
            if (expected.Checkpoints.Count != actual.Checkpoints.Count)
                return RunRecordValidationResult.Failed("状态 Checkpoint 数量不一致。", sharedCount);
            for (int index = 0; index < expected.Checkpoints.Count; index++)
            {
                RunStateCheckpoint left = expected.Checkpoints[index];
                RunStateCheckpoint right = actual.Checkpoints[index];
                if (left.Tick != right.Tick ||
                    !string.Equals(left.Checksum, right.Checksum, StringComparison.Ordinal))
                    return RunRecordValidationResult.Failed("状态 Checkpoint 不一致。", index);
            }
            return RunRecordValidationResult.Passed();
        }
    }
}
