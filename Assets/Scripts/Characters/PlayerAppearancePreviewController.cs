using UnityEngine;

public sealed class PlayerAppearancePreviewController : MonoBehaviour
{
    [SerializeField] private PlayerAppearanceCatalog catalog;
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private int selectedIndex;
    [SerializeField] private float rotationSpeed = 140f;
    [SerializeField] private bool showRuntimePanel = true;
    [SerializeField] private GameObject previewInstance;

    private Vector3 previousPointer;

    public PlayerAppearanceCatalog Catalog => catalog;
    public Transform PreviewAnchor => previewAnchor;
    public int SelectedIndex => selectedIndex;
    public GameObject PreviewInstance => previewInstance;
    public PlayerAppearanceDefinition SelectedDefinition =>
        catalog != null && selectedIndex >= 0 &&
        selectedIndex < catalog.Definitions.Count
            ? catalog.Definitions[selectedIndex]
            : null;
    public string SelectedDisplayName => SelectedDefinition?.DisplayName ?? string.Empty;

    public void Configure(PlayerAppearanceCatalog appearanceCatalog,
        Transform anchor, bool showPanel = true)
    {
        catalog = appearanceCatalog;
        previewAnchor = anchor;
        showRuntimePanel = showPanel;
        selectedIndex = 0;
        RefreshAppearance();
    }

    private void Awake()
    {
        if (previewInstance == null) RefreshAppearance();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow))
            PreviousCharacter();
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow))
            NextCharacter();
        if (Input.GetMouseButtonDown(0)) previousPointer = Input.mousePosition;
        if (Input.GetMouseButton(0))
        {
            Vector3 current = Input.mousePosition;
            RotateBy((current.x - previousPointer.x) * rotationSpeed /
                     Mathf.Max(1f, Screen.width));
            previousPointer = current;
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
        RefreshAppearance();
    }

    public void RotateBy(float yawDegrees)
    {
        if (previewAnchor != null)
            previewAnchor.Rotate(Vector3.up, yawDegrees, Space.Self);
    }

    private void RefreshAppearance()
    {
        if (catalog == null || previewAnchor == null ||
            catalog.Definitions.Count == 0) return;
        selectedIndex = Mathf.Clamp(selectedIndex, 0,
            catalog.Definitions.Count - 1);
        PlayerAppearanceDefinition definition = catalog.Definitions[selectedIndex];
        previewInstance = PlayerAppearanceFactory.Create(catalog,
            definition.StableId, previewAnchor, out _, out _);
    }

    private void OnGUI()
    {
        if (!showRuntimePanel || catalog == null) return;
        GUILayout.BeginArea(new Rect(18f, 18f, 480f, 100f), GUI.skin.box);
        GUILayout.Label("角色外观预览（拖动鼠标旋转全身）");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ 上一个")) PreviousCharacter();
        GUILayout.Label(SelectedDisplayName, GUILayout.Width(220f));
        if (GUILayout.Button("下一个 ▶")) NextCharacter();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
