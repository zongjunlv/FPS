using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FPS.Determinism
{
    [Flags]
    public enum ReplayTimelineEventKind
    {
        None = 0,
        Input = 1 << 0,
        Shot = 1 << 1,
        EnemyKilled = 1 << 2,
        Upgrade = 1 << 3,
        Loot = 1 << 4,
        Wave = 1 << 5,
        EnemySpawn = 1 << 6,
        Elite = 1 << 7,
        ChecksumDivergence = 1 << 8,
        AiDecision = 1 << 9,
        CombatDirector = 1 << 10,
        All = Input | Shot | EnemyKilled | Upgrade | Loot | Wave |
              EnemySpawn | Elite | ChecksumDivergence | AiDecision |
              CombatDirector
    }

    public readonly struct ReplayTimelineFilter
    {
        private ReplayTimelineFilter(ReplayTimelineEventKind kinds)
        {
            Kinds = kinds;
        }

        public ReplayTimelineEventKind Kinds { get; }
        public static ReplayTimelineFilter All => new ReplayTimelineFilter(ReplayTimelineEventKind.All);

        public static ReplayTimelineFilter Only(params ReplayTimelineEventKind[] kinds)
        {
            ReplayTimelineEventKind combined = ReplayTimelineEventKind.None;
            if (kinds != null)
            {
                foreach (ReplayTimelineEventKind kind in kinds) combined |= kind;
            }
            return new ReplayTimelineFilter(combined);
        }

        internal bool Includes(ReplayTimelineEventKind kind) => (Kinds & kind) != 0;
    }

    public sealed class ReplayTimelineItem
    {
        internal ReplayTimelineItem(
            long tick,
            long sequence,
            ReplayTimelineEventKind kind,
            string title,
            string entityId,
            string payload,
            ReplayDivergence divergence)
        {
            Tick = tick;
            Sequence = sequence;
            Kind = kind;
            Title = title;
            EntityId = entityId ?? string.Empty;
            Payload = payload ?? string.Empty;
            Divergence = divergence;
        }

        public long Tick { get; }
        public long Sequence { get; }
        public ReplayTimelineEventKind Kind { get; }
        public string Title { get; }
        public string EntityId { get; }
        public string Payload { get; }
        public ReplayDivergence Divergence { get; }
    }

    public sealed class ReplayTimelinePage
    {
        internal ReplayTimelinePage(
            IReadOnlyList<ReplayTimelineItem> items,
            int pageIndex,
            int pageSize,
            int totalItems)
        {
            Items = items;
            PageIndex = pageIndex;
            PageSize = pageSize;
            TotalItems = totalItems;
            TotalPages = totalItems == 0 ? 0 : (totalItems + pageSize - 1) / pageSize;
        }

        public IReadOnlyList<ReplayTimelineItem> Items { get; }
        public int PageIndex { get; }
        public int PageSize { get; }
        public int TotalItems { get; }
        public int TotalPages { get; }
    }

    public sealed class ReplayTimelineStateSummary
    {
        internal ReplayTimelineStateSummary(RunStateCheckpoint checkpoint)
        {
            Tick = checkpoint?.Tick ?? -1;
            Fields = checkpoint?.State?.Fields ?? Array.Empty<ReplayStateField>();
        }

        public long Tick { get; }
        public IReadOnlyList<ReplayStateField> Fields { get; }
        public bool Exists => Tick >= 0;
    }

    public sealed class ReplayTimelineDetails
    {
        internal ReplayTimelineDetails(
            ReplayTimelineItem item,
            IReadOnlyDictionary<string, string> payload,
            ReplayTimelineStateSummary before,
            ReplayTimelineStateSummary after,
            IReadOnlyList<string> changedFields)
        {
            Item = item;
            EntityId = item.EntityId;
            Payload = payload;
            Before = before;
            After = after;
            ChangedFields = changedFields;
        }

        public ReplayTimelineItem Item { get; }
        public string EntityId { get; }
        public IReadOnlyDictionary<string, string> Payload { get; }
        public ReplayTimelineStateSummary Before { get; }
        public ReplayTimelineStateSummary After { get; }
        public IReadOnlyList<string> ChangedFields { get; }
    }

    public readonly struct ReplayTimelineLocation
    {
        internal ReplayTimelineLocation(bool found, int pageIndex, int indexInPage, ReplayTimelineItem item)
        {
            Found = found;
            PageIndex = pageIndex;
            IndexInPage = indexInPage;
            Item = item;
        }

        public bool Found { get; }
        public int PageIndex { get; }
        public int IndexInPage { get; }
        public ReplayTimelineItem Item { get; }
    }

    public sealed class ReplayTimelineLoadResult
    {
        private ReplayTimelineLoadResult(
            bool success,
            ReplayTimeline timeline,
            int detectedSchemaVersion,
            string message)
        {
            Success = success;
            Timeline = timeline;
            DetectedSchemaVersion = detectedSchemaVersion;
            Message = message;
        }

        public bool Success { get; }
        public ReplayTimeline Timeline { get; }
        public int DetectedSchemaVersion { get; }
        public string Message { get; }

        internal static ReplayTimelineLoadResult Loaded(ReplayTimeline timeline) =>
            new ReplayTimelineLoadResult(true, timeline, RunRecord.CurrentSchemaVersion, "记录已加载。");

        internal static ReplayTimelineLoadResult Failed(int version, string message) =>
            new ReplayTimelineLoadResult(false, null, version, message);
    }

    /// <summary>
    /// Read-only, paged query model for replay diagnostics. UI adapters request only
    /// one page and one selected event's checkpoint details at a time.
    /// </summary>
    public sealed class ReplayTimeline
    {
        public const int MaximumPageSize = 200;
        private static readonly Regex SchemaVersionPattern = new Regex(
            "\\\"schemaVersion\\\"\\s*:\\s*(?<version>-?\\d+)",
            RegexOptions.CultureInvariant);

        private readonly RunRecord record;
        private readonly List<ReplayTimelineItem> items;

        public ReplayTimeline(RunRecord record, ReplayDivergence divergence = null)
        {
            RunRecordValidationResult validation = DeterministicRunValidator.Validate(record);
            if (!validation.Success) throw new ArgumentException(validation.Error, nameof(record));
            this.record = record;
            items = BuildItems(record, divergence);
        }

        public long RunSeed => record.RunSeed;
        public long LastTick => record.CurrentTick;
        public int ItemCount => items.Count;

        public ReplayTimelinePage Query(
            ReplayTimelineFilter filter,
            int pageIndex,
            int pageSize)
        {
            if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
            if (pageSize <= 0 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize),
                    $"每页数量必须在 1 到 {MaximumPageSize} 之间。");

            var page = new List<ReplayTimelineItem>(pageSize);
            int total = 0;
            int first = checked(pageIndex * pageSize);
            foreach (ReplayTimelineItem item in items)
            {
                if (!filter.Includes(item.Kind)) continue;
                if (total >= first && page.Count < pageSize) page.Add(item);
                total++;
            }
            int lastPage = total == 0 ? 0 : (total - 1) / pageSize;
            int boundedPage = total == 0 ? 0 : Math.Min(pageIndex, lastPage);
            if (boundedPage != pageIndex)
                return Query(filter, boundedPage, pageSize);
            return new ReplayTimelinePage(page.AsReadOnly(), boundedPage, pageSize, total);
        }

        public ReplayTimelineDetails Describe(ReplayTimelineItem item)
        {
            if (item == null || !items.Contains(item))
                throw new ArgumentException("事件不属于当前时间轴。", nameof(item));

            RunStateCheckpoint before = null;
            RunStateCheckpoint after = null;
            foreach (RunStateCheckpoint checkpoint in record.Checkpoints)
            {
                if (checkpoint.Tick < item.Tick) before = checkpoint;
                if (checkpoint.Tick >= item.Tick)
                {
                    after = checkpoint;
                    break;
                }
            }

            IReadOnlyDictionary<string, string> payload = item.Divergence == null
                ? StableEventPayload.Parse(item.Payload)
                : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
                {
                    { "field", item.Divergence.FieldPath },
                    { "expected", item.Divergence.Expected },
                    { "actual", item.Divergence.Actual }
                });
            return new ReplayTimelineDetails(
                item,
                payload,
                new ReplayTimelineStateSummary(before),
                new ReplayTimelineStateSummary(after),
                Compare(before?.State, after?.State));
        }

        public ReplayTimelineLocation FindFirstDivergence(int pageSize)
        {
            return FindFirstDivergence(ReplayTimelineFilter.All, pageSize);
        }

        public ReplayTimelineLocation FindFirstDivergence(
            ReplayTimelineFilter filter,
            int pageSize)
        {
            if (pageSize <= 0 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            int filteredIndex = 0;
            foreach (ReplayTimelineItem item in items)
            {
                if (!filter.Includes(item.Kind)) continue;
                if (item.Kind == ReplayTimelineEventKind.ChecksumDivergence)
                    return new ReplayTimelineLocation(
                        true, filteredIndex / pageSize, filteredIndex % pageSize, item);
                filteredIndex++;
            }
            return new ReplayTimelineLocation(false, 0, -1, null);
        }

        public static ReplayTimelineLoadResult LoadJson(string json, ReplayDivergence divergence = null)
        {
            int version = DetectSchemaVersion(json);
            if (version >= 0 && version != RunRecord.CurrentSchemaVersion)
            {
                return ReplayTimelineLoadResult.Failed(version,
                    $"记录版本 {version} 与当前版本 {RunRecord.CurrentSchemaVersion} 不兼容。请先运行对应版本的迁移工具，再打开时间轴。");
            }

            try
            {
                return ReplayTimelineLoadResult.Loaded(
                    new ReplayTimeline(RunRecordCodec.Import(json), divergence));
            }
            catch (InvalidDataException exception)
            {
                return ReplayTimelineLoadResult.Failed(version,
                    "记录无法加载：" + exception.Message + " 请确认记录完整，或先迁移到当前版本。");
            }
        }

        private static int DetectSchemaVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            Match match = SchemaVersionPattern.Match(json);
            return match.Success && int.TryParse(match.Groups["version"].Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                ? version
                : -1;
        }

        private static List<ReplayTimelineItem> BuildItems(
            RunRecord record,
            ReplayDivergence divergence)
        {
            var result = new List<ReplayTimelineItem>(record.Events.Count + (divergence == null ? 0 : 1));
            foreach (RunEvent runEvent in record.Events)
            {
                ReplayTimelineEventKind kind = Map(runEvent.Type);
                IReadOnlyDictionary<string, string> payload = StableEventPayload.Parse(runEvent.Payload);
                result.Add(new ReplayTimelineItem(
                    runEvent.Tick,
                    runEvent.Sequence,
                    kind,
                    Title(kind),
                    FindEntityId(payload),
                    runEvent.Payload,
                    null));
            }
            if (divergence != null)
            {
                result.Add(new ReplayTimelineItem(
                    divergence.Tick,
                    long.MaxValue,
                    ReplayTimelineEventKind.ChecksumDivergence,
                    "Checksum 偏差",
                    divergence.EntityId,
                    string.Empty,
                    divergence));
            }
            result.Sort((left, right) =>
            {
                int tickOrder = left.Tick.CompareTo(right.Tick);
                return tickOrder != 0 ? tickOrder : left.Sequence.CompareTo(right.Sequence);
            });
            return result;
        }

        private static ReplayTimelineEventKind Map(RunEventType type)
        {
            switch (type)
            {
                case RunEventType.InputSampled: return ReplayTimelineEventKind.Input;
                case RunEventType.ShotFired: return ReplayTimelineEventKind.Shot;
                case RunEventType.EnemyKilled: return ReplayTimelineEventKind.EnemyKilled;
                case RunEventType.UpgradeSelected: return ReplayTimelineEventKind.Upgrade;
                case RunEventType.LootGenerated: return ReplayTimelineEventKind.Loot;
                case RunEventType.WaveGenerated: return ReplayTimelineEventKind.Wave;
                case RunEventType.WaveTransition: return ReplayTimelineEventKind.Wave;
                case RunEventType.EnemySpawned: return ReplayTimelineEventKind.EnemySpawn;
                case RunEventType.EliteGenerated: return ReplayTimelineEventKind.Elite;
                case RunEventType.AiDecision: return ReplayTimelineEventKind.AiDecision;
                case RunEventType.CombatDirectorDecision:
                    return ReplayTimelineEventKind.CombatDirector;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static string Title(ReplayTimelineEventKind kind)
        {
            switch (kind)
            {
                case ReplayTimelineEventKind.Input: return "输入";
                case ReplayTimelineEventKind.Shot: return "射击";
                case ReplayTimelineEventKind.EnemyKilled: return "击杀";
                case ReplayTimelineEventKind.Upgrade: return "升级";
                case ReplayTimelineEventKind.Loot: return "掉落";
                case ReplayTimelineEventKind.Wave: return "波次";
                case ReplayTimelineEventKind.EnemySpawn: return "敌人出生";
                case ReplayTimelineEventKind.Elite: return "精英生成";
                case ReplayTimelineEventKind.AiDecision: return "AI 决策";
                case ReplayTimelineEventKind.CombatDirector: return "战斗导演";
                default: return kind.ToString();
            }
        }

        private static string FindEntityId(IReadOnlyDictionary<string, string> payload)
        {
            if (payload.TryGetValue("spawnId", out string spawnId)) return spawnId;
            if (payload.TryGetValue("sourceId", out string sourceId)) return sourceId;
            if (payload.TryGetValue("id", out string id)) return id;
            return string.Empty;
        }

        private static IReadOnlyList<string> Compare(
            ReplayStateSnapshot before,
            ReplayStateSnapshot after)
        {
            if (before == null || after == null) return Array.Empty<string>();
            var left = before.Fields.ToDictionary(field => field.Path, field => field.Value,
                StringComparer.Ordinal);
            var right = after.Fields.ToDictionary(field => field.Path, field => field.Value,
                StringComparer.Ordinal);
            var paths = new SortedSet<string>(left.Keys, StringComparer.Ordinal);
            paths.UnionWith(right.Keys);
            var changes = new List<string>();
            foreach (string path in paths)
            {
                left.TryGetValue(path, out string oldValue);
                right.TryGetValue(path, out string newValue);
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    changes.Add($"{path}: {oldValue ?? "<missing>"} → {newValue ?? "<missing>"}");
            }
            return changes.AsReadOnly();
        }
    }
}
