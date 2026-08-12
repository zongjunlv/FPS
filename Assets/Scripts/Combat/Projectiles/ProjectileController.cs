using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    [Tooltip(
        "Physical projectiles are reserved for slow special weapons " +
        "such as rockets and grenades.")]
    [SerializeField] private ProjectileDefinition bullet;
    [SerializeField] private GameObject concrete;

    private Rigidbody rigidbody;

    private void Start()
    {
        rigidbody = GetComponent<Rigidbody>();
        rigidbody.AddForce(transform.forward * bullet.Speed, ForceMode.Impulse);
        Destroy(gameObject, 1f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        ContactPoint contact = collision.GetContact(0);
        Quaternion rotation = Quaternion.LookRotation(contact.normal);

        Instantiate(concrete, contact.point, rotation);

        IDamageable damageable =
            DamageableResolver.Find(collision.transform);

        if (damageable != null)
        {
            damageable.ApplyDamage(
                new DamageInfo(
                    bullet.Damage,
                    contact.point,
                    transform.forward,
                    gameObject,
                    DamageType.Projectile));
        }

        Destroy(gameObject);
    }
}
