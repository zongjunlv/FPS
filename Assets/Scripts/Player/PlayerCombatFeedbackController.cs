using UnityEngine;

[RequireComponent(typeof(Health))]
public sealed class PlayerCombatFeedbackController : MonoBehaviour
{
    private PlayerCombatController combat;
    private PlayerController player;
    private PlayerInputReader input;
    private PlayerCrosshairPresenter crosshair;
    private Health health;
    private AudioSource damageAudioSource;
    private AudioClip playerDamagedClip;

    public int DamageFeedbackCount { get; private set; }

    private void Start()
    {
        combat = GetComponent<PlayerCombatController>();
        player = GetComponent<PlayerController>();
        input = GetComponent<PlayerInputReader>();
        crosshair = GetComponent<PlayerCrosshairPresenter>();
        health = GetComponent<Health>();
        health.Initialize(100f, 100f);
        PlayerVitalsHudPresenter vitalsHud =
            GetComponent<PlayerVitalsHudPresenter>();

        if (vitalsHud == null)
        {
            vitalsHud =
                gameObject.AddComponent<PlayerVitalsHudPresenter>();
        }

        vitalsHud.Bind(health);
        damageAudioSource = gameObject.AddComponent<AudioSource>();
        damageAudioSource.playOnAwake = false;
        damageAudioSource.spatialBlend = 0f;
        CombatFeedbackAudioProfile profile =
            Resources.Load<CombatFeedbackAudioProfile>(
                "CombatFeedbackAudio");
        playerDamagedClip =
            profile != null ? profile.PlayerDamaged : null;

        if (combat != null)
        {
            combat.ShotResolved += HandleShotResolved;
        }

        if (health != null)
        {
            health.Damaged += HandlePlayerDamaged;
        }
    }

    private void Update()
    {
        if (crosshair != null && player != null && input != null)
        {
            crosshair.SetMotionState(
                input.Move.magnitude,
                player.IsSprinting);
        }
    }

    private void OnDestroy()
    {
        if (combat != null)
        {
            combat.ShotResolved -= HandleShotResolved;
        }

        if (health != null)
        {
            health.Damaged -= HandlePlayerDamaged;
        }
    }

    private void HandleShotResolved(ShotResult result)
    {
        if (crosshair == null)
        {
            return;
        }

        crosshair.AddFireBloom();

        if (result.FeedbackKind != HitFeedbackKind.None)
        {
            crosshair.ShowHitFeedback(result.FeedbackKind);
        }
    }

    private void HandlePlayerDamaged(DamageInfo damage)
    {
        DamageFeedbackCount++;

        if (playerDamagedClip != null)
        {
            damageAudioSource.PlayOneShot(playerDamagedClip);
        }
    }
}
