using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FPS.Simulation.Offline
{
    /// <summary>
    /// Headless adapter over the authoritative run, director and encounter
    /// domains. Every input is explicit and no wall clock or scene state is read.
    /// </summary>
    public static class OfflineBalanceSimulator
    {
        public static OfflineRunResult Run(
            OfflineBalanceScenario scenario,
            long seed)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));

            var state = new SimulationState(scenario, seed);
            state.Start();
            state.RunToOutcome();
            return state.BuildResult();
        }

        private sealed class SimulationState
        {
            private readonly OfflineBalanceScenario scenario;
            private readonly RunSimulationKernel kernel;
            private readonly DynamicCombatDirector director;
            private readonly EncounterSequence encounters;
            private readonly CombatRuleDecisionKernel combatRuleKernel;
            private readonly Dictionary<string, OfflineEnemyArchetype>
                archetypes;
            private readonly WaveState[] waves;
            private readonly List<Combatant> active = new List<Combatant>();
            private readonly List<OfflineUpgradeChoice> upgradeChoices =
                new List<OfflineUpgradeChoice>();
            private readonly List<OfflineDirectorEvent> directorEvents =
                new List<OfflineDirectorEvent>();
            private readonly List<OfflineEncounterEvent> encounterEvents =
                new List<OfflineEncounterEvent>();
            private readonly List<OfflineCombatRuleEvent> combatRuleEvents =
                new List<OfflineCombatRuleEvent>();
            private readonly List<int> enemyTtkTicks = new List<int>();
            private readonly Dictionary<string, int> roleCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> encounterCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> combatRuleCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly OfflineRandom combatRandom;
            private readonly OfflineRandom upgradeRandom;
            private readonly OfflineRandom enemyAttackOpportunityRandom;
            private readonly CombatDirectorRoleCandidate[] directorCandidates;

            private int nextSpawnId = 1;
            private long nextCombatRuleEventId = 1;
            private long nextShotTick;
            private float playerHealth;
            private float playerArmor;
            private float maximumHealth;
            private float maximumArmor;
            private float damagePerShot;
            private int ammunition;
            private int ammoConsumed;
            private int ammoSupplied;
            private float healthLost;
            private float armorLost;
            private float recentDamage;
            private int totalKills;
            private long totalTtkTicks;
            private OfflineRunOutcome outcome = OfflineRunOutcome.Defeat;
            private OfflineFailureReason failureReason =
                OfflineFailureReason.None;
            private string failureDetail = string.Empty;

            public SimulationState(
                OfflineBalanceScenario scenario,
                long seed)
            {
                this.scenario = scenario;
                kernel = new RunSimulationKernel(
                    scenario.CreateRunConfiguration(seed));
                director = scenario.DirectorConfiguration == null
                    ? null
                    : new DynamicCombatDirector(
                        seed,
                        scenario.DirectorConfiguration);
                encounters = scenario.Encounters.Count == 0
                    ? null
                    : new EncounterSequence(scenario.Encounters);
                combatRuleKernel = new CombatRuleDecisionKernel(
                    unchecked((int)seed),
                    FlattenCombatRules(scenario.CombatBuilds));
                combatRandom = new OfflineRandom(seed, "combat");
                upgradeRandom = new OfflineRandom(seed, "upgrade");
                enemyAttackOpportunityRandom = new OfflineRandom(
                    seed,
                    "enemy-attack-opportunity");

                archetypes = new Dictionary<string, OfflineEnemyArchetype>(
                    StringComparer.Ordinal);
                directorCandidates = new CombatDirectorRoleCandidate[
                    scenario.Enemies.Count];
                for (int index = 0; index < scenario.Enemies.Count; index++)
                {
                    OfflineEnemyArchetype archetype = scenario.Enemies[index];
                    archetypes.Add(archetype.StableId, archetype);
                    directorCandidates[index] =
                        new CombatDirectorRoleCandidate(
                            archetype.StableId,
                            archetype.RoleTag,
                            archetype.ThreatCost);
                }

                waves = new WaveState[scenario.Waves.Count];
                for (int index = 0; index < waves.Length; index++)
                    waves[index] = new WaveState(
                        index + 1,
                        scenario.Waves[index],
                        archetypes);

                maximumHealth = scenario.Player.Health;
                maximumArmor = scenario.Player.Armor;
                playerHealth = maximumHealth;
                playerArmor = maximumArmor;
                damagePerShot = scenario.Player.DamagePerShot;
                ammunition = scenario.Player.StartingAmmo;
            }

            public void Start()
            {
                Submit(SimulationCommandType.StartRun);
                Submit(
                    SimulationCommandType.PlayerVitalsChanged,
                    primary: playerHealth,
                    secondary: playerArmor);

                if (damagePerShot <= 0f ||
                    scenario.Player.HitChance <= 0f)
                {
                    Fail(
                        OfflineFailureReason.UnsolvableRoster,
                        "Player build cannot damage any roster entry.");
                    return;
                }

                for (int waveIndex = 0;
                     waveIndex < scenario.Waves.Count;
                     waveIndex++)
                {
                    IReadOnlyList<OfflineWaveEntry> roster =
                        scenario.Waves[waveIndex].Roster;
                    for (int entryIndex = 0;
                         entryIndex < roster.Count;
                         entryIndex++)
                    {
                        if (!archetypes.ContainsKey(
                                roster[entryIndex].EnemyTypeId))
                        {
                            Fail(
                                OfflineFailureReason.UnsolvableRoster,
                                "Wave " + (waveIndex + 1) +
                                " references missing enemy '" +
                                roster[entryIndex].EnemyTypeId + "'.");
                            return;
                        }
                    }
                }
            }

            public void RunToOutcome()
            {
                while (failureReason == OfflineFailureReason.None &&
                       !kernel.Wave.IsCompleted &&
                       kernel.Tick < scenario.MaxTicks)
                {
                    StepCombatTick();
                }

                if (failureReason != OfflineFailureReason.None)
                    return;

                if (!kernel.Wave.IsCompleted)
                {
                    Fail(
                        OfflineFailureReason.TickLimitExceeded,
                        "Combat exceeded the scenario tick limit.");
                    return;
                }

                ResolveMissionAndEncounters();
                if (failureReason != OfflineFailureReason.None)
                    return;

                if (kernel.Mission.State == global::MissionFlowState.Victory)
                {
                    outcome = OfflineRunOutcome.Victory;
                    failureReason = OfflineFailureReason.None;
                    failureDetail = string.Empty;
                    return;
                }

                Fail(
                    OfflineFailureReason.UnsolvableRoster,
                    "Authoritative mission could not reach extraction.");
            }

            public OfflineRunResult BuildResult()
            {
                var waveResults = new OfflineWaveResult[waves.Length];
                var waveDistribution = new OfflineNamedCount[waves.Length];
                for (int index = 0; index < waves.Length; index++)
                {
                    waveResults[index] = waves[index].BuildResult(kernel.Tick);
                    waveDistribution[index] = new OfflineNamedCount(
                        "wave." + (index + 1),
                        waves[index].SpawnedCount);
                }

                OfflineNamedCount[] roles = ToCounts(roleCounts);
                OfflineNamedCount[] encounterDistribution =
                    ToCounts(encounterCounts);
                OfflineNamedCount[] combatRuleDistribution =
                    ToCounts(combatRuleCounts);
                SimulationCommand[] commands = CopyCommands(
                    kernel.AcceptedCommands);

                var draft = new OfflineRunResult(
                    scenario.StableId,
                    scenario.ContentVersion,
                    scenario.RulesVersion,
                    scenario.WorldModel.Version,
                    scenario.WorldModel.EnemyAttackOpportunityBasisPoints,
                    kernel.Configuration.Seed,
                    outcome,
                    failureReason,
                    failureDetail,
                    kernel.Tick,
                    scenario.FixedTickRate,
                    totalKills == 0
                        ? 0f
                        : (float)totalTtkTicks / totalKills,
                    enemyTtkTicks,
                    healthLost,
                    armorLost,
                    ammoConsumed,
                    ammoSupplied,
                    waveResults,
                    waveDistribution,
                    roles,
                    encounterDistribution,
                    combatRuleDistribution,
                    upgradeChoices,
                    directorEvents,
                    encounterEvents,
                    combatRuleEvents,
                    commands,
                    string.Empty);
                string digest = OfflineResultDigest.Compute(draft);
                return new OfflineRunResult(
                    draft.ScenarioId,
                    draft.ContentVersion,
                    draft.RulesVersion,
                    draft.WorldModelVersion,
                    draft.EnemyAttackOpportunityBasisPoints,
                    draft.Seed,
                    draft.Outcome,
                    draft.FailureReason,
                    draft.FailureDetail,
                    draft.DurationTicks,
                    draft.FixedTickRate,
                    draft.AverageTtkTicks,
                    draft.EnemyTtkTicks,
                    draft.HealthLost,
                    draft.ArmorLost,
                    draft.AmmoConsumed,
                    draft.AmmoSupplied,
                    draft.Waves,
                    draft.WaveDistribution,
                    draft.RoleDistribution,
                    draft.EncounterDistribution,
                    draft.CombatRuleTriggerDistribution,
                    draft.UpgradeChoices,
                    draft.DirectorEvents,
                    draft.EncounterEvents,
                    draft.CombatRuleEvents,
                    draft.Commands,
                    digest);
            }

            private void StepCombatTick()
            {
                int waveNumber = kernel.Wave.CurrentWave;
                if (waveNumber < 1 || waveNumber > waves.Length)
                {
                    Fail(
                        OfflineFailureReason.UnsolvableRoster,
                        "Authoritative wave index is outside scenario content.");
                    return;
                }

                WaveState wave = waves[waveNumber - 1];
                AdvanceDirector(wave);

                if (kernel.Wave.Phase == global::WaveRunPhase.Spawning)
                    SpawnAvailable(wave);

                EncounterTransition initialEncounter = AdvanceEncounter(
                    enemyKillDelta: 0,
                    eliteKillDelta: 0,
                    reachedExtraction: false,
                    playerDefeated: false);

                if (kernel.Wave.Phase != global::WaveRunPhase.Intermission)
                {
                    DamagePlayer();
                    if (playerHealth <= 0f)
                    {
                        Fail(
                            OfflineFailureReason.PlayerDefeated,
                            "Enemy attacks depleted player health.");
                        return;
                    }

                    if (active.Count > 0 && kernel.Tick >= nextShotTick)
                        FireAtCurrentTarget(initialEncounter);
                }

                if (failureReason != OfflineFailureReason.None)
                    return;

                kernel.AdvanceTick();
            }

            private void AdvanceDirector(WaveState wave)
            {
                if (director == null)
                    return;

                int remainingSlots = Math.Max(
                    0,
                    wave.Spec.TotalEnemyCount - wave.SpawnedCount);
                int aliveCapacity = Math.Max(
                    0,
                    wave.Spec.MaximumAliveCount - active.Count);
                int poolCapacity = remainingSlots;
                float healthRatio = maximumHealth <= 0f
                    ? 0f
                    : playerHealth / maximumHealth;
                float armorRatio = maximumArmor <= 0f
                    ? 1f
                    : playerArmor / maximumArmor;
                int ammoReference = Math.Max(
                    1,
                    scenario.Player.StartingAmmo + ammoSupplied);
                float ammoRatio = (float)ammunition / ammoReference;
                float elapsedSeconds = Math.Max(
                    1f,
                    (float)(kernel.Tick + 1) / scenario.FixedTickRate);
                float clearRate = Math.Min(1f, totalKills / elapsedSeconds);
                float threat = 0f;
                for (int index = 0; index < active.Count; index++)
                    threat += active[index].Archetype.ThreatCost;

                var pressure = new CombatPressureSnapshot(
                    healthRatio,
                    armorRatio,
                    ammoRatio,
                    maximumHealth <= 0f
                        ? 0f
                        : recentDamage / maximumHealth,
                    clearRate,
                    Math.Min(1f, threat / 10f),
                    0.5f,
                    0,
                    0);
                var resources = new CombatDirectorResources(
                    remainingSlots,
                    aliveCapacity,
                    poolCapacity,
                    true,
                    directorCandidates);
                CombatDirectorOutput output = director.Advance(
                    new CombatDirectorInput(
                        kernel.Tick,
                        pressure,
                        resources));
                recentDamage = 0f;
                RecordDirector(output);

                if (output.Signal != CombatDirectorSignal.SpawnRequested)
                    return;

                OfflineEnemyArchetype selected = ResolveDirectorArchetype(
                    output.CurrentEvent);
                bool success = selected != null &&
                               kernel.Wave.CanSpawn &&
                               active.Count < wave.Spec.MaximumAliveCount &&
                               wave.TryConsumeRosterSlot();
                if (success)
                    Spawn(wave, selected);
                director.ReportSpawnOutcome(kernel.Tick, success);
            }

            private void SpawnAvailable(WaveState wave)
            {
                while (kernel.Wave.CanSpawn &&
                       active.Count < wave.Spec.MaximumAliveCount &&
                       wave.TryTakeNext(out OfflineEnemyArchetype archetype))
                {
                    Spawn(wave, archetype);
                }
            }

            private void Spawn(
                WaveState wave,
                OfflineEnemyArchetype archetype)
            {
                int spawnId = nextSpawnId++;
                Submit(SimulationCommandType.EnemySpawned, spawnId);
                var combatant = new Combatant(
                    spawnId,
                    wave.WaveNumber,
                    archetype,
                    kernel.Tick);
                active.Add(combatant);
                wave.RecordSpawn(archetype.RoleTag, kernel.Tick);
                AddCount(roleCounts, archetype.RoleTag);
            }

            private void DamagePlayer()
            {
                float damage = 0f;
                for (int index = 0; index < active.Count; index++)
                {
                    Combatant enemy = active[index];
                    if (kernel.Tick < enemy.NextAttackTick)
                        continue;
                    enemy.NextAttackTick = checked(
                        enemy.NextAttackTick +
                        enemy.Archetype.AttackIntervalTicks);
                    if (enemyAttackOpportunityRandom.RollBasisPoints(
                            scenario.WorldModel
                                .EnemyAttackOpportunityBasisPoints))
                    {
                        damage += enemy.Archetype.DamagePerHit;
                    }
                }

                if (damage <= 0f)
                    return;

                CombatDamageResolution resolution = CombatDamageRules.Apply(
                    playerHealth,
                    playerArmor,
                    damage);
                playerHealth = resolution.RemainingHealth;
                playerArmor = resolution.RemainingArmor;
                armorLost += resolution.ArmorDamage;
                healthLost += resolution.HealthDamage;
                recentDamage += damage;
                Submit(
                    SimulationCommandType.PlayerVitalsChanged,
                    primary: playerHealth,
                    secondary: playerArmor);
            }

            private void FireAtCurrentTarget(
                EncounterTransition initialEncounter)
            {
                if (ammunition <= 0)
                {
                    Fail(
                        OfflineFailureReason.ResourceDepleted,
                        "Ammunition was exhausted before the roster settled.");
                    return;
                }

                ammunition--;
                ammoConsumed++;
                nextShotTick = checked(
                    kernel.Tick + scenario.Player.ShotIntervalTicks);
                if (!combatRandom.Roll(scenario.Player.HitChance))
                    return;

                Combatant target = active[0];
                target.Health -= damagePerShot;
                float healthNormalized = Math.Max(
                    0f,
                    target.Health / target.Archetype.Health);
                ProcessCombatRuleEvent(
                    CombatRuleDecisionTrigger.Hit,
                    target,
                    healthNormalized);
                if (target.Health > 0f)
                    return;

                ProcessCombatRuleEvent(
                    CombatRuleDecisionTrigger.Kill,
                    target,
                    0f);

                active.RemoveAt(0);
                Submit(SimulationCommandType.EnemySettled, target.SpawnId);
                int ttk = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(1L, kernel.Tick - target.SpawnTick + 1L));
                totalTtkTicks += ttk;
                enemyTtkTicks.Add(ttk);
                totalKills++;
                WaveState wave = waves[target.WaveNumber - 1];
                wave.RecordDefeat(ttk, kernel.Tick);

                if (target.Archetype.AmmoDrop > 0)
                {
                    ammunition = checked(
                        ammunition + target.Archetype.AmmoDrop);
                    ammoSupplied = checked(
                        ammoSupplied + target.Archetype.AmmoDrop);
                }

                if (initialEncounter.Signal == EncounterSignal.None ||
                    initialEncounter.Signal == EncounterSignal.Started ||
                    initialEncounter.Signal == EncounterSignal.Activated)
                {
                    AdvanceEncounter(
                        1,
                        target.Archetype.IsElite ? 1 : 0,
                        false,
                        false);
                }

                if (wave.IsComplete && wave.WaveNumber < waves.Length)
                    SelectUpgrade(wave.WaveNumber);
            }

            private void SelectUpgrade(int afterWave)
            {
                if (scenario.Upgrades.Count == 0)
                    return;
                OfflineUpgradeSpec selected = scenario.Upgrades[
                    upgradeRandom.NextInt(scenario.Upgrades.Count)];
                damagePerShot *= selected.DamageMultiplier;
                maximumHealth += selected.HealthBonus;
                playerHealth += selected.HealthBonus;
                maximumArmor += selected.ArmorBonus;
                playerArmor += selected.ArmorBonus;
                ammunition = checked(ammunition + selected.AmmoBonus);
                ammoSupplied = checked(ammoSupplied + selected.AmmoBonus);
                upgradeChoices.Add(new OfflineUpgradeChoice(
                    afterWave,
                    selected.StableId));
                if (selected.HealthBonus > 0f || selected.ArmorBonus > 0f)
                {
                    Submit(
                        SimulationCommandType.PlayerVitalsChanged,
                        primary: playerHealth,
                        secondary: playerArmor);
                }
            }

            private void ResolveMissionAndEncounters()
            {
                AdvanceEncounter(0, 0, false, false);
                if (kernel.Mission.State ==
                    global::MissionFlowState.ActivateTerminal)
                {
                    Submit(SimulationCommandType.TerminalCompleted);
                }

                while (encounters != null &&
                       !encounters.IsComplete &&
                       kernel.Tick < scenario.MaxTicks)
                {
                    EncounterDefinitionSpec current =
                        encounters.CurrentDefinition;
                    if (encounters.HasActiveEncounter &&
                        current != null &&
                        (current.Objective.Kind ==
                            EncounterObjectiveKind.EliminateEnemies ||
                         current.Objective.Kind ==
                            EncounterObjectiveKind.EliminateElite) &&
                        current.TimeLimitTicks == 0)
                    {
                        RecordEncounter(encounters.CancelActive(false));
                    }
                    else
                    {
                        AdvanceEncounter(
                            0,
                            0,
                            kernel.Mission.State ==
                                global::MissionFlowState.ExtractionAvailable,
                            false,
                            insideObjectiveArea: true);
                    }
                    kernel.AdvanceTick();
                }

                if (kernel.Tick >= scenario.MaxTicks)
                {
                    Fail(
                        OfflineFailureReason.TickLimitExceeded,
                        "Encounter resolution exceeded the scenario tick limit.");
                    return;
                }

                if (kernel.Mission.State ==
                    global::MissionFlowState.ExtractionAvailable)
                {
                    Submit(SimulationCommandType.EnterExtraction);
                }
            }

            private EncounterTransition AdvanceEncounter(
                int enemyKillDelta,
                int eliteKillDelta,
                bool reachedExtraction,
                bool playerDefeated,
                bool insideObjectiveArea = true)
            {
                if (encounters == null)
                    return EncounterTransition.None;
                EncounterTransition transition = encounters.Advance(
                    new EncounterFacts(
                        kernel.Tick,
                        kernel.Wave.CurrentWave,
                        kernel.Wave.Phase,
                        kernel.Mission.State,
                        true,
                        enemyKillDelta,
                        eliteKillDelta,
                        insideObjectiveArea,
                        reachedExtraction,
                        playerDefeated));
                RecordEncounter(transition);
                return transition;
            }

            private void RecordDirector(CombatDirectorOutput output)
            {
                if (output == null ||
                    output.Signal == CombatDirectorSignal.None)
                    return;
                CombatDirectorEventState current = output.CurrentEvent;
                directorEvents.Add(new OfflineDirectorEvent(
                    kernel.Tick,
                    output.Signal,
                    current?.EventId ?? 0L,
                    current?.EnemyTypeId ?? string.Empty,
                    current?.RoleTag ?? string.Empty,
                    current?.RequestedCount ?? 0));
            }

            private void RecordEncounter(EncounterTransition transition)
            {
                if (transition.Signal == EncounterSignal.None)
                    return;
                encounterEvents.Add(new OfflineEncounterEvent(
                    kernel.Tick,
                    transition.EncounterId,
                    transition.Signal,
                    transition.Phase,
                    transition.Progress,
                    transition.Target));
                if (transition.Signal == EncounterSignal.Started)
                    AddCount(encounterCounts, transition.EncounterId);
            }

            private void ProcessCombatRuleEvent(
                CombatRuleDecisionTrigger trigger,
                Combatant target,
                float targetHealthNormalized)
            {
                long eventId = nextCombatRuleEventId++;
                IReadOnlyList<CombatRuleDecision> decisions =
                    combatRuleKernel.Process(
                        new CombatRuleDecisionContext(
                            eventId,
                            kernel.Tick,
                            trigger,
                            scenario.Player.SourceTags,
                            target.Archetype.TargetTags,
                            targetHealthNormalized));
                for (int index = 0; index < decisions.Count; index++)
                {
                    string ruleId = decisions[index].RuleId;
                    combatRuleEvents.Add(new OfflineCombatRuleEvent(
                        eventId,
                        kernel.Tick,
                        trigger,
                        ruleId,
                        "player",
                        "enemy:" + target.SpawnId));
                    AddCount(combatRuleCounts, ruleId);
                }
            }

            private OfflineEnemyArchetype ResolveDirectorArchetype(
                CombatDirectorEventState current)
            {
                if (current == null)
                    return null;
                if (archetypes.TryGetValue(
                        current.EnemyTypeId,
                        out OfflineEnemyArchetype exact))
                    return exact;
                for (int index = 0; index < scenario.Enemies.Count; index++)
                {
                    OfflineEnemyArchetype candidate = scenario.Enemies[index];
                    if (string.Equals(
                            candidate.RoleTag,
                            current.RoleTag,
                            StringComparison.Ordinal))
                        return candidate;
                }
                return null;
            }

            private void Fail(
                OfflineFailureReason reason,
                string detail)
            {
                if (failureReason != OfflineFailureReason.None)
                    return;
                failureReason = reason;
                failureDetail = detail ?? string.Empty;
                outcome = OfflineRunOutcome.Defeat;
                if (!kernel.Mission.IsOutcome)
                    Submit(SimulationCommandType.PlayerDefeated);
                AdvanceEncounter(0, 0, false, true);
            }

            private void Submit(
                SimulationCommandType type,
                int entityId = 0,
                float primary = 0f,
                float secondary = 0f)
            {
                var command = SimulationCommand.Create(
                    kernel.Tick,
                    type,
                    entityId,
                    primary,
                    secondary);
                if (!kernel.Submit(command, out string error))
                {
                    throw new InvalidOperationException(
                        "Offline adapter submitted an invalid authoritative " +
                        "command: " + error);
                }
            }

            private static void AddCount(
                Dictionary<string, int> counts,
                string id)
            {
                id = id ?? string.Empty;
                counts.TryGetValue(id, out int count);
                counts[id] = count + 1;
            }

            private static OfflineNamedCount[] ToCounts(
                Dictionary<string, int> values)
            {
                string[] ids = values.Keys.ToArray();
                Array.Sort(ids, StringComparer.Ordinal);
                var result = new OfflineNamedCount[ids.Length];
                for (int index = 0; index < ids.Length; index++)
                    result[index] = new OfflineNamedCount(
                        ids[index],
                        values[ids[index]]);
                return result;
            }

            private static SimulationCommand[] CopyCommands(
                IReadOnlyList<SimulationCommand> source)
            {
                var result = new SimulationCommand[source.Count];
                for (int index = 0; index < source.Count; index++)
                    result[index] = source[index];
                return result;
            }

            private static CombatRuleDecisionSpec[] FlattenCombatRules(
                IReadOnlyList<OfflineCombatBuildSpec> builds)
            {
                var rules = new List<CombatRuleDecisionSpec>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int buildIndex = 0;
                     buildIndex < builds.Count;
                     buildIndex++)
                {
                    IReadOnlyList<CombatRuleDecisionSpec> buildRules =
                        builds[buildIndex].Rules;
                    for (int ruleIndex = 0;
                         ruleIndex < buildRules.Count;
                         ruleIndex++)
                    {
                        CombatRuleDecisionSpec rule = buildRules[ruleIndex];
                        if (ids.Add(rule.StableId)) rules.Add(rule);
                    }
                }
                return rules.ToArray();
            }
        }

        private sealed class Combatant
        {
            public Combatant(
                int spawnId,
                int waveNumber,
                OfflineEnemyArchetype archetype,
                long spawnTick)
            {
                SpawnId = spawnId;
                WaveNumber = waveNumber;
                Archetype = archetype;
                SpawnTick = spawnTick;
                Health = archetype.Health;
                NextAttackTick = checked(
                    spawnTick + archetype.FirstAttackDelayTicks);
            }

            public int SpawnId { get; }
            public int WaveNumber { get; }
            public OfflineEnemyArchetype Archetype { get; }
            public long SpawnTick { get; }
            public float Health { get; set; }
            public long NextAttackTick { get; set; }
        }

        private sealed class WaveState
        {
            private readonly OfflineEnemyArchetype[] roster;
            private readonly Dictionary<string, int> roleCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private int nextRosterIndex;
            private long startedTick = -1;
            private long completedTick = -1;
            private long totalTtkTicks;

            public WaveState(
                int waveNumber,
                OfflineWaveSpec spec,
                Dictionary<string, OfflineEnemyArchetype> archetypes)
            {
                WaveNumber = waveNumber;
                Spec = spec;
                roster = new OfflineEnemyArchetype[spec.TotalEnemyCount];
                int write = 0;
                for (int entryIndex = 0;
                     entryIndex < spec.Roster.Count;
                     entryIndex++)
                {
                    OfflineWaveEntry entry = spec.Roster[entryIndex];
                    archetypes.TryGetValue(
                        entry.EnemyTypeId,
                        out OfflineEnemyArchetype archetype);
                    for (int count = 0; count < entry.Count; count++)
                        roster[write++] = archetype;
                }
            }

            public int WaveNumber { get; }
            public OfflineWaveSpec Spec { get; }
            public int SpawnedCount { get; private set; }
            public int DefeatedCount { get; private set; }
            public bool IsComplete =>
                DefeatedCount >= Spec.TotalEnemyCount;

            public bool TryTakeNext(out OfflineEnemyArchetype archetype)
            {
                archetype = null;
                if (nextRosterIndex >= roster.Length)
                    return false;
                archetype = roster[nextRosterIndex++];
                return archetype != null;
            }

            public bool TryConsumeRosterSlot()
            {
                if (nextRosterIndex >= roster.Length)
                    return false;
                nextRosterIndex++;
                return true;
            }

            public void RecordSpawn(string roleTag, long tick)
            {
                if (startedTick < 0)
                    startedTick = tick;
                SpawnedCount++;
                roleCounts.TryGetValue(roleTag, out int count);
                roleCounts[roleTag] = count + 1;
            }

            public void RecordDefeat(int ttkTicks, long tick)
            {
                DefeatedCount++;
                totalTtkTicks += Math.Max(1, ttkTicks);
                if (IsComplete)
                    completedTick = tick;
            }

            public OfflineWaveResult BuildResult(long finalTick)
            {
                string[] roles = roleCounts.Keys.ToArray();
                Array.Sort(roles, StringComparer.Ordinal);
                var distribution = new OfflineNamedCount[roles.Length];
                for (int index = 0; index < roles.Length; index++)
                    distribution[index] = new OfflineNamedCount(
                        roles[index],
                        roleCounts[roles[index]]);
                long end = completedTick >= 0 ? completedTick : finalTick;
                long duration = startedTick < 0
                    ? 0L
                    : Math.Max(1L, end - startedTick + 1L);
                return new OfflineWaveResult(
                    WaveNumber,
                    SpawnedCount,
                    DefeatedCount,
                    duration,
                    DefeatedCount == 0
                        ? 0f
                        : (float)totalTtkTicks / DefeatedCount,
                    distribution);
            }
        }
    }

    public static class OfflineBalanceBatchRunner
    {
        public static OfflineBatchResult Run(
            OfflineBalanceScenario scenario,
            IEnumerable<long> seeds,
            int maxDegreeOfParallelism = 1)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (seeds == null)
                throw new ArgumentNullException(nameof(seeds));
            if (maxDegreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(maxDegreeOfParallelism));

            long[] seedSnapshot = seeds.ToArray();
            var results = new OfflineRunResult[seedSnapshot.Length];
            if (maxDegreeOfParallelism == 1)
            {
                for (int index = 0; index < seedSnapshot.Length; index++)
                    results[index] = OfflineBalanceSimulator.Run(
                        scenario,
                        seedSnapshot[index]);
            }
            else
            {
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism
                };
                Parallel.For(
                    0,
                    seedSnapshot.Length,
                    options,
                    index => results[index] = OfflineBalanceSimulator.Run(
                        scenario,
                        seedSnapshot[index]));
            }

            Array.Sort(
                results,
                (left, right) =>
                {
                    int seedOrder = left.Seed.CompareTo(right.Seed);
                    return seedOrder != 0
                        ? seedOrder
                        : string.CompareOrdinal(left.Digest, right.Digest);
                });
            return new OfflineBatchResult(
                scenario.StableId,
                scenario.ContentVersion,
                scenario.RulesVersion,
                results);
        }
    }

    internal sealed class OfflineRandom
    {
        private ulong state;

        public OfflineRandom(long seed, string streamName)
        {
            state = Derive(seed, streamName ?? string.Empty);
        }

        public bool Roll(float chance)
        {
            if (chance <= 0f) return false;
            if (chance >= 1f) return true;
            uint threshold = (uint)(chance * uint.MaxValue);
            return NextUInt32() <= threshold;
        }

        public bool RollBasisPoints(int basisPoints)
        {
            if (basisPoints <= 0) return false;
            if (basisPoints >= 10000) return true;
            return NextUInt32() % 10000u < (uint)basisPoints;
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMaximum));
            uint bound = (uint)exclusiveMaximum;
            uint threshold = unchecked((uint)(0U - bound)) % bound;
            uint value;
            do value = NextUInt32(); while (value < threshold);
            return (int)(value % bound);
        }

        private uint NextUInt32()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (uint)(value >> 32);
        }

        private static ulong Derive(long seed, string streamName)
        {
            ulong hash = 14695981039346656037UL;
            ulong bits = unchecked((ulong)seed);
            for (int index = 0; index < 8; index++)
            {
                hash ^= (byte)(bits >> (index * 8));
                hash *= 1099511628211UL;
            }
            for (int index = 0; index < streamName.Length; index++)
            {
                char character = streamName[index];
                hash ^= (byte)character;
                hash *= 1099511628211UL;
                hash ^= (byte)(character >> 8);
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
