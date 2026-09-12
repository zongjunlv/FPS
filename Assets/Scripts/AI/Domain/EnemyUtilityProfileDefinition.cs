using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class EnemyUtilityActionDefinition
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField] private EnemyUtilityActionKind kind;
    [SerializeField, Min(0f)] private float baseWeight = 1f;
    [SerializeField, Min(0f)] private float minimumScore = 0.05f;
    [SerializeField, Min(0f)] private float cooldown;
    [SerializeField, Min(0f)] private float commitmentDuration = 0.5f;
    [SerializeField, Min(0f)] private float hysteresisBonus = 0.05f;
    [SerializeField, Min(0f)] private float movementDistance = 5f;
    [SerializeField, Min(0.1f)] private float movementSpeedMultiplier = 1f;
    [SerializeField] private string fallbackActionId = "chase";
    [SerializeField] private List<EnemyUtilityConsiderationDefinition>
        considerations = new();

    public EnemyUtilityActionDefinition(
        string id,
        string label,
        EnemyUtilityActionKind actionKind,
        float weight,
        float threshold,
        float actionCooldown,
        float commitment,
        float hysteresis,
        float configuredMovementDistance,
        float configuredMovementSpeedMultiplier,
        string fallback,
        IEnumerable<EnemyUtilityConsiderationDefinition> configuredConsiderations)
    {
        stableId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        displayName = string.IsNullOrWhiteSpace(label)
            ? stableId
            : label.Trim();
        kind = actionKind;
        baseWeight = Mathf.Max(0f, weight);
        minimumScore = Mathf.Max(0f, threshold);
        cooldown = Mathf.Max(0f, actionCooldown);
        commitmentDuration = Mathf.Max(0f, commitment);
        hysteresisBonus = Mathf.Max(0f, hysteresis);
        movementDistance = Mathf.Max(0f, configuredMovementDistance);
        movementSpeedMultiplier = Mathf.Max(
            0.1f,
            configuredMovementSpeedMultiplier);
        fallbackActionId = string.IsNullOrWhiteSpace(fallback)
            ? string.Empty
            : fallback.Trim();
        considerations = configuredConsiderations != null
            ? new List<EnemyUtilityConsiderationDefinition>(
                configuredConsiderations)
            : new List<EnemyUtilityConsiderationDefinition>();
    }

    public string StableId => string.IsNullOrWhiteSpace(stableId)
        ? string.Empty
        : stableId.Trim();
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? StableId
        : displayName.Trim();
    public EnemyUtilityActionKind Kind => kind;
    public float BaseWeight => Mathf.Max(0f, baseWeight);
    public float MinimumScore => Mathf.Max(0f, minimumScore);
    public float Cooldown => Mathf.Max(0f, cooldown);
    public float CommitmentDuration => Mathf.Max(0f, commitmentDuration);
    public float HysteresisBonus => Mathf.Max(0f, hysteresisBonus);
    public float MovementDistance => Mathf.Max(0f, movementDistance);
    public float MovementSpeedMultiplier => Mathf.Max(
        0.1f,
        movementSpeedMultiplier);
    public string FallbackActionId => string.IsNullOrWhiteSpace(
        fallbackActionId)
        ? string.Empty
        : fallbackActionId.Trim();
    public IReadOnlyList<EnemyUtilityConsiderationDefinition> Considerations =>
        considerations;

    public float Evaluate(EnemyUtilityWorldFacts facts)
    {
        float score = BaseWeight;

        if (considerations == null)
        {
            return score;
        }

        for (int index = 0; index < considerations.Count; index++)
        {
            score *= considerations[index].Evaluate(facts);

            if (score <= Mathf.Epsilon)
            {
                return 0f;
            }
        }

        return Mathf.Max(0f, score);
    }
}

[CreateAssetMenu(
    fileName = "EnemyUtilityProfile",
    menuName = "FPS/Enemies/Utility AI Profile")]
public sealed class EnemyUtilityProfileDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string defaultActionId = "chase";
    [SerializeField, Min(1f)] private float friendlyScanRadius = 14f;
    [SerializeField] private List<EnemyUtilityActionDefinition> actions = new();

    public string StableId => string.IsNullOrWhiteSpace(stableId)
        ? name
        : stableId.Trim();
    public string DefaultActionId => string.IsNullOrWhiteSpace(defaultActionId)
        ? string.Empty
        : defaultActionId.Trim();
    public float FriendlyScanRadius => Mathf.Max(1f, friendlyScanRadius);
    public IReadOnlyList<EnemyUtilityActionDefinition> Actions => actions;

    public void Configure(
        string id,
        string configuredDefaultActionId,
        float configuredFriendlyScanRadius,
        IEnumerable<EnemyUtilityActionDefinition> configuredActions)
    {
        stableId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        defaultActionId = string.IsNullOrWhiteSpace(configuredDefaultActionId)
            ? string.Empty
            : configuredDefaultActionId.Trim();
        friendlyScanRadius = Mathf.Max(1f, configuredFriendlyScanRadius);
        actions = configuredActions != null
            ? new List<EnemyUtilityActionDefinition>(configuredActions)
            : new List<EnemyUtilityActionDefinition>();
        actions.RemoveAll(action => action == null ||
            string.IsNullOrWhiteSpace(action.StableId));
    }

    public EnemyUtilityActionDefinition FindAction(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId) || actions == null)
        {
            return null;
        }

        for (int index = 0; index < actions.Count; index++)
        {
            EnemyUtilityActionDefinition action = actions[index];

            if (action != null && string.Equals(
                    action.StableId,
                    actionId,
                    StringComparison.Ordinal))
            {
                return action;
            }
        }

        return null;
    }
}
