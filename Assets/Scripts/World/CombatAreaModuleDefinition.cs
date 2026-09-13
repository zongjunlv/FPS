using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(
    fileName = "CombatAreaModule",
    menuName = "FPS/World/Combat Area Module")]
public sealed class CombatAreaModuleDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private CombatAreaModuleKind kind;
    [SerializeField] private string resourceAddress;
    [SerializeField] private Vector2Int occupiedCells = Vector2Int.one;
    [SerializeField] private List<LayoutConnectorDefinition> connectors = new();
    [SerializeField] private List<LayoutPointDefinition> taskPoints = new();
    [SerializeField] private List<LayoutPointDefinition> enemySpawnPoints = new();

    public string StableId => stableId;
    public CombatAreaModuleKind Kind => kind;
    public string ResourceAddress => resourceAddress;
    public Vector2Int OccupiedCells => occupiedCells;
    public IReadOnlyList<LayoutConnectorDefinition> Connectors => connectors;
    public IReadOnlyList<LayoutPointDefinition> TaskPoints => taskPoints;
    public IReadOnlyList<LayoutPointDefinition> EnemySpawnPoints => enemySpawnPoints;

    public void Configure(
        string id,
        CombatAreaModuleKind moduleKind,
        string address,
        Vector2Int footprint,
        IEnumerable<LayoutConnectorDefinition> moduleConnectors,
        IEnumerable<LayoutPointDefinition> moduleTaskPoints,
        IEnumerable<LayoutPointDefinition> spawnPoints)
    {
        stableId = id?.Trim();
        kind = moduleKind;
        resourceAddress = address?.Trim();
        occupiedCells = footprint;
        connectors = moduleConnectors?.ToList() ?? new List<LayoutConnectorDefinition>();
        taskPoints = moduleTaskPoints?.ToList() ?? new List<LayoutPointDefinition>();
        enemySpawnPoints = spawnPoints?.ToList() ?? new List<LayoutPointDefinition>();
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            error = "模块 stableId 不能为空。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(resourceAddress))
        {
            error = $"模块 '{stableId}' 缺少资源地址。";
            return false;
        }
        if (occupiedCells.x < 1 || occupiedCells.y < 1)
        {
            error = $"模块 '{stableId}' 的占用范围必须为正。";
            return false;
        }
        if (connectors == null || connectors.Count == 0)
        {
            error = $"模块 '{stableId}' 缺少连接口。";
            return false;
        }

        var connectorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (LayoutConnectorDefinition connector in connectors)
        {
            if (connector == null || string.IsNullOrWhiteSpace(connector.StableId) ||
                !connectorIds.Add(connector.StableId) ||
                connector.AllowedNeighbors == CombatAreaModuleKindMask.None)
            {
                error = $"模块 '{stableId}' 的连接口为空、重复或没有允许的邻接类型。";
                return false;
            }
        }

        string requiredTask = kind switch
        {
            CombatAreaModuleKind.Spawn => "player_spawn",
            CombatAreaModuleKind.Event => "terminal",
            CombatAreaModuleKind.Extraction => "extraction",
            _ => "combat_center"
        };
        if (taskPoints == null || !taskPoints.Any(point =>
                point != null && point.StableId == requiredTask))
        {
            error = $"模块 '{stableId}' 缺少任务点 '{requiredTask}'。";
            return false;
        }
        if (kind == CombatAreaModuleKind.Combat &&
            (enemySpawnPoints == null || enemySpawnPoints.Count == 0))
        {
            error = $"战斗模块 '{stableId}' 缺少敌人出生点。";
            return false;
        }
        if (!UniquePoints(taskPoints) || !UniquePoints(enemySpawnPoints))
        {
            error = $"模块 '{stableId}' 包含为空或重复的任务/出生点。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool UniquePoints(IReadOnlyList<LayoutPointDefinition> points)
    {
        if (points == null) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return points.All(point => point != null &&
            !string.IsNullOrWhiteSpace(point.StableId) &&
            ids.Add(point.StableId));
    }
}
