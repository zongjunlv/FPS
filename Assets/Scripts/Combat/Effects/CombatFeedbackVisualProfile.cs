using UnityEngine;

[CreateAssetMenu(
    fileName = "CombatFeedbackVisual",
    menuName = "FPS/Combat Feedback Visual Profile")]
public sealed class CombatFeedbackVisualProfile : ScriptableObject
{
    public Material SurfaceMaterial;
    public Material MetalSparkMaterial;
}
