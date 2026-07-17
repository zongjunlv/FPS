using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float idleSpeed = 0f;
    [SerializeField, Min(0f)] private float walkSpeed = 2f;
    [SerializeField, Min(0f)] private float sprintSpeed = 5f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.5f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    [Header("Sensitivity")]
    [SerializeField] private Transform CameraPivot;
    [SerializeField, Min(0f)] private float xSensitivity = 2f;
    [SerializeField, Min(0f)] private float ySensitivity = 2f;



    private CharacterController characterController;
    private PlayerInputReader input;
    public float VerticalVelocity {get; private set; }
    public float MoveDirection { get; private set; }
    public bool IsAiming {get; private set; } 
    public bool IsGrounded => characterController.isGrounded;

    float xRotation = 0f; // 水平旋转角度
    float yRotation = 0f; // 垂直旋转角度


    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        input = GetComponent<PlayerInputReader>();
    }

    private void Start()
    {
        // 隐藏鼠标
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        // Rotate();
        Move();
        Jump();
        Aim();
    }

    private void Rotate()
    {
        float x = input.Look.x;
        float y = input.Look.y;
        // 上下旋转
        yRotation -= y * ySensitivity;
        yRotation = Mathf.Clamp(yRotation, -80f, 80f);
        CameraPivot.localRotation = Quaternion.Euler(yRotation, 0f, 0f);
        // 水平旋转
        xRotation = x * xSensitivity;
        transform.Rotate(Vector3.up, xRotation);
    }

    private void Jump()
    {
        // 落地速度重置，留下一点速度是为了保持isGrounded的状态在斜坡时不闪烁
        if (characterController.isGrounded && VerticalVelocity < 0f)
        {
            VerticalVelocity = -2f;
        }

        // 起跳速度计算
        if (characterController.isGrounded && input.JumpPressed)
        {
            VerticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
        }

        VerticalVelocity -= gravity * Time.deltaTime;
    }

    private void Move()
    {
        Vector2 moveInput = input.Move;
        Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
        
        bool hasMoveInput = moveInput.sqrMagnitude > 0.01f;

        float targetSpeed = hasMoveInput
            ? (input.SprintHeld ? sprintSpeed : walkSpeed)
            : idleSpeed;

        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        Vector3 horizontalVelocity = moveDirection * targetSpeed;

        Vector3 finalVelocity = horizontalVelocity + Vector3.up * VerticalVelocity;

        characterController.Move(finalVelocity * Time.deltaTime);
        
        MoveDirection = moveDirection.magnitude;
    }

    private void Aim()
    {
        if (input.AimingPressed)
        {
            IsAiming = !IsAiming;
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        if (body == null || body.isKinematic)
        {
            return;
        }

        Vector3 direction = new Vector3(
            hit.moveDirection.x,
            0f,
            hit.moveDirection.z
        );

        body.AddForceAtPosition(
            direction * 5f,
            hit.point,
            ForceMode.Force
        );
    }
}
