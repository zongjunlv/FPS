using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FPS.Determinism;
using FPS.SaveGame;
using UnityEngine;

[Flags]
public enum CombatAreaModuleKindMask
{
    None = 0,
    Spawn = 1 << 0,
    Combat = 1 << 1,
    Event = 1 << 2,
    Extraction = 1 << 3,
    All = Spawn | Combat | Event | Extraction
}

public enum CombatAreaModuleKind
{
    Spawn,
    Combat,
    Event,
    Extraction
}

public enum LayoutConnectorDirection
{
    North,
    East,
    South,
    West
}

[Serializable]
public sealed class LayoutConnectorDefinition
{
    [SerializeField] private string stableId;
    [SerializeField] private LayoutConnectorDirection direction;
    [SerializeField] private CombatAreaModuleKindMask allowedNeighbors =
        CombatAreaModuleKindMask.All;

    public LayoutConnectorDefinition(
        string id,
        LayoutConnectorDirection facing,
        CombatAreaModuleKindMask allowed)
    {
        stableId = id?.Trim();
        direction = facing;
        allowedNeighbors = allowed;
    }

    public string StableId => stableId;
    public LayoutConnectorDirection Direction => direction;
    public CombatAreaModuleKindMask AllowedNeighbors => allowedNeighbors;
}

[Serializable]
public sealed class LayoutPointDefinition
{
    [SerializeField] private string stableId;
    [SerializeField] private Vector3 localPosition;

    public LayoutPointDefinition(string id, Vector3 position)
    {
        stableId = id?.Trim();
        localPosition = position;
    }

    public string StableId => stableId;
    public Vector3 LocalPosition => localPosition;
}

public sealed class CombatLayoutPlacement
{
    public CombatLayoutPlacement(
        string instanceId,
        string definitionId,
        CombatAreaModuleKind kind,
        int gridX,
        int gridZ,
        int quarterTurns)
    {
        InstanceId = instanceId;
        DefinitionId = definitionId;
        Kind = kind;
        GridX = gridX;
        GridZ = gridZ;
        QuarterTurns = quarterTurns;
    }

    public string InstanceId { get; }
    public string DefinitionId { get; }
    public CombatAreaModuleKind Kind { get; }
    public int GridX { get; }
    public int GridZ { get; }
    public int QuarterTurns { get; }
}

public sealed class CombatLayoutConnection
{
    public CombatLayoutConnection(
        string fromInstanceId,
        string fromSocketId,
        string toInstanceId,
        string toSocketId)
    {
        FromInstanceId = fromInstanceId;
        FromSocketId = fromSocketId;
        ToInstanceId = toInstanceId;
        ToSocketId = toSocketId;
    }

    public string FromInstanceId { get; }
    public string FromSocketId { get; }
    public string ToInstanceId { get; }
    public string ToSocketId { get; }
}

public sealed class CombatLayoutPlan
{
    public CombatLayoutPlan(
        int runSeed,
        int contentVersion,
        int generatorVersion,
        IEnumerable<CombatLayoutPlacement> placements,
        IEnumerable<CombatLayoutConnection> connections,
        bool usedFallback,
        string diagnostic)
    {
        RunSeed = runSeed;
        ContentVersion = contentVersion;
        GeneratorVersion = generatorVersion;
        Placements = placements?.ToArray() ?? Array.Empty<CombatLayoutPlacement>();
        Connections = connections?.ToArray() ?? Array.Empty<CombatLayoutConnection>();
        UsedFallback = usedFallback;
        Diagnostic = diagnostic ?? string.Empty;
        Fingerprint = CombatLayoutFingerprint.Compute(this);
        LayoutId = usedFallback
            ? "city_new.layout.safe"
            : "city_new.layout." + Fingerprint.Substring(0, 12);
    }

    public int RunSeed { get; }
    public int ContentVersion { get; }
    public int GeneratorVersion { get; }
    public IReadOnlyList<CombatLayoutPlacement> Placements { get; }
    public IReadOnlyList<CombatLayoutConnection> Connections { get; }
    public bool UsedFallback { get; }
    public string Diagnostic { get; }
    public string Fingerprint { get; }
    public string LayoutId { get; }
}

public static class DeterministicModularLayoutGenerator
{
    private const int MaximumAttempts = 24;

    public static CombatLayoutPlan Generate(ModularCombatLayoutSet set, int runSeed)
    {
        if (set == null)
        {
            return EmptyFallback(runSeed, null, "内容校验失败：布局集为空。");
        }
        if (!set.TryValidate(out string catalogError))
        {
            return EmptyFallback(runSeed, set, "内容校验失败：" + catalogError);
        }

        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            DeterministicRandom random = new NamedRandomStreams(runSeed).Fork(
                RunRandomStream.Layout,
                $"content:{set.ContentVersion}|generator:{set.GeneratorVersion}|attempt:{attempt}");
            CombatLayoutPlan candidate = TryBuild(set, runSeed, random);
            if (CombatLayoutGraphValidator.TryValidate(set, candidate, out _))
                return candidate;
        }

        return BuildSafeFallback(
            set,
            runSeed,
            "连接失败：24 次确定性尝试均无法生成无重叠且连通的路线。");
    }

    public static CombatLayoutPlan BuildSafeFallback(
        ModularCombatLayoutSet set,
        int runSeed,
        string diagnostic)
    {
        if (set == null || !set.TryValidate(out _))
            return EmptyFallback(runSeed, set, diagnostic);

        var sequence = new List<CombatAreaModuleDefinition>
        {
            set.Modules.First(module => module.Kind == CombatAreaModuleKind.Spawn)
        };
        sequence.AddRange(set.Modules
            .Where(module => module.Kind == CombatAreaModuleKind.Combat)
            .Take(set.CombatModuleCount));
        sequence.Add(set.Modules.First(module => module.Kind == CombatAreaModuleKind.Event));
        sequence.Add(set.Modules.First(module => module.Kind == CombatAreaModuleKind.Extraction));
        return BuildPlan(set, runSeed, sequence, Enumerable.Repeat(0, sequence.Count).ToArray(),
            usedFallback: true, diagnostic);
    }

    private static CombatLayoutPlan TryBuild(
        ModularCombatLayoutSet set,
        int runSeed,
        DeterministicRandom random)
    {
        var sequence = new List<CombatAreaModuleDefinition>
        {
            Pick(set, CombatAreaModuleKind.Spawn, random)
        };
        var combat = set.Modules.Where(module =>
            module.Kind == CombatAreaModuleKind.Combat).ToList();
        for (int index = 0; index < set.CombatModuleCount; index++)
        {
            int selected = random.NextInt(combat.Count);
            sequence.Add(combat[selected]);
            combat.RemoveAt(selected);
        }
        sequence.Add(Pick(set, CombatAreaModuleKind.Event, random));
        sequence.Add(Pick(set, CombatAreaModuleKind.Extraction, random));

        var directions = new int[sequence.Count];
        var occupied = new HashSet<(int x, int z)> { (0, 0) };
        int x = 0;
        int z = 0;
        int heading = random.NextInt(4);
        for (int index = 1; index < sequence.Count; index++)
        {
            int[] turns = { 0, -1, 1 };
            for (int shuffle = turns.Length - 1; shuffle > 0; shuffle--)
            {
                int other = random.NextInt(shuffle + 1);
                (turns[shuffle], turns[other]) = (turns[other], turns[shuffle]);
            }
            bool placed = false;
            foreach (int turn in turns)
            {
                int nextHeading = (heading + turn + 4) % 4;
                (int dx, int dz) = Offset(nextHeading);
                if (occupied.Contains((x + dx, z + dz))) continue;
                x += dx;
                z += dz;
                heading = nextHeading;
                directions[index] = heading;
                occupied.Add((x, z));
                placed = true;
                break;
            }
            if (!placed)
                return new CombatLayoutPlan(runSeed, set.ContentVersion,
                    set.GeneratorVersion, Array.Empty<CombatLayoutPlacement>(),
                    Array.Empty<CombatLayoutConnection>(), false,
                    "路线发生占用冲突。");
        }
        return BuildPlan(set, runSeed, sequence, directions, false, string.Empty);
    }

    private static CombatLayoutPlan BuildPlan(
        ModularCombatLayoutSet set,
        int runSeed,
        IReadOnlyList<CombatAreaModuleDefinition> sequence,
        IReadOnlyList<int> directions,
        bool usedFallback,
        string diagnostic)
    {
        var placements = new List<CombatLayoutPlacement>(sequence.Count);
        var connections = new List<CombatLayoutConnection>(sequence.Count - 1);
        int x = 0;
        int z = 0;
        for (int index = 0; index < sequence.Count; index++)
        {
            int heading = index == 0 ? directions[Math.Min(1, directions.Count - 1)] : directions[index];
            if (index > 0)
            {
                (int dx, int dz) = Offset(heading);
                x += dx;
                z += dz;
            }
            string instanceId = "module_" + index.ToString("D2");
            placements.Add(new CombatLayoutPlacement(
                instanceId, sequence[index].StableId, sequence[index].Kind,
                x, z, heading));
            if (index == 0) continue;
            int opposite = (heading + 2) % 4;
            connections.Add(new CombatLayoutConnection(
                "module_" + (index - 1).ToString("D2"),
                DirectionId(heading),
                instanceId,
                DirectionId(opposite)));
        }
        return new CombatLayoutPlan(runSeed, set.ContentVersion,
            set.GeneratorVersion, placements, connections, usedFallback,
            diagnostic);
    }

    private static CombatAreaModuleDefinition Pick(
        ModularCombatLayoutSet set,
        CombatAreaModuleKind kind,
        DeterministicRandom random)
    {
        CombatAreaModuleDefinition[] choices = set.Modules
            .Where(module => module.Kind == kind).ToArray();
        return choices[random.NextInt(choices.Length)];
    }

    private static (int x, int z) Offset(int heading) => heading switch
    {
        0 => (0, 1),
        1 => (1, 0),
        2 => (0, -1),
        _ => (-1, 0)
    };

    private static string DirectionId(int heading) => ((LayoutConnectorDirection)heading)
        .ToString().ToLowerInvariant();

    private static CombatLayoutPlan EmptyFallback(
        int runSeed,
        ModularCombatLayoutSet set,
        string diagnostic) => new(
        runSeed,
        set != null ? Math.Max(1, set.ContentVersion) : 1,
        set != null ? Math.Max(1, set.GeneratorVersion) : 1,
        Array.Empty<CombatLayoutPlacement>(),
        Array.Empty<CombatLayoutConnection>(),
        true,
        diagnostic);
}

public static class CombatLayoutReroll
{
    private const int MaximumAttempts = 64;
    private const int GoldenRatioStep = unchecked((int)0x9E3779B9);

    public static int SelectSeedForDifferentRoute(
        ModularCombatLayoutSet set,
        CombatLayoutPlan current,
        int proposedSeed)
    {
        if (set == null || current == null || current.Placements.Count == 0)
            return proposedSeed;

        string currentRoute = DescribeVisibleRoute(current);
        int candidate = proposedSeed;
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            CombatLayoutPlan plan =
                DeterministicModularLayoutGenerator.Generate(set, candidate);
            if (!plan.UsedFallback && plan.Placements.Count > 0 &&
                !string.Equals(
                    DescribeVisibleRoute(plan),
                    currentRoute,
                    StringComparison.Ordinal))
            {
                return candidate;
            }

            candidate = unchecked(candidate + GoldenRatioStep + attempt);
        }

        return proposedSeed;
    }

    public static string DescribeVisibleRoute(CombatLayoutPlan plan)
    {
        if (plan == null) return string.Empty;
        return string.Join("|", plan.Placements.Select(placement =>
            $"{placement.GridX},{placement.GridZ}"));
    }
}

public static class CombatLayoutGraphValidator
{
    public static bool TryValidate(
        ModularCombatLayoutSet set,
        CombatLayoutPlan plan,
        out string error)
    {
        if (set == null || plan == null || plan.Placements.Count == 0)
        {
            error = "布局或模块清单为空。";
            return false;
        }
        var instances = new Dictionary<string, CombatLayoutPlacement>(StringComparer.Ordinal);
        var cells = new HashSet<(int, int)>();
        foreach (CombatLayoutPlacement placement in plan.Placements)
        {
            CombatAreaModuleDefinition definition = set.Find(placement.DefinitionId);
            if (definition == null || definition.Kind != placement.Kind ||
                string.IsNullOrWhiteSpace(placement.InstanceId) ||
                !instances.TryAdd(placement.InstanceId, placement) ||
                placement.QuarterTurns < 0 || placement.QuarterTurns > 3 ||
                !cells.Add((placement.GridX, placement.GridZ)))
            {
                error = $"模块 '{placement?.InstanceId}' 的定义、旋转或占用范围无效。";
                return false;
            }
        }
        if (plan.Placements.Count(item => item.Kind == CombatAreaModuleKind.Spawn) != 1 ||
            plan.Placements.Count(item => item.Kind == CombatAreaModuleKind.Event) != 1 ||
            plan.Placements.Count(item => item.Kind == CombatAreaModuleKind.Extraction) != 1 ||
            plan.Placements.Count(item => item.Kind == CombatAreaModuleKind.Combat) < 2)
        {
            error = "布局必须包含唯一出生区、事件区、撤离区和至少两个战斗区。";
            return false;
        }

        var adjacency = instances.Keys.ToDictionary(
            id => id,
            _ => new List<string>(),
            StringComparer.Ordinal);
        var usedSockets = new HashSet<string>(StringComparer.Ordinal);
        foreach (CombatLayoutConnection connection in plan.Connections)
        {
            if (connection == null ||
                !instances.TryGetValue(connection.FromInstanceId, out CombatLayoutPlacement from) ||
                !instances.TryGetValue(connection.ToInstanceId, out CombatLayoutPlacement to) ||
                !usedSockets.Add(connection.FromInstanceId + "/" + connection.FromSocketId) ||
                !usedSockets.Add(connection.ToInstanceId + "/" + connection.ToSocketId) ||
                Math.Abs(from.GridX - to.GridX) + Math.Abs(from.GridZ - to.GridZ) != 1)
            {
                error = "布局连接引用、连接口复用或网格邻接无效。";
                return false;
            }
            if (!Allows(set.Find(from.DefinitionId), connection.FromSocketId, to.Kind) ||
                !Allows(set.Find(to.DefinitionId), connection.ToSocketId, from.Kind))
            {
                error = $"模块连接 '{from.InstanceId}' → '{to.InstanceId}' 不允许当前邻接类型。";
                return false;
            }
            adjacency[from.InstanceId].Add(to.InstanceId);
            adjacency[to.InstanceId].Add(from.InstanceId);
        }

        string spawn = plan.Placements.First(item =>
            item.Kind == CombatAreaModuleKind.Spawn).InstanceId;
        var reachable = new HashSet<string>(StringComparer.Ordinal) { spawn };
        var queue = new Queue<string>();
        queue.Enqueue(spawn);
        while (queue.Count > 0)
        {
            foreach (string neighbor in adjacency[queue.Dequeue()])
                if (reachable.Add(neighbor)) queue.Enqueue(neighbor);
        }
        if (reachable.Count != instances.Count)
        {
            error = "出生点无法到达全部战斗区、事件区和撤离区。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool Allows(
        CombatAreaModuleDefinition definition,
        string socketId,
        CombatAreaModuleKind neighbor)
    {
        LayoutConnectorDefinition socket = definition.Connectors.FirstOrDefault(
            value => string.Equals(value.StableId, socketId, StringComparison.Ordinal));
        if (socket == null) return false;
        CombatAreaModuleKindMask bit = neighbor switch
        {
            CombatAreaModuleKind.Spawn => CombatAreaModuleKindMask.Spawn,
            CombatAreaModuleKind.Combat => CombatAreaModuleKindMask.Combat,
            CombatAreaModuleKind.Event => CombatAreaModuleKindMask.Event,
            _ => CombatAreaModuleKindMask.Extraction
        };
        return (socket.AllowedNeighbors & bit) != 0;
    }
}

public static class CombatLayoutFingerprint
{
    public static string Compute(CombatLayoutPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        var text = new StringBuilder();
        text.Append(plan.ContentVersion).Append('|')
            .Append(plan.GeneratorVersion).Append('|')
            .Append(plan.UsedFallback ? 1 : 0);
        foreach (CombatLayoutPlacement placement in plan.Placements
                     .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            text.Append('|').Append(placement.InstanceId).Append(':')
                .Append(placement.DefinitionId).Append(':')
                .Append((int)placement.Kind).Append(':')
                .Append(placement.GridX).Append(':').Append(placement.GridZ)
                .Append(':').Append(placement.QuarterTurns);
        }
        foreach (CombatLayoutConnection connection in plan.Connections
                     .OrderBy(item => item.FromInstanceId, StringComparer.Ordinal)
                     .ThenBy(item => item.ToInstanceId, StringComparer.Ordinal))
        {
            text.Append('|').Append(connection.FromInstanceId).Append(':')
                .Append(connection.FromSocketId).Append('>')
                .Append(connection.ToInstanceId).Append(':')
                .Append(connection.ToSocketId);
        }
        ulong hash = 14695981039346656037UL;
        foreach (byte value in Encoding.UTF8.GetBytes(text.ToString()))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }
}

public static class CombatLayoutPersistence
{
    public static LayoutSaveSnapshot ToSnapshot(CombatLayoutPlan plan)
    {
        if (plan == null) return null;
        var saved = new LayoutSaveSnapshot
        {
            LayoutId = plan.LayoutId,
            ContentVersion = plan.ContentVersion,
            GeneratorVersion = plan.GeneratorVersion,
            Fingerprint = plan.Fingerprint,
            UsedFallback = plan.UsedFallback
        };
        foreach (CombatLayoutPlacement placement in plan.Placements)
            saved.Modules.Add(new LayoutModuleSaveSnapshot
            {
                InstanceId = placement.InstanceId,
                DefinitionId = placement.DefinitionId,
                Kind = (int)placement.Kind,
                GridX = placement.GridX,
                GridZ = placement.GridZ,
                QuarterTurns = placement.QuarterTurns
            });
        foreach (CombatLayoutConnection connection in plan.Connections)
            saved.Connections.Add(new LayoutConnectionSaveSnapshot
            {
                FromInstanceId = connection.FromInstanceId,
                FromSocketId = connection.FromSocketId,
                ToInstanceId = connection.ToInstanceId,
                ToSocketId = connection.ToSocketId
            });
        return saved;
    }

    public static bool TryRestore(
        ModularCombatLayoutSet set,
        LayoutSaveSnapshot saved,
        int runSeed,
        out CombatLayoutPlan plan,
        out string error)
    {
        plan = null;
        if (saved == null)
        {
            error = "存档没有布局数据。";
            return false;
        }
        if (saved.ContentVersion != set.ContentVersion ||
            saved.GeneratorVersion != set.GeneratorVersion)
        {
            error = $"存档布局版本 {saved.ContentVersion}/{saved.GeneratorVersion} 与当前内容 {set.ContentVersion}/{set.GeneratorVersion} 不一致。";
            return false;
        }
        if (saved.UsedFallback && saved.Modules.Count == 0)
        {
            plan = new CombatLayoutPlan(runSeed, saved.ContentVersion,
                saved.GeneratorVersion, Array.Empty<CombatLayoutPlacement>(),
                Array.Empty<CombatLayoutConnection>(), true,
                "从存档恢复安全布局。");
        }
        else
        {
            plan = new CombatLayoutPlan(
                runSeed,
                saved.ContentVersion,
                saved.GeneratorVersion,
                saved.Modules.Select(module => new CombatLayoutPlacement(
                    module.InstanceId,
                    module.DefinitionId,
                    (CombatAreaModuleKind)module.Kind,
                    module.GridX,
                    module.GridZ,
                    module.QuarterTurns)),
                saved.Connections.Select(connection => new CombatLayoutConnection(
                    connection.FromInstanceId,
                    connection.FromSocketId,
                    connection.ToInstanceId,
                    connection.ToSocketId)),
                saved.UsedFallback,
                "从存档恢复布局。");
        }
        if (!string.Equals(plan.Fingerprint, saved.Fingerprint, StringComparison.Ordinal) ||
            !string.Equals(plan.LayoutId, saved.LayoutId, StringComparison.Ordinal))
        {
            error = "存档布局指纹或布局 ID 不匹配。";
            plan = null;
            return false;
        }
        if (plan.Placements.Count > 0 &&
            !CombatLayoutGraphValidator.TryValidate(set, plan, out error))
        {
            plan = null;
            return false;
        }
        error = string.Empty;
        return true;
    }
}
