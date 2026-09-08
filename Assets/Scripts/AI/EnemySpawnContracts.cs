using System;
using UnityEngine;

public enum EnemyExitReason
{
    Died,
    Disabled,
    Released
}

public readonly struct EnemySpawnRequest
{
    public EnemySpawnRequest(
        int spawnId,
        WaveEnemyEntry entry,
        Vector3 position,
        Quaternion rotation,
        Transform target)
        : this(
            spawnId,
            0,
            entry,
            position,
            rotation,
            target)
    {
    }

    public EnemySpawnRequest(
        int spawnId,
        int waveNumber,
        WaveEnemyEntry entry,
        Vector3 position,
        Quaternion rotation,
        Transform target)
    {
        SpawnId = spawnId;
        WaveNumber = Mathf.Max(0, waveNumber);
        Entry = entry;
        Position = position;
        Rotation = rotation;
        Target = target;
    }

    public int SpawnId { get; }
    public int WaveNumber { get; }
    public WaveEnemyEntry Entry { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public Transform Target { get; }
}

public readonly struct EnemySpawnHandle
{
    public EnemySpawnHandle(
        int spawnId,
        EnemyController controller,
        WaveEnemyLifecycle lifecycle)
        : this(spawnId, 0, controller, lifecycle)
    {
    }

    public EnemySpawnHandle(
        int spawnId,
        int waveNumber,
        EnemyController controller,
        WaveEnemyLifecycle lifecycle)
        : this(
            spawnId,
            waveNumber,
            controller,
            lifecycle,
            "*",
            LootRewardTier.Normal)
    {
    }

    public EnemySpawnHandle(
        int spawnId,
        int waveNumber,
        EnemyController controller,
        WaveEnemyLifecycle lifecycle,
        string enemyTypeId,
        LootRewardTier rewardTier)
        : this(
            spawnId,
            waveNumber,
            controller,
            lifecycle,
            enemyTypeId,
            rewardTier,
            0)
    {
    }

    public EnemySpawnHandle(
        int spawnId,
        int waveNumber,
        EnemyController controller,
        WaveEnemyLifecycle lifecycle,
        string enemyTypeId,
        LootRewardTier rewardTier,
        int generation)
    {
        SpawnId = spawnId;
        WaveNumber = Mathf.Max(0, waveNumber);
        Controller = controller;
        Lifecycle = lifecycle;
        EnemyTypeId = string.IsNullOrWhiteSpace(enemyTypeId)
            ? "*"
            : enemyTypeId.Trim();
        RewardTier = rewardTier;
        Generation = Mathf.Max(0, generation);
    }

    public int SpawnId { get; }
    public int WaveNumber { get; }
    public EnemyController Controller { get; }
    public WaveEnemyLifecycle Lifecycle { get; }
    public string EnemyTypeId { get; }
    public LootRewardTier RewardTier { get; }
    public int Generation { get; }
    public bool IsValid => Controller != null && Lifecycle != null;
}

public interface IEnemyFactory
{
    bool TrySpawn(
        EnemySpawnRequest request,
        Action<EnemySpawnHandle, EnemyExitReason> onEnded,
        out EnemySpawnHandle handle);

    void Release(EnemySpawnHandle handle);
}

public enum EnemyFactoryPreparationState
{
    Idle,
    Loading,
    Prewarming,
    Ready,
    Failed,
    Disposed
}

// Optional capability keeps synchronous factories and test doubles compatible.
public interface IAsyncEnemyFactory : IEnemyFactory
{
    EnemyFactoryPreparationState PreparationState { get; }
    float PreparationProgress { get; }
    string PreparationError { get; }
    System.Collections.IEnumerator PrepareAsync();
}
