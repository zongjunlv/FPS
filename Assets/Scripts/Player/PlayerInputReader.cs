using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-1000)]
public class PlayerInputReader : MonoBehaviour
{
    private const double ScrollGestureGap = 0.12d;
    private const double ScrollImpulseConfirmationWindow = 0.06d;
    private const float ScrollImpulseMinimum = 0.25f;
    private const float ScrollImpulseRise = 0.12f;
    private const float ScrollImpulseConfirmationRatio = 0.7f;
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
    private InputAction pickupAction;
    private InputAction reloadAction;
    private InputAction weaponSlot1Action;
    private InputAction weaponSlot2Action;
    private InputAction cycleWeaponAction;
    private InputAction quickUse1Action;
    private InputAction quickUse2Action;
    private bool aimingPressed;
    private bool reloadPressed;
    private int weaponSelection = -1;
    private readonly Queue<int> weaponCycleDirections = new();
    private double lastWeaponCycleEventTime = double.NegativeInfinity;
    private int lastWeaponCycleDirection;
    private float lastWeaponCycleMagnitude;
    private double pendingScrollImpulseTime;
    private int pendingScrollImpulseDirection;
    private float pendingScrollImpulseMagnitude;
    private int quickUseSelection = -1;

    public event Action<PlayerInputSample> InputSampled;

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
    public bool PickupPressed =>
        pickupAction != null && pickupAction.WasPressedThisFrame();
    public bool ReloadPressed => reloadPressed;
    public int WeaponSelection => weaponSelection;
    public int WeaponCycleDirection => weaponCycleDirections.Count > 0
        ? weaponCycleDirections.Peek()
        : 0;
    public InputActionAsset ActionsAsset => moveAction?.asset;
    public bool LookUsesPointerDelta =>
        lookAction.action.activeControl?.device is Pointer;

    public PlayerInputSample CaptureSample()
    {
        return new PlayerInputSample(
            Move,
            Look,
            LookUsesPointerDelta,
            JumpPressed,
            SprintHeld,
            InteractPressed,
            InteractHeld,
            InteractReleased,
            AttackPressed,
            AttackHeld,
            aimingPressed,
            AimingHeld,
            CrouchPressed,
            PausePressed,
            InventoryPressed,
            PickupPressed,
            reloadPressed,
            weaponSelection,
            WeaponCycleDirection,
            quickUseSelection);
    }

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
        SetActionEnabled(pickupAction, enabled);
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
        return weaponCycleDirections.Count > 0
            ? weaponCycleDirections.Dequeue()
            : 0;
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
        pickupAction = moveAction.action.actionMap.FindAction(
            "Pickup",
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

    private void Update()
    {
        InputSampled?.Invoke(CaptureSample());
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
        pickupAction?.Disable();
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
        weaponCycleDirections.Clear();
        lastWeaponCycleEventTime = double.NegativeInfinity;
        lastWeaponCycleDirection = 0;
        lastWeaponCycleMagnitude = 0f;
        ClearPendingScrollImpulse();
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
        BufferWeaponCycleDelta(context.ReadValue<float>(), context.time);
    }

    public void BufferWeaponCycleDelta(
        float scrollValue,
        double eventTime)
    {
        if (Mathf.Abs(scrollValue) <= 0.01f)
        {
            return;
        }

        int direction = scrollValue > 0f ? 1 : -1;
        float magnitude = Mathf.Abs(scrollValue);
        bool startsNewGesture =
            double.IsNegativeInfinity(lastWeaponCycleEventTime) ||
            eventTime < lastWeaponCycleEventTime ||
            eventTime - lastWeaponCycleEventTime >= ScrollGestureGap ||
            direction != lastWeaponCycleDirection;

        if (!startsNewGesture)
        {
            bool confirmsPendingImpulse =
                pendingScrollImpulseDirection == direction &&
                eventTime - pendingScrollImpulseTime <=
                ScrollImpulseConfirmationWindow &&
                magnitude >= pendingScrollImpulseMagnitude *
                ScrollImpulseConfirmationRatio;

            if (confirmsPendingImpulse)
            {
                startsNewGesture = true;
                ClearPendingScrollImpulse();
            }
            else
            {
                bool pendingExpired =
                    pendingScrollImpulseDirection != 0 &&
                    (eventTime - pendingScrollImpulseTime >
                        ScrollImpulseConfirmationWindow ||
                     magnitude < pendingScrollImpulseMagnitude *
                        ScrollImpulseConfirmationRatio);

                if (pendingExpired)
                {
                    ClearPendingScrollImpulse();
                }

                if (pendingScrollImpulseDirection == 0 &&
                    magnitude >= ScrollImpulseMinimum &&
                    magnitude - lastWeaponCycleMagnitude >=
                    ScrollImpulseRise)
                {
                    pendingScrollImpulseDirection = direction;
                    pendingScrollImpulseMagnitude = magnitude;
                    pendingScrollImpulseTime = eventTime;
                }
            }
        }
        else
        {
            ClearPendingScrollImpulse();
        }

        lastWeaponCycleEventTime = eventTime;
        lastWeaponCycleDirection = direction;
        lastWeaponCycleMagnitude = magnitude;

        if (startsNewGesture)
        {
            weaponCycleDirections.Enqueue(direction);
        }
    }

    private void ClearPendingScrollImpulse()
    {
        pendingScrollImpulseTime = 0d;
        pendingScrollImpulseDirection = 0;
        pendingScrollImpulseMagnitude = 0f;
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

public readonly struct PlayerInputSample
{
    public PlayerInputSample(
        Vector2 move,
        Vector2 look,
        bool lookUsesPointerDelta,
        bool jumpPressed,
        bool sprintHeld,
        bool interactPressed,
        bool interactHeld,
        bool interactReleased,
        bool attackPressed,
        bool attackHeld,
        bool aimingPressed,
        bool aimingHeld,
        bool crouchPressed,
        bool pausePressed,
        bool inventoryPressed,
        bool pickupPressed,
        bool reloadPressed,
        int weaponSelection,
        int weaponCycleDirection,
        int quickUseSelection)
    {
        Move = move;
        Look = look;
        LookUsesPointerDelta = lookUsesPointerDelta;
        JumpPressed = jumpPressed;
        SprintHeld = sprintHeld;
        InteractPressed = interactPressed;
        InteractHeld = interactHeld;
        InteractReleased = interactReleased;
        AttackPressed = attackPressed;
        AttackHeld = attackHeld;
        AimingPressed = aimingPressed;
        AimingHeld = aimingHeld;
        CrouchPressed = crouchPressed;
        PausePressed = pausePressed;
        InventoryPressed = inventoryPressed;
        PickupPressed = pickupPressed;
        ReloadPressed = reloadPressed;
        WeaponSelection = weaponSelection;
        WeaponCycleDirection = weaponCycleDirection;
        QuickUseSelection = quickUseSelection;
    }

    public Vector2 Move { get; }
    public Vector2 Look { get; }
    public bool LookUsesPointerDelta { get; }
    public bool JumpPressed { get; }
    public bool SprintHeld { get; }
    public bool InteractPressed { get; }
    public bool InteractHeld { get; }
    public bool InteractReleased { get; }
    public bool AttackPressed { get; }
    public bool AttackHeld { get; }
    public bool AimingPressed { get; }
    public bool AimingHeld { get; }
    public bool CrouchPressed { get; }
    public bool PausePressed { get; }
    public bool InventoryPressed { get; }
    public bool PickupPressed { get; }
    public bool ReloadPressed { get; }
    public int WeaponSelection { get; }
    public int WeaponCycleDirection { get; }
    public int QuickUseSelection { get; }
}
