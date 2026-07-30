using System;
using UnityEngine;

public class WeaponController : MonoBehaviour
{
    private static readonly int FireStateHash =
        Animator.StringToHash("Layer Base.Fire");

    [SerializeField] private WeaponDefinition weapon;
    [SerializeField] private MuzzleFlashController muzzleFlash;

    [SerializeField] private GameObject FirePoint;
    [SerializeField] private GameObject impactEffect;
    [SerializeField, Min(0.1f)] private float dryFireFeedbackInterval = 0.35f;
    [SerializeField, Min(1f)] private float maxAimDistance = 200f;
    [SerializeField, Min(200f)] private float tracerSpeed = 280f;

    public event Action AmmoChanged;
    public event Action DryFired;
    public event Action ReloadStateChanged;
    public event Action<ShotResult> ShotResolved;

    public bool IsAutomatic => weapon.IsAutomatic;
    public string WeaponName => weapon.WeaponName;
    public string FireModeName => weapon.IsAutomatic ? "AUTO" : "SEMI";
    public float VerticalRecoil => weapon.VerticalRecoil;
    public float HorizontalRecoil => weapon.HorizontalRecoil;
    public float CurrentVerticalRecoil =>
        weapon.VerticalRecoil *
        Mathf.Lerp(1f, weapon.AdsRecoilMultiplier, aimBlend);
    public float CurrentHorizontalRecoil =>
        weapon.HorizontalRecoil *
        Mathf.Lerp(1f, weapon.AdsRecoilMultiplier, aimBlend);
    public float CurrentSpreadDegrees =>
        spreadState.CurrentSpreadDegrees;
    public float Damage => weapon.Damage;
    public int CurrentAmmo => ammoState.CurrentAmmo;
    public int ReserveAmmo => ammoState.ReserveAmmo;
    public int MagazineCapacity => ammoState.MagazineCapacity;
    public float FireInterval => weapon.FireIntervel;
    public float ReloadDuration => weapon.ReloadDuration;
    public bool IsReloading => ammoState.IsReloading;
    public Transform MuzzleTransform => FirePoint.transform;
    public RuntimeAnimatorController CharacterAnimatorController =>
        weapon.CharacterAnimatorController;
    public int DryFireFeedbackCount { get; private set; }
    public ShotResult LastShotResult { get; private set; }
    public AudioSource FireAudioSource => fireAudioSource;

    private float nextFireTime;
    private float nextDryFireFeedbackTime;
    private WeaponAmmoState ammoState;
    private AudioSource audioSource;
    private AudioSource fireAudioSource;
    private Animator weaponAnimator;
    private Camera aimCamera;
    private Transform shooterRoot;
    private ShotTracerPool tracerPool;
    private WeaponSpreadState spreadState;
    private float aimBlend;
    private Vector2? spreadSampleOverride;
    private CombatSoundEventChannel soundEventChannel;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[32];

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
        GameObject fireAudioObject =
            new GameObject("Spatial Gunshot Audio");
        fireAudioObject.transform.SetParent(
            FirePoint != null ? FirePoint.transform : transform,
            false);
        fireAudioSource = fireAudioObject.AddComponent<AudioSource>();
        fireAudioSource.playOnAwake = false;
        fireAudioSource.spatialBlend = 1f;
        fireAudioSource.minDistance = 1.5f;
        fireAudioSource.maxDistance = 60f;
        fireAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        spreadState = new WeaponSpreadState();
        soundEventChannel =
            Resources.Load<CombatSoundEventChannel>(
                "CombatSoundEvents");
        spreadState.Configure(
            weapon.HipSpreadDegrees,
            weapon.AdsSpreadDegrees,
            weapon.MovementSpreadBonus,
            weapon.SprintSpreadBonus,
            weapon.SpreadPerShot,
            weapon.MaxShotSpread,
            weapon.SpreadRecoverySpeed);
        WeaponImpactFeedbackController impactFeedback =
            GetComponent<WeaponImpactFeedbackController>();

        if (impactFeedback == null)
        {
            impactFeedback =
                gameObject.AddComponent<WeaponImpactFeedbackController>();
        }

        impactFeedback.Configure(this, impactEffect);
        weaponAnimator = GetComponent<Animator>();

        if (weaponAnimator != null)
        {
            weaponAnimator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
        }
    }

    // Update is called once per frame
    void Update()
    {
        bool completed = ammoState.AdvanceReload(
            Time.deltaTime,
            weapon.ReloadDuration);
        spreadState.Tick(Time.deltaTime);

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
        ResolveHitscan();
        spreadState.RegisterShot();
        soundEventChannel?.Publish(
            new SoundStimulus(
                FirePoint.transform.position,
                weapon.GunshotHearingRadius,
                weapon.GunshotIntensity,
                shooterRoot != null
                    ? shooterRoot.gameObject
                    : gameObject));

        if (weapon.FireSound != null)
        {
            fireAudioSource.PlayOneShot(weapon.FireSound);
        }

        if (weaponAnimator != null)
        {
            weaponAnimator.Play(
                FireStateHash,
                0,
                0f);
        }

        muzzleFlash.Play();
        AmmoChanged?.Invoke();

        return true;
    }

    public void SetFiringContext(
        float adsBlend,
        float movementAmount,
        bool isSprinting)
    {
        aimBlend = Mathf.Clamp01(adsBlend);
        spreadState.SetContext(
            aimBlend,
            movementAmount,
            isSprinting);
    }

    public void SetSpreadSampleOverride(Vector2 sample)
    {
        spreadSampleOverride = Vector2.ClampMagnitude(sample, 1f);
    }

    public void ClearSpreadSampleOverride()
    {
        spreadSampleOverride = null;
    }

    public void ConfigureAiming(
        Camera camera,
        Transform ownerRoot,
        ShotTracerPool sharedTracerPool)
    {
        aimCamera = camera;
        shooterRoot = ownerRoot;
        tracerPool = sharedTracerPool;
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

    public void PlayHolsterFeedback()
    {
        if (weapon.HolsterSound != null)
        {
            audioSource.PlayOneShot(weapon.HolsterSound);
        }
    }

    public void PlayUnholsterFeedback()
    {
        if (weapon.UnholsterSound != null)
        {
            audioSource.PlayOneShot(weapon.UnholsterSound);
        }
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

    private void ResolveHitscan()
    {
        Transform firePoint = FirePoint.transform;

        if (aimCamera == null)
        {
            ResolveShotRay(
                new Ray(firePoint.position, firePoint.forward),
                maxAimDistance);
            return;
        }

        Ray aimRay = aimCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f));
        Vector2 spreadSample = spreadSampleOverride ??
            UnityEngine.Random.insideUnitCircle;
        aimRay.direction = WeaponSpreadState.ApplySpread(
            aimRay.direction,
            aimCamera.transform.right,
            aimCamera.transform.up,
            CurrentSpreadDegrees,
            spreadSample);
        Vector3 aimPoint = aimRay.GetPoint(maxAimDistance);

        if (TryGetFirstValidHit(
                aimRay,
                maxAimDistance,
                out RaycastHit cameraHit))
        {
            aimPoint = cameraHit.point;
        }

        Vector3 direction = aimPoint - firePoint.position;
        float rayDistance = direction.magnitude;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = aimRay.direction;
            rayDistance = maxAimDistance;
        }

        ResolveShotRay(
            new Ray(firePoint.position, direction.normalized),
            rayDistance + 0.05f);
    }

    private void ResolveShotRay(Ray ray, float distance)
    {
        Vector3 tracerEnd = ray.GetPoint(distance);
        ShotResult result = ShotResult.Miss;

        if (TryGetFirstValidHit(
                ray,
                distance,
                out RaycastHit hit))
        {
            tracerEnd = hit.point;
            result = ApplyHit(hit, ray.direction);
        }

        if (tracerPool != null)
        {
            tracerPool.Play(
                ray.origin,
                tracerEnd,
                tracerSpeed);
        }

        LastShotResult = result;
        ShotResolved?.Invoke(result);
    }

    private ShotResult ApplyHit(
        RaycastHit hit,
        Vector3 shotDirection)
    {
        IDamageable damageable =
            DamageableResolver.Find(hit.collider.transform);
        DamageResult damageResult = DamageResult.None;

        if (damageable != null)
        {
            damageResult = damageable.ApplyDamage(
                new DamageInfo(
                    weapon.Damage,
                    hit.point,
                    shotDirection,
                    shooterRoot != null
                        ? shooterRoot.gameObject
                        : gameObject));
        }

        return new ShotResult(
            true,
            hit.point,
            hit.normal,
            SurfaceResolver.Resolve(hit.collider),
            damageResult);
    }

    private bool TryGetFirstValidHit(
        Ray ray,
        float distance,
        out RaycastHit validHit)
    {
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            hitBuffer,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        validHit = default;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = hitBuffer[index];

            if (shooterRoot != null &&
                hit.collider.transform.IsChildOf(shooterRoot))
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                validHit = hit;
            }
        }

        return nearestDistance < float.PositiveInfinity;
    }
}
