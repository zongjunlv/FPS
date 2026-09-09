using System;
using System.Collections.Generic;
using FPS.Determinism;
using UnityEngine;

public enum WaveCompositionMode
{
    FixedCount,
    ThreatBudget
}

[Serializable]
public sealed class ThreatRoleConstraint
{
    [SerializeField] private string roleTag = "assault";
    [SerializeField, Min(0)] private int minimumCount;
    [SerializeField, Min(0)] private int maximumCount = int.MaxValue;

    public ThreatRoleConstraint(string role, int minimum, int maximum)
    {
        roleTag = NormalizeRole(role);
        minimumCount = Mathf.Max(0, minimum);
        maximumCount = Mathf.Max(minimumCount, maximum);
    }

    public string RoleTag => NormalizeRole(roleTag);
    public int MinimumCount => Mathf.Max(0, minimumCount);
    public int MaximumCount => Mathf.Max(MinimumCount, maximumCount);

    internal static string NormalizeRole(string role)
    {
        return string.IsNullOrWhiteSpace(role)
            ? "assault"
            : role.Trim();
    }
}

public sealed class ThreatBudgetWavePlan
{
    private readonly List<WaveEnemyEntry> entries;

    internal ThreatBudgetWavePlan(
        List<WaveEnemyEntry> selectedEntries,
        int totalThreat,
        int eliteThreat,
        bool usedFallback)
    {
        entries = selectedEntries;
        TotalThreat = totalThreat;
        EliteThreat = eliteThreat;
        UsedFallback = usedFallback;
    }

    public IReadOnlyList<WaveEnemyEntry> Entries => entries;
    public int TotalThreat { get; }
    public int EliteThreat { get; }
    public bool UsedFallback { get; }

    public int CountRole(string role)
    {
        string normalized = ThreatRoleConstraint.NormalizeRole(role);
        int count = 0;

        for (int index = 0; index < entries.Count; index++)
        {
            if (string.Equals(
                    entries[index].RoleTag,
                    normalized,
                    StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}

public static class ThreatBudgetWaveComposer
{
    public static ThreatBudgetWavePlan Compose(
        IReadOnlyList<WaveEnemyEntry> candidates,
        int threatBudget,
        int seed,
        IReadOnlyList<ThreatRoleConstraint> roleConstraints,
        float maximumEliteThreatRatio)
    {
        var streams = new NamedRandomStreams(seed);
        return Compose(
            candidates,
            threatBudget,
            streams.Fork(RunRandomStream.Wave, "default"),
            streams.Fork(RunRandomStream.Elite, "default"),
            roleConstraints,
            maximumEliteThreatRatio);
    }

    public static ThreatBudgetWavePlan Compose(
        IReadOnlyList<WaveEnemyEntry> candidates,
        int threatBudget,
        DeterministicRandom waveRandom,
        DeterministicRandom eliteRandom,
        IReadOnlyList<ThreatRoleConstraint> roleConstraints,
        float maximumEliteThreatRatio)
    {
        if (candidates == null || candidates.Count == 0)
        {
            throw new ArgumentException(
                "Threat-budget composition requires candidates.",
                nameof(candidates));
        }

        int budget = Mathf.Max(1, threatBudget);
        float eliteRatio = Mathf.Clamp01(maximumEliteThreatRatio);
        int eliteBudget = Mathf.FloorToInt(budget * eliteRatio);
        var valid = new List<WaveEnemyEntry>(candidates.Count);

        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index] != null)
            {
                valid.Add(candidates[index]);
            }
        }

        if (valid.Count == 0)
        {
            throw new ArgumentException(
                "Threat-budget composition has no valid candidates.",
                nameof(candidates));
        }

        var selected = new List<WaveEnemyEntry>();
        var roleCounts = new Dictionary<string, int>(
            StringComparer.Ordinal);
        if (waveRandom == null) throw new ArgumentNullException(nameof(waveRandom));
        if (eliteRandom == null) throw new ArgumentNullException(nameof(eliteRandom));
        int spent = 0;
        int eliteSpent = 0;
        bool usedFallback = false;

        if (roleConstraints != null)
        {
            for (int constraintIndex = 0;
                 constraintIndex < roleConstraints.Count;
                 constraintIndex++)
            {
                ThreatRoleConstraint constraint =
                    roleConstraints[constraintIndex];

                if (constraint == null)
                {
                    continue;
                }

                while (GetRoleCount(roleCounts, constraint.RoleTag) <
                       constraint.MinimumCount)
                {
                    WaveEnemyEntry required = SelectWeighted(
                        valid,
                        waveRandom,
                        eliteRandom,
                        entry => string.Equals(
                                entry.RoleTag,
                                constraint.RoleTag,
                                StringComparison.Ordinal) &&
                            CanAdd(
                                entry,
                                spent,
                                eliteSpent,
                                budget,
                                eliteBudget,
                                roleCounts,
                                roleConstraints,
                                true));

                    if (required == null)
                    {
                        usedFallback = true;
                        break;
                    }

                    Add(required, selected, roleCounts,
                        ref spent, ref eliteSpent);
                }
            }
        }

        int guard = budget + valid.Count + 1;

        while (guard-- > 0)
        {
            WaveEnemyEntry next = SelectWeighted(
                valid,
                waveRandom,
                eliteRandom,
                entry => CanAdd(
                    entry,
                    spent,
                    eliteSpent,
                    budget,
                    eliteBudget,
                    roleCounts,
                    roleConstraints,
                    false));

            if (next == null)
            {
                break;
            }

            Add(next, selected, roleCounts,
                ref spent, ref eliteSpent);
        }

        if (selected.Count == 0)
        {
            WaveEnemyEntry cheapest = valid[0];

            for (int index = 1; index < valid.Count; index++)
            {
                if (valid[index].ThreatCost < cheapest.ThreatCost)
                {
                    cheapest = valid[index];
                }
            }

            Add(cheapest, selected, roleCounts,
                ref spent, ref eliteSpent);
            usedFallback = true;
        }

        if (!MinimumsSatisfied(roleCounts, roleConstraints))
        {
            usedFallback = true;
        }

        return new ThreatBudgetWavePlan(
            selected,
            spent,
            eliteSpent,
            usedFallback);
    }

    private static bool CanAdd(
        WaveEnemyEntry entry,
        int spent,
        int eliteSpent,
        int budget,
        int eliteBudget,
        IReadOnlyDictionary<string, int> roleCounts,
        IReadOnlyList<ThreatRoleConstraint> constraints,
        bool satisfyingMinimum)
    {
        if (spent + entry.ThreatCost > budget)
        {
            return false;
        }

        if (entry.IsElite && eliteSpent + entry.ThreatCost > eliteBudget)
        {
            return false;
        }

        ThreatRoleConstraint constraint = FindConstraint(
            constraints,
            entry.RoleTag);
        return constraint == null ||
            GetRoleCount(roleCounts, entry.RoleTag) <
            constraint.MaximumCount ||
            satisfyingMinimum &&
            GetRoleCount(roleCounts, entry.RoleTag) <
            constraint.MinimumCount;
    }

    private static WaveEnemyEntry SelectWeighted(
        IReadOnlyList<WaveEnemyEntry> candidates,
        DeterministicRandom waveRandom,
        DeterministicRandom eliteRandom,
        Predicate<WaveEnemyEntry> predicate)
    {
        int normalWeight = 0;
        int eliteWeight = 0;

        for (int index = 0; index < candidates.Count; index++)
        {
            if (predicate(candidates[index]))
            {
                if (candidates[index].IsElite)
                {
                    eliteWeight += candidates[index].Weight;
                }
                else
                {
                    normalWeight += candidates[index].Weight;
                }
            }
        }

        int totalWeight = normalWeight + eliteWeight;
        if (totalWeight <= 0)
        {
            return null;
        }

        // Wave stream decides whether this slot is normal or elite. The elite
        // stream only decides which eligible elite is used, so adding an elite
        // affix roll cannot advance or perturb the wave composition stream.
        bool selectElite = waveRandom.NextInt(totalWeight) >= normalWeight;
        int bucketWeight = selectElite ? eliteWeight : normalWeight;
        DeterministicRandom candidateRandom = selectElite
            ? eliteRandom
            : waveRandom;
        int selection = candidateRandom.NextInt(bucketWeight);

        for (int index = 0; index < candidates.Count; index++)
        {
            WaveEnemyEntry candidate = candidates[index];

            if (!predicate(candidate) || candidate.IsElite != selectElite)
            {
                continue;
            }

            if (selection < candidate.Weight)
            {
                return candidate;
            }

            selection -= candidate.Weight;
        }

        return null;
    }

    private static void Add(
        WaveEnemyEntry entry,
        ICollection<WaveEnemyEntry> selected,
        Dictionary<string, int> roleCounts,
        ref int spent,
        ref int eliteSpent)
    {
        selected.Add(entry);
        spent += entry.ThreatCost;

        if (entry.IsElite)
        {
            eliteSpent += entry.ThreatCost;
        }

        roleCounts[entry.RoleTag] =
            GetRoleCount(roleCounts, entry.RoleTag) + 1;
    }

    private static bool MinimumsSatisfied(
        IReadOnlyDictionary<string, int> roleCounts,
        IReadOnlyList<ThreatRoleConstraint> constraints)
    {
        if (constraints == null)
        {
            return true;
        }

        for (int index = 0; index < constraints.Count; index++)
        {
            ThreatRoleConstraint constraint = constraints[index];

            if (constraint != null &&
                GetRoleCount(roleCounts, constraint.RoleTag) <
                constraint.MinimumCount)
            {
                return false;
            }
        }

        return true;
    }

    private static ThreatRoleConstraint FindConstraint(
        IReadOnlyList<ThreatRoleConstraint> constraints,
        string role)
    {
        if (constraints == null)
        {
            return null;
        }

        for (int index = 0; index < constraints.Count; index++)
        {
            ThreatRoleConstraint constraint = constraints[index];

            if (constraint != null && string.Equals(
                    constraint.RoleTag,
                    role,
                    StringComparison.Ordinal))
            {
                return constraint;
            }
        }

        return null;
    }

    private static int GetRoleCount(
        IReadOnlyDictionary<string, int> roleCounts,
        string role)
    {
        return roleCounts != null &&
            roleCounts.TryGetValue(role, out int count)
            ? count
            : 0;
    }
}
