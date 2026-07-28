using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerController))]
public class PlayerAnimatorController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private static readonly int PickupTrigger = Animator.StringToHash("pickup");
    private static readonly int Movement = Animator.StringToHash("Movement");
    private static readonly int Running = Animator.StringToHash("Running");
    private static readonly int Aiming = Animator.StringToHash("Aiming");

    private PlayerController playerController;
    
    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        animator.SetBool(Running, playerController.IsSprinting);
        animator.SetFloat(Movement, playerController.MoveDirection, 0.1f, Time.deltaTime);

        float aimingTarget = playerController.IsAiming? 1f : 0f;
        animator.SetFloat(Aiming, aimingTarget, 0.1f, Time.deltaTime);
    }
}
