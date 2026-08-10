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
    {
        SpawnId = spawnId;
        Entry = entry;
        Position = position;
        Rotation = rotation;
        Target = target;
    }

    public int SpawnId { get; }
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
    {
        SpawnId = spawnId;
        Controller = controller;
        Lifecycle = lifecycle;
    }

    public int SpawnId { get; }
    public EnemyController Controller { get; }
    public WaveEnemyLifecycle Lifecycle { get; }
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
