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
    private InputAction inventoryAction;
    private InputAction reloadAction;
    private InputAction weaponSlot1Action;
    private InputAction weaponSlot2Action;
    private InputAction cycleWeaponAction;
    private InputAction quickUse1Action;
    private InputAction quickUse2Action;
    private bool aimingPressed;
    private bool reloadPressed;
    private int weaponSelection = -1;
    private int weaponCycleDirection;
    private int quickUseSelection = -1;

    // 其他脚本只能读取输入结果，不需要直接管理 Input Action。
    public Vector2 Move => moveAction.action.ReadValue<Vector2>();
    public bool JumpPressed => jumpAction.action.WasPressedThisFrame();
    public Vector2 Look => lookAction.action.ReadValue<Vector2>();
    public bool SprintHeld => sprintAction.action.IsPressed();
    public bool InteractPressed => interactAction.action.WasPressedThisFrame();
    public bool InteractHeld => interactAction.action.IsPressed();
    public bool InteractReleased =>
        interactAction.action.WasReleasedThisFrame();
    public bool AttackPressed => attackAction.action.WasPressedThisFrame();
    public bool AttackHeld => attackAction.action.IsPressed();
    public bool AimingPressed => aimingPressed;
    public bool AimingHeld => aimingAction.action.IsPressed();
    public bool CrouchPressed =>
        crouchAction != null && crouchAction.WasPressedThisFrame();
    public bool PausePressed =>
        pauseAction != null && pauseAction.WasPressedThisFrame();
    public bool InventoryPressed =>
        inventoryAction != null && inventoryAction.WasPressedThisFrame();
    public bool InventoryActionEnabled =>
        inventoryAction != null && inventoryAction.enabled;
    public bool ReloadPressed => reloadPressed;
    public int WeaponSelection => weaponSelection;
    public int WeaponCycleDirection => weaponCycleDirection;
    public InputActionAsset ActionsAsset => moveAction?.asset;
    public bool LookUsesPointerDelta =>
        lookAction.action.activeControl?.device is Pointer;

    public void SetGameplayActionsEnabled(bool enabled)
    {
        SetActionEnabled(moveAction?.action, enabled);
        SetActionEnabled(jumpAction?.action, enabled);
        SetActionEnabled(lookAction?.action, enabled);
        SetActionEnabled(sprintAction?.action, enabled);
        SetActionEnabled(interactAction?.action, enabled);
        SetActionEnabled(attackAction?.action, enabled);
        SetActionEnabled(aimingAction?.action, enabled);
        SetActionEnabled(crouchAction, enabled);
        SetActionEnabled(reloadAction, enabled);
        SetActionEnabled(weaponSlot1Action, enabled);
        SetActionEnabled(weaponSlot2Action, enabled);
        SetActionEnabled(cycleWeaponAction, enabled);
        SetActionEnabled(quickUse1Action, enabled);
        SetActionEnabled(quickUse2Action, enabled);

        if (!enabled)
        {
            ClearBufferedGameplayInput();
        }
    }

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

    public int ConsumeQuickUse()
    {
        int requestedSlot = quickUseSelection;
        quickUseSelection = -1;
        return requestedSlot;
    }

    private void Awake()
    {
        crouchAction = moveAction.action.actionMap.FindAction(
            "Crouch",
            true);
        pauseAction = moveAction.action.actionMap.FindAction(
            "Pause",
            true);
        inventoryAction = moveAction.action.actionMap.FindAction(
            "Inventory",
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
        quickUse1Action = moveAction.action.actionMap.FindAction(
            "QuickUse1",
            true);
        quickUse2Action = moveAction.action.actionMap.FindAction(
            "QuickUse2",
            true);
    }

    private void OnEnable()
    {
        aimingAction.action.performed += OnAimingPerformed;
        reloadAction.performed += OnReloadPerformed;
        weaponSlot1Action.performed += OnWeaponSlot1Performed;
        weaponSlot2Action.performed += OnWeaponSlot2Performed;
        cycleWeaponAction.performed += OnCycleWeaponPerformed;
        quickUse1Action.performed += OnQuickUse1Performed;
        quickUse2Action.performed += OnQuickUse2Performed;
        SetGameplayActionsEnabled(true);
        pauseAction?.Enable();
        inventoryAction?.Enable();
    }

    private void OnDisable()
    {
        aimingAction.action.performed -= OnAimingPerformed;
        reloadAction.performed -= OnReloadPerformed;
        weaponSlot1Action.performed -= OnWeaponSlot1Performed;
        weaponSlot2Action.performed -= OnWeaponSlot2Performed;
        cycleWeaponAction.performed -= OnCycleWeaponPerformed;
        quickUse1Action.performed -= OnQuickUse1Performed;
        quickUse2Action.performed -= OnQuickUse2Performed;
        ClearBufferedGameplayInput();
        moveAction.action.Disable();
        jumpAction.action.Disable();
        lookAction.action.Disable();
        sprintAction.action.Disable();
        interactAction.action.Disable();
        attackAction.action.Disable();
        aimingAction.action.Disable();
        crouchAction?.Disable();
        pauseAction?.Disable();
        inventoryAction?.Disable();
        reloadAction?.Disable();
        weaponSlot1Action?.Disable();
        weaponSlot2Action?.Disable();
        cycleWeaponAction?.Disable();
        quickUse1Action?.Disable();
        quickUse2Action?.Disable();
    }

    private void ClearBufferedGameplayInput()
    {
        aimingPressed = false;
        reloadPressed = false;
        weaponSelection = -1;
        weaponCycleDirection = 0;
        quickUseSelection = -1;
    }

    private static void SetActionEnabled(
        InputAction action,
        bool enabled)
    {
        if (action == null)
        {
            return;
        }

        if (enabled)
        {
            action.Enable();
        }
        else
        {
            action.Disable();
        }
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

    private void OnQuickUse1Performed(InputAction.CallbackContext context)
    {
        quickUseSelection = 0;
    }

    private void OnQuickUse2Performed(InputAction.CallbackContext context)
    {
        quickUseSelection = 1;
    }
}
