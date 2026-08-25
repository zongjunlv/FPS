using System;
using FPS.GameplayEffects;
using UnityEngine;

[CreateAssetMenu(
    fileName = "EnemyAffixDefinition",
    menuName = "FPS/Enemies/Affix Definition")]
public sealed class EnemyAffixDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string statusLabel = "ELITE";
    [SerializeField] private Color statusColor =
        new(1f, 0.72f, 0.12f, 1f);
    [SerializeField] private GameplayEffectDefinition gameplayEffect;

    public string StableId => stableId;
    public string StatusLabel => string.IsNullOrWhiteSpace(statusLabel)
        ? "ELITE"
        : statusLabel.Trim();
    public Color StatusColor => statusColor;
    public GameplayEffectDefinition GameplayEffect => gameplayEffect;

    public void Configure(
        string id,
        string label,
        Color color,
        GameplayEffectDefinition effect)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An enemy affix requires a stable ID.",
                nameof(id));
        }

        if (effect == null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        stableId = id.Trim();
        statusLabel = string.IsNullOrWhiteSpace(label)
            ? "ELITE"
            : label.Trim();
        statusColor = color;
        gameplayEffect = effect;
    }
}
