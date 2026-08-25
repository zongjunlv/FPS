using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public sealed class PlayerCombatEventRouter : MonoBehaviour
{
    private readonly HashSet<EnemyDeathKey> publishedDeaths = new();
    private WaveDirector killSource;
    private bool subscribed;
    private long nextEventId;

    public GameplayEffectEventStream Events { get; } = new();
    public int AcceptedKillCount { get; private set; }

    private void OnEnable()
    {
        if (killSource == null && WaveDirector.Active != null)
        {
            killSource = WaveDirector.Active;
        }

        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    public void BindKillSource(WaveDirector source)
    {
        Unsubscribe();
        killSource = source;
        publishedDeaths.Clear();
        AcceptedKillCount = 0;
        Subscribe();
    }

    public bool TryPublishEnemyDeath(EnemyDeathEvent death)
    {
        if (death.Enemy == null || death.SpawnId <= 0 ||
            !IsPlayerOwned(death.DamageSource))
        {
            return false;
        }

        var key = new EnemyDeathKey(death.WaveNumber, death.SpawnId);

        if (!publishedDeaths.Add(key))
        {
            return false;
        }

        AcceptedKillCount++;
        Events.Publish(new GameplayEffectEventContext(
            ++nextEventId,
            GameplayEffectEventType.EnemyKilled,
            $"wave:{death.WaveNumber}/spawn:{death.SpawnId}",
            death.DamageSource,
            death.Enemy));
        return true;
    }

    private bool IsPlayerOwned(GameObject source)
    {
        return source != null &&
               (source == gameObject ||
                source.transform.IsChildOf(transform));
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || subscribed || killSource == null)
        {
            return;
        }

        killSource.EnemyDied += HandleEnemyDied;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (subscribed && killSource != null)
        {
            killSource.EnemyDied -= HandleEnemyDied;
        }

        subscribed = false;
    }

    private void HandleEnemyDied(EnemyDeathEvent death)
    {
        TryPublishEnemyDeath(death);
    }

    private readonly struct EnemyDeathKey : IEquatable<EnemyDeathKey>
    {
        public EnemyDeathKey(int waveNumber, int spawnId)
        {
            WaveNumber = waveNumber;
            SpawnId = spawnId;
        }

        private int WaveNumber { get; }
        private int SpawnId { get; }

        public bool Equals(EnemyDeathKey other) =>
            WaveNumber == other.WaveNumber && SpawnId == other.SpawnId;

        public override bool Equals(object obj) =>
            obj is EnemyDeathKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            WaveNumber,
            SpawnId);
    }
}
