#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FPS.GameplayEffects;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(9999)]
public sealed class GameplayEffectRuntimeInspectorInput : MonoBehaviour
{
    private GameplayEffectRuntimeInspector inspector;

    public void Configure(GameplayEffectRuntimeInspector configuredInspector)
    {
        inspector = configuredInspector;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            inspector?.SetVisible(!inspector.IsVisible);
        }
    }
}

public static class GameplayEffectRuntimeInspectorBootstrap
{
    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (GameplayEffectRuntimeInspector.Instance != null ||
            Object.FindAnyObjectByType<GameplayEffectRuntimeInspector>() != null)
        {
            return;
        }

        var root = new GameObject("[DEV] Gameplay Effect Inspector");
        GameplayEffectRuntimeInspector inspector =
            root.AddComponent<GameplayEffectRuntimeInspector>();
        GameplayEffectRuntimeInspectorInput input =
            root.AddComponent<GameplayEffectRuntimeInspectorInput>();
        input.Configure(inspector);
    }
}
#endif
