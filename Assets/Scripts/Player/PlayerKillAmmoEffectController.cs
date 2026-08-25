using FPS.GameplayEffects;
using UnityEngine;

public sealed class PlayerKillAmmoEffectController : MonoBehaviour
{
    private const string EffectId = "player.kill-magazine-refill";

    [SerializeField, Min(1)] private int refillAmount = 3;
    [SerializeField] private bool applyOnInitialize = true;

    private PlayerCombatController combat;
    private PlayerCombatEventRouter eventRouter;
    private GameplayEffectDefinition definition;
    private GameplayEffectRuntime runtime;
    private GameplayEffectInstance effectInstance;

    public bool IsInitialized { get; private set; }
    public bool IsEffectActive => effectInstance != null;
    public int RefillAmount => Mathf.Max(1, refillAmount);
    public int TriggerCount { get; private set; }
    public int TotalRoundsGranted { get; private set; }

    public void Configure(
        PlayerCombatController combatController,
        PlayerCombatEventRouter combatEvents)
    {
        combat = combatController;
        eventRouter = combatEvents;
    }

    public void ConfigureRefillAmount(int amount)
    {
        refillAmount = Mathf.Max(1, amount);
    }

    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (combat == null || eventRouter == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerKillAmmoEffectController)}] '{name}' " +
                "requires combat and event router dependencies.",
                this);
            enabled = false;
            return false;
        }

        EnsureRuntime();
        IsInitialized = true;

        if (applyOnInitialize)
        {
            ApplyEffect();
        }

        return true;
    }

    public bool ApplyEffect()
    {
        if (!IsInitialized || effectInstance != null)
        {
            return false;
        }

        effectInstance = runtime.Apply(
            definition,
            new GameplayEffectContext(EffectId, definition, gameObject));
        eventRouter.Events.Published += HandleGameplayEvent;
        return true;
    }

    public bool RemoveEffect()
    {
        if (effectInstance == null)
        {
            return false;
        }

        eventRouter.Events.Published -= HandleGameplayEvent;
        runtime.Remove(effectInstance.InstanceId);
        effectInstance = null;
        return true;
    }

    private void OnDestroy()
    {
        RemoveEffect();

        if (definition != null)
        {
            Destroy(definition);
        }
    }

    private void EnsureRuntime()
    {
        runtime ??= new GameplayEffectRuntime(
            gameObject,
            "Player Kill Event Effects");

        if (definition != null)
        {
            return;
        }

        definition = ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        definition.name = "Kill Magazine Refill Effect";
        definition.hideFlags = HideFlags.HideAndDontSave;
        definition.Configure(EffectId);
    }

    private void HandleGameplayEvent(GameplayEffectEventContext context)
    {
        if (context.EventType != GameplayEffectEventType.EnemyKilled ||
            combat.IsSwitching)
        {
            return;
        }

        WeaponController weapon = combat.EquippedWeapon;

        if (weapon == null)
        {
            return;
        }

        int granted = weapon.AddMagazineAmmo(RefillAmount);

        if (granted <= 0)
        {
            return;
        }

        TriggerCount++;
        TotalRoundsGranted += granted;
    }
}
