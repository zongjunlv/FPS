using System;
using FPS.GameplayEffects;
using UnityEngine;

public sealed class PlayerLowHealthFireRateEffectController : MonoBehaviour
{
    private const string EffectId = "player.low-health-fire-rate";

    [SerializeField, Range(0.01f, 0.99f)]
    private float healthThresholdNormalized = 0.35f;
    [SerializeField, Range(0.01f, 2f)]
    private float fireRateBonus = 0.35f;
    [SerializeField] private bool applyOnInitialize = true;

    private Health health;
    private PlayerRuntimeCombatStats runtimeStats;
    private GameplayEffectDefinition definition;
    private GameplayEffectConditionState condition;
    private GameplayEffectInstance activeInstance;
    private bool subscribed;

    public event Action StateChanged;

    public bool IsInitialized { get; private set; }
    public bool IsEffectInstalled { get; private set; }
    public bool IsConditionActive => activeInstance != null;
    public float HealthThresholdNormalized =>
        Mathf.Clamp01(healthThresholdNormalized);
    public float FireRateBonus => Mathf.Max(0.01f, fireRateBonus);
    public float CurrentHealthNormalized => health != null &&
        health.MaxHealth > Mathf.Epsilon
            ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth)
            : 0f;
    public string StatusText => IsConditionActive
        ? $"低生命增幅  射速 +{Mathf.RoundToInt(FireRateBonus * 100f)}%"
        : string.Empty;
    public int ActivationCount { get; private set; }
    public int DeactivationCount { get; private set; }

    public void Configure(
        Health playerHealth,
        PlayerRuntimeCombatStats combatStats)
    {
        health = playerHealth;
        runtimeStats = combatStats;
    }

    public void ConfigureCondition(float thresholdNormalized, float bonus)
    {
        if (IsInitialized)
        {
            throw new InvalidOperationException(
                "A running low-health effect cannot be reconfigured.");
        }

        healthThresholdNormalized = Mathf.Clamp(
            thresholdNormalized,
            0.01f,
            0.99f);
        fireRateBonus = Mathf.Max(0.01f, bonus);
    }

    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (health == null || runtimeStats == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerLowHealthFireRateEffectController)}] " +
                $"'{name}' requires health and runtime stats.",
                this);
            enabled = false;
            return false;
        }

        condition = new GameplayEffectConditionState(
            HealthThresholdNormalized);
        EnsureDefinition();
        IsInitialized = true;

        if (applyOnInitialize)
        {
            ApplyEffect();
        }

        return true;
    }

    public bool ApplyEffect()
    {
        if (!IsInitialized || IsEffectInstalled)
        {
            return false;
        }

        IsEffectInstalled = true;
        Subscribe();
        EvaluateCondition();
        return true;
    }

    public bool RemoveEffect()
    {
        if (!IsEffectInstalled)
        {
            return false;
        }

        IsEffectInstalled = false;
        Unsubscribe();
        condition?.Reset();
        RemoveActiveInstance();
        StateChanged?.Invoke();
        return true;
    }

    private void OnDestroy()
    {
        if (IsEffectInstalled)
        {
            RemoveEffect();
        }
        else
        {
            RemoveActiveInstance();
        }

        if (definition != null)
        {
            Destroy(definition);
        }
    }

    private void EnsureDefinition()
    {
        if (definition != null)
        {
            return;
        }

        definition = ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        definition.name = "Low Health Fire Rate Effect";
        definition.hideFlags = HideFlags.HideAndDontSave;
        definition.Configure(
            EffectId,
            new GameplayEffectModifier(
                GameplayAttributeId.WeaponFireRate,
                GameplayModifierOperation.Add,
                FireRateBonus));
    }

    private void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        health.VitalsChanged += EvaluateCondition;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || health == null)
        {
            return;
        }

        health.VitalsChanged -= EvaluateCondition;
        subscribed = false;
    }

    private void EvaluateCondition()
    {
        if (!IsEffectInstalled || condition == null || health == null)
        {
            return;
        }

        GameplayEffectConditionTransition transition = condition.Evaluate(
            health.CurrentHealth,
            health.MaxHealth,
            health.IsDead);

        switch (transition)
        {
            case GameplayEffectConditionTransition.Activated:
                activeInstance = runtimeStats.ApplyGameplayEffect(
                    definition,
                    EffectId,
                    this);

                if (activeInstance != null)
                {
                    ActivationCount++;
                }
                break;
            case GameplayEffectConditionTransition.Deactivated:
                RemoveActiveInstance();
                break;
            default:
                return;
        }

        StateChanged?.Invoke();
    }

    private void RemoveActiveInstance()
    {
        if (activeInstance == null)
        {
            return;
        }

        runtimeStats?.RemoveGameplayEffect(activeInstance.InstanceId);
        activeInstance = null;
        DeactivationCount++;
    }
}
