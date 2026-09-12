using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyUtilityFactId
{
    TargetDistance,
    HasLineOfSight,
    TargetInCover,
    HealthRatio,
    FriendlyRaiderCount,
    FriendlySuppressorCount,
    FriendlySupportCount,
    HasSupportCoverage,
    ActionCooldownRemaining,
    ActionCommitmentRemaining
}

public enum EnemyUtilityResponseCurve
{
    Constant,
    Rising,
    Falling,
    BooleanTrue,
    BooleanFalse,
    InRange,
    OutsideRange
}

public enum EnemyUtilityActionKind
{
    None,
    Chase,
    Flank,
    Retreat
}

public readonly struct EnemyUtilityWorldFacts
{
    public EnemyUtilityWorldFacts(
        float targetDistance,
        bool hasLineOfSight,
        bool targetInCover,
        float healthRatio,
        int friendlyRaiderCount,
        int friendlySuppressorCount,
        int friendlySupportCount,
        bool hasSupportCoverage,
        float actionCooldownRemaining = 0f,
        float actionCommitmentRemaining = 0f)
    {
        TargetDistance = Mathf.Max(0f, targetDistance);
        HasLineOfSight = hasLineOfSight;
        TargetInCover = targetInCover;
        HealthRatio = Mathf.Clamp01(healthRatio);
        FriendlyRaiderCount = Mathf.Max(0, friendlyRaiderCount);
        FriendlySuppressorCount = Mathf.Max(0, friendlySuppressorCount);
        FriendlySupportCount = Mathf.Max(0, friendlySupportCount);
        HasSupportCoverage = hasSupportCoverage;
        ActionCooldownRemaining = Mathf.Max(0f, actionCooldownRemaining);
        ActionCommitmentRemaining = Mathf.Max(0f, actionCommitmentRemaining);
    }

    public float TargetDistance { get; }
    public bool HasLineOfSight { get; }
    public bool TargetInCover { get; }
    public float HealthRatio { get; }
    public int FriendlyRaiderCount { get; }
    public int FriendlySuppressorCount { get; }
    public int FriendlySupportCount { get; }
    public bool HasSupportCoverage { get; }
    public float ActionCooldownRemaining { get; }
    public float ActionCommitmentRemaining { get; }

    public float Read(EnemyUtilityFactId fact)
    {
        return fact switch
        {
            EnemyUtilityFactId.TargetDistance => TargetDistance,
            EnemyUtilityFactId.HasLineOfSight => HasLineOfSight ? 1f : 0f,
            EnemyUtilityFactId.TargetInCover => TargetInCover ? 1f : 0f,
            EnemyUtilityFactId.HealthRatio => HealthRatio,
            EnemyUtilityFactId.FriendlyRaiderCount => FriendlyRaiderCount,
            EnemyUtilityFactId.FriendlySuppressorCount => FriendlySuppressorCount,
            EnemyUtilityFactId.FriendlySupportCount => FriendlySupportCount,
            EnemyUtilityFactId.HasSupportCoverage =>
                HasSupportCoverage ? 1f : 0f,
            EnemyUtilityFactId.ActionCooldownRemaining =>
                ActionCooldownRemaining,
            EnemyUtilityFactId.ActionCommitmentRemaining =>
                ActionCommitmentRemaining,
            _ => 0f
        };
    }

    public EnemyUtilityWorldFacts WithActionState(
        float cooldownRemaining,
        float commitmentRemaining)
    {
        return new EnemyUtilityWorldFacts(
            TargetDistance,
            HasLineOfSight,
            TargetInCover,
            HealthRatio,
            FriendlyRaiderCount,
            FriendlySuppressorCount,
            FriendlySupportCount,
            HasSupportCoverage,
            cooldownRemaining,
            commitmentRemaining);
    }
}

[Serializable]
public struct EnemyUtilityConsiderationDefinition
{
    [SerializeField] private EnemyUtilityFactId fact;
    [SerializeField] private EnemyUtilityResponseCurve response;
    [SerializeField] private float inputMinimum;
    [SerializeField] private float inputMaximum;
    [SerializeField, Min(0.01f)] private float exponent;
    [SerializeField, Range(0f, 1f)] private float influence;

    public EnemyUtilityConsiderationDefinition(
        EnemyUtilityFactId configuredFact,
        EnemyUtilityResponseCurve configuredResponse,
        float configuredMinimum = 0f,
        float configuredMaximum = 1f,
        float configuredExponent = 1f,
        float configuredInfluence = 1f)
    {
        fact = configuredFact;
        response = configuredResponse;
        inputMinimum = configuredMinimum;
        inputMaximum = configuredMaximum;
        exponent = Mathf.Max(0.01f, configuredExponent);
        influence = Mathf.Clamp01(configuredInfluence);
    }

    public EnemyUtilityFactId Fact => fact;
    public EnemyUtilityResponseCurve Response => response;
    public float Influence => influence;

    public float Evaluate(EnemyUtilityWorldFacts facts)
    {
        float value = facts.Read(fact);
        float lower = Mathf.Min(inputMinimum, inputMaximum);
        float upper = Mathf.Max(inputMinimum, inputMaximum);
        float normalized = upper - lower <= Mathf.Epsilon
            ? value >= upper ? 1f : 0f
            : Mathf.InverseLerp(lower, upper, value);
        float responseValue = response switch
        {
            EnemyUtilityResponseCurve.Constant => 1f,
            EnemyUtilityResponseCurve.Rising => normalized,
            EnemyUtilityResponseCurve.Falling => 1f - normalized,
            EnemyUtilityResponseCurve.BooleanTrue => value >= 0.5f ? 1f : 0f,
            EnemyUtilityResponseCurve.BooleanFalse => value < 0.5f ? 1f : 0f,
            EnemyUtilityResponseCurve.InRange =>
                value >= lower && value <= upper ? 1f : 0f,
            EnemyUtilityResponseCurve.OutsideRange =>
                value < lower || value > upper ? 1f : 0f,
            _ => 0f
        };

        responseValue = Mathf.Pow(Mathf.Clamp01(responseValue),
            Mathf.Max(0.01f, exponent));
        return Mathf.Lerp(1f, responseValue, Mathf.Clamp01(influence));
    }
}

public readonly struct EnemyUtilityCandidateScore
{
    public EnemyUtilityCandidateScore(
        EnemyUtilityActionDefinition action,
        float score,
        bool executable,
        float cooldownRemaining,
        string status)
    {
        Action = action;
        Score = Mathf.Max(0f, score);
        Executable = executable;
        CooldownRemaining = Mathf.Max(0f, cooldownRemaining);
        Status = status ?? string.Empty;
    }

    public EnemyUtilityActionDefinition Action { get; }
    public float Score { get; }
    public bool Executable { get; }
    public float CooldownRemaining { get; }
    public string Status { get; }
    public bool Eligible => Executable &&
        CooldownRemaining <= Mathf.Epsilon &&
        Action != null && Score >= Action.MinimumScore;
}

public sealed class EnemyUtilityDecisionResult
{
    public EnemyUtilityDecisionResult(
        EnemyUtilityActionDefinition selectedAction,
        string previousActionId,
        string reasonCode,
        bool changed,
        EnemyUtilityWorldFacts facts,
        IReadOnlyList<EnemyUtilityCandidateScore> candidates)
    {
        SelectedAction = selectedAction;
        PreviousActionId = previousActionId ?? string.Empty;
        ReasonCode = reasonCode ?? string.Empty;
        Changed = changed;
        Facts = facts;
        Candidates = candidates ?? Array.Empty<EnemyUtilityCandidateScore>();
    }

    public EnemyUtilityActionDefinition SelectedAction { get; }
    public string PreviousActionId { get; }
    public string ReasonCode { get; }
    public bool Changed { get; }
    public EnemyUtilityWorldFacts Facts { get; }
    public IReadOnlyList<EnemyUtilityCandidateScore> Candidates { get; }
    public bool HasSelection => SelectedAction != null;
}

public sealed class EnemyUtilityDecisionEngine
{
    private const float ScoreEpsilon = 0.0001f;
    private readonly Dictionary<string, float> cooldowns =
        new(StringComparer.Ordinal);
    private EnemyUtilityProfileDefinition profile;
    private string selectedActionId = string.Empty;
    private float commitmentRemaining;
    private string pendingFallbackFrom = string.Empty;

    public string SelectedActionId => selectedActionId;
    public float CommitmentRemaining => commitmentRemaining;

    public void Reset(EnemyUtilityProfileDefinition configuredProfile)
    {
        profile = configuredProfile;
        selectedActionId = string.Empty;
        commitmentRemaining = 0f;
        pendingFallbackFrom = string.Empty;
        cooldowns.Clear();
    }

    public void CancelActive()
    {
        selectedActionId = string.Empty;
        commitmentRemaining = 0f;
        pendingFallbackFrom = string.Empty;
    }

    public void CompleteActive()
    {
        PutSelectedActionOnCooldown();
        selectedActionId = string.Empty;
        commitmentRemaining = 0f;
    }

    public void ReportFailure(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        EnemyUtilityActionDefinition failed = profile?.FindAction(actionId);

        if (failed != null)
        {
            cooldowns[failed.StableId] = Mathf.Max(
                GetCooldownRemaining(failed.StableId),
                failed.Cooldown);
            pendingFallbackFrom = failed.StableId;
        }

        if (string.Equals(selectedActionId, actionId,
                StringComparison.Ordinal))
        {
            selectedActionId = string.Empty;
            commitmentRemaining = 0f;
        }
    }

    public float GetCooldownRemaining(string actionId)
    {
        return !string.IsNullOrWhiteSpace(actionId) &&
               cooldowns.TryGetValue(actionId, out float remaining)
            ? Mathf.Max(0f, remaining)
            : 0f;
    }

    public EnemyUtilityDecisionResult Evaluate(
        EnemyUtilityWorldFacts facts,
        float deltaTime,
        long seed,
        Func<EnemyUtilityActionDefinition, bool> isExecutable = null)
    {
        float elapsed = Mathf.Max(0f, deltaTime);
        TickActionState(elapsed);
        string previous = selectedActionId;

        if (profile == null || profile.Actions.Count == 0)
        {
            selectedActionId = string.Empty;
            commitmentRemaining = 0f;
            return new EnemyUtilityDecisionResult(
                null,
                previous,
                "no-profile",
                !string.IsNullOrEmpty(previous),
                facts,
                Array.Empty<EnemyUtilityCandidateScore>());
        }

        var candidates = new List<EnemyUtilityCandidateScore>(
            profile.Actions.Count);
        EnemyUtilityCandidateScore? best = null;
        EnemyUtilityCandidateScore? current = null;

        for (int index = 0; index < profile.Actions.Count; index++)
        {
            EnemyUtilityActionDefinition action = profile.Actions[index];

            if (action == null || string.IsNullOrWhiteSpace(action.StableId))
            {
                continue;
            }

            float cooldown = GetCooldownRemaining(action.StableId);
            EnemyUtilityWorldFacts actionFacts = facts.WithActionState(
                cooldown,
                string.Equals(selectedActionId, action.StableId,
                    StringComparison.Ordinal)
                    ? commitmentRemaining
                    : 0f);
            float score = action.Evaluate(actionFacts);
            bool executable = isExecutable == null || isExecutable(action);
            string status = !executable
                ? "unreachable"
                : cooldown > Mathf.Epsilon
                    ? "cooldown"
                    : score + ScoreEpsilon < action.MinimumScore
                        ? "below-threshold"
                        : "ready";
            var candidate = new EnemyUtilityCandidateScore(
                action,
                score,
                executable,
                cooldown,
                status);
            candidates.Add(candidate);

            if (string.Equals(action.StableId, selectedActionId,
                    StringComparison.Ordinal))
            {
                current = candidate;
            }

            if (!candidate.Eligible ||
                best.HasValue && !IsBetter(candidate, best.Value, seed))
            {
                continue;
            }

            best = candidate;
        }

        EnemyUtilityCandidateScore? selected = best;
        string reason = "highest-score";

        if (current.HasValue && current.Value.Executable &&
            current.Value.CooldownRemaining <= ScoreEpsilon &&
            commitmentRemaining > ScoreEpsilon)
        {
            selected = current;
            reason = "commitment";
        }
        else if (current.HasValue && current.Value.Eligible &&
                 best.HasValue &&
                 !ReferenceEquals(
                     current.Value.Action,
                     best.Value.Action) &&
                 best.Value.Score <= current.Value.Score +
                     current.Value.Action.HysteresisBonus + ScoreEpsilon)
        {
            selected = current;
            reason = "hysteresis";
        }

        if (!selected.HasValue)
        {
            EnemyUtilityActionDefinition fallback =
                ResolveFallbackAction(pendingFallbackFrom);

            if (fallback != null)
            {
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (ReferenceEquals(candidates[index].Action, fallback) &&
                        candidates[index].Eligible)
                    {
                        selected = candidates[index];
                        reason = "fallback";
                        break;
                    }
                }
            }
        }
        else if (!string.IsNullOrEmpty(pendingFallbackFrom))
        {
            EnemyUtilityActionDefinition fallback =
                ResolveFallbackAction(pendingFallbackFrom);

            if (fallback != null && ReferenceEquals(
                    fallback,
                    selected.Value.Action))
            {
                reason = "fallback";
            }
        }

        pendingFallbackFrom = string.Empty;
        EnemyUtilityActionDefinition chosen = selected?.Action;
        string nextId = chosen?.StableId ?? string.Empty;
        bool changed = !string.Equals(
            previous,
            nextId,
            StringComparison.Ordinal);

        if (changed)
        {
            PutActionOnCooldown(previous);
            selectedActionId = nextId;
            commitmentRemaining = chosen != null
                ? chosen.CommitmentDuration
                : 0f;
        }

        return new EnemyUtilityDecisionResult(
            chosen,
            previous,
            selected.HasValue ? reason : "no-candidate",
            changed,
            facts.WithActionState(
                chosen != null ? GetCooldownRemaining(chosen.StableId) : 0f,
                commitmentRemaining),
            candidates.AsReadOnly());
    }

    private void TickActionState(float deltaTime)
    {
        commitmentRemaining = Mathf.Max(
            0f,
            commitmentRemaining - deltaTime);

        if (profile == null)
        {
            return;
        }

        for (int index = 0; index < profile.Actions.Count; index++)
        {
            EnemyUtilityActionDefinition action = profile.Actions[index];

            if (action == null ||
                !cooldowns.TryGetValue(action.StableId, out float remaining))
            {
                continue;
            }

            cooldowns[action.StableId] = Mathf.Max(0f, remaining - deltaTime);
        }
    }

    private bool IsBetter(
        EnemyUtilityCandidateScore candidate,
        EnemyUtilityCandidateScore incumbent,
        long seed)
    {
        if (candidate.Score > incumbent.Score + ScoreEpsilon)
        {
            return true;
        }

        if (Mathf.Abs(candidate.Score - incumbent.Score) > ScoreEpsilon)
        {
            return false;
        }

        return StableTieRank(seed, candidate.Action.StableId) <
               StableTieRank(seed, incumbent.Action.StableId);
    }

    private EnemyUtilityActionDefinition ResolveFallbackAction(
        string failedActionId)
    {
        EnemyUtilityActionDefinition failed = profile?.FindAction(
            failedActionId);
        EnemyUtilityActionDefinition fallback = failed != null
            ? profile.FindAction(failed.FallbackActionId)
            : null;
        return fallback ?? profile?.FindAction(profile.DefaultActionId);
    }

    private void PutSelectedActionOnCooldown()
    {
        PutActionOnCooldown(selectedActionId);
    }

    private void PutActionOnCooldown(string actionId)
    {
        EnemyUtilityActionDefinition action = profile?.FindAction(actionId);

        if (action != null && action.Cooldown > 0f)
        {
            cooldowns[action.StableId] = Mathf.Max(
                GetCooldownRemaining(action.StableId),
                action.Cooldown);
        }
    }

    private static ulong StableTieRank(long seed, string stableId)
    {
        ulong hash = 14695981039346656037UL;
        ulong bits = unchecked((ulong)seed);

        for (int index = 0; index < 8; index++)
        {
            hash ^= (byte)(bits >> (index * 8));
            hash *= 1099511628211UL;
        }

        string id = stableId ?? string.Empty;

        for (int index = 0; index < id.Length; index++)
        {
            char value = id[index];
            hash ^= (byte)value;
            hash *= 1099511628211UL;
            hash ^= (byte)(value >> 8);
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
