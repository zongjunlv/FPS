using System;
using System.Collections.Generic;

public readonly struct RunExperienceSnapshot
{
    public RunExperienceSnapshot(
        int level,
        int currentExperience,
        int experienceToNextLevel,
        int totalExperience,
        int levelUpCount)
    {
        Level = level;
        CurrentExperience = currentExperience;
        ExperienceToNextLevel = experienceToNextLevel;
        TotalExperience = totalExperience;
        LevelUpCount = levelUpCount;
    }

    public int Level { get; }
    public int CurrentExperience { get; }
    public int ExperienceToNextLevel { get; }
    public int TotalExperience { get; }
    public int LevelUpCount { get; }
    public bool IsMaxLevel => ExperienceToNextLevel <= 0;
    public float ProgressNormalized => IsMaxLevel
        ? 1f
        : Math.Clamp(
            (float)CurrentExperience / ExperienceToNextLevel,
            0f,
            1f);
}

public sealed class RunExperienceState
{
    private readonly int[] thresholds;

    public RunExperienceState(IReadOnlyList<int> levelThresholds)
    {
        if (levelThresholds == null || levelThresholds.Count == 0)
        {
            throw new ArgumentException(
                "At least one level threshold is required.",
                nameof(levelThresholds));
        }

        thresholds = new int[levelThresholds.Count];

        for (int index = 0; index < levelThresholds.Count; index++)
        {
            if (levelThresholds[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(levelThresholds),
                    "Experience thresholds must be positive.");
            }

            thresholds[index] = levelThresholds[index];
        }

        Reset();
    }

    public event Action<RunExperienceSnapshot> Changed;

    public int Level { get; private set; }
    public int CurrentExperience { get; private set; }
    public int TotalExperience { get; private set; }
    public int LevelUpCount { get; private set; }
    public int ExperienceToNextLevel =>
        Level <= thresholds.Length ? thresholds[Level - 1] : 0;
    public bool IsMaxLevel => Level > thresholds.Length;
    public RunExperienceSnapshot Current => new(
        Level,
        CurrentExperience,
        ExperienceToNextLevel,
        TotalExperience,
        LevelUpCount);

    public int GrantExperience(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        TotalExperience += amount;
        CurrentExperience += amount;
        int levelsGained = 0;

        while (!IsMaxLevel &&
               CurrentExperience >= ExperienceToNextLevel)
        {
            CurrentExperience -= ExperienceToNextLevel;
            Level++;
            LevelUpCount++;
            levelsGained++;
        }

        Changed?.Invoke(Current);
        return levelsGained;
    }

    public void Reset()
    {
        Level = 1;
        CurrentExperience = 0;
        TotalExperience = 0;
        LevelUpCount = 0;
        Changed?.Invoke(Current);
    }
}
