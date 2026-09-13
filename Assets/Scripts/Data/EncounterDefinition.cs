using System;
using System.Collections.Generic;
using FPS.Simulation;
using UnityEngine;

[Serializable]
public sealed class EncounterRosterEntry
{
    [SerializeField] private EnemyArchetypeDefinition archetype;
    [SerializeField, Min(1)] private int count = 1;
    [SerializeField, Range(-180, 180)] private int signedDirectionDegrees;

    public EncounterRosterEntry(
        EnemyArchetypeDefinition configuredArchetype,
        int configuredCount,
        int directionDegrees)
    {
        archetype = configuredArchetype;
        count = Mathf.Max(1, configuredCount);
        signedDirectionDegrees = Mathf.Clamp(directionDegrees, -180, 180);
    }

    public EnemyArchetypeDefinition Archetype => archetype;
    public int Count => Mathf.Max(1, count);
    public int SignedDirectionDegrees => signedDirectionDegrees;
}

[CreateAssetMenu(
    fileName = "EncounterDefinition",
    menuName = "FPS/Encounters/Encounter Definition")]
public sealed class EncounterDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField, Min(1)] private int contentVersion = 1;
    [SerializeField] private EncounterKind kind;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string objectiveText;
    [SerializeField] private EncounterTriggerKind triggerKind;
    [SerializeField, Min(1)] private int triggerWave = 1;
    [SerializeField] private MissionFlowState triggerMissionState =
        MissionFlowState.EliminateTargets;
    [SerializeField] private EncounterObjectiveKind objectiveKind;
    [SerializeField, Min(1)] private int objectiveTarget = 1;
    [SerializeField] private EncounterMainFlowPolicy mainFlowPolicy =
        EncounterMainFlowPolicy.Parallel;
    [SerializeField, Min(0f)] private float introSeconds = 1.5f;
    [SerializeField, Min(0f)] private float timeLimitSeconds = 30f;
    [SerializeField] private List<EncounterRosterEntry> roster = new();
    [SerializeField] private ItemDefinition rewardItem;
    [SerializeField, Min(0)] private int rewardQuantity;

    public string StableId => stableId;
    public int ContentVersion => Mathf.Max(1, contentVersion);
    public EncounterKind Kind => kind;
    public string DisplayName => displayName;
    public string ObjectiveText => objectiveText;
    public EncounterTriggerKind TriggerKind => triggerKind;
    public int TriggerWave => Mathf.Max(1, triggerWave);
    public MissionFlowState TriggerMissionState => triggerMissionState;
    public EncounterObjectiveKind ObjectiveKind => objectiveKind;
    public int ObjectiveTarget => Mathf.Max(1, objectiveTarget);
    public EncounterMainFlowPolicy MainFlowPolicy => mainFlowPolicy;
    public float IntroSeconds => Mathf.Max(0f, introSeconds);
    public float TimeLimitSeconds => Mathf.Max(0f, timeLimitSeconds);
    public IReadOnlyList<EncounterRosterEntry> Roster => roster;
    public ItemDefinition RewardItem => rewardItem;
    public int RewardQuantity => Mathf.Max(0, rewardQuantity);

    public void Configure(
        string id,
        int version,
        EncounterKind encounterKind,
        string configuredName,
        string configuredObjectiveText,
        EncounterTriggerKind configuredTriggerKind,
        int configuredTriggerWave,
        MissionFlowState configuredTriggerMission,
        EncounterObjectiveKind configuredObjectiveKind,
        int configuredObjectiveTarget,
        EncounterMainFlowPolicy configuredPolicy,
        float configuredIntroSeconds,
        float configuredTimeLimitSeconds,
        IEnumerable<EncounterRosterEntry> configuredRoster,
        ItemDefinition configuredReward,
        int configuredRewardQuantity)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Encounter ID is required.", nameof(id));
        stableId = id.Trim();
        contentVersion = Mathf.Max(1, version);
        kind = encounterKind;
        displayName = configuredName?.Trim() ?? string.Empty;
        objectiveText = configuredObjectiveText?.Trim() ?? string.Empty;
        triggerKind = configuredTriggerKind;
        triggerWave = Mathf.Max(1, configuredTriggerWave);
        triggerMissionState = configuredTriggerMission;
        objectiveKind = configuredObjectiveKind;
        objectiveTarget = Mathf.Max(1, configuredObjectiveTarget);
        mainFlowPolicy = configuredPolicy;
        introSeconds = Mathf.Max(0f, configuredIntroSeconds);
        timeLimitSeconds = Mathf.Max(0f, configuredTimeLimitSeconds);
        roster = configuredRoster != null
            ? new List<EncounterRosterEntry>(configuredRoster)
            : new List<EncounterRosterEntry>();
        rewardItem = configuredReward;
        rewardQuantity = configuredReward != null
            ? Mathf.Max(0, configuredRewardQuantity)
            : 0;
    }

    public EncounterDefinitionSpec ToSpec(int fixedTickRate)
    {
        EncounterTrigger trigger = triggerKind == EncounterTriggerKind.WaveReached
            ? EncounterTrigger.Wave(TriggerWave)
            : EncounterTrigger.Mission(triggerMissionState);
        EncounterObjective objective = objectiveKind switch
        {
            EncounterObjectiveKind.EliminateEnemies =>
                EncounterObjective.Eliminate(ObjectiveTarget),
            EncounterObjectiveKind.EliminateElite =>
                EncounterObjective.EliminateElite(),
            EncounterObjectiveKind.HoldArea =>
                EncounterObjective.HoldTicks(ObjectiveTarget),
            EncounterObjectiveKind.ReachExtraction =>
                EncounterObjective.ReachExtraction(),
            _ => EncounterObjective.Eliminate(1)
        };
        return new EncounterDefinitionSpec(
            stableId,
            ContentVersion,
            kind,
            displayName,
            objectiveText,
            trigger,
            objective,
            mainFlowPolicy,
            Mathf.RoundToInt(IntroSeconds * Mathf.Max(1, fixedTickRate)),
            Mathf.RoundToInt(TimeLimitSeconds * Mathf.Max(1, fixedTickRate)));
    }

    public bool TryValidate(
        ISet<EnemyArchetypeDefinition> catalogArchetypes,
        ISet<ItemDefinition> catalogItems,
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(stableId) || contentVersion < 1 ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(objectiveText))
        {
            error = "Encounter requires ID, version, name and objective text.";
            return false;
        }
        if (roster == null || roster.Count == 0)
        {
            error = $"Encounter '{stableId}' roster is missing or empty.";
            return false;
        }
        for (int index = 0; index < roster.Count; index++)
        {
            EncounterRosterEntry entry = roster[index];
            if (entry == null || entry.Archetype == null ||
                catalogArchetypes == null ||
                !catalogArchetypes.Contains(entry.Archetype))
            {
                error = $"Encounter '{stableId}' references an enemy outside the catalog.";
                return false;
            }
        }
        if (rewardQuantity > 0 &&
            (rewardItem == null || catalogItems == null ||
             !catalogItems.Contains(rewardItem)))
        {
            error = $"Encounter '{stableId}' references a reward outside the catalog.";
            return false;
        }
        return true;
    }
}
