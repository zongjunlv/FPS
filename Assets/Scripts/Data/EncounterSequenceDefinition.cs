using System;
using System.Collections.Generic;
using FPS.Simulation;
using UnityEngine;

[CreateAssetMenu(
    fileName = "EncounterSequence",
    menuName = "FPS/Encounters/Encounter Sequence")]
public sealed class EncounterSequenceDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField, Min(1)] private int contentVersion = 1;
    [SerializeField] private List<EncounterDefinition> encounters = new();

    public string StableId => stableId;
    public int ContentVersion => Mathf.Max(1, contentVersion);
    public IReadOnlyList<EncounterDefinition> Encounters => encounters;
    public int Count => encounters?.Count ?? 0;

    public void Configure(
        string id,
        int version,
        IEnumerable<EncounterDefinition> definitions)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Encounter sequence ID is required.", nameof(id));
        stableId = id.Trim();
        contentVersion = Mathf.Max(1, version);
        encounters = definitions != null
            ? new List<EncounterDefinition>(definitions)
            : new List<EncounterDefinition>();
    }

    public EncounterSequence CreateRuntime(int fixedTickRate)
    {
        var specs = new EncounterDefinitionSpec[Count];
        for (int index = 0; index < specs.Length; index++)
            specs[index] = encounters[index].ToSpec(fixedTickRate);
        return new EncounterSequence(specs);
    }

    public bool TryValidate(
        ISet<EnemyArchetypeDefinition> catalogArchetypes,
        ISet<ItemDefinition> catalogItems,
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(stableId) || contentVersion < 1 ||
            encounters == null || encounters.Count != 4)
        {
            error = "CityNew encounter sequence requires a stable ID, version and four events.";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var kinds = new HashSet<EncounterKind>();
        for (int index = 0; index < encounters.Count; index++)
        {
            EncounterDefinition definition = encounters[index];
            if (definition == null || !ids.Add(definition.StableId) ||
                !kinds.Add(definition.Kind) ||
                !definition.TryValidate(
                    catalogArchetypes,
                    catalogItems,
                    out error))
                return false;
        }
        error = string.Empty;
        return true;
    }
}
