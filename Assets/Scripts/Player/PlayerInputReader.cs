using System;
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

    // 其他脚本只能读取输入结果，不需要直接管理 Input Action。
    public Vector2 Move => moveAction.action.ReadValue<Vector2>();
    public bool JumpPressed => jumpAction.action.WasPressedThisFrame();
    public Vector2 Look => lookAction.action.ReadValue<Vector2>();
    public bool SprintHeld => sprintAction.action.IsPressed();
    public bool InteractPressed => interactAction.action.WasPressedThisFrame();
    public bool AttackPressed => attackAction.action.WasPressedThisFrame();
    public bool AimingPressed => aimingAction.action.WasPressedThisFrame();


    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        lookAction.action.Enable();
        sprintAction.action.Enable();
        interactAction.action.Enable();
        attackAction.action.Enable();
        aimingAction.action.Enable();
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        jumpAction.action.Disable();
        lookAction.action.Disable();
        sprintAction.action.Disable();
        interactAction.action.Disable();
        attackAction.action.Disable();
        aimingAction.action.Disable();
    }
}
