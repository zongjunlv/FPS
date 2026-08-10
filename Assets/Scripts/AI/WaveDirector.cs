using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class WaveDirector : MonoBehaviour, IWaveProgressSource
{
    private readonly Dictionary<int, EnemySpawnHandle> activeEnemies = new();
    private readonly List<Vector3> occupiedPositions = new();

    private WaveDefinition definition;
    private IEnemyFactory enemyFactory;
    private IEnemySpawnPointResolver spawnPointResolver;
    private Transform player;
    private SingleWaveState state;
    private float spawnCooldown;
    private int nextSpawnId = 1;
    private bool configured;

    public static WaveDirector Active { get; private set; }

    public event Action<WaveProgressSnapshot> ProgressChanged;
    public event Action WaveCompleted;

    public WaveProgressSnapshot CurrentProgress =>
        state != null
            ? state.Progress
            : new WaveProgressSnapshot(0, 0, 0, 0);
    public bool IsRunning { get; private set; }
    public bool IsCompleted => state != null && state.IsComplete;
    public int PeakAliveCount { get; private set; }
    public int SpawnAttemptCount { get; private set; }
    public int CompletionEventCount { get; private set; }
    public float MinimumSpawnSafetyDistanceObserved { get; private set; }
    public float MinimumSpawnEnemySpacingObserved { get; private set; }
    public IReadOnlyDictionary<int, EnemySpawnHandle> ActiveEnemies =>
        activeEnemies;

    private void Awake()
    {
        Active = this;
    }

    private void Update()
    {
        if (!configured || !IsRunning || !state.CanSpawn)
        {
            return;
        }

        spawnCooldown -= Time.deltaTime;

        if (spawnCooldown > 0f)
        {
            return;
        }

        TrySpawnNext();
    }

    private void OnDestroy()
    {
        if (state != null)
        {
            state.Completed -= HandleCompleted;
        }

        foreach (EnemySpawnHandle handle in activeEnemies.Values)
        {
            handle.Lifecycle?.Disarm();
        }

        activeEnemies.Clear();

        if (Active == this)
        {
            Active = null;
        }
    }

    public void Configure(
        WaveDefinition waveDefinition,
        IEnemyFactory factory,
        IEnemySpawnPointResolver resolver,
        Transform playerTarget)
    {
        if (waveDefinition == null)
        {
            throw new ArgumentNullException(nameof(waveDefinition));
        }

        definition = waveDefinition;
        enemyFactory = factory ??
            throw new ArgumentNullException(nameof(factory));
        spawnPointResolver = resolver ??
            throw new ArgumentNullException(nameof(resolver));
        player = playerTarget ??
            throw new ArgumentNullException(nameof(playerTarget));

        if (state != null)
        {
            state.Completed -= HandleCompleted;
        }

        state = new SingleWaveState(
            definition.TotalEnemyCount,
            definition.MaximumAliveCount);
        state.Completed += HandleCompleted;
        activeEnemies.Clear();
        nextSpawnId = 1;
        PeakAliveCount = 0;
        SpawnAttemptCount = 0;
        CompletionEventCount = 0;
        MinimumSpawnSafetyDistanceObserved = float.PositiveInfinity;
        MinimumSpawnEnemySpacingObserved = float.PositiveInfinity;
        spawnCooldown = 0f;
        configured = true;
        IsRunning = true;
        PublishProgress();
        UnityEngine.Object.FindAnyObjectByType<UnifiedGameHud>()
            ?.BindWave(this);
    }

    private void TrySpawnNext()
    {
        SpawnAttemptCount++;
        occupiedPositions.Clear();

        foreach (EnemySpawnHandle active in activeEnemies.Values)
        {
            if (active.Controller != null)
            {
                occupiedPositions.Add(active.Controller.transform.position);
            }
        }

        if (!spawnPointResolver.TryResolve(
                player,
                occupiedPositions,
                out Vector3 spawnPoint))
        {
            spawnCooldown = definition.RetryInterval;
            return;
        }

        WaveEnemyEntry entry = definition.GetEntry(state.SpawnedCount);
        int spawnId = nextSpawnId;
        Vector3 facing = player.position - spawnPoint;
        facing.y = 0f;
        Quaternion rotation = facing.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(facing.normalized)
            : Quaternion.identity;
        var request = new EnemySpawnRequest(
            spawnId,
            entry,
            spawnPoint,
            rotation,
            player);

        if (!enemyFactory.TrySpawn(
                request,
                HandleEnemyEnded,
                out EnemySpawnHandle handle))
        {
            spawnCooldown = definition.RetryInterval;
            return;
        }

        if (!state.TryRegisterSpawn(spawnId))
        {
            enemyFactory.Release(handle);
            spawnCooldown = definition.RetryInterval;
            return;
        }

        activeEnemies.Add(spawnId, handle);
        RecordSpawnClearances(spawnPoint);
        nextSpawnId++;
        PeakAliveCount = Mathf.Max(PeakAliveCount, state.AliveCount);
        spawnCooldown = definition.SpawnInterval;
        PublishProgress();
    }

    private void RecordSpawnClearances(Vector3 spawnPoint)
    {
        Vector3 playerOffset = spawnPoint - player.position;
        playerOffset.y = 0f;
        MinimumSpawnSafetyDistanceObserved = Mathf.Min(
            MinimumSpawnSafetyDistanceObserved,
            playerOffset.magnitude);

        for (int index = 0; index < occupiedPositions.Count; index++)
        {
            Vector3 enemyOffset = spawnPoint - occupiedPositions[index];
            enemyOffset.y = 0f;
            MinimumSpawnEnemySpacingObserved = Mathf.Min(
                MinimumSpawnEnemySpacingObserved,
                enemyOffset.magnitude);
        }
    }

    private void HandleEnemyEnded(
        EnemySpawnHandle handle,
        EnemyExitReason reason)
    {
        if (state == null || !state.TrySettle(handle.SpawnId))
        {
            return;
        }

        activeEnemies.Remove(handle.SpawnId);
        enemyFactory.Release(handle);
        PublishProgress();
        spawnCooldown = Mathf.Min(spawnCooldown, definition.SpawnInterval);
    }

    private void HandleCompleted()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        CompletionEventCount++;
        WaveCompleted?.Invoke();
    }

    private void PublishProgress()
    {
        ProgressChanged?.Invoke(CurrentProgress);
    }
}
