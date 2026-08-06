using System;
using UnityEngine;

public sealed class PlayerVitalsHudPresenter : MonoBehaviour
{
    private Health health;
    private PlayerHudVisualProfile profile;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private float trailingHealth = 1f;
    private float trailingArmor = 1f;

    public event Action ViewChanged;

    public bool LegacyOnGuiEnabled { get; private set; } = true;

    public float DisplayedHealth =>
        health != null ? health.CurrentHealth : 0f;
    public float DisplayedArmor =>
        health != null ? health.CurrentArmor : 0f;
    public float HealthNormalized =>
        health != null
            ? health.CurrentHealth / health.MaxHealth
            : 0f;
    public float ArmorNormalized =>
        health != null && health.MaxArmor > 0f
            ? health.CurrentArmor / health.MaxArmor
            : 0f;
    public float TrailingHealthNormalized => trailingHealth;
    public float TrailingArmorNormalized => trailingArmor;

    public void Bind(Health target)
    {
        if (health != null)
        {
            health.VitalsChanged -= HandleVitalsChanged;
        }

        health = target;
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
        trailingHealth = HealthNormalized;
        trailingArmor = ArmorNormalized;

        if (health != null)
        {
            health.VitalsChanged += HandleVitalsChanged;
        }

        ViewChanged?.Invoke();
    }

    public void SetLegacyPresentation(bool enabled)
    {
        LegacyOnGuiEnabled = enabled;
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.VitalsChanged -= HandleVitalsChanged;
        }
    }

    private void Update()
    {
        float previousHealth = trailingHealth;
        float previousArmor = trailingArmor;
        trailingHealth = Mathf.MoveTowards(
            trailingHealth,
            HealthNormalized,
            Time.unscaledDeltaTime * 0.55f);
        trailingArmor = Mathf.MoveTowards(
            trailingArmor,
            ArmorNormalized,
            Time.unscaledDeltaTime * 0.75f);

        if (!Mathf.Approximately(previousHealth, trailingHealth) ||
            !Mathf.Approximately(previousArmor, trailingArmor))
        {
            ViewChanged?.Invoke();
        }
    }

    private void HandleVitalsChanged()
    {
        trailingHealth = Mathf.Max(
            trailingHealth,
            HealthNormalized);
        trailingArmor = Mathf.Max(
            trailingArmor,
            ArmorNormalized);
        ViewChanged?.Invoke();
    }

    private void OnGUI()
    {
        if (!LegacyOnGuiEnabled || health == null)
        {
            return;
        }

        EnsureStyles();
        float scale = Mathf.Clamp(
            Screen.height / 1080f,
            0.75f,
            1.25f);
        float width = 320f * scale;
        float height = 104f * scale;
        Rect panel = new Rect(
            28f * scale,
            Screen.height - height - 28f * scale,
            width,
            height);
        DrawRect(panel, profile.PanelColor);

        float inset = 16f * scale;
        Rect armorBar = new Rect(
            panel.x + inset,
            panel.y + 18f * scale,
            width - inset * 2f,
            22f * scale);
        Rect healthBar = new Rect(
            panel.x + inset,
            panel.y + 58f * scale,
            width - inset * 2f,
            30f * scale);
        DrawBar(
            armorBar,
            ArmorNormalized,
            trailingArmor,
            profile.ArmorColor);
        DrawBar(
            healthBar,
            HealthNormalized,
            trailingHealth,
            profile.HealthColor);

        GUI.Label(
            armorBar,
            $"  ARMOR   {Mathf.CeilToInt(DisplayedArmor):000}",
            labelStyle);
        GUI.Label(
            healthBar,
            $"  HEALTH  {Mathf.CeilToInt(DisplayedHealth):000}",
            valueStyle);
    }

    private void DrawBar(
        Rect rect,
        float fill,
        float trail,
        Color color)
    {
        DrawRect(rect, new Color(0f, 0f, 0f, 0.72f));
        Rect trailRect = rect;
        trailRect.width *= Mathf.Clamp01(trail);
        DrawRect(trailRect, profile.DamageTrailColor);
        Rect fillRect = rect;
        fillRect.width *= Mathf.Clamp01(fill);
        DrawRect(fillRect, color);
    }

    private void EnsureStyles()
    {
        if (labelStyle != null)
        {
            return;
        }

        profile ??= Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            font = profile.Font,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        valueStyle = new GUIStyle(labelStyle)
        {
            fontSize = 17
        };
    }

    private static void DrawRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
