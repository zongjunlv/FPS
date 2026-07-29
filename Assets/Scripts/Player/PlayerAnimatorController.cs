using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerController))]
public class PlayerAnimatorController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private static readonly int PickupTrigger = Animator.StringToHash("pickup");
    private static readonly int Movement = Animator.StringToHash("Movement");
    private static readonly int Running = Animator.StringToHash("Running");
    private static readonly int Aiming = Animator.StringToHash("Aiming");
    private static readonly int Holstered =
        Animator.StringToHash("Holstered");
    private static readonly int Reload =
        Animator.StringToHash("Layer Actions.Reload");
    private static readonly int ReloadEmpty =
        Animator.StringToHash("Layer Actions.Reload Empty");
    private static readonly int DefaultAction =
        Animator.StringToHash("Layer Actions.Default");
    private static readonly int Holster =
        Animator.StringToHash("Layer Holster.Holster");
    private static readonly int Unholster =
        Animator.StringToHash("Layer Holster.Unholster");

    private PlayerController playerController;
    private int actionsLayerIndex = -1;
    private int holsterLayerIndex = -1;

    public bool IsReloadAnimationPlaying { get; private set; }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        playerController = GetComponent<PlayerController>();
        RefreshLayerIndices();
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

    public void SetWeaponAnimatorController(
        RuntimeAnimatorController controller)
    {
        if (controller == null ||
            animator.runtimeAnimatorController == controller)
        {
            return;
        }

        animator.runtimeAnimatorController = controller;
        RefreshLayerIndices();
        IsReloadAnimationPlaying = false;
    }

    public bool PlayHolsterAnimation()
    {
        return SetHolsteredState(true, Holster);
    }

    public bool PlayUnholsterAnimation()
    {
        return SetHolsteredState(false, Unholster);
    }

    private bool SetHolsteredState(bool holstered, int stateHash)
    {
        if (holsterLayerIndex < 0 ||
            !animator.HasState(holsterLayerIndex, stateHash))
        {
            return false;
        }

        animator.SetBool(Holstered, holstered);
        return true;
    }

    private void RefreshLayerIndices()
    {
        actionsLayerIndex = animator.GetLayerIndex("Layer Actions");
        holsterLayerIndex = animator.GetLayerIndex("Layer Holster");
    }
}
