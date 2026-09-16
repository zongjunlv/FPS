using System;
using System.Collections.Generic;

public readonly struct TutorialProgressSnapshot
{
    public TutorialProgressSnapshot(
        string sequenceId,
        int totalSteps,
        int currentStepIndex,
        TutorialStepDefinition currentStep,
        float currentValue,
        bool isComplete,
        string lastCompletedTitle)
    {
        SequenceId = sequenceId;
        TotalSteps = totalSteps;
        CurrentStepIndex = currentStepIndex;
        CurrentStep = currentStep;
        CurrentValue = currentValue;
        IsComplete = isComplete;
        LastCompletedTitle = lastCompletedTitle ?? string.Empty;
    }

    public string SequenceId { get; }
    public int TotalSteps { get; }
    public int CurrentStepIndex { get; }
    public TutorialStepDefinition CurrentStep { get; }
    public float CurrentValue { get; }
    public bool IsComplete { get; }
    public string LastCompletedTitle { get; }
    public int CompletedStepCount => IsComplete
        ? TotalSteps
        : Math.Max(0, CurrentStepIndex);
}

public sealed class TutorialProgressionStateMachine
{
    private readonly TutorialSequenceDefinition definition;
    private readonly HashSet<string> acceptedEvidenceKeys =
        new(StringComparer.Ordinal);
    private int currentStepIndex;
    private float currentValue;
    private string lastCompletedTitle = string.Empty;

    public TutorialProgressionStateMachine(
        TutorialSequenceDefinition configuredDefinition)
    {
        definition = configuredDefinition ??
            throw new ArgumentNullException(nameof(configuredDefinition));
        if (!definition.TryValidate(out string error))
        {
            throw new ArgumentException(error, nameof(configuredDefinition));
        }

        Reset(false);
    }

    public event Action<TutorialProgressSnapshot> ProgressChanged;
    public event Action<TutorialProgressSnapshot> StepCompleted;
    public event Action<TutorialProgressSnapshot> SequenceCompleted;

    public TutorialSequenceDefinition Definition => definition;
    public int CurrentStepIndex => currentStepIndex;
    public float CurrentValue => currentValue;
    public bool IsComplete => currentStepIndex >= definition.Steps.Count;
    public TutorialStepDefinition CurrentStep => IsComplete
        ? null
        : definition.Steps[currentStepIndex];
    public TutorialProgressSnapshot Snapshot => BuildSnapshot();

    public bool ReportEvidence(
        TutorialEvidenceType evidenceType,
        float amount = 1f,
        string evidenceKey = null)
    {
        if (IsComplete || amount <= 0f ||
            CurrentStep.EvidenceType != evidenceType)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(evidenceKey) &&
            !acceptedEvidenceKeys.Add(evidenceKey))
        {
            return false;
        }

        currentValue = Math.Min(
            CurrentStep.TargetValue,
            currentValue + amount);
        ProgressChanged?.Invoke(BuildSnapshot());
        if (currentValue < CurrentStep.TargetValue)
        {
            return true;
        }

        TutorialStepDefinition completedStep = CurrentStep;
        lastCompletedTitle = completedStep.Title;
        StepCompleted?.Invoke(BuildSnapshot());

        currentStepIndex++;
        currentValue = 0f;
        acceptedEvidenceKeys.Clear();
        TutorialProgressSnapshot next = BuildSnapshot();
        if (IsComplete)
        {
            SequenceCompleted?.Invoke(next);
        }
        else
        {
            ProgressChanged?.Invoke(next);
        }

        return true;
    }

    public void Reset(bool notify = true)
    {
        currentStepIndex = 0;
        currentValue = 0f;
        lastCompletedTitle = string.Empty;
        acceptedEvidenceKeys.Clear();
        if (notify)
        {
            ProgressChanged?.Invoke(BuildSnapshot());
        }
    }

    private TutorialProgressSnapshot BuildSnapshot()
    {
        return new TutorialProgressSnapshot(
            definition.StableId,
            definition.Steps.Count,
            currentStepIndex,
            CurrentStep,
            currentValue,
            IsComplete,
            lastCompletedTitle);
    }
}
