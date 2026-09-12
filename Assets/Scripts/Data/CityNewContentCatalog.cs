using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

[CreateAssetMenu(
    fileName = "CityNewContentCatalog",
    menuName = "FPS/Content/CityNew Content Catalog")]
public sealed class CityNewContentCatalog : ScriptableObject
{
    public const string DefaultResourcePath =
        "Content/CityNew/CityNewContentCatalog";

    [SerializeField] private string stableId;
    [SerializeField] private EnemyDefinition defaultEnemy;
    [SerializeField] private List<EnemyArchetypeDefinition> enemyArchetypes =
        new();
    [SerializeField] private WaveSequenceDefinition waveSequence;
    [SerializeField] private LootDropTableDefinition lootDropTable;
    [SerializeField] private List<UpgradeDefinition> upgrades = new();
    [SerializeField] private List<ItemDefinition> items = new();
    [SerializeField] private List<CombatBuildDefinition> combatBuilds = new();

    public string StableId => stableId;
    public EnemyDefinition DefaultEnemy => defaultEnemy;
    public IReadOnlyList<EnemyArchetypeDefinition> EnemyArchetypes =>
        enemyArchetypes;
    public WaveSequenceDefinition WaveSequence => waveSequence;
    public LootDropTableDefinition LootDropTable => lootDropTable;
    public IReadOnlyList<UpgradeDefinition> Upgrades => upgrades;
    public IReadOnlyList<ItemDefinition> Items => items;
    public IReadOnlyList<CombatBuildDefinition> CombatBuilds => combatBuilds;

    public static CityNewContentCatalog LoadDefault()
    {
        return Resources.Load<CityNewContentCatalog>(DefaultResourcePath);
    }

    public void Configure(
        string id,
        EnemyDefinition enemy,
        IEnumerable<EnemyArchetypeDefinition> archetypes,
        WaveSequenceDefinition sequence,
        LootDropTableDefinition drops,
        IEnumerable<UpgradeDefinition> upgradeDefinitions,
        IEnumerable<ItemDefinition> itemDefinitions,
        IEnumerable<CombatBuildDefinition> buildDefinitions = null)
    {
        stableId = id?.Trim();
        defaultEnemy = enemy;
        enemyArchetypes = archetypes != null
            ? new List<EnemyArchetypeDefinition>(archetypes)
            : new List<EnemyArchetypeDefinition>();
        waveSequence = sequence;
        lootDropTable = drops;
        upgrades = upgradeDefinitions != null
            ? new List<UpgradeDefinition>(upgradeDefinitions)
            : new List<UpgradeDefinition>();
        items = itemDefinitions != null
            ? new List<ItemDefinition>(itemDefinitions)
            : new List<ItemDefinition>();
        combatBuilds = buildDefinitions != null
            ? new List<CombatBuildDefinition>(buildDefinitions)
            : new List<CombatBuildDefinition>();
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            error = "CityNew content catalog requires a stable ID.";
            return false;
        }

        if (defaultEnemy == null ||
            string.IsNullOrWhiteSpace(defaultEnemy.StableId))
        {
            error = "CityNew content catalog requires an enemy asset with a stable ID.";
            return false;
        }

        if (waveSequence == null ||
            string.IsNullOrWhiteSpace(waveSequence.StableId) ||
            waveSequence.WaveCount == 0)
        {
            error = "CityNew content catalog requires a non-empty wave sequence asset with a stable ID.";
            return false;
        }

        if (!ValidateDefinitions(
                enemyArchetypes,
                archetype => archetype != null ? archetype.StableId : null,
                "enemy archetype",
                out error))
        {
            return false;
        }

        var archetypeSet = new HashSet<EnemyArchetypeDefinition>(
            enemyArchetypes);

        var waveIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < waveSequence.WaveCount; index++)
        {
            WaveDefinition wave = waveSequence.GetStage(index).Wave;
            if (wave == null || string.IsNullOrWhiteSpace(wave.StableId))
            {
                error = $"CityNew wave stage {index + 1} requires an asset with a stable ID.";
                return false;
            }

            if (!waveIds.Add(wave.StableId))
            {
                error = $"CityNew wave stable ID '{wave.StableId}' is duplicated.";
                return false;
            }

            if (wave.ResolvedEntries == null || wave.ResolvedEntries.Count == 0)
            {
                error = $"CityNew wave '{wave.StableId}' has no enemy entries.";
                return false;
            }

            for (int entryIndex = 0;
                 entryIndex < wave.EnemyEntries.Count;
                 entryIndex++)
            {
                EnemyArchetypeDefinition archetype =
                    wave.EnemyEntries[entryIndex]?.Archetype;
                if (archetype == null || !archetypeSet.Contains(archetype))
                {
                    error = $"CityNew wave '{wave.StableId}' references an enemy archetype outside the catalog.";
                    return false;
                }
            }
        }

        if (lootDropTable == null ||
            string.IsNullOrWhiteSpace(lootDropTable.StableId) ||
            lootDropTable.Rules == null || lootDropTable.Rules.Count == 0)
        {
            error = "CityNew content catalog requires a non-empty loot table asset with a stable ID.";
            return false;
        }

        if (upgrades == null || upgrades.Count == 0)
        {
            error = "CityNew content catalog requires upgrade assets.";
            return false;
        }

        var upgradeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < upgrades.Count; index++)
        {
            UpgradeDefinition upgrade = upgrades[index];
            if (upgrade == null || string.IsNullOrWhiteSpace(upgrade.StableId))
            {
                error = $"CityNew upgrade entry {index + 1} requires an asset with a stable ID.";
                return false;
            }

            if (!upgradeIds.Add(upgrade.StableId))
            {
                error = $"CityNew upgrade stable ID '{upgrade.StableId}' is duplicated.";
                return false;
            }
        }

        if (!ValidateDefinitions(
                items,
                item => item != null ? item.StableId : null,
                "item",
                out error))
        {
            return false;
        }

        if (!ValidateDefinitions(
                combatBuilds,
                build => build != null ? build.StableId : null,
                "combat build",
                out error))
        {
            return false;
        }

        IReadOnlyList<CombatRuleValidationIssue> buildIssues =
            CombatRuleContentValidator.Validate(combatBuilds);
        if (buildIssues.Count > 0)
        {
            error = buildIssues[0].Message;
            return false;
        }

        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < items.Count; index++)
        {
            itemIds.Add(items[index].StableId);
        }

        for (int ruleIndex = 0;
             ruleIndex < lootDropTable.Rules.Count;
             ruleIndex++)
        {
            LootDropRule rule = lootDropTable.Rules[ruleIndex];
            if (rule == null)
            {
                continue;
            }

            for (int entryIndex = 0;
                 entryIndex < rule.Entries.Count;
                 entryIndex++)
            {
                string itemId = rule.Entries[entryIndex]?.ItemStableId;
                if (string.IsNullOrWhiteSpace(itemId) ||
                    !itemIds.Contains(itemId))
                {
                    error = $"Loot rule {ruleIndex + 1} references unknown item '{itemId}'.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateDefinitions<T>(
        IReadOnlyList<T> definitions,
        Func<T, string> getId,
        string label,
        out string error)
    {
        if (definitions == null || definitions.Count == 0)
        {
            error = $"CityNew content catalog requires {label} assets.";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < definitions.Count; index++)
        {
            string id = getId(definitions[index]);
            if (string.IsNullOrWhiteSpace(id))
            {
                error = $"CityNew {label} entry {index + 1} requires a stable ID.";
                return false;
            }

            if (!ids.Add(id))
            {
                error = $"CityNew {label} stable ID '{id}' is duplicated.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
