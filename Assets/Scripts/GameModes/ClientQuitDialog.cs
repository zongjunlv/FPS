using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Client-wide exit confirmation, independent of gameplay pause state.</summary>
[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class ClientQuitDialog : MonoBehaviour
{
    private GameObject modal;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text status;
    private GameObject previousSelection;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private int previousModeEpoch;
    private Func<Task> cleanupOverride;
    private Action terminateOverride;
    private float shutdownTimeoutSeconds = 5f;

    public static ClientQuitDialog Instance { get; private set; }
    public static bool IsBlockingInput => CoopUiInputGate.QuitConfirmationVisible;
    public static bool EscapeHandledThisFrame =>
        CoopUiInputGate.QuitConfirmationTransitionFrame == Time.frameCount;
    public bool IsVisible => modal != null && modal.activeSelf;
    public bool IsQuitting { get; private set; }
    public Button CancelButton { get; private set; }
    public Button ConfirmButton { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        CoopUiInputGate.QuitConfirmationTransitionFrame = -1;
        CoopUiInputGate.QuitConfirmationVisible = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if !UNITY_SERVER
        if (!Application.isBatchMode) Ensure();
#endif
    }

    public static ClientQuitDialog Ensure()
    {
        if (Instance != null) return Instance;
        GameObject root = ModeUiFactory.CreateCanvas("Client Quit Dialog", 32000);
        return root.AddComponent<ClientQuitDialog>();
    }

    public static void RequestQuitConfirmation() => Ensure().Open();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        // Also cover cursor writes from later scripts and asynchronous scene activation.
        Canvas.willRenderCanvases += KeepCursorAvailable;
    }

    private void Update()
    {
        KeepCursorAvailable();
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            HandleEscape();
    }

    private void LateUpdate() => KeepCursorAvailable();

    public bool HandleEscape()
    {
        if (EscapeHandledThisFrame) return true;
        if (IsVisible)
        {
            CoopUiInputGate.QuitConfirmationTransitionFrame = Time.frameCount;
            Cancel();
            return true;
        }

        GameModeStage stage = GameModeContext.IsTransitioning
            ? GameModeContext.RequestedStage : GameModeContext.CurrentStage;
        bool gameplay = stage == GameModeStage.Tutorial ||
                        stage == GameModeStage.Battle || stage == GameModeStage.CoopBattle;
        // A naked CityNew scene may contain a player before a mode marker activates.
        if (!GameModeContext.IsTransitioning &&
            FindFirstObjectByType<PlayerController>() != null &&
            (gameplay || GameModeContext.CurrentMode == GameModeId.None))
            return false;
        Open();
        return true;
    }

    private void Open()
    {
        if (IsVisible || IsQuitting) return;
        previousSelection = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        previousModeEpoch = GameModeContext.Epoch;
        CoopUiInputGate.QuitConfirmationTransitionFrame = Time.frameCount;
        CoopUiInputGate.QuitConfirmationVisible = true;
        modal.SetActive(true);
        status.text = "退出将关闭客户端，未保存的单人进度不会自动保存。\n联机时会先尝试离开当前房间。";
        ModeUiFactory.EnsureEventSystem();
        EventSystem.current?.SetSelectedGameObject(CancelButton.gameObject);
        KeepCursorAvailable();
    }

    public void Cancel()
    {
        if (!IsVisible || IsQuitting) return;
        CoopUiInputGate.QuitConfirmationTransitionFrame = Time.frameCount;
        modal.SetActive(false);
        CoopUiInputGate.QuitConfirmationVisible = false;
        if (EventSystem.current != null)
        {
            GameObject selection = previousSelection != null &&
                previousSelection.activeInHierarchy ? previousSelection : null;
            EventSystem.current.SetSelectedGameObject(selection);
            TMP_InputField input = selection != null
                ? selection.GetComponent<TMP_InputField>() : null;
            if (input != null && input.interactable) input.ActivateInputField();
        }
        if (previousModeEpoch == GameModeContext.Epoch)
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
        else
        {
            PlayerController player = FindFirstObjectByType<PlayerController>();
            bool gameplay = player != null && player.GameplayInputEnabled &&
                            !player.IsPaused && !CoopUiInputGate.GameplayInputSuppressed;
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplay;
        }
        previousSelection = null;
    }

    public void Confirm()
    {
        if (!IsVisible || IsQuitting) return;
        IsQuitting = true;
        CancelButton.interactable = false;
        ConfirmButton.interactable = false;
        status.text = "正在退出客户端…";
        StartCoroutine(ExitRoutine());
    }

    private IEnumerator ExitRoutine()
    {
        Task cleanup;
        try { cleanup = cleanupOverride != null ? cleanupOverride() : LeaveSessionsAsync(); }
        catch (Exception exception)
        {
            Debug.LogWarning($"退出时房间清理未完成：{exception.Message}");
            cleanup = Task.CompletedTask;
        }
        // Solo pause has timeScale == 0. Network failure must not trap the user.
        double deadline = Time.realtimeSinceStartupAsDouble + shutdownTimeoutSeconds;
        while (cleanup != null && !cleanup.IsCompleted &&
               Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        if (cleanup != null)
            _ = cleanup.ContinueWith(task => { _ = task.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        if (cleanupOverride == null)
        {
            foreach (NetworkManager manager in FindObjectsByType<NetworkManager>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                try { if (manager.IsListening) manager.Shutdown(true); }
                catch (Exception exception)
                {
                    Debug.LogWarning($"退出时连接清理未完成：{exception.Message}");
                }
            }
        }
        if (terminateOverride != null) terminateOverride();
        else TerminateClient();
    }

    private static Task LeaveSessionsAsync()
    {
        var tasks = new List<Task>();
        foreach (CoopSessionController session in FindObjectsByType<CoopSessionController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            tasks.Add(session.ShutdownForModeExitAsync());
        return Task.WhenAll(tasks);
    }

    private static void TerminateClient()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void KeepCursorAvailable()
    {
        if (!IsVisible) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        EventSystem events = EventSystem.current;
        GameObject selection = events != null ? events.currentSelectedGameObject : null;
        if (!IsQuitting && events != null && (selection == null ||
            !selection.transform.IsChildOf(modal.transform)))
        {
            if (selection != null && (previousSelection == null ||
                !previousSelection.activeInHierarchy)) previousSelection = selection;
            events.SetSelectedGameObject(CancelButton.gameObject);
        }
    }

    private void Build()
    {
        font = ModeUiFactory.CreateChineseFont(out ownsFont);
        RectTransform shade = ModeUiFactory.CreateRect("Exit Confirmation Overlay", transform);
        ModeUiFactory.Stretch(shade);
        modal = shade.gameObject;
        Image dim = shade.gameObject.AddComponent<Image>();
        dim.color = new Color(0.005f, 0.01f, 0.018f, 0.82f);
        dim.raycastTarget = true;
        RectTransform panel = ModeUiFactory.CreateRect("Exit Confirmation Panel", shade);
        SetCentered(panel, new Vector2(660f, 380f), Vector2.zero);
        Image surface = panel.gameObject.AddComponent<Image>();
        surface.color = TacticalUiTheme.Surface;
        surface.raycastTarget = true;
        TacticalUiTheme.ApplyMenuArt(surface, "nescia-panel-wide", Color.white);
        TacticalUiTheme.AddSurfaceChrome(panel, TacticalUiTheme.Cyan);
        TMP_Text title = ModeUiFactory.CreateText("Exit Title", panel,
            "退出客户端？", 36f, TextAlignmentOptions.Center, font, TacticalUiTheme.TextPrimary);
        title.fontStyle = FontStyles.Bold;
        SetCentered(title.rectTransform, new Vector2(580f, 56f), new Vector2(0f, 112f));
        status = ModeUiFactory.CreateText("Exit Status", panel, string.Empty, 21f,
            TextAlignmentOptions.Center, font, TacticalUiTheme.TextSecondary);
        status.enableWordWrapping = true;
        SetCentered(status.rectTransform, new Vector2(570f, 110f), new Vector2(0f, 25f));
        CancelButton = CreateButton(panel, "取消", -146f, TacticalUiTheme.SurfaceRaised, Cancel);
        ConfirmButton = CreateButton(panel, "确认退出", 146f, new Color(0.43f, 0.15f, 0.15f, 1f), Confirm);
        CancelButton.navigation = new Navigation { mode = Navigation.Mode.Explicit,
            selectOnLeft = ConfirmButton, selectOnRight = ConfirmButton };
        ConfirmButton.navigation = new Navigation { mode = Navigation.Mode.Explicit,
            selectOnLeft = CancelButton, selectOnRight = CancelButton };
        TMP_Text hint = ModeUiFactory.CreateText("Exit Hint", panel,
            "ESC 取消  ·  方向键选择  ·  Enter 确认", 17f,
            TextAlignmentOptions.Center, font, TacticalUiTheme.TextSecondary);
        SetCentered(hint.rectTransform, new Vector2(570f, 30f), new Vector2(0f, -146f));
        modal.SetActive(false);
    }

    private Button CreateButton(Transform parent, string label, float x, Color color, Action callback)
    {
        RectTransform rect = ModeUiFactory.CreateRect(label, parent);
        SetCentered(rect, new Vector2(260f, 58f), new Vector2(x, -87f));
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => callback());
        TMP_Text text = ModeUiFactory.CreateText("Label", rect, label, 23f,
            TextAlignmentOptions.Center, font, TacticalUiTheme.TextPrimary);
        ModeUiFactory.Stretch(text.rectTransform, 12f, 4f);
        return button;
    }

    private static void SetCentered(RectTransform rect, Vector2 size, Vector2 position) =>
        ModeUiFactory.SetRect(rect, Vector2.one * 0.5f, Vector2.one * 0.5f,
            Vector2.one * 0.5f, size, position);

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= KeepCursorAvailable;
        if (Instance == this)
        {
            Instance = null;
            CoopUiInputGate.QuitConfirmationVisible = false;
        }
        if (ownsFont && font != null) Destroy(font);
    }

#if UNITY_EDITOR
    public void ConfigureExitForTests(Func<Task> cleanup, Action terminate, float timeoutSeconds = 5f)
    {
        cleanupOverride = cleanup;
        terminateOverride = terminate ?? throw new ArgumentNullException(nameof(terminate));
        shutdownTimeoutSeconds = Mathf.Max(0.01f, timeoutSeconds);
    }

    public static void ResetForTests()
    {
        if (Instance != null) DestroyImmediate(Instance.gameObject);
        ResetStatics();
    }
#endif
}
