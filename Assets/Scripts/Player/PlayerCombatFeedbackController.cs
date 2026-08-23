using System;
using UnityEngine;

public enum DamageIndicatorSide
{
    Front,
    Right,
    Back,
    Left
}

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
    private PlayerVitalsHudPresenter vitalsHud;

    public event Action ViewChanged;

    public bool LegacyOnGuiEnabled { get; private set; } = true;
    public bool IsInitialized { get; private set; }

    public int DamageFeedbackCount { get; private set; }
    public DamageIndicatorSide LastDamageSide { get; private set; }
    public float DamageFlashAlpha { get; private set; }

    private void Start()
    {
        if (IsInitialized)
        {
            return;
        }

        Debug.LogError(
            $"[{nameof(PlayerCombatFeedbackController)}] '{name}' was " +
            "not initialized by PlayerCombatCompositionRoot.",
            this);
        enabled = false;
    }

    public void Configure(
        PlayerCombatController combatController,
        PlayerController playerController,
        PlayerInputReader inputReader,
        PlayerCrosshairPresenter crosshairPresenter,
        Health playerHealth,
        PlayerVitalsHudPresenter vitalsPresenter,
        AudioSource feedbackAudioSource,
        CombatFeedbackAudioProfile audioProfile)
    {
        combat = combatController;
        player = playerController;
        input = inputReader;
        crosshair = crosshairPresenter;
        health = playerHealth;
        vitalsHud = vitalsPresenter;
        damageAudioSource = feedbackAudioSource;
        playerDamagedClip = audioProfile != null
            ? audioProfile.PlayerDamaged
            : null;
    }

    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (combat == null || player == null || input == null ||
            crosshair == null || health == null || vitalsHud == null ||
            damageAudioSource == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerCombatFeedbackController)}] '{name}' " +
                "has incomplete injected dependencies.",
                this);
            enabled = false;
            return false;
        }

        combat.ShotResolved += HandleShotResolved;
        health.Damaged += HandlePlayerDamaged;
        IsInitialized = true;
        return true;
    }

    private void Update()
    {
        float previousAlpha = DamageFlashAlpha;
        DamageFlashAlpha = Mathf.MoveTowards(
            DamageFlashAlpha,
            0f,
            Time.unscaledDeltaTime * 1.8f);

        if (!Mathf.Approximately(previousAlpha, DamageFlashAlpha))
        {
            ViewChanged?.Invoke();
        }

        if (crosshair != null && player != null && input != null)
        {
            crosshair.SetMotionState(
                input.Move.magnitude,
                player.IsSprinting);
        }
    }

    public void SetLegacyPresentation(bool enabled)
    {
        LegacyOnGuiEnabled = enabled;
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
        DamageFlashAlpha = 1f;
        LastDamageSide = ResolveDamageSide(damage);
        ViewChanged?.Invoke();

        if (playerDamagedClip != null)
        {
            damageAudioSource.PlayOneShot(playerDamagedClip);
        }
    }

    private DamageIndicatorSide ResolveDamageSide(DamageInfo damage)
    {
        Vector3 toSource = damage.Source != null
            ? damage.Source.transform.position - transform.position
            : -damage.HitDirection;
        toSource.y = 0f;

        if (toSource.sqrMagnitude <= 0.001f)
        {
            return DamageIndicatorSide.Front;
        }

        toSource.Normalize();
        float horizontal = Vector3.Dot(transform.right, toSource);
        float forward = Vector3.Dot(transform.forward, toSource);

        if (Mathf.Abs(horizontal) > Mathf.Abs(forward))
        {
            return horizontal >= 0f
                ? DamageIndicatorSide.Right
                : DamageIndicatorSide.Left;
        }

        return forward >= 0f
            ? DamageIndicatorSide.Front
            : DamageIndicatorSide.Back;
    }

    private void OnGUI()
    {
        if (!LegacyOnGuiEnabled || DamageFlashAlpha <= 0f)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.depth = -80;
        GUI.color = new Color(
            0.9f,
            0.02f,
            0.01f,
            DamageFlashAlpha * 0.12f);
        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            Texture2D.whiteTexture);
        GUI.color = new Color(
            1f,
            0.05f,
            0.02f,
            DamageFlashAlpha * 0.78f);
        Rect indicator = LastDamageSide switch
        {
            DamageIndicatorSide.Left =>
                new Rect(0f, Screen.height * 0.25f, 20f,
                    Screen.height * 0.5f),
            DamageIndicatorSide.Right =>
                new Rect(Screen.width - 20f, Screen.height * 0.25f,
                    20f, Screen.height * 0.5f),
            DamageIndicatorSide.Back =>
                new Rect(Screen.width * 0.25f, Screen.height - 20f,
                    Screen.width * 0.5f, 20f),
            _ => new Rect(
                Screen.width * 0.25f,
                0f,
                Screen.width * 0.5f,
                20f)
        };
        GUI.DrawTexture(indicator, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
