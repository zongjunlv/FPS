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
    [SerializeField] private bool appliesBurnOnHit = true;

    public event Action AmmoChanged;
    public event Action DryFired;
    public event Action ReloadStateChanged;
    public event Action RuntimePropertiesChanged;
    public event Action<ShotResult> ShotResolved;

    public bool IsAutomatic => weapon.IsAutomatic;
    public string WeaponName => weapon.WeaponName;
    public string FireModeName => weapon.IsAutomatic ? "AUTO" : "SEMI";
    public float VerticalRecoil => weapon.VerticalRecoil;
    public float HorizontalRecoil => weapon.HorizontalRecoil;
    public float CurrentVerticalRecoil =>
        ApplyRuntimeRecoil(
            weapon.VerticalRecoil *
            Mathf.Lerp(1f, weapon.AdsRecoilMultiplier, aimBlend));
    public float CurrentHorizontalRecoil =>
        ApplyRuntimeRecoil(
            weapon.HorizontalRecoil *
            Mathf.Lerp(1f, weapon.AdsRecoilMultiplier, aimBlend));
    public float CurrentSpreadDegrees =>
        runtimeCombatStats != null
            ? runtimeCombatStats.ApplySpread(
                spreadState.CurrentSpreadDegrees)
            : spreadState.CurrentSpreadDegrees;
    public float BaseDamage => weapon.Damage;
    public float Damage => runtimeCombatStats != null
        ? runtimeCombatStats.ApplyWeaponDamage(BaseDamage)
        : BaseDamage;
    public int CurrentAmmo => ammoState.CurrentAmmo;
    public int ReserveAmmo => ammoState.ReserveAmmo;
    public int MaximumReserveAmmo => ammoState.MaximumReserveAmmo;
    public WeaponAmmoType AmmoType => weapon.AmmoType;
    public int MagazineCapacity => ammoState.MagazineCapacity;
    public float FireInterval => runtimeCombatStats != null
        ? runtimeCombatStats.ApplyFireInterval(weapon.FireIntervel)
        : weapon.FireIntervel;
    public float ReloadDuration => runtimeCombatStats != null
        ? runtimeCombatStats.ApplyReloadDuration(weapon.ReloadDuration)
        : weapon.ReloadDuration;
    public float ReloadAnimationSpeed =>
        Mathf.Max(0.01f, weapon.ReloadDuration) /
        Mathf.Max(0.01f, ReloadDuration);
    public float FireAnimationSpeed => runtimeCombatStats != null
        ? runtimeCombatStats.FireRateMultiplier
        : 1f;
    public float WeaponAnimatorPlaybackSpeed => weaponAnimator != null
        ? weaponAnimator.speed
        : 1f;
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
    private CombatEffectPool combatEffectPool;
    private PlayerRuntimeCombatStats runtimeCombatStats;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    private void Awake()
    {
        ammoState = new WeaponAmmoState(
            weapon.MagazineCapacity,
            weapon.InitialReserveAmmo,
            Mathf.Max(
                weapon.InitialReserveAmmo,
                weapon.MaximumReserveAmmo));
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
        combatEffectPool = impactFeedback.EffectPool;
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
            ReloadDuration);
        spreadState.Tick(Time.deltaTime);

        if (completed)
        {
            ResetWeaponAnimationSpeed();
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

        nextFireTime = Time.time + FireInterval;
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
            if (combatEffectPool == null ||
                !combatEffectPool.PlayAudio(
                    FirePoint.transform.position,
                    weapon.FireSound))
            {
                fireAudioSource.PlayOneShot(weapon.FireSound);
            }
        }

        if (weaponAnimator != null)
        {
            weaponAnimator.speed = FireAnimationSpeed;
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
        ShotTracerPool sharedTracerPool,
        PlayerRuntimeCombatStats combatStats,
        CombatSoundEventChannel combatSoundEvents)
    {
        aimCamera = camera;
        shooterRoot = ownerRoot;
        tracerPool = sharedTracerPool;
        soundEventChannel = combatSoundEvents;
        SetRuntimeCombatStats(combatStats);
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
            weaponAnimator.speed = ReloadAnimationSpeed;
            weaponAnimator.CrossFade(
                wasEmpty ? "Reload Empty" : "Reload",
                0.05f,
                0);
        }

        ReloadStateChanged?.Invoke();
        return true;
    }

    public int AddReserveAmmo(int amount)
    {
        int accepted = ammoState.AddReserveAmmo(amount);

        if (accepted > 0)
        {
            AmmoChanged?.Invoke();
        }

        return accepted;
    }

    public int AddMagazineAmmo(int amount)
    {
        int accepted = ammoState.AddMagazineAmmo(amount);

        if (accepted > 0)
        {
            AmmoChanged?.Invoke();
        }

        return accepted;
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
            ResetWeaponAnimationSpeed();
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

        ResetWeaponAnimationSpeed();
    }

    private void OnDestroy()
    {
        SetRuntimeCombatStats(null);
    }

    private float ApplyRuntimeRecoil(float baseRecoil)
    {
        return runtimeCombatStats != null
            ? runtimeCombatStats.ApplyRecoil(baseRecoil)
            : baseRecoil;
    }

    private void SetRuntimeCombatStats(PlayerRuntimeCombatStats stats)
    {
        if (runtimeCombatStats == stats)
        {
            RefreshRuntimeWeaponState();
            return;
        }

        if (runtimeCombatStats != null)
        {
            runtimeCombatStats.ModifiersChanged -=
                RefreshRuntimeWeaponState;
        }

        runtimeCombatStats = stats;

        if (runtimeCombatStats != null)
        {
            runtimeCombatStats.ModifiersChanged +=
                RefreshRuntimeWeaponState;
        }

        RefreshRuntimeWeaponState();
    }

    private void RefreshRuntimeWeaponState()
    {
        if (ammoState == null || weapon == null)
        {
            return;
        }

        int capacity = runtimeCombatStats != null
            ? runtimeCombatStats.ApplyMagazineCapacity(
                weapon.MagazineCapacity)
            : Mathf.Max(1, weapon.MagazineCapacity);

        if (ammoState.SetMagazineCapacity(capacity))
        {
            AmmoChanged?.Invoke();
        }

        if (weaponAnimator != null && IsReloading)
        {
            weaponAnimator.speed = ReloadAnimationSpeed;
        }
        else if (weaponAnimator != null)
        {
            weaponAnimator.speed = FireAnimationSpeed;
        }

        RuntimePropertiesChanged?.Invoke();
    }

    private void ResetWeaponAnimationSpeed()
    {
        if (weaponAnimator != null)
        {
            weaponAnimator.speed = 1f;
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
        GameObject damageTarget = null;

        if (damageable != null)
        {
            Health targetHealth =
                hit.collider.GetComponentInParent<Health>();
            damageTarget = targetHealth != null
                ? targetHealth.gameObject
                : hit.collider.gameObject;
            damageResult = damageable.ApplyDamage(
                new DamageInfo(
                    Damage,
                    hit.point,
                    shotDirection,
                    shooterRoot != null
                        ? shooterRoot.gameObject
                        : gameObject,
                    DamageType.Hitscan));

            if (appliesBurnOnHit && damageResult.WasApplied &&
                !damageResult.WasKilled && targetHealth != null)
            {
                targetHealth.GetComponent<EnemyBurnEffectController>()?
                    .ApplyBurn(
                        shooterRoot != null
                            ? shooterRoot.gameObject
                            : gameObject);
            }
        }

        return new ShotResult(
            true,
            hit.point,
            hit.normal,
            SurfaceResolver.Resolve(hit.collider),
            damageResult,
            damageTarget,
            hit.collider.gameObject);
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
