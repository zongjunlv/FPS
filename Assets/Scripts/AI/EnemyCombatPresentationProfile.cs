using UnityEngine;

[CreateAssetMenu(
    fileName = "EnemyCombatPresentation",
    menuName = "FPS/Enemy Combat Presentation")]
public sealed class EnemyCombatPresentationProfile : ScriptableObject
{
    public AnimationClip AttackClip;
    public AudioClip AttackImpact;
}
