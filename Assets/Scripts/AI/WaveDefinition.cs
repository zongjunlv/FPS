using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WaveEnemyEntry
{
    [SerializeField] private EnemyArchetypeDefinition archetype;
    [SerializeField] private EnemyController template;
    [SerializeField, Min(1)] private int weight = 1;
    [SerializeField] private LootRewardTier rewardTier = LootRewardTier.Normal;
    [SerializeField] private string enemyTypeId = "*";
    [SerializeField] private EnemyAffixDefinition affix;
    [SerializeField] private EnemyAbilitySetDefinition abilitySet;
    [SerializeField, Min(1)] private int threatCost = 1;
    [SerializeField] private string roleTag = "assault";

    public WaveEnemyEntry(EnemyController enemyTemplate, int entryWeight = 1)
        : this(
            enemyTemplate,
            entryWeight,
            LootRewardTier.Normal,
            "*",
            null,
            null)
    {
    }

    public WaveEnemyEntry(
        EnemyArchetypeDefinition enemyArchetype,
        int entryWeight = 1)
    {
        archetype = enemyArchetype != null
            ? enemyArchetype
            : throw new ArgumentNullException(nameof(enemyArchetype));
        weight = Mathf.Max(1, entryWeight);
    }

    public WaveEnemyEntry(
        EnemyController enemyTemplate,
        int entryWeight,
        LootRewardTier tier,
        string typeId = "*",
        EnemyAffixDefinition enemyAffix = null,
        EnemyAbilitySetDefinition enemyAbilitySet = null,
        int configuredThreatCost = 1,
        string configuredRoleTag = "assault")
    {
        template = enemyTemplate;
        weight = Mathf.Max(1, entryWeight);
        rewardTier = tier;
        enemyTypeId = string.IsNullOrWhiteSpace(typeId)
            ? "*"
            : typeId.Trim();
        affix = enemyAffix;
        abilitySet = enemyAbilitySet;
        threatCost = Mathf.Max(1, configuredThreatCost);
        roleTag = ThreatRoleConstraint.NormalizeRole(configuredRoleTag);
    }

    public EnemyArchetypeDefinition Archetype => archetype;
    public EnemyController Template => archetype != null
        ? archetype.Template
        : template;
    public int Weight => Mathf.Max(1, weight);
    public LootRewardTier RewardTier => archetype != null
        ? archetype.RewardTier
        : rewardTier;
    public string EnemyTypeId => archetype != null
        ? archetype.EnemyTypeId
        : string.IsNullOrWhiteSpace(enemyTypeId)
            ? "*"
            : enemyTypeId.Trim();
    public EnemyAffixDefinition Affix => archetype != null
        ? archetype.Affix
        : affix;
    public EnemyAbilitySetDefinition AbilitySet => archetype != null
        ? archetype.AbilitySet
        : abilitySet;
    public int ThreatCost => archetype != null
        ? archetype.ThreatCost
        : Mathf.Max(1, threatCost);
    public string RoleTag => archetype != null
        ? archetype.RoleTag
        : ThreatRoleConstraint.NormalizeRole(roleTag);
    public bool IsElite => RewardTier == LootRewardTier.Elite;
}

[CreateAssetMenu(
    fileName = "WaveDefinition",
    menuName = "FPS/Waves/Wave Definition")]
public sealed class WaveDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField, Min(1)] private int totalEnemyCount = 6;
    [SerializeField, Min(1)] private int maximumAliveCount = 3;
    [SerializeField, Min(0f)] private float spawnInterval = 0.75f;
    [SerializeField, Min(0.05f)] private float retryInterval = 0.2f;
    [SerializeField, Min(1f)] private float playerSafetyDistance = 10f;
    [SerializeField, Min(0.5f)] private float enemySpacing = 3f;
    [SerializeField, Min(1f)] private float minimumSpawnRadius = 12f;
    [SerializeField, Min(1f)] private float maximumSpawnRadius = 24f;
    [SerializeField] private List<WaveEnemyEntry> enemyEntries = new();
    [SerializeField] private WaveCompositionMode compositionMode;
    [SerializeField, Min(1)] private int threatBudget = 1;
    [SerializeField] private int compositionSeed;
    [SerializeField, Range(0f, 1f)] private float maximumEliteThreatRatio;
    [SerializeField] private List<ThreatRoleConstraint> roleConstraints = new();
    private List<WaveEnemyEntry> resolvedBudgetEntries = new();
    private int resolvedThreatCost;
    private bool usedCompositionFallback;

    public string StableId => stableId;
    public int TotalEnemyCount => totalEnemyCount;
    public int MaximumAliveCount => maximumAliveCount;
    public float SpawnInterval => spawnInterval;
    public float RetryInterval => retryInterval;
    public float PlayerSafetyDistance => playerSafetyDistance;
    public float EnemySpacing => enemySpacing;
    public float MinimumSpawnRadius => minimumSpawnRadius;
    public float MaximumSpawnRadius => maximumSpawnRadius;
    public IReadOnlyList<WaveEnemyEntry> EnemyEntries => enemyEntries;
    public WaveCompositionMode CompositionMode => compositionMode;
    public int ThreatBudget => threatBudget;
    public int CompositionSeed => compositionSeed;
    public int ResolvedThreatCost => resolvedThreatCost;
    public bool UsedCompositionFallback => usedCompositionFallback;
    public IReadOnlyList<WaveEnemyEntry> ResolvedEntries =>
        compositionMode == WaveCompositionMode.ThreatBudget
            ? resolvedBudgetEntries
            : enemyEntries;

    public void ConfigureIdentity(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A wave requires a stable ID.", nameof(id));
        }

        stableId = id.Trim();
    }

    public void Configure(
        int totalCount,
        int maximumAlive,
        float interval,
        IEnumerable<WaveEnemyEntry> entries,
        float safetyDistance = 10f,
        float spacing = 3f,
        float minimumRadius = 12f,
        float maximumRadius = 24f,
        float failedRetryInterval = 0.2f)
    {
        if (totalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        }

        if (maximumAlive < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAlive));
        }

        enemyEntries = entries != null
            ? new List<WaveEnemyEntry>(entries)
            : new List<WaveEnemyEntry>();

        if (enemyEntries.Count == 0)
        {
            throw new ArgumentException(
                "A wave requires at least one enemy entry.",
                nameof(entries));
        }

        totalEnemyCount = totalCount;
        compositionMode = WaveCompositionMode.FixedCount;
        resolvedBudgetEntries.Clear();
        resolvedThreatCost = 0;
        usedCompositionFallback = false;
        maximumAliveCount = Mathf.Min(maximumAlive, totalCount);
        spawnInterval = Mathf.Max(0f, interval);
        retryInterval = Mathf.Max(0.05f, failedRetryInterval);
        playerSafetyDistance = Mathf.Max(1f, safetyDistance);
        enemySpacing = Mathf.Max(0.5f, spacing);
        minimumSpawnRadius = Mathf.Max(
            playerSafetyDistance,
            minimumRadius);
        maximumSpawnRadius = Mathf.Max(
            minimumSpawnRadius,
            maximumRadius);
    }

    public void ConfigureThreatBudget(
        int budget,
        int seed,
        int maximumAlive,
        float interval,
        IEnumerable<WaveEnemyEntry> candidates,
        IEnumerable<ThreatRoleConstraint> constraints,
        float eliteThreatRatio,
        float safetyDistance = 10f,
        float spacing = 3f,
        float minimumRadius = 12f,
        float maximumRadius = 24f,
        float failedRetryInterval = 0.2f)
    {
        enemyEntries = candidates != null
            ? new List<WaveEnemyEntry>(candidates)
            : new List<WaveEnemyEntry>();
        roleConstraints = constraints != null
            ? new List<ThreatRoleConstraint>(constraints)
            : new List<ThreatRoleConstraint>();
        compositionMode = WaveCompositionMode.ThreatBudget;
        threatBudget = Mathf.Max(1, budget);
        compositionSeed = seed;
        maximumEliteThreatRatio = Mathf.Clamp01(eliteThreatRatio);
        RebuildThreatBudgetPlan();
        totalEnemyCount = resolvedBudgetEntries.Count;
        maximumAliveCount = Mathf.Clamp(
            maximumAlive,
            1,
            totalEnemyCount);
        spawnInterval = Mathf.Max(0f, interval);
        retryInterval = Mathf.Max(0.05f, failedRetryInterval);
        playerSafetyDistance = Mathf.Max(1f, safetyDistance);
        enemySpacing = Mathf.Max(0.5f, spacing);
        minimumSpawnRadius = Mathf.Max(
            playerSafetyDistance,
            minimumRadius);
        maximumSpawnRadius = Mathf.Max(
            minimumSpawnRadius,
            maximumRadius);
    }

    private void OnEnable()
    {
        if (compositionMode == WaveCompositionMode.ThreatBudget)
        {
            RebuildThreatBudgetPlan();
        }
    }

    private void RebuildThreatBudgetPlan()
    {
        ThreatBudgetWavePlan plan = ThreatBudgetWaveComposer.Compose(
            enemyEntries ?? new List<WaveEnemyEntry>(),
            Mathf.Max(1, threatBudget),
            compositionSeed,
            roleConstraints ?? new List<ThreatRoleConstraint>(),
            maximumEliteThreatRatio);
        resolvedBudgetEntries = new List<WaveEnemyEntry>(plan.Entries);
        resolvedThreatCost = plan.TotalThreat;
        usedCompositionFallback = plan.UsedFallback;
        totalEnemyCount = resolvedBudgetEntries.Count;
    }

    public WaveEnemyEntry GetEntry(int spawnIndex)
    {
        if (compositionMode == WaveCompositionMode.ThreatBudget)
        {
            return resolvedBudgetEntries != null &&
                   resolvedBudgetEntries.Count > 0
                ? resolvedBudgetEntries[
                    Mathf.Abs(spawnIndex) % resolvedBudgetEntries.Count]
                : null;
        }

        if (enemyEntries == null || enemyEntries.Count == 0)
        {
            return null;
        }

        int totalWeight = 0;

        foreach (WaveEnemyEntry entry in enemyEntries)
        {
            totalWeight += entry != null ? entry.Weight : 0;
        }

        if (totalWeight <= 0)
        {
            return null;
        }

        int selection = Mathf.Abs(spawnIndex) % totalWeight;

        foreach (WaveEnemyEntry entry in enemyEntries)
        {
            if (entry == null)
            {
                continue;
            }

            if (selection < entry.Weight)
            {
                return entry;
            }

            selection -= entry.Weight;
        }

        return enemyEntries[0];
    }
}
