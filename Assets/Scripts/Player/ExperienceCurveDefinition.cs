using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "ExperienceCurveDefinition",
    menuName = "FPS/Progression/Experience Curve")]
public sealed class ExperienceCurveDefinition : ScriptableObject
{
    [SerializeField] private List<int> thresholds = new()
    {
        100,
        150,
        225,
        325,
        450,
        600,
        800,
        1050,
        1350
    };

    public IReadOnlyList<int> Thresholds => thresholds;

    public void Configure(IEnumerable<int> configuredThresholds)
    {
        thresholds = configuredThresholds != null
            ? new List<int>(configuredThresholds)
            : new List<int>();

        if (thresholds.Count == 0)
        {
            throw new ArgumentException(
                "At least one experience threshold is required.",
                nameof(configuredThresholds));
        }

        for (int index = 0; index < thresholds.Count; index++)
        {
            if (thresholds[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuredThresholds));
            }
        }
    }
}
