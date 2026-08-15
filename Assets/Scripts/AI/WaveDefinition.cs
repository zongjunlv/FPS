using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WaveEnemyEntry
{
    [SerializeField] private EnemyController template;
    [SerializeField, Min(1)] private int weight = 1;
    [SerializeField] private LootRewardTier rewardTier = LootRewardTier.Normal;
    [SerializeField] private string enemyTypeId = "*";

    public WaveEnemyEntry(EnemyController enemyTemplate, int entryWeight = 1)
        : this(
            enemyTemplate,
            entryWeight,
            LootRewardTier.Normal,
            "*")
    {
    }

    public WaveEnemyEntry(
        EnemyController enemyTemplate,
        int entryWeight,
        LootRewardTier tier,
        string typeId = "*")
    {
        template = enemyTemplate;
        weight = Mathf.Max(1, entryWeight);
        rewardTier = tier;
        enemyTypeId = string.IsNullOrWhiteSpace(typeId)
            ? "*"
            : typeId.Trim();
    }

    public EnemyController Template => template;
    public int Weight => Mathf.Max(1, weight);
    public LootRewardTier RewardTier => rewardTier;
    public string EnemyTypeId => string.IsNullOrWhiteSpace(enemyTypeId)
        ? "*"
        : enemyTypeId.Trim();
}

[CreateAssetMenu(
    fileName = "WaveDefinition",
    menuName = "FPS/Waves/Wave Definition")]
public sealed class WaveDefinition : ScriptableObject
{
    [SerializeField, Min(1)] private int totalEnemyCount = 6;
    [SerializeField, Min(1)] private int maximumAliveCount = 3;
    [SerializeField, Min(0f)] private float spawnInterval = 0.75f;
    [SerializeField, Min(0.05f)] private float retryInterval = 0.2f;
    [SerializeField, Min(1f)] private float playerSafetyDistance = 10f;
    [SerializeField, Min(0.5f)] private float enemySpacing = 3f;
    [SerializeField, Min(1f)] private float minimumSpawnRadius = 12f;
    [SerializeField, Min(1f)] private float maximumSpawnRadius = 24f;
    [SerializeField] private List<WaveEnemyEntry> enemyEntries = new();

    public int TotalEnemyCount => totalEnemyCount;
    public int MaximumAliveCount => maximumAliveCount;
    public float SpawnInterval => spawnInterval;
    public float RetryInterval => retryInterval;
    public float PlayerSafetyDistance => playerSafetyDistance;
    public float EnemySpacing => enemySpacing;
    public float MinimumSpawnRadius => minimumSpawnRadius;
    public float MaximumSpawnRadius => maximumSpawnRadius;
    public IReadOnlyList<WaveEnemyEntry> EnemyEntries => enemyEntries;

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

    public WaveEnemyEntry GetEntry(int spawnIndex)
    {
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
