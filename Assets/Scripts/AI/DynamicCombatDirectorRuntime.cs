using System;
using System.Collections.Generic;
using FPS.Simulation;
using UnityEngine;

/// <summary>
/// Unity-facing adapter for the pure fixed-tick director. It samples combat
/// facts, but leaves spawn execution to WaveDirector.
/// </summary>
public sealed class DynamicCombatDirectorRuntime
{
    private const int DamageWindowTicks = WaveDirector.SimulationTickRate * 8;
    private const int KillWindowTicks = WaveDirector.SimulationTickRate * 15;
    private const int HeatWindowTicks = WaveDirector.SimulationTickRate * 15;
    private const float HeatCellSize = 8f;

    private readonly Queue<TimedAmount> damageSamples = new();
    private readonly Queue<long> killSamples = new();
    private readonly Queue<HeatSample> heatSamples = new();
    private readonly Dictionary<long, int> heatCounts = new();
    private readonly List<CombatDirectorRoleCandidate> candidates = new();
    private DynamicCombatDirector director;
    private WaveDirector waves;
    private Transform player;
    private Health health;
    private WeaponLoadoutController loadout;
    private UnifiedGameHud hud;
    private long currentTick;
    private CombatPressureSnapshot lastPressure;
    private CombatDirectorResources lastResources;
    private CombatDirectorSignal lastSignal;

    public CombatDirectorPhase Phase => director?.Phase ??
        CombatDirectorPhase.Observing;
    public CombatDirectorEventState CurrentEvent => director?.CurrentEvent;
    public bool PausesNormalSpawning =>
        Phase == CombatDirectorPhase.Warning ||
        Phase == CombatDirectorPhase.Deploying;
    public bool HasSpawnRequest => lastSignal ==
        CombatDirectorSignal.SpawnRequested;

    public void Configure(
        int runSeed,
        WaveDirector waveDirector,
        Transform playerTarget,
        Health playerHealth,
        WeaponLoadoutController weaponLoadout)
    {
        Unbind();
        waves = waveDirector;
        player = playerTarget;
        health = playerHealth;
        loadout = weaponLoadout;
        hud = UnityEngine.Object.FindAnyObjectByType<UnifiedGameHud>();
        director = new DynamicCombatDirector(
            runSeed,
            new CombatDirectorConfiguration(
                WaveDirector.SimulationTickRate / 2,
                WaveDirector.SimulationTickRate / 2,
                WaveDirector.SimulationTickRate * 3 / 2,
                WaveDirector.SimulationTickRate * 12,
                1,
                1,
                0.55f));
        currentTick = 0;
        lastSignal = CombatDirectorSignal.None;
        lastPressure = null;
        lastResources = null;
        if (health != null) health.DamageApplied += HandleDamage;
        if (waves != null) waves.EnemyDied += HandleEnemyDied;
    }

    public void Unbind()
    {
        if (health != null) health.DamageApplied -= HandleDamage;
        if (waves != null) waves.EnemyDied -= HandleEnemyDied;
        waves = null;
        player = null;
        health = null;
        loadout = null;
        hud = null;
        director = null;
        damageSamples.Clear();
        killSamples.Clear();
        heatSamples.Clear();
        heatCounts.Clear();
        candidates.Clear();
    }

    public CombatDirectorOutput AdvanceTick(
        long tick,
        WaveDefinition definition,
        int spawnedCount,
        int aliveCount,
        int poolCapacity,
        bool directedSpawnReady)
    {
        currentTick = Math.Max(0, tick);
        PruneSamples();
        SampleHeat();
        lastPressure = BuildPressure(definition, aliveCount);
        lastResources = BuildResources(
            definition,
            spawnedCount,
            aliveCount,
            poolCapacity,
            directedSpawnReady);
        CombatDirectorOutput output = director.Advance(
            new CombatDirectorInput(currentTick, lastPressure, lastResources));
        lastSignal = output.Signal;

        if (output.Signal == CombatDirectorSignal.WarningStarted)
        {
            string side = output.CurrentEvent.SignedDirectionDegrees < 0
                ? "左后方"
                : "右后方";
            hud?.ShowCombatWarning(
                $"战术警报：{side}侦测到 {RoleLabel(output.CurrentEvent.RoleTag)} 增援",
                2f);
            RecordDecision("warning");
        }
        else if (output.Signal == CombatDirectorSignal.EventCancelled)
        {
            RecordDecision("cancelled");
        }

        return output;
    }

    public void ReportSpawnOutcome(bool success)
    {
        if (director == null) return;
        CombatDirectorEventState before = director.CurrentEvent;
        director.ReportSpawnOutcome(currentTick, success);
        RecordDecision(
            success ? "spawned" : "spawn-failed",
            director.CurrentEvent ?? before);
        lastSignal = director.Phase == CombatDirectorPhase.Deploying
            ? CombatDirectorSignal.SpawnRequested
            : CombatDirectorSignal.None;
    }

    public CombatDirectorRuntimeSnapshot CaptureState() =>
        director?.CaptureState();

    public bool CanRestore(
        CombatDirectorRuntimeSnapshot snapshot,
        out string error)
    {
        if (director == null)
        {
            error = "战斗导演尚未配置。";
            return false;
        }
        return director.CanRestore(snapshot, out error);
    }

    public bool TryRestore(
        CombatDirectorRuntimeSnapshot snapshot,
        out string error)
    {
        if (director == null)
        {
            error = "战斗导演尚未配置。";
            return false;
        }
        if (!director.TryRestore(snapshot, out error))
        {
            return false;
        }
        lastSignal = director.Phase == CombatDirectorPhase.Deploying
            ? CombatDirectorSignal.SpawnRequested
            : CombatDirectorSignal.None;
        return true;
    }

    public CombatDirectorRuntimeDiagnostics CaptureDiagnostics()
    {
        CombatDirectorRuntimeSnapshot state = director?.CaptureState();
        return new CombatDirectorRuntimeDiagnostics(
            Phase.ToString(),
            lastSignal.ToString(),
            lastPressure?.OverallPressure ?? 0f,
            lastPressure?.HealthRatio ?? 0f,
            lastPressure?.ArmorRatio ?? 0f,
            lastPressure?.AmmoRatio ?? 0f,
            lastPressure?.RecentDamageRatio ?? 0f,
            lastPressure?.ClearRateNormalized ?? 0f,
            lastPressure?.ActiveThreatNormalized ?? 0f,
            lastPressure?.HabitualHeatRatio ?? 0f,
            lastPressure?.HeatCellX ?? 0,
            lastPressure?.HeatCellZ ?? 0,
            lastResources?.RemainingWaveSlots ?? 0,
            lastResources?.AliveCapacity ?? 0,
            lastResources?.PoolCapacity ?? 0,
            lastResources?.DirectedSpawnReachable ?? false,
            CurrentEvent?.EnemyTypeId,
            CurrentEvent?.RoleTag,
            CurrentEvent?.RequestedCount ?? 0,
            CurrentEvent?.SpawnedCount ?? 0,
            CurrentEvent?.SignedDirectionDegrees ?? 0,
            CurrentEvent?.SelectedScore ?? 0f,
            CurrentEvent?.Reason,
            state?.WarningEndTick ?? 0,
            state?.CooldownEndTick ?? 0);
    }

    private CombatPressureSnapshot BuildPressure(
        WaveDefinition definition,
        int aliveCount)
    {
        float healthRatio = health != null && health.MaxHealth > 0f
            ? health.CurrentHealth / health.MaxHealth
            : 0f;
        // A build with no armour is neutral rather than permanently "empty".
        float armorRatio = health != null && health.MaxArmor > 0f
            ? health.CurrentArmor / health.MaxArmor
            : 0.5f;
        WeaponController weapon = loadout != null ? loadout.CurrentWeapon : null;
        float ammoRatio = weapon != null
            ? (weapon.CurrentAmmo + weapon.ReserveAmmo) /
              (float)Math.Max(1, weapon.MagazineCapacity + weapon.MaximumReserveAmmo)
            : 0f;
        float recentDamage = 0f;
        foreach (TimedAmount sample in damageSamples) recentDamage += sample.Amount;
        recentDamage /= health != null ? Math.Max(1f, health.MaxHealth) : 100f;
        float clearRate = Math.Min(1f, killSamples.Count / 4f);
        float activeThreat = definition != null
            ? Math.Min(1f, aliveCount /
                (float)Math.Max(1, definition.MaximumAliveCount))
            : 0f;
        int cellX = player != null
            ? Mathf.FloorToInt(player.position.x / HeatCellSize)
            : 0;
        int cellZ = player != null
            ? Mathf.FloorToInt(player.position.z / HeatCellSize)
            : 0;
        heatCounts.TryGetValue(HeatKey(cellX, cellZ), out int heatCount);
        float heat = heatSamples.Count > 0
            ? heatCount / (float)heatSamples.Count
            : 0f;
        return new CombatPressureSnapshot(
            healthRatio,
            armorRatio,
            ammoRatio,
            recentDamage,
            clearRate,
            activeThreat,
            heat,
            cellX,
            cellZ);
    }

    private CombatDirectorResources BuildResources(
        WaveDefinition definition,
        int spawnedCount,
        int aliveCount,
        int poolCapacity,
        bool directedSpawnReady)
    {
        candidates.Clear();
        // A director intervention relocates the next scheduled wave member;
        // it never replaces the authored/deterministically composed roster.
        if (definition != null && spawnedCount < definition.TotalEnemyCount)
        {
            WaveEnemyEntry entry = definition.GetEntry(spawnedCount);
            if (entry != null)
                candidates.Add(new CombatDirectorRoleCandidate(
                    entry.EnemyTypeId,
                    entry.RoleTag,
                    entry.ThreatCost));
        }
        return new CombatDirectorResources(
            Math.Max(0, (definition?.TotalEnemyCount ?? 0) - spawnedCount),
            Math.Max(0, (definition?.MaximumAliveCount ?? 0) - aliveCount),
            Math.Max(0, poolCapacity),
            directedSpawnReady,
            candidates.ToArray());
    }

    private void HandleDamage(DamageResult result)
    {
        if (result.WasApplied && result.AppliedAmount > 0f)
            damageSamples.Enqueue(new TimedAmount(currentTick, result.AppliedAmount));
    }

    private void HandleEnemyDied(EnemyDeathEvent value)
    {
        killSamples.Enqueue(currentTick);
    }

    private void SampleHeat()
    {
        if (player == null) return;
        int x = Mathf.FloorToInt(player.position.x / HeatCellSize);
        int z = Mathf.FloorToInt(player.position.z / HeatCellSize);
        long key = HeatKey(x, z);
        heatSamples.Enqueue(new HeatSample(currentTick, key));
        heatCounts.TryGetValue(key, out int count);
        heatCounts[key] = count + 1;
    }

    private void PruneSamples()
    {
        while (damageSamples.Count > 0 &&
               currentTick - damageSamples.Peek().Tick > DamageWindowTicks)
            damageSamples.Dequeue();
        while (killSamples.Count > 0 &&
               currentTick - killSamples.Peek() > KillWindowTicks)
            killSamples.Dequeue();
        while (heatSamples.Count > 0 &&
               currentTick - heatSamples.Peek().Tick > HeatWindowTicks)
        {
            HeatSample expired = heatSamples.Dequeue();
            int remaining = heatCounts[expired.Key] - 1;
            if (remaining <= 0) heatCounts.Remove(expired.Key);
            else heatCounts[expired.Key] = remaining;
        }
    }

    private void RecordDecision(
        string result,
        CombatDirectorEventState value = null)
    {
        RunDeterminismRecorder.Active?.TryRecordCombatDirectorDecision(
            result,
            value ?? CurrentEvent,
            lastPressure);
    }

    private static string RoleLabel(string role)
    {
        return role switch
        {
            "raider" => "突袭",
            "support" => "支援",
            "suppressor" => "压制",
            "elite" => "精英",
            _ => "战斗"
        };
    }

    private static long HeatKey(int x, int z) =>
        ((long)x << 32) ^ (uint)z;

    private readonly struct TimedAmount
    {
        public TimedAmount(long tick, float amount)
        {
            Tick = tick;
            Amount = amount;
        }
        public long Tick { get; }
        public float Amount { get; }
    }

    private readonly struct HeatSample
    {
        public HeatSample(long tick, long key)
        {
            Tick = tick;
            Key = key;
        }
        public long Tick { get; }
        public long Key { get; }
    }
}
