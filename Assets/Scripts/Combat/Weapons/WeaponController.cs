using System;
using UnityEngine;

public class WeaponController : MonoBehaviour
{
    [SerializeField] private WeaponDefinition weapon;
    [SerializeField] private MuzzleFlashController muzzleFlash;

    [SerializeField] private GameObject Bullet;
    [SerializeField] private GameObject FirePoint;
    [SerializeField] private GameObject FireEffects;
    [SerializeField, Min(0.1f)] private float dryFireFeedbackInterval = 0.35f;

    public event Action AmmoChanged;
    public event Action DryFired;
    public event Action ReloadStateChanged;

    public bool IsAutomatic => weapon.IsAutomatic;
    public float VerticalRecoil => weapon.VerticalRecoil;
    public float HorizontalRecoil => weapon.HorizontalRecoil;
    public int CurrentAmmo => ammoState.CurrentAmmo;
    public int ReserveAmmo => ammoState.ReserveAmmo;
    public int MagazineCapacity => ammoState.MagazineCapacity;
    public float FireInterval => weapon.FireIntervel;
    public float ReloadDuration => weapon.ReloadDuration;
    public bool IsReloading => ammoState.IsReloading;
    public int DryFireFeedbackCount { get; private set; }

    private float nextFireTime;
    private float nextDryFireFeedbackTime;
    private WeaponAmmoState ammoState;
    private AudioSource audioSource;
    private Animator weaponAnimator;

    private void Awake()
    {
        ammoState = new WeaponAmmoState(
            weapon.MagazineCapacity,
            weapon.InitialReserveAmmo);
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        weaponAnimator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        bool completed = ammoState.AdvanceReload(
            Time.deltaTime,
            weapon.ReloadDuration);

        if (completed)
        {
            AmmoChanged?.Invoke();
            ReloadStateChanged?.Invoke();
        }
    }

    public bool TryFire()
    {
        if (Time.time < nextFireTime || IsReloading)
        {
            return false;
        }

        if (!ammoState.TryConsumeRound())
        {
            PlayDryFireFeedback();
            return false;
        }

        nextFireTime = Time.time + weapon.FireIntervel;
        Instantiate(Bullet, FirePoint.transform.position, FirePoint.transform.rotation);
        muzzleFlash.Play();
        AmmoChanged?.Invoke();

        return true;
    }

    public bool TryStartReload()
    {
        bool wasEmpty = CurrentAmmo == 0;

        if (!ammoState.TryBeginReload())
        {
            return false;
        }

        AudioClip reloadClip =
            wasEmpty ? weapon.EmptyReloadSound : weapon.ReloadSound;

        if (reloadClip != null)
        {
            audioSource.PlayOneShot(reloadClip);
        }

        if (weaponAnimator != null)
        {
            weaponAnimator.CrossFade(
                wasEmpty ? "Reload Empty" : "Reload",
                0.05f,
                0);
        }

        ReloadStateChanged?.Invoke();
        return true;
    }

    public bool CancelReload()
    {
        if (!ammoState.CancelReload())
        {
            return false;
        }

        audioSource.Stop();

        if (weaponAnimator != null)
        {
            weaponAnimator.CrossFade("Default", 0.1f, 0);
        }

        ReloadStateChanged?.Invoke();
        return true;
    }

    private void OnDisable()
    {
        if (ammoState != null)
        {
            CancelReload();
        }
    }

    private void PlayDryFireFeedback()
    {
        if (Time.unscaledTime < nextDryFireFeedbackTime)
        {
            return;
        }

        nextDryFireFeedbackTime =
            Time.unscaledTime + dryFireFeedbackInterval;
        DryFireFeedbackCount++;

        if (weapon.DryFireSound != null)
        {
            audioSource.PlayOneShot(weapon.DryFireSound);
        }

        if (weaponAnimator != null)
        {
            weaponAnimator.CrossFade("Fire Empty", 0.05f, 0);
        }

        DryFired?.Invoke();
    }
}
