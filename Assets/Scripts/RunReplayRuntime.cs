using System;
using System.Collections.Generic;
using System.Globalization;
using FPS.Determinism;
using FPS.GameplayEffects;
using UnityEngine;

/// <summary>Unity adapter for logical replay; physics and rendering are not pixel deterministic.</summary>
public sealed class RunReplayRuntimeAdapter : MonoBehaviour, IRunReplayAdapter
{
    private PlayerInputReader input;
    private PlayerUpgradeController upgrades;
    private PlayerRunProgression progression;
    private Health health;
    private WeaponLoadoutController loadout;
    private PlayerInventoryController inventory;
    private WaveDirector waves;
    private WaveSequenceDefinition sequence;
    private PlayerCombatBuildController combatBuilds;

    public IReadOnlyList<RunEvent> ConfigurationEvents { get; private set; } =
        Array.Empty<RunEvent>();

    public void Configure(
        PlayerInputReader inputSource,
        WaveDirector waveSource,
        WaveSequenceDefinition waveSequence = null)
    {
        input = inputSource ?? GetComponent<PlayerInputReader>();
        upgrades = GetComponent<PlayerUpgradeController>();
        progression = GetComponent<PlayerRunProgression>();
        health = GetComponent<Health>();
        loadout = GetComponent<WeaponLoadoutController>();
        inventory = GetComponent<PlayerInventoryController>();
        combatBuilds = GetComponent<PlayerCombatBuildController>();
        waves = waveSource;
        if (waveSequence != null) sequence = waveSequence;
    }

    public void Begin(long runSeed, IReadOnlyList<RunEvent> configurationEvents)
    {
        EnsureConfigured();
        if (upgrades != null && upgrades.RunSeed != runSeed)
            throw new InvalidOperationException(
                $"回放 seed {runSeed} 与当前战局 seed {upgrades.RunSeed} 不一致。");
        ConfigurationEvents = configurationEvents ?? Array.Empty<RunEvent>();
        ValidateConfiguration(ConfigurationEvents);
    }

    public void Apply(RunEvent runEvent)
    {
        if (runEvent == null) throw new ArgumentNullException(nameof(runEvent));
        if (runEvent.Type == RunEventType.InputSampled)
        {
            if (!TryDecodeInput(runEvent.Payload, out PlayerInputSample sample, out string error))
                throw new FormatException(error);
            input.ApplyReplaySample(sample);
            return;
        }

        if (runEvent.Type == RunEventType.UpgradeSelected)
        {
            IReadOnlyDictionary<string, string> payload = StableEventPayload.Parse(runEvent.Payload);
            if (!TryReadInt(payload, "candidateIndex", out int candidateIndex) ||
                upgrades == null || !upgrades.TrySelect(candidateIndex))
                throw new InvalidOperationException("记录的升级选择未能在当前 Tick 应用。");
        }
        // Wave/spawn/elite/loot events are audit markers. Their deterministic
        // runtime systems produce the state that checkpoints subsequently verify.
    }

    public ReplayStateSnapshot CaptureState()
    {
        EnsureConfigured();
        var fields = new List<ReplayStateField>(96)
        {
            ReplayStateField.Number("player/position/x", Quantize(transform.position.x)),
            ReplayStateField.Number("player/position/y", Quantize(transform.position.y)),
            ReplayStateField.Number("player/position/z", Quantize(transform.position.z)),
            ReplayStateField.Number("player/vitals/health", Quantize(health.CurrentHealth)),
            ReplayStateField.Number("player/vitals/armor", Quantize(health.CurrentArmor))
        };

        RunExperienceSnapshot experience = progression.CurrentProgress;
        fields.Add(ReplayStateField.Number("player/progression/level", experience.Level));
        fields.Add(ReplayStateField.Number("player/progression/xp", experience.CurrentExperience));
        fields.Add(ReplayStateField.Number("player/progression/totalXp", experience.TotalExperience));

        fields.Add(ReplayStateField.Text("player/weapon/current", loadout.CurrentWeapon.StableId));
        for (int index = 0; index < loadout.WeaponCount; index++)
        {
            WeaponController weapon = loadout.GetWeapon(index);
            string prefix = "player/weapon/" + index.ToString(CultureInfo.InvariantCulture);
            fields.Add(ReplayStateField.Text(prefix + "/id", weapon.StableId));
            fields.Add(ReplayStateField.Number(prefix + "/magazine", weapon.CurrentAmmo));
            fields.Add(ReplayStateField.Number(prefix + "/reserve", weapon.ReserveAmmo));
        }

        PlayerInventorySnapshot inventorySnapshot = inventory.CaptureSnapshot();
        for (int index = 0; index < inventorySnapshot.Inventory.Slots.Count; index++)
        {
            global::InventorySlotSnapshot slot = inventorySnapshot.Inventory.Slots[index];
            string prefix = "inventory/" + index.ToString(CultureInfo.InvariantCulture);
            fields.Add(ReplayStateField.Text(prefix + "/id", slot.StableId));
            fields.Add(ReplayStateField.Number(prefix + "/quantity", slot.Quantity));
        }
        for (int index = 0; index < inventorySnapshot.QuickSlots.Bindings.Count; index++)
            fields.Add(ReplayStateField.Text(
                "quickslot/" + index.ToString(CultureInfo.InvariantCulture),
                inventorySnapshot.QuickSlots.Bindings[index]));

        CaptureWaveState(fields);
        CaptureCombatBuildState(fields);
        return ReplayStateSnapshot.Create(fields);
    }

    public void Stop()
    {
        input?.StopReplay();
    }

    public static bool TryDecodeInput(
        string encoded,
        out PlayerInputSample sample,
        out string error)
    {
        sample = default;
        error = string.Empty;
        IReadOnlyDictionary<string, string> payload;
        try { payload = StableEventPayload.Parse(encoded); }
        catch (FormatException exception) { error = exception.Message; return false; }

        if (!TryReadInt(payload, "flags", out int flags) ||
            !TryReadInt(payload, "lookX", out int lookX) ||
            !TryReadInt(payload, "lookY", out int lookY) ||
            !TryReadInt(payload, "moveX", out int moveX) ||
            !TryReadInt(payload, "moveY", out int moveY) ||
            !TryReadInt(payload, "quickUse", out int quickUse) ||
            !TryReadInt(payload, "weaponCycle", out int weaponCycle) ||
            !TryReadInt(payload, "weaponSlot", out int weaponSlot))
        {
            error = "输入事件缺少字段或数值无效。";
            return false;
        }

        sample = new PlayerInputSample(
            new Vector2(moveX / 1000f, moveY / 1000f),
            new Vector2(lookX / 1000f, lookY / 1000f),
            Flag(flags, 14), Flag(flags, 0), Flag(flags, 1), Flag(flags, 2),
            Flag(flags, 3), Flag(flags, 4), Flag(flags, 5), Flag(flags, 6),
            Flag(flags, 7), Flag(flags, 8), Flag(flags, 9), Flag(flags, 10),
            Flag(flags, 11), Flag(flags, 12), Flag(flags, 13), weaponSlot,
            weaponCycle, quickUse);
        return true;
    }

    private void CaptureWaveState(List<ReplayStateField> fields)
    {
        WaveProgressSnapshot progress = waves.CurrentProgress;
        fields.Add(ReplayStateField.Number("wave/current", progress.CurrentWave));
        fields.Add(ReplayStateField.Number("wave/total", progress.TotalWaves));
        fields.Add(ReplayStateField.Number("wave/alive", progress.AliveCount));
        fields.Add(ReplayStateField.Number("wave/remaining", progress.RemainingCount));
        fields.Add(ReplayStateField.Number("wave/phase", (int)waves.Phase));

        foreach (KeyValuePair<int, EnemySpawnHandle> pair in waves.ActiveEnemies)
        {
            EnemySpawnHandle handle = pair.Value;
            string prefix = "enemy/" + pair.Key.ToString(CultureInfo.InvariantCulture);
            fields.Add(ReplayStateField.Flag(prefix + "/alive", handle.Controller != null));
            fields.Add(ReplayStateField.Text(prefix + "/type", handle.EnemyTypeId));
            if (handle.Controller == null) continue;
            Health enemyHealth = handle.Controller.GetComponent<Health>();
            Vector3 position = handle.Controller.transform.position;
            fields.Add(ReplayStateField.Number(prefix + "/position/x", Quantize(position.x)));
            fields.Add(ReplayStateField.Number(prefix + "/position/y", Quantize(position.y)));
            fields.Add(ReplayStateField.Number(prefix + "/position/z", Quantize(position.z)));
            fields.Add(ReplayStateField.Number(prefix + "/health",
                Quantize(enemyHealth != null ? enemyHealth.CurrentHealth : 0f)));
            fields.Add(ReplayStateField.Number(prefix + "/armor",
                Quantize(enemyHealth != null ? enemyHealth.CurrentArmor : 0f)));
        }
    }

    private void CaptureCombatBuildState(List<ReplayStateField> fields)
    {
        CombatRuleRuntimeSnapshot snapshot = combatBuilds.CaptureSnapshot();
        fields.Add(ReplayStateField.Number(
            "combatBuild/event/next",
            snapshot.NextEventId));
        fields.Add(ReplayStateField.Number(
            "combatBuild/installed/count",
            snapshot.InstalledBuildIds.Count));
        for (int index = 0; index < snapshot.InstalledBuildIds.Count; index++)
        {
            fields.Add(ReplayStateField.Text(
                "combatBuild/installed/" +
                index.ToString(CultureInfo.InvariantCulture),
                snapshot.InstalledBuildIds[index]));
        }
        fields.Add(ReplayStateField.Number(
            "combatBuild/cooldown/count",
            snapshot.Cooldowns.Count));
        for (int index = 0; index < snapshot.Cooldowns.Count; index++)
        {
            CombatRuleCooldownSnapshot cooldown = snapshot.Cooldowns[index];
            string prefix = "combatBuild/cooldown/" +
                index.ToString(CultureInfo.InvariantCulture);
            fields.Add(ReplayStateField.Text(prefix + "/rule", cooldown.RuleId));
            fields.Add(ReplayStateField.Number(prefix + "/readyTick", cooldown.ReadyTick));
        }
        fields.Add(ReplayStateField.Number(
            "combatBuild/events/count",
            snapshot.ProcessedEventIds.Count));
        for (int index = 0; index < snapshot.ProcessedEventIds.Count; index++)
        {
            fields.Add(ReplayStateField.Number(
                "combatBuild/events/" +
                index.ToString(CultureInfo.InvariantCulture),
                snapshot.ProcessedEventIds[index]));
        }
    }

    private void EnsureConfigured()
    {
        if (input == null || health == null || progression == null || loadout == null ||
            inventory == null || waves == null || combatBuilds == null ||
            !combatBuilds.IsInitialized)
            throw new InvalidOperationException("回放运行时依赖尚未配置完成。");
    }

    private void ValidateConfiguration(IReadOnlyList<RunEvent> recorded)
    {
        if (sequence == null) return;
        if (recorded.Count != sequence.WaveCount)
            throw new InvalidOperationException("回放记录与当前战局的波次数量不一致。");
        for (int index = 0; index < sequence.WaveCount; index++)
        {
            WaveDefinition wave = sequence.GetStage(index).Wave;
            var entries = new List<string>(wave.ResolvedEntries.Count);
            for (int entryIndex = 0; entryIndex < wave.ResolvedEntries.Count; entryIndex++)
                entries.Add(wave.ResolvedEntries[entryIndex]?.EnemyTypeId ?? "*");
            string expected = StableEventPayload.Create(
                RunPayloadField.Text("entries", string.Join(",", entries)),
                RunPayloadField.Text("id", wave.StableId ?? string.Empty),
                RunPayloadField.Number("mode", (int)wave.CompositionMode),
                RunPayloadField.Number("threat", wave.ResolvedThreatCost),
                RunPayloadField.Number("wave", index + 1));
            if (!string.Equals(recorded[index].Payload, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"回放记录与当前战局第 {index + 1} 波配置不一致。");
        }
    }

    private static bool TryReadInt(
        IReadOnlyDictionary<string, string> payload,
        string key,
        out int value)
    {
        value = 0;
        return payload.TryGetValue(key, out string text) &&
               int.TryParse(text, NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out value);
    }

    private static bool Flag(int flags, int bit) => (flags & (1 << bit)) != 0;
    private static int Quantize(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.RoundToInt(value * 1000f);
}

[DefaultExecutionOrder(-1100)]
public sealed class RunReplayController : MonoBehaviour
{
    private RunReplayPlayer replay;
    private RunReplayRuntimeAdapter adapter;
    private RunReplayChecksumVerifier verifier;
    private long nextTick;

    public RunReplayStatus Status => replay?.Status ?? RunReplayStatus.Ready;
    public ReplayDivergence Divergence => replay?.Divergence;
    public RunRecord LoadedRecord { get; private set; }
    public string LastError { get; private set; }

    public bool LoadAndStart(
        string json,
        PlayerInputReader input,
        WaveDirector waves,
        out string error)
    {
        try
        {
            RunRecord record = RunRecordCodec.Import(json);
            LoadedRecord = record;
            adapter = GetComponent<RunReplayRuntimeAdapter>() ??
                      gameObject.AddComponent<RunReplayRuntimeAdapter>();
            adapter.Configure(input, waves);
            replay = new RunReplayPlayer(record, adapter);
            verifier = GetComponent<RunReplayChecksumVerifier>() ??
                       gameObject.AddComponent<RunReplayChecksumVerifier>();
            verifier.Configure(this);
            nextTick = 0;
            LastError = string.Empty;
            error = string.Empty;
            enabled = true;
            return true;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            error = LastError;
            return false;
        }
    }

    public void StopReplay()
    {
        adapter?.Stop();
        if (verifier != null) verifier.enabled = false;
        enabled = false;
    }

    private void FixedUpdate()
    {
        if (replay == null) return;
        try
        {
            ReplayAdvanceResult result = replay.DispatchTo(nextTick++);
            if (result.Status == RunReplayStatus.Diverged)
            {
                LastError = result.Divergence.Summary;
                Debug.LogError("Replay divergence: " + LastError, this);
                StopReplay();
            }
            else if (result.Status == RunReplayStatus.Completed)
            {
                StopReplay();
            }
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Debug.LogError("Replay stopped: " + LastError, this);
            StopReplay();
        }
    }

    internal void VerifyCurrentTick()
    {
        if (replay == null || nextTick <= 0 || !enabled) return;
        try
        {
            ReplayAdvanceResult result = replay.VerifyThrough(nextTick - 1);
            if (result.Status == RunReplayStatus.Diverged)
            {
                LastError = result.Divergence.Summary;
                Debug.LogError("Replay divergence: " + LastError, this);
                StopReplay();
            }
            else if (result.Status == RunReplayStatus.Completed)
            {
                StopReplay();
            }
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Debug.LogError("Replay verification stopped: " + LastError, this);
            StopReplay();
        }
    }

    private void OnDisable()
    {
        adapter?.Stop();
    }
}

[DefaultExecutionOrder(1100)]
public sealed class RunReplayChecksumVerifier : MonoBehaviour
{
    private RunReplayController controller;

    public void Configure(RunReplayController replayController)
    {
        controller = replayController;
        enabled = true;
    }

    private void FixedUpdate()
    {
        controller?.VerifyCurrentTick();
    }
}
