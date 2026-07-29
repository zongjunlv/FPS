using UnityEngine;

[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class PlayerDamageTestPad : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float testDamage = 10f;
    private bool hasTriggered;

    private void Awake()
    {
        BoxCollider trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered)
        {
            return;
        }

        Health playerHealth = other.GetComponentInParent<Health>();
        PlayerController player =
            other.GetComponentInParent<PlayerController>();

        if (playerHealth == null || player == null)
        {
            return;
        }

        hasTriggered = true;
        playerHealth.ApplyDamage(
            new DamageInfo(
                testDamage,
                other.ClosestPoint(transform.position),
                Vector3.up,
                gameObject));
    }
}
