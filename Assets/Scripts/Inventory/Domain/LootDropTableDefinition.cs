using System;
using System.Collections.Generic;
using UnityEngine;

public enum LootRewardTier
{
    Normal,
    Elite,
    WaveClear,
    FinalWave
}

[Serializable]
public sealed class LootDropEntry
{
    [SerializeField] private string itemStableId;
    [SerializeField, Min(1)] private int weight = 1;
    [SerializeField, Min(1)] private int minimumQuantity = 1;
    [SerializeField, Min(1)] private int maximumQuantity = 1;
    [SerializeField, Range(0f, 1f)] private float dropChance = 1f;

    public LootDropEntry(
        string stableId,
        int entryWeight,
        int minimum,
        int maximum,
        float chance)
    {
        Configure(stableId, entryWeight, minimum, maximum, chance);
    }

    public string ItemStableId => itemStableId;
    public int Weight => Mathf.Max(1, weight);
    public int MinimumQuantity => Mathf.Max(1, minimumQuantity);
    public int MaximumQuantity => Mathf.Max(MinimumQuantity, maximumQuantity);
    public float DropChance => Mathf.Clamp01(dropChance);

    public void Configure(
        string stableId,
        int entryWeight,
        int minimum,
        int maximum,
        float chance)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException(
                "A loot entry requires an item stable ID.",
                nameof(stableId));
        }

        itemStableId = stableId.Trim();
        weight = Mathf.Max(1, entryWeight);
        minimumQuantity = Mathf.Max(1, minimum);
        maximumQuantity = Mathf.Max(minimumQuantity, maximum);
        dropChance = Mathf.Clamp01(chance);
    }
}

[Serializable]
public sealed class LootDropRule
{
    [SerializeField] private string enemyTypeId = "*";
    [SerializeField, Min(1)] private int minimumWave = 1;
    [SerializeField, Min(1)] private int maximumWave = 99;
    [SerializeField] private LootRewardTier rewardTier;
    [SerializeField, Min(1)] private int minimumRolls = 1;
    [SerializeField, Min(1)] private int maximumRolls = 1;
    [SerializeField] private List<LootDropEntry> entries = new();

    public LootDropRule(
        string typeId,
        int firstWave,
        int lastWave,
        LootRewardTier tier,
        int minimumDropRolls,
        int maximumDropRolls,
        IEnumerable<LootDropEntry> configuredEntries)
    {
        Configure(
            typeId,
            firstWave,
            lastWave,
            tier,
            minimumDropRolls,
            maximumDropRolls,
            configuredEntries);
    }

    public string EnemyTypeId => string.IsNullOrWhiteSpace(enemyTypeId)
        ? "*"
        : enemyTypeId.Trim();
    public int MinimumWave => Mathf.Max(1, minimumWave);
    public int MaximumWave => Mathf.Max(MinimumWave, maximumWave);
    public LootRewardTier RewardTier => rewardTier;
    public int MinimumRolls => Mathf.Max(1, minimumRolls);
    public int MaximumRolls => Mathf.Max(MinimumRolls, maximumRolls);
    public IReadOnlyList<LootDropEntry> Entries => entries;

    public bool Matches(
        string typeId,
        int waveNumber,
        LootRewardTier tier)
    {
        bool typeMatches = EnemyTypeId == "*" || string.Equals(
            EnemyTypeId,
            typeId,
            StringComparison.Ordinal);
        return typeMatches && tier == rewardTier &&
               waveNumber >= MinimumWave && waveNumber <= MaximumWave;
    }

    public int Specificity(string typeId)
    {
        int typeScore = string.Equals(
            EnemyTypeId,
            typeId,
            StringComparison.Ordinal)
            ? 10000
            : 0;
        return typeScore - (MaximumWave - MinimumWave);
    }

    public void Configure(
        string typeId,
        int firstWave,
        int lastWave,
        LootRewardTier tier,
        int minimumDropRolls,
        int maximumDropRolls,
        IEnumerable<LootDropEntry> configuredEntries)
    {
        enemyTypeId = string.IsNullOrWhiteSpace(typeId)
            ? "*"
            : typeId.Trim();
        minimumWave = Mathf.Max(1, firstWave);
        maximumWave = Mathf.Max(minimumWave, lastWave);
        rewardTier = tier;
        minimumRolls = Mathf.Max(1, minimumDropRolls);
        maximumRolls = Mathf.Max(minimumRolls, maximumDropRolls);
        entries = configuredEntries != null
            ? new List<LootDropEntry>(configuredEntries)
            : new List<LootDropEntry>();
    }
}

[CreateAssetMenu(
    fileName = "LootDropTable",
    menuName = "FPS/Loot/Drop Table")]
public sealed class LootDropTableDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private List<LootDropRule> rules = new();

    public string StableId => stableId;
    public IReadOnlyList<LootDropRule> Rules => rules;

    public void Configure(IEnumerable<LootDropRule> configuredRules)
    {
        ConfigureWithStableId(stableId, configuredRules);
    }

    public void ConfigureWithStableId(
        string id,
        IEnumerable<LootDropRule> configuredRules)
    {
        stableId = id?.Trim();
        rules = configuredRules != null
            ? new List<LootDropRule>(configuredRules)
            : new List<LootDropRule>();
    }

    public LootDropRule ResolveRule(
        string enemyTypeId,
        int waveNumber,
        LootRewardTier tier)
    {
        LootDropRule best = null;
        int bestScore = int.MinValue;

        for (int index = 0; index < rules.Count; index++)
        {
            LootDropRule rule = rules[index];

            if (rule == null || !rule.Matches(
                    enemyTypeId,
                    waveNumber,
                    tier))
            {
                continue;
            }

            int score = rule.Specificity(enemyTypeId);

            if (best == null || score > bestScore)
            {
                best = rule;
                bestScore = score;
            }
        }

        return best;
    }
}
