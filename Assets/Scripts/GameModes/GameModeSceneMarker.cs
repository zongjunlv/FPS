using FPS.Core.GameModes;
using UnityEngine;

[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class GameModeSceneMarker : MonoBehaviour
{
    [SerializeField] private GameModeId mode;
    [SerializeField] private GameModeStage stage;

    public GameModeId Mode => mode;
    public GameModeStage Stage => stage;
    public bool IsActivated { get; private set; }
    public string ActivationError { get; private set; } = string.Empty;

    public void Configure(GameModeId configuredMode, GameModeStage configuredStage)
    {
        mode = configuredMode;
        stage = configuredStage;
    }

    private void Awake()
    {
        ActivateContext();
    }

    public bool ActivateContext()
    {
        IsActivated = GameModeContext.TryActivate(
            mode,
            stage,
            out string error);
        ActivationError = error;

        if (!IsActivated)
        {
            Debug.LogError(
                $"[{nameof(GameModeSceneMarker)}] {ActivationError}",
                this);
        }

        return IsActivated;
    }
}
