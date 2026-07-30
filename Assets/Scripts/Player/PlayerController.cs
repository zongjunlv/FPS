using UnityEngine;

public enum AimInputMode
{
    Hold,
    Toggle
}

[RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 2f;
    [SerializeField, Min(0f)] private float sprintSpeed = 5f;
    [SerializeField, Min(0f)] private float crouchSpeed = 1.5f;

    [Header("Stance")]
    [SerializeField, Min(0.1f)] private float standingHeight = 1.8f;
    [SerializeField, Min(0.1f)] private float crouchingHeight = 1.2f;
    [SerializeField, Min(0f)] private float standingCameraHeight = 1.65f;
    [SerializeField, Min(0f)] private float crouchingCameraHeight = 1.05f;
    [SerializeField, Min(0.1f)] private float stanceTransitionSpeed = 6f;
    [SerializeField] private LayerMask stanceObstacleMask = ~0;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.5f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    [Header("Look")]
    [SerializeField] private Transform CameraPivot;
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;
    [SerializeField, Min(0f)] private float gamepadSensitivity = 180f;
    [SerializeField] private bool invertY;

    [Header("Aim")]
    [SerializeField] private AimInputMode aimInputMode = AimInputMode.Hold;
    [SerializeField] private Camera aimCamera;
    [SerializeField, Range(20f, 100f)] private float hipFieldOfView = 60f;
    [SerializeField, Range(15f, 90f)] private float aimFieldOfView = 45f;
    [SerializeField, Min(0.01f)] private float aimTransitionDuration = 0.2f;
    [SerializeField, Range(0.1f, 1f)]
    private float aimSensitivityMultiplier = 0.55f;

    private CharacterController characterController;
    private PlayerInputReader input;
    private PlayerCrosshairPresenter crosshairPresenter;
    public float VerticalVelocity {get; private set; }
    public float MoveDirection { get; private set; }
    public bool IsAiming { get; private set; }
    public float AimBlend { get; private set; }
    public bool IsAdsCrosshairActive => AimBlend >= 0.5f;
    public Camera AimCamera => aimCamera;
    public bool IsCrouching { get; private set; }
    public bool IsPaused { get; private set; }
    public bool GameplayInputEnabled { get; private set; } = true;
    public bool IsSprinting =>
        CanSprint(input.Move, input.SprintHeld);
    public bool InvertY
    {
        get => invertY;
        set => invertY = value;
    }
    public AimInputMode AimMode
    {
        get => aimInputMode;
        set => aimInputMode = value;
    }
    public bool IsGrounded => characterController.isGrounded;

    private float capsuleBottom;
    private readonly Collider[] stanceOverlaps = new Collider[8];
    private bool hasFocus = true;
    private float timeScaleBeforePause = 1f;
    private float xRotation = 0f; // 水平旋转角度
    private float yRotation = 0f; // 垂直旋转角度


    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        input = GetComponent<PlayerInputReader>();
        capsuleBottom =
            characterController.center.y - characterController.height * 0.5f;
        Vector3 cameraStandingPosition = CameraPivot.localPosition;
        cameraStandingPosition.y = standingCameraHeight;
        CameraPivot.localPosition = cameraStandingPosition;

        if (aimCamera == null)
        {
            aimCamera = CameraPivot.GetComponentInChildren<Camera>(true);
        }

        if (aimCamera != null)
        {
            aimCamera.fieldOfView = hipFieldOfView;
        }

        crosshairPresenter = GetComponent<PlayerCrosshairPresenter>();

        if (crosshairPresenter == null)
        {
            crosshairPresenter =
                gameObject.AddComponent<PlayerCrosshairPresenter>();
        }

        crosshairPresenter.SetState(0f, true);
    }

    private void Start()
    {
        ApplyCursorState();
    }

    private void Update()
    {
        if (!GameplayInputEnabled)
        {
            ApplyCursorState();
            return;
        }

        HandlePauseInput();

        if (IsPaused)
        {
            return;
        }

        HandleAimInput();
        Rotate();
        HandleCrouchInput();
        UpdateStance();
        Jump();
        Move();
        UpdateAimPresentation();
    }

    public void SetPaused(bool paused)
    {
        if (paused == IsPaused)
        {
            ApplyCursorState();
            return;
        }

        IsPaused = paused;
        input.ConsumeAimingPressed();
        crosshairPresenter?.SetState(AimBlend, !paused);

        if (paused)
        {
            TrySetAiming(false);

            if (Time.timeScale > 0f)
            {
                timeScaleBeforePause = Time.timeScale;
            }

            Time.timeScale = 0f;
        }
        else if (Time.timeScale <= 0f)
        {
            Time.timeScale = Mathf.Max(0.01f, timeScaleBeforePause);
        }

        ApplyCursorState();
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        GameplayInputEnabled = enabled;

        if (!enabled)
        {
            IsAiming = false;
            AimBlend = 0f;
            MoveDirection = 0f;
            VerticalVelocity = 0f;

            if (aimCamera != null)
            {
                aimCamera.fieldOfView = hipFieldOfView;
            }

            crosshairPresenter?.SetState(0f, false);
        }

        ApplyCursorState();
    }

    private void HandlePauseInput()
    {
        if (input.PausePressed)
        {
            SetPaused(!IsPaused);
        }

        ApplyCursorState();
    }

    private void ApplyCursorState()
    {
        bool shouldLock =
            hasFocus &&
            GameplayInputEnabled &&
            !IsPaused &&
            Time.timeScale > 0f;

        Cursor.lockState = shouldLock
            ? CursorLockMode.Locked
            : CursorLockMode.None;
        Cursor.visible = !shouldLock;
    }

    private void OnApplicationFocus(bool focus)
    {
        hasFocus = focus;
        ApplyCursorState();
    }

    private void OnDisable()
    {
        if (IsPaused)
        {
            IsPaused = false;
            Time.timeScale = Mathf.Max(0.01f, timeScaleBeforePause);
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (aimCamera != null)
        {
            aimCamera.fieldOfView = hipFieldOfView;
        }
    }

    public bool TrySetCrouching(bool shouldCrouch)
    {
        if (!shouldCrouch && IsCrouching && !CanStand())
        {
            return false;
        }

        IsCrouching = shouldCrouch;
        return true;
    }

    private bool CanStand()
    {
        float radius = Mathf.Max(
            0.01f,
            characterController.radius - characterController.skinWidth);
        Vector3 localCenter = characterController.center;
        localCenter.y = capsuleBottom + standingHeight * 0.5f;

        Vector3 worldCenter = transform.TransformPoint(localCenter);
        float halfSegment = Mathf.Max(
            0f,
            standingHeight * 0.5f - radius);
        Vector3 bottom = worldCenter - transform.up * halfSegment;
        Vector3 top = worldCenter + transform.up * halfSegment;

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            radius,
            stanceOverlaps,
            stanceObstacleMask,
            QueryTriggerInteraction.Ignore);

        for (int index = 0; index < overlapCount; index++)
        {
            Collider overlap = stanceOverlaps[index];

            if (overlap == null ||
                overlap == characterController ||
                overlap.transform.IsChildOf(transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void HandleCrouchInput()
    {
        if (input.CrouchPressed)
        {
            TrySetCrouching(!IsCrouching);
        }
    }

    private void UpdateStance()
    {
        float targetHeight =
            IsCrouching ? crouchingHeight : standingHeight;
        float targetCameraHeight =
            IsCrouching ? crouchingCameraHeight : standingCameraHeight;

        characterController.height = Mathf.MoveTowards(
            characterController.height,
            targetHeight,
            stanceTransitionSpeed * Time.deltaTime);

        Vector3 center = characterController.center;
        center.y = capsuleBottom + characterController.height * 0.5f;
        characterController.center = center;

        Vector3 cameraPosition = CameraPivot.localPosition;
        cameraPosition.y = Mathf.MoveTowards(
            cameraPosition.y,
            targetCameraHeight,
            stanceTransitionSpeed * Time.deltaTime);
        CameraPivot.localPosition = cameraPosition;
    }

    private void Rotate()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 lookDelta = CalculateLookDelta(
            input.Look,
            input.LookUsesPointerDelta,
            Time.unscaledDeltaTime);

        // 上下旋转
        yRotation += lookDelta.y;
        yRotation = Mathf.Clamp(yRotation, -80f, 80f);
        CameraPivot.localRotation = Quaternion.Euler(yRotation, 0f, 0f);
        // 水平旋转
        xRotation = lookDelta.x;
        transform.Rotate(Vector3.up, xRotation);
    }

    public Vector2 CalculateLookDelta(
        Vector2 lookInput,
        bool usesPointerDelta,
        float unscaledDeltaTime)
    {
        float sensitivity = usesPointerDelta
            ? mouseSensitivity
            : gamepadSensitivity * Mathf.Max(0f, unscaledDeltaTime);
        float verticalDirection = invertY ? 1f : -1f;
        float aimMultiplier =
            IsAiming ? aimSensitivityMultiplier : 1f;

        return new Vector2(
            lookInput.x * sensitivity * aimMultiplier,
            lookInput.y * sensitivity * verticalDirection * aimMultiplier);
    }

    private void Jump()
    {
        // 落地速度重置，留下一点速度是为了保持isGrounded的状态在斜坡时不闪烁
        if (characterController.isGrounded && VerticalVelocity < 0f)
        {
            VerticalVelocity = -2f;
        }

        if (input.JumpPressed)
        {
            if (IsCrouching)
            {
                TrySetCrouching(false);
            }

            // 起跳速度计算
            else if (characterController.isGrounded)
            {
                VerticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
        }

        VerticalVelocity -= gravity * Time.deltaTime;
    }

    private void Move()
    {
        Vector2 moveInput = input.Move;
        Vector3 horizontalVelocity = CalculateHorizontalVelocity(
            moveInput,
            input.SprintHeld);

        Vector3 finalVelocity = horizontalVelocity + Vector3.up * VerticalVelocity;

        characterController.Move(finalVelocity * Time.deltaTime);

        MoveDirection = Mathf.Clamp01(moveInput.magnitude);
    }

    public Vector3 CalculateHorizontalVelocity(
        Vector2 moveInput,
        bool sprintRequested)
    {
        Vector2 clampedInput = Vector2.ClampMagnitude(moveInput, 1f);
        bool hasMoveInput = clampedInput.sqrMagnitude > 0.01f;

        if (!hasMoveInput)
        {
            return Vector3.zero;
        }

        float targetSpeed = IsCrouching
            ? crouchSpeed
            : CanSprint(clampedInput, sprintRequested)
                ? sprintSpeed
                : walkSpeed;
        Vector3 direction =
            transform.right * clampedInput.x +
            transform.forward * clampedInput.y;

        return direction * targetSpeed;
    }

    private bool CanSprint(Vector2 moveInput, bool sprintRequested)
    {
        return sprintRequested &&
               !IsCrouching &&
               moveInput.sqrMagnitude > 0.01f &&
               moveInput.y > 0.1f;
    }

    public bool TrySetAiming(bool shouldAim)
    {
        return ApplyAimingState(shouldAim, IsSprinting);
    }

    private bool ApplyAimingState(
        bool shouldAim,
        bool isSprinting)
    {
        if (isSprinting)
        {
            IsAiming = false;
            return !shouldAim;
        }

        if (shouldAim && IsPaused)
        {
            IsAiming = false;
            return false;
        }

        IsAiming = shouldAim;
        return true;
    }

    private void HandleAimInput()
    {
        if (IsSprinting)
        {
            input.ConsumeAimingPressed();
            ApplyAimingState(false, true);
            return;
        }

        if (aimInputMode == AimInputMode.Hold)
        {
            input.ConsumeAimingPressed();
            TrySetAiming(input.AimingHeld);
        }
        else if (input.ConsumeAimingPressed())
        {
            TrySetAiming(!IsAiming);
        }
    }

    private void UpdateAimPresentation()
    {
        float targetBlend = IsAiming ? 1f : 0f;
        AimBlend = Mathf.MoveTowards(
            AimBlend,
            targetBlend,
            Time.deltaTime / aimTransitionDuration);
        float easedBlend = Mathf.SmoothStep(0f, 1f, AimBlend);

        if (aimCamera != null)
        {
            aimCamera.fieldOfView = Mathf.Lerp(
                hipFieldOfView,
                aimFieldOfView,
                easedBlend);
        }

        crosshairPresenter?.SetState(AimBlend, !IsPaused);
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
