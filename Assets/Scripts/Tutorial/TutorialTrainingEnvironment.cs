using System;
using System.Collections.Generic;
using FPS.Core.GameModes;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialTrainingEnvironment : MonoBehaviour
{
    [SerializeField] private PlayerGameplayRig playerRig;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Collider movementFloor;
    [SerializeField] private Collider shootingWall;
    [SerializeField] private Transform damageTrainingPoint;
    [SerializeField] private Collider[] safetyBoundaries =
        Array.Empty<Collider>();
    [SerializeField] private TutorialSafetyResetVolume recoveryVolume;
    [SerializeField] private Bounds playableBounds;

    public PlayerGameplayRig PlayerRig => playerRig;
    public Transform SpawnPoint => spawnPoint;
    public Collider MovementFloor => movementFloor;
    public Collider ShootingWall => shootingWall;
    public Transform DamageTrainingPoint => damageTrainingPoint;
    public IReadOnlyList<Collider> SafetyBoundaries => safetyBoundaries;
    public TutorialSafetyResetVolume RecoveryVolume => recoveryVolume;
    public Bounds PlayableBounds => playableBounds;
    public bool IsReady { get; private set; }
    public string InitializationError { get; private set; } = string.Empty;

    public void Configure(
        PlayerGameplayRig configuredPlayerRig,
        Transform configuredSpawnPoint,
        Collider configuredMovementFloor,
        Collider configuredShootingWall,
        Transform configuredDamageTrainingPoint,
        Collider[] configuredSafetyBoundaries,
        TutorialSafetyResetVolume configuredRecoveryVolume,
        Bounds configuredPlayableBounds)
    {
        playerRig = configuredPlayerRig;
        spawnPoint = configuredSpawnPoint;
        movementFloor = configuredMovementFloor;
        shootingWall = configuredShootingWall;
        damageTrainingPoint = configuredDamageTrainingPoint;
        safetyBoundaries = configuredSafetyBoundaries ??
            Array.Empty<Collider>();
        recoveryVolume = configuredRecoveryVolume;
        playableBounds = configuredPlayableBounds;
    }

    public bool ContainsPlayablePoint(Vector3 point)
    {
        return playableBounds.Contains(point);
    }

    public bool TryValidate(out string error)
    {
        if (playerRig == null || spawnPoint == null || movementFloor == null ||
            shootingWall == null || damageTrainingPoint == null ||
            recoveryVolume == null)
        {
            error = "教学场景缺少玩家、出生点、训练地面、射击墙、伤害训练位置或恢复区。";
            return false;
        }

        if (!playerRig.TryValidate(out string rigError))
        {
            error = "教学玩家 Rig 无效：" + rigError;
            return false;
        }

        if (safetyBoundaries == null || safetyBoundaries.Length != 4)
        {
            error = "教学场景必须具有四面安全边界。";
            return false;
        }

        for (int index = 0; index < safetyBoundaries.Length; index++)
        {
            Collider boundary = safetyBoundaries[index];
            if (boundary == null || boundary.isTrigger ||
                boundary.bounds.size.y < 3f)
            {
                error = $"教学安全边界 {index + 1} 无效。";
                return false;
            }
        }

        Bounds floorBounds = movementFloor.bounds;
        Bounds bounds = playableBounds;
        bool floorCoversArena =
            floorBounds.min.x <= bounds.min.x &&
            floorBounds.max.x >= bounds.max.x &&
            floorBounds.min.z <= bounds.min.z &&
            floorBounds.max.z >= bounds.max.z;
        if (!floorCoversArena)
        {
            error = "教学地面没有覆盖完整可玩区域。";
            return false;
        }

        if (!ContainsPlayablePoint(spawnPoint.position) ||
            !ContainsPlayablePoint(damageTrainingPoint.position) ||
            !ContainsPlayablePoint(playerRig.transform.position))
        {
            error = "出生点、伤害训练位置或玩家位于安全区域外。";
            return false;
        }

        if (recoveryVolume.SpawnPoint != spawnPoint ||
            recoveryVolume.Trigger == null ||
            !recoveryVolume.Trigger.isTrigger)
        {
            error = "教学跌落恢复区没有绑定正确的出生点。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void Start()
    {
        if (!GameModeContext.IsActive(
                GameModeId.Tutorial,
                GameModeStage.Tutorial))
        {
            InitializationError = "当前模式不是新手教学，已停用训练场。";
            enabled = false;
            return;
        }

        IsReady = TryValidate(out string error);
        InitializationError = error;
        if (!IsReady)
        {
            Debug.LogError(
                $"[{nameof(TutorialTrainingEnvironment)}] {error}",
                this);
            enabled = false;
        }
    }
}
