using UnityEngine;

public enum WeaponCalibrationView
{
    Front,
    Side,
    Aim
}

public sealed class WeaponCalibrationPreviewController : MonoBehaviour
{
    [SerializeField] private PlayerAppearanceCatalog appearances;
    [SerializeField] private ThirdPersonWeaponCatalog weapons;
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private int selectedCharacter;
    [SerializeField] private int selectedWeapon;
    [SerializeField] private WeaponCalibrationView selectedView;
    [SerializeField] private bool showRuntimePanel = true;

    private GameObject appearanceInstance;
    private ThirdPersonWeaponRig weaponInstance;

    public PlayerAppearanceCatalog Appearances => appearances;
    public ThirdPersonWeaponCatalog Weapons => weapons;
    public int SelectedCharacter => selectedCharacter;
    public int SelectedWeapon => selectedWeapon;
    public WeaponCalibrationView SelectedView => selectedView;
    public GameObject AppearanceInstance => appearanceInstance;
    public ThirdPersonWeaponRig WeaponInstance => weaponInstance;
    public Camera PreviewCamera => previewCamera;

    public void Configure(
        PlayerAppearanceCatalog appearanceCatalog,
        ThirdPersonWeaponCatalog weaponCatalog,
        Transform anchor,
        Camera camera,
        bool showPanel = true)
    {
        appearances = appearanceCatalog;
        weapons = weaponCatalog;
        previewAnchor = anchor;
        previewCamera = camera;
        showRuntimePanel = showPanel;
        selectedCharacter = 0;
        selectedWeapon = 0;
        selectedView = WeaponCalibrationView.Front;
        Refresh();
    }

    private void Awake()
    {
        Refresh();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q)) PreviousCharacter();
        if (Input.GetKeyDown(KeyCode.E)) NextCharacter();
        if (Input.GetKeyDown(KeyCode.Z)) PreviousWeapon();
        if (Input.GetKeyDown(KeyCode.X)) NextWeapon();
        if (Input.GetKeyDown(KeyCode.Alpha1))
            SetView(WeaponCalibrationView.Front);
        if (Input.GetKeyDown(KeyCode.Alpha2))
            SetView(WeaponCalibrationView.Side);
        if (Input.GetKeyDown(KeyCode.Alpha3))
            SetView(WeaponCalibrationView.Aim);
    }

    public void PreviousCharacter() => SelectCharacter(
        selectedCharacter - 1);
    public void NextCharacter() => SelectCharacter(selectedCharacter + 1);
    public void PreviousWeapon() => SelectWeapon(selectedWeapon - 1);
    public void NextWeapon() => SelectWeapon(selectedWeapon + 1);

    public void SelectCharacter(int index)
    {
        if (appearances == null || appearances.Definitions.Count == 0) return;
        selectedCharacter = Wrap(index, appearances.Definitions.Count);
        Refresh();
    }

    public void SelectWeapon(int index)
    {
        if (weapons == null || weapons.Definitions.Count == 0) return;
        selectedWeapon = Wrap(index, weapons.Definitions.Count);
        Refresh();
    }

    public void SetView(WeaponCalibrationView view)
    {
        selectedView = view;
        ApplyCameraView();
    }

    public void Refresh()
    {
        if (appearances == null || weapons == null || previewAnchor == null ||
            appearances.Definitions.Count == 0 ||
            weapons.Definitions.Count == 0)
        {
            return;
        }
        selectedCharacter = Wrap(selectedCharacter,
            appearances.Definitions.Count);
        selectedWeapon = Wrap(selectedWeapon, weapons.Definitions.Count);
        PlayerAppearanceDefinition appearance =
            appearances.Definitions[selectedCharacter];
        appearanceInstance = PlayerAppearanceFactory.Create(
            appearances, appearance.StableId, previewAnchor,
            out _, out _);
        Animator animator = appearanceInstance.GetComponent<Animator>();
        animator.Rebind();
        animator.SetBool(ThirdPersonAnimationParameters.Grounded, true);
        animator.SetBool(ThirdPersonAnimationParameters.Aiming, true);
        animator.Update(0.2f);
        weaponInstance = ThirdPersonWeaponFactory.Create(
            weapons.Definitions[selectedWeapon], animator, out _);
        ApplyCameraView();
    }

    private void ApplyCameraView()
    {
        if (previewCamera == null || previewAnchor == null) return;
        if (selectedView == WeaponCalibrationView.Aim &&
            weaponInstance != null && weaponInstance.AimPoint != null)
        {
            Transform aim = weaponInstance.AimPoint;
            Vector3 aimCameraPosition = aim.position - aim.forward * 0.42f +
                                        aim.up * 0.025f;
            Vector3 aimCameraTarget = aim.position + aim.forward * 4f;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.transform.SetPositionAndRotation(
                aimCameraPosition,
                Quaternion.LookRotation(
                    aimCameraTarget - aimCameraPosition, aim.up));
            return;
        }
        previewCamera.nearClipPlane = 0.1f;
        Vector3 target = previewAnchor.position + new Vector3(0f, 1.15f, 0f);
        Vector3 position = selectedView switch
        {
            WeaponCalibrationView.Side =>
                previewAnchor.position + new Vector3(2.8f, 1.2f, 0.15f),
            _ => previewAnchor.position + new Vector3(0f, 1.15f, 3.4f)
        };
        previewCamera.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(target - position, Vector3.up));
    }

    private static int Wrap(int value, int count)
    {
        if (count <= 0) return 0;
        return (value % count + count) % count;
    }

    private void OnGUI()
    {
        if (!showRuntimePanel || appearances == null || weapons == null ||
            appearances.Definitions.Count == 0 ||
            weapons.Definitions.Count == 0)
        {
            return;
        }
        GUILayout.BeginArea(new Rect(18f, 18f, 560f, 145f), GUI.skin.box);
        GUILayout.Label("第三人称武器校准 · Q/E 切角色 · Z/X 切武器");
        GUILayout.Label(
            $"角色：{appearances.Definitions[selectedCharacter].DisplayName}    " +
            $"武器：{weapons.Definitions[selectedWeapon].DisplayName}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1 正面")) SetView(WeaponCalibrationView.Front);
        if (GUILayout.Button("2 侧面")) SetView(WeaponCalibrationView.Side);
        if (GUILayout.Button("3 瞄准")) SetView(WeaponCalibrationView.Aim);
        GUILayout.EndHorizontal();
        GUILayout.Label($"当前视图：{selectedView}");
        GUILayout.EndArea();
    }
}
