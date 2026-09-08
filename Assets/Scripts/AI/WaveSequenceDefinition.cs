using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WaveStageDefinition
{
    [SerializeField] private WaveDefinition wave;
    [SerializeField, Min(0f)] private float intermissionAfterSeconds = 4f;

    public WaveStageDefinition(
        WaveDefinition waveDefinition,
        float intermissionSeconds)
    {
        wave = waveDefinition != null
            ? waveDefinition
            : throw new ArgumentNullException(nameof(waveDefinition));
        intermissionAfterSeconds = Mathf.Max(0f, intermissionSeconds);
    }

    public WaveDefinition Wave => wave;
    public float IntermissionAfterSeconds =>
        Mathf.Max(0f, intermissionAfterSeconds);
}

[CreateAssetMenu(
    fileName = "WaveSequenceDefinition",
    menuName = "FPS/Waves/Wave Sequence Definition")]
public sealed class WaveSequenceDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private List<WaveStageDefinition> stages = new();

    public string StableId => stableId;
    public int WaveCount => stages != null ? stages.Count : 0;
    public IReadOnlyList<WaveStageDefinition> Stages => stages;

    public void Configure(IEnumerable<WaveStageDefinition> configuredStages)
    {
        ConfigureWithStableId(stableId, configuredStages);
    }

    public void ConfigureWithStableId(
        string id,
        IEnumerable<WaveStageDefinition> configuredStages)
    {
        stableId = id?.Trim();
        stages = configuredStages != null
            ? new List<WaveStageDefinition>(configuredStages)
            : new List<WaveStageDefinition>();

        if (stages.Count == 0)
        {
            throw new ArgumentException(
                "A sequence requires at least one wave stage.",
                nameof(configuredStages));
        }

        for (int index = 0; index < stages.Count; index++)
        {
            if (stages[index]?.Wave == null)
            {
                throw new ArgumentException(
                    $"Wave stage {index + 1} has no definition.",
                    nameof(configuredStages));
            }
        }
    }

    public WaveStageDefinition GetStage(int zeroBasedIndex)
    {
        if (stages == null ||
            zeroBasedIndex < 0 ||
            zeroBasedIndex >= stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        }

        return stages[zeroBasedIndex];
    }
}
