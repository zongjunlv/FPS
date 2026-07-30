using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "CombatSoundEvents",
    menuName = "FPS/Combat Sound Event Channel")]
public sealed class CombatSoundEventChannel : ScriptableObject
{
    public event Action<SoundStimulus> SoundPublished;
    public int PublishCount { get; private set; }
    public SoundStimulus LastStimulus { get; private set; }

    public void Publish(SoundStimulus stimulus)
    {
        if (stimulus.Radius <= 0f ||
            stimulus.Intensity <= 0f)
        {
            return;
        }

        PublishCount++;
        LastStimulus = stimulus;
        SoundPublished?.Invoke(stimulus);
    }
}
