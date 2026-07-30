using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class PooledAudioVoice : MonoBehaviour
{
    private AudioSource source;
    private RuntimeGameObjectPool owner;
    private float remainingLifetime;

    public void Prepare()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.minDistance = 1f;
        source.maxDistance = 60f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    public void Play(
        RuntimeGameObjectPool sourcePool,
        AudioClip clip,
        Vector3 position,
        float volume,
        float pitch)
    {
        owner = sourcePool;
        transform.position = position;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
        remainingLifetime =
            clip.length / Mathf.Abs(source.pitch) + 0.05f;
        source.Play();
    }

    private void Update()
    {
        remainingLifetime -= Time.unscaledDeltaTime;

        if (remainingLifetime <= 0f)
        {
            owner?.Return(gameObject);
        }
    }

    private void OnDisable()
    {
        if (source != null)
        {
            source.Stop();
            source.clip = null;
        }

        owner = null;
        remainingLifetime = 0f;
    }
}
