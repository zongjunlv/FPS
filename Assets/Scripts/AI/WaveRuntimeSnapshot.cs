using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public readonly struct EnemyRuntimeSnapshot
{
    public EnemyRuntimeSnapshot(
        int waveNumber,
        int spawnId,
        string enemyTypeId,
        Vector3 position,
        Quaternion rotation,
        float health,
        float armor,
        GameplayEffectRuntimeSnapshot effects = null)
    {
        WaveNumber = waveNumber;
        SpawnId = spawnId;
        EnemyTypeId = enemyTypeId;
        Position = position;
        Rotation = rotation;
        Health = health;
        Armor = armor;
        Effects = effects ?? new GameplayEffectRuntimeSnapshot(
            Array.Empty<GameplayEffectInstanceSnapshot>());
    }

    public int WaveNumber { get; }
    public int SpawnId { get; }
    public string EnemyTypeId { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public float Health { get; }
    public float Armor { get; }
    public GameplayEffectRuntimeSnapshot Effects { get; }
}

public sealed class WaveRuntimeSnapshot
{
    public WaveRuntimeSnapshot(
        MultiWaveFlowStateSnapshot flow,
        float spawnCooldownRemaining,
        int nextSpawnId,
        IReadOnlyList<EnemyRuntimeSnapshot> enemies)
    {
        Flow = flow;
        SpawnCooldownRemaining = spawnCooldownRemaining;
        NextSpawnId = nextSpawnId;
        Enemies = enemies ?? Array.Empty<EnemyRuntimeSnapshot>();
    }

    public MultiWaveFlowStateSnapshot Flow { get; }
    public float SpawnCooldownRemaining { get; }
    public int NextSpawnId { get; }
    public IReadOnlyList<EnemyRuntimeSnapshot> Enemies { get; }
}
