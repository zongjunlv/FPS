using FPS.Networking.Domain;
using UnityEngine;

namespace FPS.Networking.Session
{
    /// <summary>Animation follows received state and rendered velocity, never navigation or damage.</summary>
    public sealed class CoopEnemyVisualAnimator : MonoBehaviour
    {
        private Animator animator;
        private CoopEnemyPresentationDefinition definition;
        private Transform visual;
        private Vector3 visualRestPosition;
        private Quaternion visualRestRotation;
        private string currentState;
        private long lastAttackTick = -1;
        private float attackRemaining;
        private float phase;

        public string PresentedState => currentState;

        private void Awake()
        {
            definition = GetComponent<CoopEnemyPresentationDefinition>();
            animator = GetComponentInChildren<Animator>(true);
            visual = animator != null ? animator.transform : transform.Find("Visual");
            if (visual != null)
            {
                visualRestPosition = visual.localPosition;
                visualRestRotation = visual.localRotation;
            }
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.fireEvents = false;
            }
        }

        public void ResetPose()
        {
            currentState = null;
            attackRemaining = 0f;
            lastAttackTick = -1;
            phase = 0f;
            if (visual != null) visual.SetLocalPositionAndRotation(visualRestPosition, visualRestRotation);
            if (animator != null && animator.isActiveAndEnabled) animator.Rebind();
        }

        public void Present(AuthoritativeEnemyBehavior behavior, Vector3 velocity,
            float deltaTime, long attackTick = -1)
        {
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (attackTick >= 0 && attackTick != lastAttackTick)
            {
                lastAttackTick = attackTick;
                attackRemaining = 0.5f;
                currentState = null;
            }
            attackRemaining = Mathf.Max(0f, attackRemaining - deltaTime);
            string state = attackRemaining > 0f ? "Attack" : speed > 0.08f ? "Run" : "Idle";
            if (behavior == AuthoritativeEnemyBehavior.Dead || behavior == AuthoritativeEnemyBehavior.Pooled)
                state = "Idle";
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            {
                if (currentState != state)
                {
                    int hash = Animator.StringToHash("Base Layer." + state);
                    if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.08f, 0);
                }
                animator.speed = state == "Run"
                    ? Mathf.Clamp(speed / Mathf.Max(0.1f, definition?.LocomotionReferenceSpeed ?? 2.4f), 0.35f, 2f)
                    : 1f;
            }
            currentState = state;
            if (definition != null && !definition.HasLocomotionClip && visual != null)
            {
                // Imported hovering drones lack a run clip. A small, bounded visual-only
                // response communicates travel without moving the authoritative root.
                phase += deltaTime * 5f;
                float moving = Mathf.Clamp01(speed / 2f);
                visual.localPosition = visualRestPosition + Vector3.up * (Mathf.Sin(phase) * 0.018f * moving);
                visual.localRotation = visualRestRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(phase) * 2f * moving);
            }
        }
    }
}
