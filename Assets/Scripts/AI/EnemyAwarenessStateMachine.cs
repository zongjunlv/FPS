using UnityEngine;

public enum EnemyAwarenessState
{
    Patrol,
    Suspicious,
    Alert,
    Search
}

public sealed class EnemyAwarenessStateMachine
{
    private const float LostSightGraceDuration = 0.75f;
    private const float MaxSearchApproachDuration = 4f;

    private float alertSpeed = 1f;
    private float awarenessDecaySpeed = 0.5f;
    private float searchDuration = 4f;
    private float searchRemaining;
    private float searchApproachRemaining;
    private float lostSightElapsed;
    private bool searchAreaReached;

    public EnemyAwarenessState State { get; private set; } =
        EnemyAwarenessState.Patrol;
    public float Awareness { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public float SearchTimeRemaining => searchRemaining;

    public void Configure(
        float configuredAlertSpeed,
        float configuredDecaySpeed,
        float configuredSearchDuration)
    {
        alertSpeed = Mathf.Max(0.01f, configuredAlertSpeed);
        awarenessDecaySpeed =
            Mathf.Max(0.01f, configuredDecaySpeed);
        searchDuration = Mathf.Max(0.1f, configuredSearchDuration);
    }

    public void Observe(Vector3 position, float deltaTime)
    {
        LastKnownPosition = position;
        lostSightElapsed = 0f;
        Awareness = Mathf.Clamp01(
            Awareness + Mathf.Max(0f, deltaTime) * alertSpeed);
        State = Awareness >= 1f
            ? EnemyAwarenessState.Alert
            : EnemyAwarenessState.Suspicious;
    }

    public void Hear(Vector3 position, float strength)
    {
        if (State == EnemyAwarenessState.Alert ||
            strength <= 0f)
        {
            return;
        }

        LastKnownPosition = position;
        Awareness = Mathf.Max(
            Awareness,
            Mathf.Clamp01(strength));
        State = EnemyAwarenessState.Suspicious;
    }

    public void Tick(float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0f, deltaTime);

        if (State == EnemyAwarenessState.Alert)
        {
            lostSightElapsed += safeDeltaTime;

            if (lostSightElapsed >= LostSightGraceDuration)
            {
                BeginSearch();
            }

            return;
        }

        if (State == EnemyAwarenessState.Search)
        {
            return;
        }

        Awareness = Mathf.Clamp01(
            Awareness - safeDeltaTime * awarenessDecaySpeed);

        State = Awareness > 0f
            ? EnemyAwarenessState.Suspicious
            : EnemyAwarenessState.Patrol;
    }

    public void AdvanceSearch(
        float deltaTime,
        bool destinationReached)
    {
        if (State != EnemyAwarenessState.Search)
        {
            return;
        }

        float remainingDelta = Mathf.Max(0f, deltaTime);

        if (!searchAreaReached)
        {
            if (destinationReached)
            {
                searchAreaReached = true;
            }
            else
            {
                searchApproachRemaining -= remainingDelta;

                if (searchApproachRemaining > 0f)
                {
                    return;
                }

                remainingDelta = -searchApproachRemaining;
                searchApproachRemaining = 0f;
                searchAreaReached = true;
            }
        }

        searchRemaining -= remainingDelta;

        if (searchRemaining > 0f)
        {
            return;
        }

        searchRemaining = 0f;
        Awareness = 0f;
        State = EnemyAwarenessState.Patrol;
    }

    public void BeginSearchAt(Vector3 position)
    {
        LastKnownPosition = position;
        BeginSearch();
    }

    private void BeginSearch()
    {
        State = EnemyAwarenessState.Search;
        searchRemaining = searchDuration;
        searchApproachRemaining = MaxSearchApproachDuration;
        searchAreaReached = false;
        lostSightElapsed = 0f;
    }
}
