using NUnit.Framework;

public sealed class Issue51MissionRestoreTests
{
    [Test]
    public void MissionRestoreIsSilentAndPreservesValidatedPhase()
    {
        var flow = new MissionFlowStateMachine();
        int events = 0;
        flow.StateChanged += _ => events++;

        Assert.That(flow.TryRestoreSilently(
            new MissionFlowRestoreState(
                MissionFlowState.ActivateTerminal,
                1,
                1,
                false),
            out string error), Is.True, error);
        Assert.That(flow.State, Is.EqualTo(MissionFlowState.ActivateTerminal));
        Assert.That(flow.EliminatedTargets, Is.EqualTo(1));
        Assert.That(flow.TerminalCompleted, Is.False);
        Assert.That(events, Is.Zero, "读取存档不能重放任务阶段事件。");
    }

    [Test]
    public void InvalidMissionRestoreLeavesCurrentStateUntouched()
    {
        var flow = new MissionFlowStateMachine();
        flow.Configure(1);
        MissionFlowRestoreState before = flow.CaptureState();

        Assert.That(flow.TryRestoreSilently(
            new MissionFlowRestoreState(
                MissionFlowState.ExtractionAvailable,
                1,
                0,
                true),
            out _), Is.False);

        MissionFlowRestoreState after = flow.CaptureState();
        Assert.That(after.State, Is.EqualTo(before.State));
        Assert.That(after.EliminatedTargets, Is.EqualTo(before.EliminatedTargets));
        Assert.That(after.TerminalCompleted, Is.EqualTo(before.TerminalCompleted));
    }

    [Test]
    public void TerminalRestoreDoesNotPublishCompletion()
    {
        var terminal = new TerminalInteractionStateMachine();
        terminal.Configure(2.5f, true);

        Assert.That(terminal.TryRestoreSilently(false, 0.4f), Is.True);
        Assert.That(terminal.State, Is.EqualTo(TerminalInteractionState.Inactive));
        Assert.That(terminal.ProgressNormalized, Is.EqualTo(0.4f).Within(0.001f));

        Assert.That(terminal.TryRestoreSilently(true, 1f), Is.True);
        Assert.That(terminal.State, Is.EqualTo(TerminalInteractionState.Completed));
        Assert.That(terminal.ProgressNormalized, Is.EqualTo(1f));
    }
}
