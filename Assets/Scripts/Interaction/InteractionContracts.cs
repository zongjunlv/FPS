using UnityEngine;

public enum InteractionCancelReason
{
    Released,
    OutOfRange,
    LostFocus,
    Damaged,
    GameplayDisabled
}

public readonly struct InteractionView
{
    public InteractionView(
        string prompt,
        float progressNormalized,
        bool isAvailable,
        bool isCompleted)
    {
        Prompt = prompt;
        ProgressNormalized =
            Mathf.Clamp01(progressNormalized);
        IsAvailable = isAvailable;
        IsCompleted = isCompleted;
    }

    public string Prompt { get; }
    public float ProgressNormalized { get; }
    public bool IsAvailable { get; }
    public bool IsCompleted { get; }
}

public interface IInteractable
{
    InteractionView View { get; }
    bool TryBegin(GameObject actor);
    bool Advance(GameObject actor, float deltaTime);
    bool Cancel(
        GameObject actor,
        InteractionCancelReason reason);
}
