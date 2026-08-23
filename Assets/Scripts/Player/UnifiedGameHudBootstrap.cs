using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class UnifiedGameHudBootstrap : MonoBehaviour
{
    public UnifiedGameHud Hud { get; private set; }
    public bool IsInitialized { get; private set; }

    private GameObject playerRoot;

    public void Configure(GameObject targetPlayer, UnifiedGameHud sceneHud)
    {
        playerRoot = targetPlayer;
        Hud = sceneHud;
    }

    private IEnumerator Start()
    {
        yield return null;

        if (playerRoot == null)
        {
            Debug.LogError(
                $"[{nameof(UnifiedGameHudBootstrap)}] Missing player root.",
                this);
            enabled = false;
            yield break;
        }

        if (Hud == null)
        {
            Hud = CreateHud();
        }

        Hud.Bind(playerRoot);
        EnsureEventSystem(
            playerRoot.GetComponent<PlayerInputReader>()?.ActionsAsset);
        IsInitialized = true;
    }

    private static UnifiedGameHud CreateHud()
    {
        GameObject canvasObject = new GameObject(
            "GameUICanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(UnifiedGameHud));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvasObject.GetComponent<UnifiedGameHud>();
    }

    private static void EnsureEventSystem(InputActionAsset actionsAsset)
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystemObject.transform.SetAsLastSibling();
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        InputSystemUIInputModule inputModule =
            eventSystem.GetComponent<InputSystemUIInputModule>();
        inputModule ??=
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

        if (actionsAsset == null)
        {
            Debug.LogError(
                "Unified HUD could not bind the project UI input actions.");
            return;
        }

        RuntimeUiActionsOwner owner =
            eventSystem.GetComponent<RuntimeUiActionsOwner>();
        owner ??= eventSystem.gameObject.AddComponent<RuntimeUiActionsOwner>();
        InputActionAsset isolatedUiActions = owner.Configure(actionsAsset);
        InputActionMap ui = isolatedUiActions.FindActionMap("UI", true);
        inputModule.actionsAsset = isolatedUiActions;
        owner.ResetReferences();
        inputModule.move = owner.CreateReference(ui, "Navigate");
        inputModule.submit = owner.CreateReference(ui, "Submit");
        inputModule.cancel = owner.CreateReference(ui, "Cancel");
        inputModule.point = owner.CreateReference(ui, "Point");
        inputModule.leftClick = owner.CreateReference(ui, "Click");
        inputModule.rightClick = owner.CreateReference(ui, "RightClick");
        inputModule.middleClick = owner.CreateReference(ui, "MiddleClick");
        inputModule.scrollWheel = owner.CreateReference(ui, "ScrollWheel");
        inputModule.trackedDevicePosition = owner.CreateReference(
            ui,
            "TrackedDevicePosition");
        inputModule.trackedDeviceOrientation = owner.CreateReference(
            ui,
            "TrackedDeviceOrientation");
    }
}

public sealed class RuntimeUiActionsOwner : MonoBehaviour
{
    private readonly List<InputActionReference> runtimeReferences = new();
    private InputActionAsset source;
    private InputActionAsset runtimeCopy;

    public InputActionAsset Configure(InputActionAsset configuredSource)
    {
        if (configuredSource == null)
        {
            return null;
        }

        if (runtimeCopy != null && source == configuredSource)
        {
            return runtimeCopy;
        }

        if (runtimeCopy != null)
        {
            Destroy(runtimeCopy);
        }

        source = configuredSource;
        runtimeCopy = Instantiate(configuredSource);
        runtimeCopy.name = $"{configuredSource.name} (UI Runtime Copy)";
        return runtimeCopy;
    }

    public InputActionReference CreateReference(
        InputActionMap map,
        string actionName)
    {
        InputActionReference reference = InputActionReference.Create(
            map.FindAction(actionName, true));
        runtimeReferences.Add(reference);
        return reference;
    }

    public void ResetReferences()
    {
        for (int index = 0; index < runtimeReferences.Count; index++)
        {
            if (runtimeReferences[index] != null)
            {
                Destroy(runtimeReferences[index]);
            }
        }

        runtimeReferences.Clear();
    }

    private void OnDestroy()
    {
        ResetReferences();

        if (runtimeCopy != null)
        {
            Destroy(runtimeCopy);
        }
    }
}
