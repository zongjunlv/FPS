using System;
using System.Collections.Generic;
using System.Text;
using FPS.Determinism;

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
        DeterministicRandom random = new NamedRandomStreams(runSeed).Fork(
            RunRandomStream.Upgrade,
            CreateChoiceContext(state.SelectionHistory));

        for (int index = eligible.Count - 1; index > 0; index--)
        {
            int swapIndex = random.NextInt(index + 1);
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

    private static string CreateChoiceContext(IReadOnlyList<string> history)
    {
        var context = new StringBuilder("selection:");
        for (int index = 0; index < history.Count; index++)
        {
            if (index > 0) context.Append('|');
            context.Append(history[index] ?? string.Empty);
        }
        return context.ToString();
    }
}
