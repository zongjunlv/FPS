using System;
using FPS.Networking.Domain;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    /// <summary>
    /// Small input port for the existing FPS controller. The gameplay layer
    /// feeds current movement/aim through SetInputFrame; this component emits
    /// authoritative commands at the replicated network tick rate.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerReplica))]
    public sealed class NetworkVerticalSliceInputDriver : MonoBehaviour
    {
        private NetworkPlayerReplica replica;
        private Vector2 movement;
        private float aimYaw;
        private float aimPitch;
        private bool fireQueued;
        private bool jumpQueued;
        private bool sprintHeld;
        private bool crouchRequested;
        private bool aimingHeld;
        private double accumulatedSeconds;
        private long clientTick;
        private object exclusiveInputOwner;

        public long ClientTick => clientTick;
        public NetcodePlayerCommand LastSubmittedCommand { get; private set; }
        public bool HasSubmittedCommand { get; private set; }
        public bool HasExclusiveInputOwner => exclusiveInputOwner != null;
        public bool CanSubmitCurrentFrame => HasServerTickBudget();
        public event Action<NetcodePlayerCommand> CommandSubmitted;

        private void Awake()
        {
            replica = GetComponent<NetworkPlayerReplica>();
        }

        private void Update()
        {
            if (replica == null || !replica.IsLocallyControlled ||
                !replica.IsPresentationReady)
            {
                return;
            }
            if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive ||
                replica.Session.WorldState.MissionPhase ==
                    AuthoritativeMissionPhase.Victory ||
                replica.Session.WorldState.MissionPhase ==
                    AuthoritativeMissionPhase.Defeat)
            {
                movement = Vector2.zero;
                fireQueued = false;
                jumpQueued = false;
                sprintHeld = false;
                aimingHeld = false;
                return;
            }

            int tickRate = replica.Session.Rules.TickRate;
            double tickSeconds = 1d / tickRate;
            accumulatedSeconds += Time.unscaledDeltaTime;
            int safety = 0;
            while (accumulatedSeconds >= tickSeconds && safety++ < 4)
            {
                if (!HasServerTickBudget())
                {
                    accumulatedSeconds = Math.Min(
                        accumulatedSeconds, tickSeconds);
                    break;
                }
                accumulatedSeconds -= tickSeconds;
                SubmitCurrentFrame();
            }
        }

        public void SetInputFrame(
            Vector2 move,
            float absoluteAimYawDegrees,
            float absoluteAimPitchDegrees,
            bool firePressed)
        {
            SetInputFrame(move, absoluteAimYawDegrees,
                absoluteAimPitchDegrees, firePressed, jumpPressed: false,
                sprintRequested: false, crouching: false,
                aiming: false);
        }

        public void SetInputFrame(
            Vector2 move,
            float absoluteAimYawDegrees,
            float absoluteAimPitchDegrees,
            bool firePressed,
            bool jumpPressed,
            bool sprintRequested,
            bool crouching)
        {
            SetInputFrame(move, absoluteAimYawDegrees,
                absoluteAimPitchDegrees, firePressed, jumpPressed,
                sprintRequested, crouching, aiming: false);
        }

        public void SetInputFrame(
            Vector2 move,
            float absoluteAimYawDegrees,
            float absoluteAimPitchDegrees,
            bool firePressed,
            bool jumpPressed,
            bool sprintRequested,
            bool crouching,
            bool aiming)
        {
            if (exclusiveInputOwner != null) return;
            ApplyInputFrame(move, absoluteAimYawDegrees,
                absoluteAimPitchDegrees, firePressed, jumpPressed,
                sprintRequested, crouching, aiming);
        }

        /// <summary>
        /// Grants one automation/replay producer exclusive control over the
        /// input frame. Normal gameplay writers remain connected but their
        /// frames are ignored until the owner releases the lease.
        /// </summary>
        public bool TryAcquireExclusiveInput(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (exclusiveInputOwner != null &&
                !ReferenceEquals(exclusiveInputOwner, owner))
                return false;
            exclusiveInputOwner = owner;
            return true;
        }

        public bool ReleaseExclusiveInput(object owner)
        {
            if (owner == null ||
                !ReferenceEquals(exclusiveInputOwner, owner))
                return false;
            exclusiveInputOwner = null;
            return true;
        }

        public bool SetExclusiveInputFrame(
            object owner,
            Vector2 move,
            float absoluteAimYawDegrees,
            float absoluteAimPitchDegrees,
            bool firePressed,
            bool jumpPressed,
            bool sprintRequested,
            bool crouching,
            bool aiming)
        {
            if (owner == null ||
                !ReferenceEquals(exclusiveInputOwner, owner))
                return false;
            ApplyInputFrame(move, absoluteAimYawDegrees,
                absoluteAimPitchDegrees, firePressed, jumpPressed,
                sprintRequested, crouching, aiming);
            return true;
        }

        private void ApplyInputFrame(
            Vector2 move,
            float absoluteAimYawDegrees,
            float absoluteAimPitchDegrees,
            bool firePressed,
            bool jumpPressed,
            bool sprintRequested,
            bool crouching,
            bool aiming)
        {
            movement = Vector2.ClampMagnitude(move, 1f);
            aimYaw = absoluteAimYawDegrees;
            aimPitch = Mathf.Clamp(absoluteAimPitchDegrees, -89f, 89f);
            fireQueued |= firePressed;
            jumpQueued |= jumpPressed;
            sprintHeld = sprintRequested;
            crouchRequested = crouching;
            aimingHeld = aiming;
        }

        public void SetCombatFrame(string gameplayWeaponId, Vector3 shotOrigin)
        {
            if (exclusiveInputOwner != null) return;
            if (replica == null)
                replica = GetComponent<NetworkPlayerReplica>();
            replica?.ConfigureLocalCombatContext(gameplayWeaponId, shotOrigin);
        }

        public bool SetExclusivePredictedCombatFrame(
            object owner,
            string gameplayWeaponId)
        {
            if (owner == null ||
                !ReferenceEquals(exclusiveInputOwner, owner))
                return false;
            if (replica == null)
                replica = GetComponent<NetworkPlayerReplica>();
            replica?.ConfigurePredictedCombatContext(gameplayWeaponId);
            return replica != null;
        }

        public NetcodePlayerCommand SubmitCurrentFrame()
        {
            if (replica == null)
            {
                replica = GetComponent<NetworkPlayerReplica>();
            }
            if (!replica.IsLocallyControlled || !replica.IsPresentationReady)
            {
                throw new InvalidOperationException(
                    "The local network player is not ready to submit input.");
            }

            clientTick = Math.Max(
                clientTick,
                replica.Session.WorldState.ServerTick);
            clientTick++;
            bool fire = fireQueued;
            fireQueued = false;
            bool jump = jumpQueued;
            jumpQueued = false;
            LastSubmittedCommand = replica.SubmitLocalCommand(
                movement.x,
                movement.y,
                aimYaw,
                aimPitch,
                fire,
                clientTick,
                jump,
                sprintHeld,
                crouchRequested,
                aimingHeld);
            HasSubmittedCommand = true;
            CommandSubmitted?.Invoke(LastSubmittedCommand);
            return LastSubmittedCommand;
        }

        public bool TrySubmitCurrentFrame(
            out NetcodePlayerCommand command)
        {
            if (!HasServerTickBudget())
            {
                command = default;
                return false;
            }
            command = SubmitCurrentFrame();
            return true;
        }

        private bool HasServerTickBudget()
        {
            if (replica == null)
                replica = GetComponent<NetworkPlayerReplica>();
            if (replica == null || !replica.IsLocallyControlled ||
                !replica.IsPresentationReady || replica.Session == null ||
                replica.Session.Rules == null)
                return false;
            long maximumTick = replica.Session.WorldState.ServerTick +
                               replica.Session.Rules
                                   .MaximumFutureCommandTicks;
            return clientTick < maximumTick;
        }

        public void ResetDriver()
        {
            movement = Vector2.zero;
            aimYaw = 0f;
            aimPitch = 0f;
            fireQueued = false;
            jumpQueued = false;
            sprintHeld = false;
            crouchRequested = false;
            aimingHeld = false;
            accumulatedSeconds = 0d;
            clientTick = 0;
            LastSubmittedCommand = default;
            HasSubmittedCommand = false;
        }

        public NetcodePresentationCommand SubmitPresentationAction(
            NetworkPresentationAction action,
            string weaponId = null)
        {
            if (replica == null)
                replica = GetComponent<NetworkPlayerReplica>();
            if (!replica.IsLocallyControlled || !replica.IsPresentationReady)
                throw new InvalidOperationException(
                    "The local network player is not ready to submit input.");
            return replica.SubmitPresentationAction(action, weaponId);
        }
    }
}
