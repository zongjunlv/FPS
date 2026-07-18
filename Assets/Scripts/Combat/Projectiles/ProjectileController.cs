using System.Security.Cryptography;
using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    [SerializeField] private ProjectileDefinition bullet;
    [SerializeField] private GameObject concrete;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private Rigidbody rigidbody;
    void Start()
    {
        rigidbody = GetComponent<Rigidbody>();
        rigidbody.AddForce(transform.forward * bullet.Speed, ForceMode.Impulse);
        Destroy(gameObject, 1f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnCollisionEnter(Collision collision)
    {
        
        ContactPoint contact = collision.GetContact(0);
        Quaternion rotation = Quaternion.LookRotation(contact.normal);

        Instantiate(concrete, contact.point, rotation);

        if(collision.gameObject.tag == "Enemy")
        {
            collision.gameObject.GetComponent<EnemyController>().GetHit(bullet.Damage);
            
        }
        
        Destroy(gameObject);
    }

}
