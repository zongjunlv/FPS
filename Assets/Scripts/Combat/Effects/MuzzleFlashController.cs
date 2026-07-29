using UnityEngine;

public class MuzzleFlashController : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private ParticleSystem[] particles;
    [SerializeField] private Light muzzleLight;

    [Header("Setting")]
    [SerializeField] private float lightDuration = 0.05f;

    private float remainingTime;
    public int PlayCount { get; private set; }
    public bool IsLightActive =>
        muzzleLight != null && muzzleLight.enabled;

    private void Awake()
    {
        ResetEffect();
    }

    private void OnEnable()
    {
        ResetEffect();
    }

    // Update is called once per frame
    void Update()
    {
        if(!muzzleLight.enabled) return;
        remainingTime -= Time.deltaTime;
        if(remainingTime <= 0f)
        {
            muzzleLight.enabled = false;
        }
    }

    public void Play()
    {
        PlayCount++;
        foreach (ParticleSystem particle in particles)
        {
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.loop = false;
            main.playOnAwake = false;
            particle.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(false);
        }

        if (muzzleLight != null)
        {
            muzzleLight.enabled = true;
        }
        remainingTime = lightDuration;
    }

    private void OnDisable()
    {
        StopParticles();
        if (muzzleLight != null)
        {
            muzzleLight.enabled = false;
        }
        remainingTime = 0f;
    }

    private void ResetEffect()
    {
        foreach (ParticleSystem particle in particles)
        {
            if (particle == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particle.main;
            main.loop = false;
            main.playOnAwake = false;
        }

        StopParticles();
        if (muzzleLight != null)
        {
            muzzleLight.enabled = false;
        }
        remainingTime = 0f;
    }

    private void StopParticles()
    {
        foreach (ParticleSystem particle in particles)
        {
            if (particle != null)
            {
                particle.Stop(
                    false,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
