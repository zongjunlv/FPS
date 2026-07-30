using UnityEngine;

public sealed class MissionRunStatistics
{
    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public int Kills { get; private set; }
    public int DamageTakenCount { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public float Accuracy =>
        ShotsFired <= 0 ? 0f : (float)Hits / ShotsFired;

    public void RegisterShot(ShotResult result)
    {
        ShotsFired++;

        if (!result.Damage.WasApplied)
        {
            return;
        }

        Hits++;

        if (result.Damage.WasKilled &&
            result.DamageTarget != null &&
            result.DamageTarget.GetComponentInParent<EnemyController>() !=
            null)
        {
            Kills++;
        }
    }

    public void RegisterDamageTaken()
    {
        DamageTakenCount++;
    }

    public void AdvanceTime(float deltaTime)
    {
        ElapsedSeconds += Mathf.Max(0f, deltaTime);
    }

    public void Reset()
    {
        ShotsFired = 0;
        Hits = 0;
        Kills = 0;
        DamageTakenCount = 0;
        ElapsedSeconds = 0f;
    }
}
