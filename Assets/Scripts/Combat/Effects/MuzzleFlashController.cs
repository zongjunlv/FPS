using UnityEngine;

public class MuzzleFlashController : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private ParticleSystem[] particles;
    [SerializeField] private Light muzzleLight;

    [Header("Setting")]
    [SerializeField] private float lightDuration = 0.05f;

    private float remainingTime;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        muzzleLight.enabled = false;
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
        foreach(ParticleSystem particle in particles)
        {
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(true);
        }
        muzzleLight.enabled = true;
        remainingTime = lightDuration;
    }

    private void OnDisable()
    {
        muzzleLight.enabled = false;
        remainingTime = 0f;
    }
}
