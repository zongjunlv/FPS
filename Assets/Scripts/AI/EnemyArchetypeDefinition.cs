using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "EnemyArchetype",
    menuName = "FPS/Enemies/Archetype Definition")]
public sealed class EnemyArchetypeDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string enemyTypeId;
    [SerializeField] private EnemyController template;
    [SerializeField] private string templateAddress = "enemy/spider";
    [SerializeField] private LootRewardTier rewardTier = LootRewardTier.Normal;
    [SerializeField] private EnemyAffixDefinition affix;
    [SerializeField] private EnemyAbilitySetDefinition abilitySet;
    [SerializeField, Min(1)] private int threatCost = 1;
    [SerializeField] private string roleTag = "assault";

    public string StableId => stableId;
    public string EnemyTypeId => string.IsNullOrWhiteSpace(enemyTypeId)
        ? stableId
        : enemyTypeId.Trim();
    public EnemyController Template => template;
    public string TemplateAddress => templateAddress;

    public void ConfigureTemplateAddress(string address)
    {
        templateAddress = address?.Trim();
    }
    public LootRewardTier RewardTier => rewardTier;
    public EnemyAffixDefinition Affix => affix;
    public EnemyAbilitySetDefinition AbilitySet => abilitySet;
    public int ThreatCost => Mathf.Max(1, threatCost);
    public string RoleTag => ThreatRoleConstraint.NormalizeRole(roleTag);

    public void Configure(
        string id,
        string typeId,
        EnemyController enemyTemplate,
        LootRewardTier tier,
        EnemyAffixDefinition enemyAffix,
        EnemyAbilitySetDefinition enemyAbilitySet,
        int configuredThreatCost,
        string configuredRoleTag)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An enemy archetype requires a stable ID.", nameof(id));
        }

        stableId = id.Trim();
        enemyTypeId = string.IsNullOrWhiteSpace(typeId)
            ? stableId
            : typeId.Trim();
        template = enemyTemplate;
        rewardTier = tier;
        affix = enemyAffix;
        abilitySet = enemyAbilitySet;
        threatCost = Mathf.Max(1, configuredThreatCost);
        roleTag = ThreatRoleConstraint.NormalizeRole(configuredRoleTag);
    }
}
