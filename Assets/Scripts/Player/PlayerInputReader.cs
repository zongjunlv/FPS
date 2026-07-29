using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputReader : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference sprintAction;
    [SerializeField] private InputActionReference interactAction;
    [SerializeField] private InputActionReference attackAction;
    [SerializeField] private InputActionReference aimingAction;

    private InputAction crouchAction;
    private InputAction pauseAction;
    private InputAction reloadAction;
    private InputAction weaponSlot1Action;
    private InputAction weaponSlot2Action;
    private InputAction cycleWeaponAction;
    private bool aimingPressed;
    private bool reloadPressed;
    private int weaponSelection = -1;
    private int weaponCycleDirection;

    // 其他脚本只能读取输入结果，不需要直接管理 Input Action。
    public Vector2 Move => moveAction.action.ReadValue<Vector2>();
    public bool JumpPressed => jumpAction.action.WasPressedThisFrame();
    public Vector2 Look => lookAction.action.ReadValue<Vector2>();
    public bool SprintHeld => sprintAction.action.IsPressed();
    public bool InteractPressed => interactAction.action.WasPressedThisFrame();
    public bool AttackPressed => attackAction.action.WasPressedThisFrame();
    public bool AttackHeld => attackAction.action.IsPressed();
    public bool AimingPressed => aimingPressed;
    public bool AimingHeld => aimingAction.action.IsPressed();
    public bool CrouchPressed =>
        crouchAction != null && crouchAction.WasPressedThisFrame();
    public bool PausePressed =>
        pauseAction != null && pauseAction.WasPressedThisFrame();
    public bool ReloadPressed => reloadPressed;
    public int WeaponSelection => weaponSelection;
    public int WeaponCycleDirection => weaponCycleDirection;
    public bool LookUsesPointerDelta =>
        lookAction.action.activeControl?.device is Pointer;

    public bool ConsumeAimingPressed()
    {
        bool wasPressed = aimingPressed;
        aimingPressed = false;
        return wasPressed;
    }

    public bool ConsumeReloadPressed()
    {
        bool wasPressed = reloadPressed;
        reloadPressed = false;
        return wasPressed;
    }

    public int ConsumeWeaponSelection()
    {
        int requestedSlot = weaponSelection;
        weaponSelection = -1;
        return requestedSlot;
    }

    public int ConsumeWeaponCycleDirection()
    {
        int direction = weaponCycleDirection;
        weaponCycleDirection = 0;
        return direction;
    }

    private void Awake()
    {
        crouchAction = moveAction.action.actionMap.FindAction(
            "Crouch",
            true);
        pauseAction = moveAction.action.actionMap.FindAction(
            "Pause",
            true);
        reloadAction = moveAction.action.actionMap.FindAction(
            "Reload",
            true);
        weaponSlot1Action = moveAction.action.actionMap.FindAction(
            "WeaponSlot1",
            true);
        weaponSlot2Action = moveAction.action.actionMap.FindAction(
            "WeaponSlot2",
            true);
        cycleWeaponAction = moveAction.action.actionMap.FindAction(
            "CycleWeapon",
            true);
    }

    private void OnEnable()
    {
        aimingAction.action.performed += OnAimingPerformed;
        reloadAction.performed += OnReloadPerformed;
        weaponSlot1Action.performed += OnWeaponSlot1Performed;
        weaponSlot2Action.performed += OnWeaponSlot2Performed;
        cycleWeaponAction.performed += OnCycleWeaponPerformed;
        moveAction.action.Enable();
        jumpAction.action.Enable();
        lookAction.action.Enable();
        sprintAction.action.Enable();
        interactAction.action.Enable();
        attackAction.action.Enable();
        aimingAction.action.Enable();
        crouchAction?.Enable();
        pauseAction?.Enable();
        reloadAction?.Enable();
        weaponSlot1Action?.Enable();
        weaponSlot2Action?.Enable();
        cycleWeaponAction?.Enable();
    }

    private void OnDisable()
    {
        aimingAction.action.performed -= OnAimingPerformed;
        reloadAction.performed -= OnReloadPerformed;
        weaponSlot1Action.performed -= OnWeaponSlot1Performed;
        weaponSlot2Action.performed -= OnWeaponSlot2Performed;
        cycleWeaponAction.performed -= OnCycleWeaponPerformed;
        aimingPressed = false;
        reloadPressed = false;
        weaponSelection = -1;
        weaponCycleDirection = 0;
        moveAction.action.Disable();
        jumpAction.action.Disable();
        lookAction.action.Disable();
        sprintAction.action.Disable();
        interactAction.action.Disable();
        attackAction.action.Disable();
        aimingAction.action.Disable();
        crouchAction?.Disable();
        pauseAction?.Disable();
        reloadAction?.Disable();
        weaponSlot1Action?.Disable();
        weaponSlot2Action?.Disable();
        cycleWeaponAction?.Disable();
    }

    private void OnAimingPerformed(InputAction.CallbackContext context)
    {
        aimingPressed = true;
    }

    private void OnReloadPerformed(InputAction.CallbackContext context)
    {
        reloadPressed = true;
    }

    private void OnWeaponSlot1Performed(InputAction.CallbackContext context)
    {
        weaponSelection = 0;
    }

    private void OnWeaponSlot2Performed(InputAction.CallbackContext context)
    {
        weaponSelection = 1;
    }

    private void OnCycleWeaponPerformed(InputAction.CallbackContext context)
    {
        float scrollValue = context.ReadValue<float>();

        if (Mathf.Abs(scrollValue) > 0.01f)
        {
            weaponCycleDirection = scrollValue > 0f ? 1 : -1;
        }
    }
}
