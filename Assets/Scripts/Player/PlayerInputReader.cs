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
    private bool aimingPressed;
    private bool reloadPressed;

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
    }

    private void OnEnable()
    {
        aimingAction.action.performed += OnAimingPerformed;
        reloadAction.performed += OnReloadPerformed;
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
    }

    private void OnDisable()
    {
        aimingAction.action.performed -= OnAimingPerformed;
        reloadAction.performed -= OnReloadPerformed;
        aimingPressed = false;
        reloadPressed = false;
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
    }

    private void OnAimingPerformed(InputAction.CallbackContext context)
    {
        aimingPressed = true;
    }

    private void OnReloadPerformed(InputAction.CallbackContext context)
    {
        reloadPressed = true;
    }
}
