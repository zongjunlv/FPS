using System;
using System.Collections.Generic;

public readonly struct RuntimeDiagnosticCount
{
    public RuntimeDiagnosticCount(string label, int count)
    {
        Label = string.IsNullOrWhiteSpace(label) ? "未分类" : label.Trim();
        Count = Math.Max(0, count);
    }

    public string Label { get; }
    public int Count { get; }
}

public sealed class PerceptionRuntimeDiagnostics
{
    public PerceptionRuntimeDiagnostics(
        int budgetPerFrame, int registeredCount, int checksLastFrame,
        int queueLength, int pendingBatchCount,
        int maximumLatencyFrames, float averageLatencyFrames)
    {
        BudgetPerFrame = Math.Max(0, budgetPerFrame);
        RegisteredCount = Math.Max(0, registeredCount);
        ChecksLastFrame = Math.Max(0, checksLastFrame);
        QueueLength = Math.Max(0, queueLength);
        PendingBatchCount = Math.Max(0, pendingBatchCount);
        MaximumLatencyFrames = Math.Max(0, maximumLatencyFrames);
        AverageLatencyFrames = Math.Max(0f, averageLatencyFrames);
    }

    public int BudgetPerFrame { get; }
    public int RegisteredCount { get; }
    public int ChecksLastFrame { get; }
    public int QueueLength { get; }
    public int PendingBatchCount { get; }
    public int MaximumLatencyFrames { get; }
    public float AverageLatencyFrames { get; }
}

public sealed class WaveRuntimeDiagnostics
{
    public WaveRuntimeDiagnostics(
        int currentWave, int totalWaves, string phase,
        int threatBudget, int resolvedThreat,
        IReadOnlyList<RuntimeDiagnosticCount> candidates,
        IReadOnlyList<RuntimeDiagnosticCount> spawnQueue)
    {
        CurrentWave = Math.Max(0, currentWave);
        TotalWaves = Math.Max(0, totalWaves);
        Phase = string.IsNullOrWhiteSpace(phase) ? "Idle" : phase;
        ThreatBudget = Math.Max(0, threatBudget);
        ResolvedThreat = Math.Max(0, resolvedThreat);
        Candidates = candidates ?? Array.Empty<RuntimeDiagnosticCount>();
        SpawnQueue = spawnQueue ?? Array.Empty<RuntimeDiagnosticCount>();
    }

    public int CurrentWave { get; }
    public int TotalWaves { get; }
    public string Phase { get; }
    public int ThreatBudget { get; }
    public int ResolvedThreat { get; }
    public IReadOnlyList<RuntimeDiagnosticCount> Candidates { get; }
    public IReadOnlyList<RuntimeDiagnosticCount> SpawnQueue { get; }
}

public sealed class EnemyPoolRuntimeDiagnostics
{
    public EnemyPoolRuntimeDiagnostics(
        string template, int capacity, int active, int available,
        int reuse, int expansion)
    {
        Template = string.IsNullOrWhiteSpace(template) ? "未命名模板" : template;
        Capacity = Math.Max(0, capacity);
        Active = Math.Max(0, active);
        Available = Math.Max(0, available);
        Reuse = Math.Max(0, reuse);
        Expansion = Math.Max(0, expansion);
    }

    public string Template { get; }
    public int Capacity { get; }
    public int Active { get; }
    public int Available { get; }
    public int Reuse { get; }
    public int Expansion { get; }
}

public sealed class EnemyUtilityCandidateRuntimeDiagnostics
{
    public EnemyUtilityCandidateRuntimeDiagnostics(
        string actionId,
        string displayName,
        float score,
        bool eligible,
        float cooldownRemaining,
        string status)
    {
        ActionId = actionId ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? ActionId
            : displayName.Trim();
        Score = Math.Max(0f, score);
        Eligible = eligible;
        CooldownRemaining = Math.Max(0f, cooldownRemaining);
        Status = status ?? string.Empty;
    }

    public string ActionId { get; }
    public string DisplayName { get; }
    public float Score { get; }
    public bool Eligible { get; }
    public float CooldownRemaining { get; }
    public string Status { get; }
}

public sealed class EnemyUtilityRuntimeDiagnostics
{
    public EnemyUtilityRuntimeDiagnostics(
        int spawnId,
        string role,
        string selectedAction,
        string reason,
        EnemyUtilityWorldFacts facts,
        IReadOnlyList<EnemyUtilityCandidateRuntimeDiagnostics> candidates)
    {
        SpawnId = Math.Max(0, spawnId);
        Role = string.IsNullOrWhiteSpace(role) ? "未分类" : role.Trim();
        SelectedAction = string.IsNullOrWhiteSpace(selectedAction)
            ? "无可执行行动"
            : selectedAction.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? "无" : reason.Trim();
        Facts = facts;
        Candidates = candidates ??
            Array.Empty<EnemyUtilityCandidateRuntimeDiagnostics>();
    }

    public int SpawnId { get; }
    public string Role { get; }
    public string SelectedAction { get; }
    public string Reason { get; }
    public EnemyUtilityWorldFacts Facts { get; }
    public IReadOnlyList<EnemyUtilityCandidateRuntimeDiagnostics> Candidates { get; }
}

public sealed class RuntimeCombatDiagnosticsSnapshot
{
    public RuntimeCombatDiagnosticsSnapshot(
        IReadOnlyList<RuntimeDiagnosticCount> awarenessStates,
        IReadOnlyList<RuntimeDiagnosticCount> roles,
        IReadOnlyList<RuntimeDiagnosticCount> lodTiers,
        PerceptionRuntimeDiagnostics perception,
        WaveRuntimeDiagnostics wave,
        IReadOnlyList<EnemyPoolRuntimeDiagnostics> pools,
        IReadOnlyList<EnemyUtilityRuntimeDiagnostics> utilityDecisions = null)
    {
        AwarenessStates = awarenessStates ?? Array.Empty<RuntimeDiagnosticCount>();
        Roles = roles ?? Array.Empty<RuntimeDiagnosticCount>();
        LodTiers = lodTiers ?? Array.Empty<RuntimeDiagnosticCount>();
        Perception = perception;
        Wave = wave;
        Pools = pools ?? Array.Empty<EnemyPoolRuntimeDiagnostics>();
        UtilityDecisions = utilityDecisions ??
            Array.Empty<EnemyUtilityRuntimeDiagnostics>();
    }

    public IReadOnlyList<RuntimeDiagnosticCount> AwarenessStates { get; }
    public IReadOnlyList<RuntimeDiagnosticCount> Roles { get; }
    public IReadOnlyList<RuntimeDiagnosticCount> LodTiers { get; }
    public PerceptionRuntimeDiagnostics Perception { get; }
    public WaveRuntimeDiagnostics Wave { get; }
    public IReadOnlyList<EnemyPoolRuntimeDiagnostics> Pools { get; }
    public IReadOnlyList<EnemyUtilityRuntimeDiagnostics> UtilityDecisions { get; }
}

public sealed class RuntimeDiagnosticsRefreshGate
{
    private readonly float interval;
    private float nextRefreshTime;

    public RuntimeDiagnosticsRefreshGate(float refreshInterval)
    {
        interval = Math.Max(0.05f, refreshInterval);
    }

    public bool Visible { get; private set; }

    public void SetVisible(bool visible, float currentTime)
    {
        Visible = visible;
        nextRefreshTime = visible ? currentTime : float.PositiveInfinity;
    }

    public bool TryConsume(float currentTime)
    {
        if (!Visible || currentTime < nextRefreshTime)
        {
            return false;
        }
        nextRefreshTime = currentTime + interval;
        return true;
    }
}
