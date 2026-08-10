using System;
using UnityEngine;

public sealed class WaveEnemyLifecycle : MonoBehaviour
{
    private Health health;
    private EnemySpawnHandle handle;
    private Action<EnemySpawnHandle, EnemyExitReason> onEnded;
    private bool armed;
    private bool settled;
    private bool subscribed;

    public int SpawnId => handle.SpawnId;
    public bool IsSettled => settled;

    public void Arm(
        int spawnId,
        EnemyController controller,
        Action<EnemySpawnHandle, EnemyExitReason> ended)
    {
        Disarm();
        handle = new EnemySpawnHandle(spawnId, controller, this);
        onEnded = ended;
        settled = false;
        armed = true;
        Subscribe();
    }

    public void Disarm()
    {
        Unsubscribe();
        armed = false;
        onEnded = null;
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        TryEnd(EnemyExitReason.Disabled);
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (!armed || subscribed)
        {
            return;
        }

        health = GetComponent<Health>();

        if (health == null)
        {
            return;
        }

        health.Died += HandleDeath;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (health != null && subscribed)
        {
            health.Died -= HandleDeath;
        }

        subscribed = false;
    }

    private void HandleDeath()
    {
        TryEnd(EnemyExitReason.Died);
    }

    private bool TryEnd(EnemyExitReason reason)
    {
        if (!armed || settled)
        {
            return false;
        }

        settled = true;
        onEnded?.Invoke(handle, reason);
        return true;
    }
}
