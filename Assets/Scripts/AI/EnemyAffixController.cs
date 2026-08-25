using FPS.GameplayEffects;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class EnemyAffixController : MonoBehaviour
{
    private Health health;
    private EnemyBurnEffectController overhead;
    private GameplayEffectRuntime runtime;
    private EnemyAffixDefinition activeAffix;
    private float baseArmor;

    public bool HasAffix => activeAffix != null;
    public EnemyAffixDefinition ActiveAffix => activeAffix;
    public int ActiveEffectCount => runtime?.ActiveInstances.Count ?? 0;
    public float LootQuantityMultiplier => Evaluate(
        GameplayAttributeId.EnemyLootQuantity,
        1f);

    private void Awake()
    {
        health = GetComponent<Health>();
        overhead = GetComponent<EnemyBurnEffectController>();
        runtime = new GameplayEffectRuntime(
            gameObject,
            "Enemy Affix Effects");
        baseArmor = health != null ? health.MaxArmor : 0f;
    }

    public bool ApplyAffix(EnemyAffixDefinition definition, float armorBase)
    {
        ClearAffix(false);
        baseArmor = Mathf.Max(0f, armorBase);

        if (definition == null || definition.GameplayEffect == null)
        {
            RestoreBaseArmor();
            return false;
        }

        activeAffix = definition;
        runtime.SetDebugBaseValue(
            GameplayAttributeId.EnemyMaximumArmor,
            baseArmor);
        EnemyController enemy = GetComponent<EnemyController>();
        runtime.SetDebugBaseValue(
            GameplayAttributeId.EnemyAttackDamage,
            enemy != null ? enemy.BaseAttackDamage : 20f);
        runtime.SetDebugBaseValue(
            GameplayAttributeId.EnemyExperienceReward,
            enemy != null ? enemy.BaseRewardExperience : 40f);
        runtime.SetDebugBaseValue(
            GameplayAttributeId.EnemyLootQuantity,
            1f);
        runtime.Apply(
            definition.GameplayEffect,
            new GameplayEffectContext(
                definition.StableId,
                definition,
                gameObject));
        float maximumArmor = Evaluate(
            GameplayAttributeId.EnemyMaximumArmor,
            baseArmor);
        health?.SetMaximumArmor(maximumArmor);
        EnsureOverhead();
        overhead?.SetAffixStatus(
            definition.StatusLabel,
            definition.StatusColor);
        return true;
    }

    public void ClearAffix(bool restoreArmor = true)
    {
        activeAffix = null;
        runtime?.Clear();
        EnsureOverhead();
        overhead?.ClearAffixStatus();

        if (restoreArmor)
        {
            RestoreBaseArmor();
        }
    }

    public float Evaluate(GameplayAttributeId attribute, float baseValue)
    {
        return runtime != null
            ? runtime.Evaluate(attribute, baseValue)
            : baseValue;
    }

    private void RestoreBaseArmor()
    {
        if (health != null && !health.IsDead)
        {
            health.SetMaximumArmor(baseArmor);
        }
    }

    private void EnsureOverhead()
    {
        overhead ??= GetComponent<EnemyBurnEffectController>();
    }
}
