using System;

public enum MissionFlowState
{
    ActivateTerminal,
    EliminateTargets,
    ExtractionAvailable,
    Victory,
    Defeat
}

public sealed class MissionFlowStateMachine
{
    public event Action<MissionFlowState> StateChanged;

    public MissionFlowState State { get; private set; } =
        MissionFlowState.EliminateTargets;
    public bool TerminalCompleted { get; private set; }
    public int EliminatedTargets { get; private set; }
    public int RequiredTargets { get; private set; } = 1;
    public bool IsOutcome =>
        State == MissionFlowState.Victory ||
        State == MissionFlowState.Defeat;

    public void Configure(int requiredTargets)
    {
        RequiredTargets = Math.Max(1, requiredTargets);
        TerminalCompleted = false;
        EliminatedTargets = 0;
        State = MissionFlowState.EliminateTargets;
    }

    public bool CompleteTerminal()
    {
        if (TerminalCompleted || IsOutcome ||
            EliminatedTargets < RequiredTargets)
        {
            return false;
        }

        TerminalCompleted = true;
        EvaluateObjectives();
        return true;
    }

    public bool RegisterTargetEliminated()
    {
        if (EliminatedTargets >= RequiredTargets || IsOutcome)
        {
            return false;
        }

        EliminatedTargets++;
        EvaluateObjectives();
        return true;
    }

    public bool TryExtract()
    {
        if (State != MissionFlowState.ExtractionAvailable)
        {
            return false;
        }

        SetState(MissionFlowState.Victory);
        return true;
    }

    public bool Fail()
    {
        if (IsOutcome)
        {
            return false;
        }

        SetState(MissionFlowState.Defeat);
        return true;
    }

    private void EvaluateObjectives()
    {
        if (EliminatedTargets < RequiredTargets)
        {
            SetState(MissionFlowState.EliminateTargets);
            return;
        }

        if (!TerminalCompleted)
        {
            SetState(MissionFlowState.ActivateTerminal);
            return;
        }

        SetState(MissionFlowState.ExtractionAvailable);
    }

    private void SetState(MissionFlowState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(next);
    }
}
