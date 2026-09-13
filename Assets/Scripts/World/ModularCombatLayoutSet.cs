using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ModularCombatLayoutSet",
    menuName = "FPS/World/Modular Combat Layout Set")]
public sealed class ModularCombatLayoutSet : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private int contentVersion = 1;
    [SerializeField] private int generatorVersion = 1;
    [SerializeField] private float tileSize = 16f;
    [SerializeField] private int combatModuleCount = 3;
    [SerializeField] private List<CombatAreaModuleDefinition> modules = new();

    public string StableId => stableId;
    public int ContentVersion => contentVersion;
    public int GeneratorVersion => generatorVersion;
    public float TileSize => tileSize;
    public int CombatModuleCount => combatModuleCount;
    public IReadOnlyList<CombatAreaModuleDefinition> Modules => modules;

    public void Configure(
        string id,
        int version,
        int plannerVersion,
        float moduleTileSize,
        int combatCount,
        IEnumerable<CombatAreaModuleDefinition> definitions)
    {
        stableId = id?.Trim();
        contentVersion = version;
        generatorVersion = plannerVersion;
        tileSize = moduleTileSize;
        combatModuleCount = combatCount;
        modules = definitions?.ToList() ?? new List<CombatAreaModuleDefinition>();
    }

    public CombatAreaModuleDefinition Find(string id) => modules?.FirstOrDefault(
        module => module != null && module.StableId == id);

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(stableId) || contentVersion < 1 ||
            generatorVersion < 1 || tileSize < 8f || combatModuleCount < 2 ||
            combatModuleCount > 8)
        {
            error = "布局集标识、版本、尺寸或战斗模块数量无效。";
            return false;
        }
        if (modules == null || modules.Count < combatModuleCount + 3)
        {
            error = "布局集缺少出生、战斗、事件或撤离模块。";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (CombatAreaModuleDefinition module in modules)
        {
            if (module == null)
            {
                error = "布局集包含空模块引用。";
                return false;
            }
            if (!module.TryValidate(out error)) return false;
            if (!ids.Add(module.StableId))
            {
                error = $"布局模块 stableId '{module.StableId}' 重复。";
                return false;
            }
            if (!addresses.Add(module.ResourceAddress))
            {
                error = $"布局模块资源地址 '{module.ResourceAddress}' 重复。";
                return false;
            }
        }
        foreach (CombatAreaModuleKind kind in Enum.GetValues(typeof(CombatAreaModuleKind)))
        {
            int required = kind == CombatAreaModuleKind.Combat
                ? combatModuleCount
                : 1;
            if (modules.Count(module => module.Kind == kind) < required)
            {
                error = $"布局集缺少足够的 {kind} 模块。";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }
}
