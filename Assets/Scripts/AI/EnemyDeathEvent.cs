using UnityEngine;

public readonly struct EnemyDeathEvent
{
    public EnemyDeathEvent(
        EnemyController enemy,
        int spawnId,
        int waveNumber,
        DamageInfo lastDamage,
        int rewardExperience)
        : this(
            enemy,
            spawnId,
            waveNumber,
            lastDamage,
            rewardExperience,
            enemy != null ? enemy.transform.position : Vector3.zero,
            "*",
            LootRewardTier.Normal,
            1f)
    {
    }

    public EnemyDeathEvent(
        EnemyController enemy,
        int spawnId,
        int waveNumber,
        DamageInfo lastDamage,
        int rewardExperience,
        Vector3 worldPosition,
        string enemyTypeId,
        LootRewardTier rewardTier,
        float lootQuantityMultiplier = 1f)
    {
        Enemy = enemy;
        SpawnId = spawnId;
        WaveNumber = Mathf.Max(0, waveNumber);
        LastDamage = lastDamage;
        RewardExperience = Mathf.Max(0, rewardExperience);
        WorldPosition = worldPosition;
        EnemyTypeId = string.IsNullOrWhiteSpace(enemyTypeId)
            ? "*"
            : enemyTypeId.Trim();
        RewardTier = rewardTier;
        LootQuantityMultiplier = float.IsNaN(lootQuantityMultiplier) ||
                                 float.IsInfinity(lootQuantityMultiplier)
            ? 1f
            : Mathf.Max(1f, lootQuantityMultiplier);
    }

    public EnemyController Enemy { get; }
    public int SpawnId { get; }
    public int WaveNumber { get; }
    public DamageInfo LastDamage { get; }
    public GameObject DamageSource => LastDamage.Source;
    public DamageType DamageType => LastDamage.Type;
    public int RewardExperience { get; }
    public Vector3 WorldPosition { get; }
    public string EnemyTypeId { get; }
    public LootRewardTier RewardTier { get; }
    public float LootQuantityMultiplier { get; }
}
