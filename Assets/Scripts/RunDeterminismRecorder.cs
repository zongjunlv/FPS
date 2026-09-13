using System;
using System.Collections.Generic;
using System.IO;
using FPS.Determinism;
using UnityEngine;

[DefaultExecutionOrder(1100)]
public sealed class RunDeterminismRecorder : MonoBehaviour
{
    private DeterministicRun run;
    private PlayerInputReader input;
    private PlayerUpgradeController upgrades;
    private WaveDirector waves;
    private PlayerLootRewardController loot;
    private PlayerCombatController combat;
    private PlayerInputSample pendingInput;
    private bool hasPendingInput;
    private string lastRecordedInputPayload;
    private RunReplayRuntimeAdapter stateCapture;
    private int checkpointIntervalTicks = 50;

    public static RunDeterminismRecorder Active { get; private set; }
    public RunRecord Record => run?.Record;
    public int EventCount => run?.Record.Events.Count ?? 0;
    public string LastCheckpointError { get; private set; }

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            enabled = false;
            return;
        }

        Active = this;
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void OnDestroy()
    {
        Unbind();
        if (Active == this) Active = null;
    }

    public void Configure(
        int runSeed,
        PlayerInputReader inputSource,
        PlayerUpgradeController upgradeSource,
        WaveDirector waveSource,
        PlayerLootRewardController lootSource,
        WaveSequenceDefinition sequence)
    {
        Unbind();
        run = new DeterministicRun(runSeed);
        hasPendingInput = false;
        lastRecordedInputPayload = null;
        LastCheckpointError = string.Empty;
        input = inputSource;
        upgrades = upgradeSource;
        waves = waveSource;
        loot = lootSource;
        combat = GetComponent<PlayerCombatController>();

        if (input != null) input.InputSampled += HandleInputSampled;
        if (upgrades != null) upgrades.UpgradeSelected += HandleUpgradeSelected;
        if (waves != null) waves.EnemySpawned += HandleEnemySpawned;
        if (waves != null) waves.EnemyDied += HandleEnemyDied;
        if (waves != null) waves.WaveStarted += HandleWaveStarted;
        if (waves != null) waves.WaveEnded += HandleWaveEnded;
        if (waves != null) waves.WaveCompleted += HandleWavesCompleted;
        if (loot != null) loot.RewardSettled += HandleLootSettled;
        if (combat != null) combat.ShotResolved += HandleShotResolved;

        RecordPreparedWaves(sequence);
    }

    public string ExportJson()
    {
        if (run == null)
        {
            throw new InvalidOperationException("战局记录器尚未配置。");
        }

        return RunRecordCodec.Export(run.Record);
    }

    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("战局记录路径不能为空。", nameof(path));
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, ExportJson());
    }

    private void Unbind()
    {
        if (input != null) input.InputSampled -= HandleInputSampled;
        if (upgrades != null) upgrades.UpgradeSelected -= HandleUpgradeSelected;
        if (waves != null) waves.EnemySpawned -= HandleEnemySpawned;
        if (waves != null) waves.EnemyDied -= HandleEnemyDied;
        if (waves != null) waves.WaveStarted -= HandleWaveStarted;
        if (waves != null) waves.WaveEnded -= HandleWaveEnded;
        if (waves != null) waves.WaveCompleted -= HandleWavesCompleted;
        if (loot != null) loot.RewardSettled -= HandleLootSettled;
        if (combat != null) combat.ShotResolved -= HandleShotResolved;
        input = null;
        upgrades = null;
        waves = null;
        loot = null;
        combat = null;
        stateCapture = null;
    }

    private void HandleInputSampled(PlayerInputSample sample)
    {
        pendingInput = sample;
        hasPendingInput = true;
    }

    private void FixedUpdate()
    {
        if (run == null || !hasPendingInput) return;

        run.AdvanceTick();
        string payload = CreateInputPayload(pendingInput);
        if (!string.Equals(payload, lastRecordedInputPayload,
                StringComparison.Ordinal))
        {
            run.RecordEvent(RunEventType.InputSampled, payload);
            lastRecordedInputPayload = payload;
        }

        if (stateCapture != null && run.Tick % checkpointIntervalTicks == 0)
        {
            try
            {
                run.RecordCheckpoint(stateCapture.CaptureState());
                LastCheckpointError = string.Empty;
            }
            catch (Exception exception)
            {
                // Determinism diagnostics must never interrupt the live run.
                LastCheckpointError = exception.Message;
            }
        }
    }

    public void ConfigureStateCapture(
        RunReplayRuntimeAdapter capture,
        int intervalTicks = 50)
    {
        stateCapture = capture;
        checkpointIntervalTicks = Mathf.Max(1, intervalTicks);
    }

    public void RecordCheckpoint(ReplayStateSnapshot state)
    {
        EnsureConfigured();
        run.RecordCheckpoint(state);
    }

    public void RecordInput(PlayerInputSample sample)
    {
        EnsureConfigured();
        run.AdvanceTick();
        string payload = CreateInputPayload(sample);
        run.RecordEvent(RunEventType.InputSampled, payload);
        lastRecordedInputPayload = payload;
    }

    private static string CreateInputPayload(PlayerInputSample sample)
    {
        int flags = 0;
        SetFlag(ref flags, 0, sample.JumpPressed);
        SetFlag(ref flags, 1, sample.SprintHeld);
        SetFlag(ref flags, 2, sample.InteractPressed);
        SetFlag(ref flags, 3, sample.InteractHeld);
        SetFlag(ref flags, 4, sample.InteractReleased);
        SetFlag(ref flags, 5, sample.AttackPressed);
        SetFlag(ref flags, 6, sample.AttackHeld);
        SetFlag(ref flags, 7, sample.AimingPressed);
        SetFlag(ref flags, 8, sample.AimingHeld);
        SetFlag(ref flags, 9, sample.CrouchPressed);
        SetFlag(ref flags, 10, sample.PausePressed);
        SetFlag(ref flags, 11, sample.InventoryPressed);
        SetFlag(ref flags, 12, sample.PickupPressed);
        SetFlag(ref flags, 13, sample.ReloadPressed);
        SetFlag(ref flags, 14, sample.LookUsesPointerDelta);

        return StableEventPayload.Create(
            RunPayloadField.Number("flags", flags),
            RunPayloadField.Number("lookX", Quantize(sample.Look.x)),
            RunPayloadField.Number("lookY", Quantize(sample.Look.y)),
            RunPayloadField.Number("moveX", Quantize(sample.Move.x)),
            RunPayloadField.Number("moveY", Quantize(sample.Move.y)),
            RunPayloadField.Number("quickUse", sample.QuickUseSelection),
            RunPayloadField.Number("weaponCycle", sample.WeaponCycleDirection),
            RunPayloadField.Number("weaponSlot", sample.WeaponSelection));
    }

    private void HandleUpgradeSelected(PlayerUpgradeSelectionEvent selection)
    {
        RecordUpgrade(selection);
    }

    public void RecordUpgrade(PlayerUpgradeSelectionEvent selection)
    {
        EnsureConfigured();
        run.RecordEvent(RunEventType.UpgradeSelected, StableEventPayload.Create(
            RunPayloadField.Number("candidateIndex", selection.CandidateIndex),
            RunPayloadField.Text("candidates", Join(selection.CandidateIds)),
            RunPayloadField.Text("id", selection.StableId),
            RunPayloadField.Number("level", selection.ResultingLevel)));
    }

    private void HandleEnemySpawned(EnemySpawnedEvent spawned)
    {
        RecordEnemySpawn(spawned);
    }

    public void RecordEnemySpawn(EnemySpawnedEvent spawned)
    {
        EnsureConfigured();
        WaveEnemyEntry entry = spawned.Request.Entry;
        Vector3 position = spawned.Handle.Controller != null
            ? spawned.Handle.Controller.transform.position
            : spawned.Request.Position;
        run.RecordEvent(RunEventType.EnemySpawned, StableEventPayload.Create(
            RunPayloadField.Text("ability", entry?.AbilitySet?.StableId ?? string.Empty),
            RunPayloadField.Text("affix", entry?.Affix?.StableId ?? string.Empty),
            RunPayloadField.Text("archetype", entry?.Archetype?.StableId ?? string.Empty),
            RunPayloadField.Text("enemyType", entry?.EnemyTypeId ?? "*"),
            RunPayloadField.Number("spawnId", spawned.Request.SpawnId),
            RunPayloadField.Number("wave", spawned.Request.WaveNumber),
            RunPayloadField.Number("x", Quantize(position.x)),
            RunPayloadField.Number("y", Quantize(position.y)),
            RunPayloadField.Number("z", Quantize(position.z))));

        if (entry != null && entry.IsElite)
        {
            run.RecordEvent(RunEventType.EliteGenerated, StableEventPayload.Create(
                RunPayloadField.Text("affix", entry.Affix?.StableId ?? string.Empty),
                RunPayloadField.Text("enemyType", entry.EnemyTypeId),
                RunPayloadField.Number("spawnId", spawned.Request.SpawnId),
                RunPayloadField.Number("wave", spawned.Request.WaveNumber)));
        }
    }

    public bool TryRecordAiDecision(
        int spawnId,
        string previousActionId,
        string selectedActionId,
        string reasonCode,
        float score)
    {
        if (run == null)
        {
            return false;
        }

        run.RecordEvent(RunEventType.AiDecision, StableEventPayload.Create(
            RunPayloadField.Text("action", selectedActionId ?? string.Empty),
            RunPayloadField.Text("previous", previousActionId ?? string.Empty),
            RunPayloadField.Text("reason", reasonCode ?? string.Empty),
            RunPayloadField.Number("score", Quantize(score)),
            RunPayloadField.Number("spawnId", Mathf.Max(0, spawnId))));
        return true;
    }

    public bool TryRecordCombatDirectorDecision(
        string result,
        FPS.Simulation.CombatDirectorEventState directorEvent,
        FPS.Simulation.CombatPressureSnapshot pressure)
    {
        if (run == null || directorEvent == null) return false;
        run.RecordEvent(
            RunEventType.CombatDirectorDecision,
            StableEventPayload.Create(
                RunPayloadField.Number("angle", directorEvent.SignedDirectionDegrees),
                RunPayloadField.Number("eventId", directorEvent.EventId),
                RunPayloadField.Text("reason", directorEvent.Reason),
                RunPayloadField.Text("result", result ?? string.Empty),
                RunPayloadField.Text("role", directorEvent.RoleTag),
                RunPayloadField.Number("score", Quantize(directorEvent.SelectedScore)),
                RunPayloadField.Number("spawned", directorEvent.SpawnedCount),
                RunPayloadField.Number("requested", directorEvent.RequestedCount),
                RunPayloadField.Text("type", directorEvent.EnemyTypeId),
                RunPayloadField.Number(
                    "pressure",
                    Quantize(pressure?.OverallPressure ?? 0f))));
        return true;
    }

    public bool RecordEncounterTransition(
        FPS.Simulation.EncounterTransition transition,
        FPS.Simulation.EncounterKind kind)
    {
        if (run == null || transition.Signal ==
            FPS.Simulation.EncounterSignal.None)
            return false;
        run.RecordEvent(
            RunEventType.EncounterTransition,
            StableEventPayload.Create(
                RunPayloadField.Text("id", transition.EncounterId),
                RunPayloadField.Number("kind", (int)kind),
                RunPayloadField.Number("phase", (int)transition.Phase),
                RunPayloadField.Number("progress", transition.Progress),
                RunPayloadField.Number("signal", (int)transition.Signal),
                RunPayloadField.Number("target", transition.Target),
                RunPayloadField.Number("deadlineTick", transition.DeadlineTick)));
        return true;
    }

    private void HandleShotResolved(ShotResult result)
    {
        RecordShot(result);
    }

    public void RecordShot(ShotResult result)
    {
        EnsureConfigured();
        WaveEnemyLifecycle lifecycle = result.DamageTarget != null
            ? result.DamageTarget.GetComponentInParent<WaveEnemyLifecycle>()
            : null;
        run.RecordEvent(RunEventType.ShotFired, StableEventPayload.Create(
            RunPayloadField.Number("damage", Quantize(result.Damage.AppliedAmount)),
            RunPayloadField.Flag("hit", result.DidHit),
            RunPayloadField.Flag("killed", result.Damage.WasKilled),
            RunPayloadField.Number("region", (int)result.Damage.Region),
            RunPayloadField.Number("spawnId", lifecycle != null ? lifecycle.SpawnId : 0),
            RunPayloadField.Number("surface", (int)result.Surface),
            RunPayloadField.Text("weapon", combat?.EquippedWeapon?.StableId ?? string.Empty),
            RunPayloadField.Number("x", Quantize(result.Point.x)),
            RunPayloadField.Number("y", Quantize(result.Point.y)),
            RunPayloadField.Number("z", Quantize(result.Point.z))));
    }

    private void HandleEnemyDied(EnemyDeathEvent death)
    {
        RecordEnemyDeath(death);
    }

    public void RecordEnemyDeath(EnemyDeathEvent death)
    {
        EnsureConfigured();
        run.RecordEvent(RunEventType.EnemyKilled, StableEventPayload.Create(
            RunPayloadField.Number("damage", Quantize(death.LastDamage.Amount)),
            RunPayloadField.Number("damageType", (int)death.DamageType),
            RunPayloadField.Text("enemyType", death.EnemyTypeId),
            RunPayloadField.Number("rewardXp", death.RewardExperience),
            RunPayloadField.Number("spawnId", death.SpawnId),
            RunPayloadField.Number("wave", death.WaveNumber),
            RunPayloadField.Number("x", Quantize(death.WorldPosition.x)),
            RunPayloadField.Number("y", Quantize(death.WorldPosition.y)),
            RunPayloadField.Number("z", Quantize(death.WorldPosition.z))));
    }

    private void HandleWaveStarted(int waveNumber)
    {
        RecordWaveTransition("started", waveNumber);
    }

    private void HandleWaveEnded(int waveNumber)
    {
        RecordWaveTransition("ended", waveNumber);
    }

    private void HandleWavesCompleted()
    {
        RecordWaveTransition("run-completed", waves != null ? waves.CurrentProgress.CurrentWave : 0);
    }

    public void RecordWaveTransition(string phase, int waveNumber)
    {
        EnsureConfigured();
        run.RecordEvent(RunEventType.WaveTransition, StableEventPayload.Create(
            RunPayloadField.Text("phase", phase ?? string.Empty),
            RunPayloadField.Number("wave", waveNumber)));
    }

    private void HandleLootSettled(LootRewardSettlement settlement)
    {
        RecordLoot(settlement);
    }

    public void RecordLoot(LootRewardSettlement settlement)
    {
        EnsureConfigured();
        var drops = new List<string>(settlement.ResolvedDrops.Count);
        for (int index = 0; index < settlement.ResolvedDrops.Count; index++)
        {
            LootDropStack drop = settlement.ResolvedDrops[index];
            drops.Add(drop.ItemStableId + ":" + drop.Quantity);
        }

        run.RecordEvent(RunEventType.LootGenerated, StableEventPayload.Create(
            RunPayloadField.Text("drops", string.Join(",", drops)),
            RunPayloadField.Number("sourceId", settlement.SourceId),
            RunPayloadField.Number("spawnedStacks", settlement.SpawnedStackCount),
            RunPayloadField.Number("tier", (int)settlement.RewardTier),
            RunPayloadField.Number("wave", settlement.WaveNumber)));
    }

    private void RecordPreparedWaves(WaveSequenceDefinition sequence)
    {
        if (sequence == null) return;
        for (int index = 0; index < sequence.WaveCount; index++)
        {
            WaveDefinition wave = sequence.GetStage(index).Wave;
            var entries = new List<string>(wave.ResolvedEntries.Count);
            for (int entryIndex = 0; entryIndex < wave.ResolvedEntries.Count; entryIndex++)
            {
                WaveEnemyEntry entry = wave.ResolvedEntries[entryIndex];
                entries.Add(entry?.EnemyTypeId ?? "*");
            }

            run.RecordEvent(RunEventType.WaveGenerated, StableEventPayload.Create(
                RunPayloadField.Text("entries", string.Join(",", entries)),
                RunPayloadField.Text("id", wave.StableId ?? string.Empty),
                RunPayloadField.Number("mode", (int)wave.CompositionMode),
                RunPayloadField.Number("threat", wave.ResolvedThreatCost),
                RunPayloadField.Number("wave", index + 1)));
        }
    }

    private static int Quantize(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
        return Mathf.RoundToInt(value * 1000f);
    }

    private static void SetFlag(ref int flags, int bit, bool value)
    {
        if (value) flags |= 1 << bit;
    }

    private static string Join(IReadOnlyList<string> values)
    {
        return values == null ? string.Empty : string.Join(",", values);
    }

    private void EnsureConfigured()
    {
        if (run == null)
        {
            throw new InvalidOperationException("战局记录器尚未配置。");
        }
    }
}
