using UnityEngine;

public sealed class PooledEffectInstance : MonoBehaviour
{
    private ParticleSystem[] particles;
    private RuntimeGameObjectPool owner;
    private float remainingLifetime;
    private string pooledName;

    public void Prepare()
    {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        pooledName = gameObject.name;
    }

    public void Play(
        RuntimeGameObjectPool sourcePool,
        float lifetime)
    {
        owner = sourcePool;
        remainingLifetime = Mathf.Max(0.01f, lifetime);

        foreach (ParticleSystem particle in particles)
        {
            if (particle == null)
            {
                continue;
            }

            particle.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(true);
        }
    }

    private void Update()
    {
        remainingLifetime -= Time.deltaTime;

        if (remainingLifetime <= 0f)
        {
            owner?.Return(gameObject);
        }
    }

    private void OnDisable()
    {
        if (particles != null)
        {
            foreach (ParticleSystem particle in particles)
            {
                if (particle != null)
                {
                    particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        owner = null;
        remainingLifetime = 0f;

        if (!string.IsNullOrEmpty(pooledName))
        {
            gameObject.name = pooledName;
        }
    }
}
