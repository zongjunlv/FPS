using UnityEngine;

[CreateAssetMenu(
    fileName = "PlayerHudVisualProfile",
    menuName = "FPS/Player HUD Visual Profile")]
public sealed class PlayerHudVisualProfile : ScriptableObject
{
    public Font Font;
    public Color PanelColor = new Color(0.02f, 0.03f, 0.04f, 0.82f);
    public Color HealthColor = new Color(0.92f, 0.24f, 0.22f, 1f);
    public Color ArmorColor = new Color(0.18f, 0.68f, 0.95f, 1f);
    public Color DamageTrailColor = new Color(1f, 0.85f, 0.5f, 0.8f);
}
