using System;

public enum MissionFlowState
{
    ActivateTerminal,
    EliminateTargets,
    ExtractionAvailable,
    Victory,
    Defeat
}

public readonly struct MissionFlowRestoreState
{
    public MissionFlowRestoreState(
        MissionFlowState state,
        int requiredTargets,
        int eliminatedTargets,
        bool terminalCompleted)
    {
        State = state;
        RequiredTargets = requiredTargets;
        EliminatedTargets = eliminatedTargets;
        TerminalCompleted = terminalCompleted;
    }

    public MissionFlowState State { get; }
    public int RequiredTargets { get; }
    public int EliminatedTargets { get; }
    public bool TerminalCompleted { get; }
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

    public MissionFlowRestoreState CaptureState()
    {
        return new MissionFlowRestoreState(
            State,
            RequiredTargets,
            EliminatedTargets,
            TerminalCompleted);
    }

    public bool TryRestoreSilently(
        MissionFlowRestoreState snapshot,
        out string error)
    {
        if (snapshot.RequiredTargets < 1 ||
            snapshot.EliminatedTargets < 0 ||
            snapshot.EliminatedTargets > snapshot.RequiredTargets)
        {
            error = "任务目标计数无效。";
            return false;
        }

        MissionFlowState expected = snapshot.EliminatedTargets <
                                    snapshot.RequiredTargets
            ? MissionFlowState.EliminateTargets
            : !snapshot.TerminalCompleted
                ? MissionFlowState.ActivateTerminal
                : MissionFlowState.ExtractionAvailable;

        if (snapshot.State != expected)
        {
            error = "任务阶段与目标完成状态不一致。";
            return false;
        }

        RequiredTargets = snapshot.RequiredTargets;
        EliminatedTargets = snapshot.EliminatedTargets;
        TerminalCompleted = snapshot.TerminalCompleted;
        State = snapshot.State;
        error = string.Empty;
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
