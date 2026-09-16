using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class TutorialSafetyResetVolume : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private BoxCollider trigger;

    public Transform SpawnPoint => spawnPoint;
    public BoxCollider Trigger => trigger;
    public int RecoveryCount { get; private set; }
    public string LastRecoveryError { get; private set; } = string.Empty;

    public void Configure(Transform configuredSpawnPoint)
    {
        spawnPoint = configuredSpawnPoint;
        trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
    }

    public bool Recover(PlayerController player)
    {
        if (player == null || spawnPoint == null)
        {
            LastRecoveryError = "教学安全区缺少玩家或出生点。";
            return false;
        }

        bool recovered = player.TryRestoreSnapshotPose(
            spawnPoint.position,
            spawnPoint.rotation,
            0f,
            false,
            out string error);
        LastRecoveryError = error;

        if (recovered)
        {
            RecoveryCount++;
        }

        return recovered;
    }

    private void Awake()
    {
        trigger ??= GetComponent<BoxCollider>();
        trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            Recover(player);
        }
    }
}
