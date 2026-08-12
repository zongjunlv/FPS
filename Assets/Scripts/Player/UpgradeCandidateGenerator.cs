using System;
using System.Collections.Generic;

public enum UpgradeCandidateStatus
{
    Complete,
    Reduced,
    NoEligibleUpgrade
}

public readonly struct UpgradeCandidateResult
{
    public UpgradeCandidateResult(
        IReadOnlyList<UpgradeDefinition> candidates,
        UpgradeCandidateStatus status)
    {
        Candidates = candidates;
        Status = status;
    }

    public IReadOnlyList<UpgradeDefinition> Candidates { get; }
    public UpgradeCandidateStatus Status { get; }
}

public sealed class UpgradeCandidateGenerator
{
    private readonly int runSeed;

    public UpgradeCandidateGenerator(int seed)
    {
        runSeed = seed;
    }

    public UpgradeCandidateResult Generate(
        IReadOnlyList<UpgradeDefinition> definitions,
        RunUpgradeState state,
        int requestedCount = 3)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var unique = new Dictionary<string, UpgradeDefinition>(
            StringComparer.Ordinal);

        if (definitions != null)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                UpgradeDefinition definition = definitions[index];

                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.StableId) ||
                    state.IsMaximumLevel(definition))
                {
                    continue;
                }

                unique.TryAdd(definition.StableId, definition);
            }
        }

        var eligible = new List<UpgradeDefinition>(unique.Values);
        eligible.Sort((left, right) => string.CompareOrdinal(
            left.StableId,
            right.StableId));
        var random = new Random(CreateChoiceSeed(state.SelectionHistory));

        for (int index = eligible.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (eligible[index], eligible[swapIndex]) =
                (eligible[swapIndex], eligible[index]);
        }

        int count = Math.Min(Math.Max(0, requestedCount), eligible.Count);
        var candidates = eligible.GetRange(0, count);
        UpgradeCandidateStatus status = count == 0
            ? UpgradeCandidateStatus.NoEligibleUpgrade
            : count < requestedCount
                ? UpgradeCandidateStatus.Reduced
                : UpgradeCandidateStatus.Complete;
        return new UpgradeCandidateResult(candidates, status);
    }

    private int CreateChoiceSeed(IReadOnlyList<string> history)
    {
        unchecked
        {
            uint hash = 2166136261u;
            Mix(ref hash, runSeed.ToString());

            for (int index = 0; index < history.Count; index++)
            {
                Mix(ref hash, history[index] ?? string.Empty);
                hash ^= 255u;
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }

    private static void Mix(ref uint hash, string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            hash ^= value[index];
            hash *= 16777619u;
        }
    }
}
