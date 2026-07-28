using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerController))]
public class PlayerAnimatorController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private static readonly int PickupTrigger = Animator.StringToHash("pickup");
    private static readonly int Movement = Animator.StringToHash("Movement");
    private static readonly int Running = Animator.StringToHash("Running");
    private static readonly int Aiming = Animator.StringToHash("Aiming");
    private static readonly int Reload =
        Animator.StringToHash("Layer Actions.Reload");
    private static readonly int ReloadEmpty =
        Animator.StringToHash("Layer Actions.Reload Empty");
    private static readonly int DefaultAction =
        Animator.StringToHash("Layer Actions.Default");

    private PlayerController playerController;
    private int actionsLayerIndex = -1;

    public bool IsReloadAnimationPlaying { get; private set; }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        playerController = GetComponent<PlayerController>();
        actionsLayerIndex = animator.GetLayerIndex("Layer Actions");
    }

    private void Update()
    {
        animator.SetBool(Running, playerController.IsSprinting);
        animator.SetFloat(Movement, playerController.MoveDirection, 0.1f, Time.deltaTime);

        float aimingTarget = playerController.IsAiming ? 1f : 0f;
        animator.SetFloat(Aiming, aimingTarget, 0.1f, Time.deltaTime);
    }

    public bool PlayReloadAnimation(bool emptyMagazine)
    {
        int stateHash = emptyMagazine ? ReloadEmpty : Reload;

        if (actionsLayerIndex < 0 ||
            !animator.HasState(actionsLayerIndex, stateHash))
        {
            IsReloadAnimationPlaying = false;
            return false;
        }

        animator.CrossFadeInFixedTime(
            stateHash,
            0.08f,
            actionsLayerIndex,
            0f);
        IsReloadAnimationPlaying = true;
        return true;
    }

    public void StopReloadAnimation()
    {
        if (!IsReloadAnimationPlaying)
        {
            return;
        }

        if (actionsLayerIndex >= 0 &&
            animator.HasState(actionsLayerIndex, DefaultAction))
        {
            animator.CrossFadeInFixedTime(
                DefaultAction,
                0.1f,
                actionsLayerIndex,
                0f);
        }

        IsReloadAnimationPlaying = false;
    }
}
