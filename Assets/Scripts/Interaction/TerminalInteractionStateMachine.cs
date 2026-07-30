using UnityEngine;

public enum TerminalInteractionState
{
    Inactive,
    Interacting,
    Completed
}

public sealed class TerminalInteractionStateMachine
{
    private float holdDuration = 2.5f;
    private bool preserveProgressOnInterrupt;
    private float elapsed;

    public TerminalInteractionState State { get; private set; } =
        TerminalInteractionState.Inactive;
    public float ProgressNormalized =>
        State == TerminalInteractionState.Completed
            ? 1f
            : Mathf.Clamp01(elapsed / holdDuration);

    public void Configure(
        float configuredHoldDuration,
        bool configuredPreserveProgress)
    {
        holdDuration = Mathf.Max(0.05f, configuredHoldDuration);
        preserveProgressOnInterrupt =
            configuredPreserveProgress;
        elapsed = 0f;
        State = TerminalInteractionState.Inactive;
    }

    public bool TryBegin()
    {
        if (State == TerminalInteractionState.Completed)
        {
            return false;
        }

        State = TerminalInteractionState.Interacting;
        return true;
    }

    public bool Advance(float deltaTime)
    {
        if (State != TerminalInteractionState.Interacting)
        {
            return false;
        }

        elapsed = Mathf.Min(
            holdDuration,
            elapsed + Mathf.Max(0f, deltaTime));

        if (elapsed < holdDuration)
        {
            return false;
        }

        State = TerminalInteractionState.Completed;
        return true;
    }

    public bool Cancel()
    {
        if (State != TerminalInteractionState.Interacting)
        {
            return false;
        }

        State = TerminalInteractionState.Inactive;

        if (!preserveProgressOnInterrupt)
        {
            elapsed = 0f;
        }

        return true;
    }
}
