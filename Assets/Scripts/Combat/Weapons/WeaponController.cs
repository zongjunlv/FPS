using UnityEngine;

public class WeaponController : MonoBehaviour
{
    [SerializeField] private WeaponDefinition weapon;
    [SerializeField]private MuzzleFlashController muzzleFlash;

    [SerializeField] private GameObject Bullet;
    [SerializeField] private GameObject FirePoint;
    [SerializeField] private GameObject FireEffects;

    
    public bool IsAutomatic => weapon.IsAutomatic;
    public float VerticalRecoil => weapon.VerticalRecoil;
    public float HorizontalRecoil => weapon.HorizontalRecoil;

    
    private float nextFireTime;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public bool TryFire()
    {
        if(Time.time < nextFireTime) return false;
        nextFireTime = Time.time + weapon.FireIntervel;
        Instantiate(Bullet, FirePoint.transform.position, FirePoint.transform.rotation);
        muzzleFlash.Play();

        return true;
    }
}
