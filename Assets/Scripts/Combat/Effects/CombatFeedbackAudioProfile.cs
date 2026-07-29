using UnityEngine;

[CreateAssetMenu(
    fileName = "CombatFeedbackAudio",
    menuName = "FPS/Combat Feedback Audio")]
public sealed class CombatFeedbackAudioProfile : ScriptableObject
{
    public AudioClip ConcreteImpact;
    public AudioClip MetalImpact;
    public AudioClip PlayerDamaged;
}
