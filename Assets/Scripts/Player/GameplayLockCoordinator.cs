using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(
    typeof(PlayerController),
    typeof(PlayerCombatController),
    typeof(PlayerInputReader))]
public sealed class GameplayLockCoordinator : MonoBehaviour
{
    private readonly GameplayLockState state = new();
    private readonly Dictionary<GameplayLockReason, GameplayLockLease>
        persistentLocks = new();

    private PlayerController player;
    private PlayerCombatController combat;
    private float timeScaleBeforeFirstLock = 1f;
    private CursorLockMode cursorLockBeforeFirstLock;
    private bool cursorVisibleBeforeFirstLock;
    private bool lockApplied;
    private bool preservingWeaponState;

    public event Action<bool> LockStateChanged;

    public GameplayLockState State => state;
    public bool IsLocked => state.IsLocked;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombatController>();
        state.Changed += ApplyState;
    }

    private void Start()
    {
        ApplyState();
    }

    private void OnDestroy()
    {
        state.Changed -= ApplyState;

        if (lockApplied)
        {
            RestoreRuntimeState();
        }
    }

    public GameplayLockLease Acquire(GameplayLockReason reason)
    {
        return state.Acquire(reason);
    }

    public void SetReasonActive(
        GameplayLockReason reason,
        bool active)
    {
        if (active)
        {
            if (!persistentLocks.ContainsKey(reason))
            {
                persistentLocks.Add(reason, Acquire(reason));
            }

            return;
        }

        if (!persistentLocks.Remove(reason, out GameplayLockLease lease))
        {
            return;
        }

        lease.Dispose();
    }

    public void ResetForSceneTransition()
    {
        state.Reset();

        foreach (GameplayLockLease lease in persistentLocks.Values)
        {
            lease.Dispose();
        }

        persistentLocks.Clear();

        if (lockApplied)
        {
            RestoreRuntimeState();
        }
    }

    private void ApplyState()
    {
        bool shouldLock = state.IsLocked;

        if (shouldLock)
        {
            bool preserveWeaponState =
                state.ActiveLockCount == 1 &&
                state.IsReasonActive(GameplayLockReason.UpgradeChoice);

            if (lockApplied)
            {
                if (preservingWeaponState && !preserveWeaponState)
                {
                    preservingWeaponState = false;
                    combat.SuspendGameplayInput(false);
                }

                return;
            }

            timeScaleBeforeFirstLock = Time.timeScale > 0f
                ? Time.timeScale
                : 1f;
            cursorLockBeforeFirstLock = Cursor.lockState;
            cursorVisibleBeforeFirstLock = Cursor.visible;
            lockApplied = true;
            player.SetGameplayInputEnabled(false);
            preservingWeaponState = preserveWeaponState;
            combat.SuspendGameplayInput(preserveWeaponState);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            LockStateChanged?.Invoke(true);
            return;
        }

        if (!lockApplied)
        {
            return;
        }

        RestoreRuntimeState();
        LockStateChanged?.Invoke(false);
    }

    private void RestoreRuntimeState()
    {
        Time.timeScale = Mathf.Max(0.01f, timeScaleBeforeFirstLock);
        lockApplied = false;
        preservingWeaponState = false;
        player.SetGameplayInputEnabled(true);
        combat.SetGameplayInputEnabled(true);
        Cursor.lockState = cursorLockBeforeFirstLock;
        Cursor.visible = cursorVisibleBeforeFirstLock;
    }
}
