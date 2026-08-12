using UnityEngine;

public readonly struct EnemyDeathEvent
{
    public EnemyDeathEvent(
        EnemyController enemy,
        int spawnId,
        int waveNumber,
        DamageInfo lastDamage,
        int rewardExperience)
    {
        Enemy = enemy;
        SpawnId = spawnId;
        WaveNumber = Mathf.Max(0, waveNumber);
        LastDamage = lastDamage;
        RewardExperience = Mathf.Max(0, rewardExperience);
    }

    public EnemyController Enemy { get; }
    public int SpawnId { get; }
    public int WaveNumber { get; }
    public DamageInfo LastDamage { get; }
    public GameObject DamageSource => LastDamage.Source;
    public DamageType DamageType => LastDamage.Type;
    public int RewardExperience { get; }
}
