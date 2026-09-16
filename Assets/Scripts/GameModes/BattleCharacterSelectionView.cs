using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Core.GameModes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class BattleCharacterSelectionView : MonoBehaviour
{
    private readonly List<UnityEngine.UI.Button> characterButtons = new();
    private readonly List<GameObject> characterInstances = new();
    private readonly List<Transform> characterSlots = new();
    private readonly List<UnityEngine.UI.Image> buttonImages = new();

    private GameModeFlowController flow;
    private PlayerAppearanceCatalog catalog;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text selectedName;
    private TMP_Text statusText;
    private int selectedIndex;

    public IReadOnlyList<UnityEngine.UI.Button> CharacterButtons => characterButtons;
    public IReadOnlyList<GameObject> CharacterInstances => characterInstances;
    public UnityEngine.UI.Button ConfirmButton { get; private set; }
    public UnityEngine.UI.Button ReturnButton { get; private set; }
    public int SelectedIndex => selectedIndex;
    public string SelectedAppearanceId => catalog != null &&
                                          selectedIndex >= 0 &&
                                          selectedIndex < catalog.Definitions.Count
        ? catalog.Definitions[selectedIndex].StableId
        : string.Empty;
    public string VisibleStatus => statusText != null ? statusText.text : string.Empty;
    public string SelectedDisplayName => selectedName != null
        ? selectedName.text
        : string.Empty;

    public static BattleCharacterSelectionView Create(
        GameModeFlowController configuredFlow,
        PlayerAppearanceCatalog appearanceCatalog)
    {
        var root = new GameObject("Battle Character Selection");
        BattleCharacterSelectionView view =
            root.AddComponent<BattleCharacterSelectionView>();
        view.Build(configuredFlow, appearanceCatalog);
        return view;
    }

    private void Build(GameModeFlowController configuredFlow,
        PlayerAppearanceCatalog appearanceCatalog)
    {
        flow = configuredFlow;
        catalog = appearanceCatalog;
        font = ModeUiFactory.CreateChineseFont(out ownsFont);

        string error = "角色资源目录不存在。";
        if (catalog == null || !catalog.TryValidate(out error))
        {
            BuildFailureUi(string.IsNullOrWhiteSpace(error)
                ? "角色资源目录加载失败，请返回模式入口。"
                : $"角色资源无效：{error}");
            return;
        }

        ConfigurePreviewCamera();
        BuildCharacters();
        BuildOverlay();
        PlayerAppearanceDefinition remembered =
            PlayerAppearanceSelection.LoadOrDefault(catalog, out _);
        int rememberedIndex = catalog.Definitions
            .Select((definition, index) => (definition, index))
            .Where(value => value.definition == remembered)
            .Select(value => value.index)
            .DefaultIfEmpty(0)
            .First();
        SelectCharacter(rememberedIndex);
        SetStatus(PlayerAppearanceSelection.LastWarning, false);
    }

    private void ConfigurePreviewCamera()
    {
        Camera camera = Camera.main ?? FindFirstObjectByType<Camera>();
        if (camera == null)
        {
            camera = new GameObject("Character Selection Camera", typeof(Camera))
                .GetComponent<Camera>();
            camera.tag = "MainCamera";
        }
        camera.orthographic = false;
        camera.fieldOfView = 42f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.018f, 0.028f, 0.045f);
        camera.transform.position = new Vector3(0f, 1.15f, 6.1f);
        camera.transform.LookAt(new Vector3(0f, 0.92f, 0f));

        CreateLight("Character Key Light", Quaternion.Euler(42f, 155f, 0f),
            1.25f, new Color(1f, 0.9f, 0.78f));
        CreateLight("Character Fill Light", Quaternion.Euler(28f, -50f, 0f),
            0.75f, new Color(0.42f, 0.64f, 1f));
    }

    private void BuildCharacters()
    {
        var stage = new GameObject("Character Stage");
        float spacing = 1.65f;
        float origin = -spacing * (catalog.Definitions.Count - 1) * 0.5f;
        for (int index = 0; index < catalog.Definitions.Count; index++)
        {
            var slotObject = new GameObject($"Character Slot {index + 1}");
            slotObject.transform.SetParent(stage.transform, false);
            slotObject.transform.localPosition = new Vector3(
                origin + spacing * index, 0f, 0f);
            PlayerAppearanceDefinition definition = catalog.Definitions[index];
            try
            {
                GameObject instance = PlayerAppearanceFactory.Create(catalog,
                    definition.StableId, slotObject.transform, out _, out _);
                characterSlots.Add(slotObject.transform);
                characterInstances.Add(instance);
            }
            catch (Exception exception)
            {
                Debug.LogError($"角色预览加载失败：{definition.StableId}，" +
                               exception.Message, this);
                GameObject fallback = PlayerAppearanceFactory.Create(catalog,
                    catalog.DefaultAppearanceId, slotObject.transform,
                    out _, out _);
                characterSlots.Add(slotObject.transform);
                characterInstances.Add(fallback);
            }
        }
    }

    private void BuildOverlay()
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Character Selection Canvas", 220);
        canvasObject.transform.SetParent(transform, false);
        RectTransform canvas = (RectTransform)canvasObject.transform;

        UnityEngine.UI.Image top = ModeUiFactory.CreateImage("Top Panel", canvas,
            new Color(0.015f, 0.028f, 0.043f, 0.93f), false);
        ModeUiFactory.SetRect(top.rectTransform, new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 158f), Vector2.zero);
        TMP_Text title = ModeUiFactory.CreateText("Title", top.transform,
            "选择行动角色", 42f, TextAlignmentOptions.Center, font, Color.white);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(title.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(800f, 58f), new Vector2(0f, -24f));
        selectedName = ModeUiFactory.CreateText("Selected Name", top.transform,
            string.Empty, 22f, TextAlignmentOptions.Center, font,
            new Color(0.31f, 0.92f, 0.85f));
        ModeUiFactory.SetRect(selectedName.rectTransform, new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(700f, 42f), new Vector2(0f, 24f));

        UnityEngine.UI.Image bottom = ModeUiFactory.CreateImage("Bottom Panel", canvas,
            new Color(0.015f, 0.028f, 0.043f, 0.94f), false);
        ModeUiFactory.SetRect(bottom.rectTransform, Vector2.zero,
            new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 270f), Vector2.zero);

        RectTransform cards = ModeUiFactory.CreateRect("Character Cards", bottom.transform);
        ModeUiFactory.SetRect(cards, new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(1080f, 78f), new Vector2(0f, -18f));
        UnityEngine.UI.HorizontalLayoutGroup layout =
            cards.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = true;

        for (int index = 0; index < catalog.Definitions.Count; index++)
        {
            int captured = index;
            UnityEngine.UI.Button button = CreateButton(cards,
                catalog.Definitions[index].DisplayName, 21f,
                () => SelectCharacter(captured));
            characterButtons.Add(button);
            buttonImages.Add(button.GetComponent<UnityEngine.UI.Image>());
        }

        ConfirmButton = CreateButton(bottom.transform, "确认角色并开始战斗", 22f,
            ConfirmSelection);
        ModeUiFactory.SetRect((RectTransform)ConfirmButton.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(380f, 58f),
            new Vector2(210f, 67f));
        ReturnButton = CreateButton(bottom.transform, "返回模式选择", 20f,
            ReturnToModes);
        ModeUiFactory.SetRect((RectTransform)ReturnButton.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(300f, 58f),
            new Vector2(-180f, 67f));

        statusText = ModeUiFactory.CreateText("Status", bottom.transform,
            string.Empty, 16f, TextAlignmentOptions.Center, font,
            new Color(0.62f, 0.74f, 0.78f));
        ModeUiFactory.SetRect(statusText.rectTransform, new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(1200f, 30f), new Vector2(0f, 18f));

        TMP_Text help = ModeUiFactory.CreateText("Help", bottom.transform,
            "A / D 或方向键切换    拖动鼠标旋转当前角色    Enter 确认    Esc 返回",
            16f, TextAlignmentOptions.Center, font,
            new Color(0.48f, 0.6f, 0.64f));
        ModeUiFactory.SetRect(help.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(1200f, 30f), new Vector2(0f, -104f));

        ModeUiFactory.EnsureEventSystem();
        EventSystem.current.SetSelectedGameObject(ConfirmButton.gameObject);
        flow.StateChanged += RefreshLoadingState;
        RefreshLoadingState();
    }

    private void BuildFailureUi(string message)
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Character Selection Failure", 220);
        canvasObject.transform.SetParent(transform, false);
        RectTransform canvas = (RectTransform)canvasObject.transform;
        ModeUiFactory.CreateImage("Background", canvas,
            new Color(0.04f, 0.02f, 0.025f, 1f), true);
        statusText = ModeUiFactory.CreateText("Failure", canvas, message, 25f,
            TextAlignmentOptions.Center, font, new Color(1f, 0.48f, 0.4f));
        ModeUiFactory.SetRect(statusText.rectTransform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(980f, 120f), new Vector2(0f, 60f));
        ReturnButton = CreateButton(canvas, "返回模式选择", 21f, ReturnToModes);
        ModeUiFactory.SetRect((RectTransform)ReturnButton.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(320f, 60f),
            new Vector2(0f, -70f));
        ModeUiFactory.EnsureEventSystem();
        EventSystem.current.SetSelectedGameObject(ReturnButton.gameObject);
    }

    private UnityEngine.UI.Button CreateButton(Transform parent, string label,
        float fontSize, UnityEngine.Events.UnityAction action)
    {
        GameObject gameObject = new(label, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        gameObject.transform.SetParent(parent, false);
        UnityEngine.UI.Image image = gameObject.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.045f, 0.11f, 0.14f, 0.98f);
        UnityEngine.UI.Button button = gameObject.GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(action);
        TMP_Text text = ModeUiFactory.CreateText("Label", gameObject.transform,
            label, fontSize, TextAlignmentOptions.Center, font, Color.white);
        ModeUiFactory.Stretch(text.rectTransform, 10f, 4f);
        return button;
    }

    private static void CreateLight(string name, Quaternion rotation,
        float intensity, Color color)
    {
        Light light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = rotation;
        light.intensity = intensity;
        light.color = color;
    }

    private void Update()
    {
        if (catalog == null || flow == null || flow.IsLoading) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame ||
                keyboard.aKey.wasPressedThisFrame) PreviousCharacter();
            if (keyboard.rightArrowKey.wasPressedThisFrame ||
                keyboard.dKey.wasPressedThisFrame) NextCharacter();
            if (keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame) ConfirmSelection();
            if (keyboard.escapeKey.wasPressedThisFrame) ReturnToModes();
        }
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed &&
            selectedIndex >= 0 && selectedIndex < characterSlots.Count)
        {
            float yaw = -mouse.delta.ReadValue().x * 0.18f;
            if (Mathf.Abs(yaw) > 0.001f)
                characterSlots[selectedIndex].Rotate(Vector3.up, yaw, Space.Self);
        }
    }

    public void PreviousCharacter()
    {
        if (catalog == null || catalog.Definitions.Count == 0) return;
        SelectCharacter((selectedIndex - 1 + catalog.Definitions.Count) %
                        catalog.Definitions.Count);
    }

    public void NextCharacter()
    {
        if (catalog == null || catalog.Definitions.Count == 0) return;
        SelectCharacter((selectedIndex + 1) % catalog.Definitions.Count);
    }

    public void SelectCharacter(int index)
    {
        if (catalog == null || catalog.Definitions.Count == 0) return;
        selectedIndex = Mathf.Clamp(index, 0, catalog.Definitions.Count - 1);
        if (selectedName != null)
            selectedName.text = catalog.Definitions[selectedIndex].DisplayName;
        for (int current = 0; current < buttonImages.Count; current++)
        {
            buttonImages[current].color = current == selectedIndex
                ? new Color(0.055f, 0.36f, 0.34f, 1f)
                : new Color(0.045f, 0.11f, 0.14f, 0.98f);
            if (current < characterSlots.Count)
                characterSlots[current].localScale = current == selectedIndex
                    ? Vector3.one * 1.08f
                    : Vector3.one;
        }
    }

    public void ConfirmSelection()
    {
        if (flow == null || flow.IsLoading || catalog == null) return;
        if (!PlayerAppearanceSelection.TrySave(catalog, SelectedAppearanceId,
                out PlayerAppearanceDefinition definition, out string message))
        {
            SetStatus(message, true);
            return;
        }
        SetStatus(string.IsNullOrWhiteSpace(message)
            ? $"已选择 {definition.DisplayName}，正在进入 CityNew……"
            : message, false);
        if (!flow.TryEnterGameplay(GameModeId.SoloBattle))
            SetStatus(flow.FailureMessage, true);
    }

    public void ReturnToModes()
    {
        if (flow != null && !flow.TryReturnToEntry())
            SetStatus(flow.FailureMessage, true);
    }

    private void RefreshLoadingState()
    {
        bool interactable = flow != null && !flow.IsLoading;
        foreach (UnityEngine.UI.Button button in characterButtons)
            button.interactable = interactable;
        if (ConfirmButton != null) ConfirmButton.interactable = interactable;
        if (ReturnButton != null) ReturnButton.interactable = interactable;
        if (flow != null && !string.IsNullOrWhiteSpace(flow.FailureMessage))
            SetStatus(flow.FailureMessage, true);
    }

    private void SetStatus(string message, bool error)
    {
        if (statusText == null) return;
        statusText.text = string.IsNullOrWhiteSpace(message)
            ? "请选择角色后确认；三名角色仅改变外观，不改变战斗数值。"
            : message;
        statusText.color = error
            ? new Color(1f, 0.48f, 0.4f)
            : new Color(0.52f, 0.82f, 0.8f);
    }

    private void OnDestroy()
    {
        if (flow != null) flow.StateChanged -= RefreshLoadingState;
        if (ownsFont && font != null) Destroy(font);
    }
}
