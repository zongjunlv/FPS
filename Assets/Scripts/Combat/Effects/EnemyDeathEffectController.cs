using UnityEngine;

public sealed class EnemyDeathEffectController : MonoBehaviour
{
    public float Lifetime { get; private set; }
    public int ActiveParticleSystemCount { get; private set; }

    public static GameObject Present(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
        {
            GameObject fallback = FallbackFeedbackEffect.Create();
            fallback.name = "Enemy Death Fallback";
            fallback.transform.position = position + Vector3.up * 0.25f;
            fallback.GetComponent<FallbackFeedbackEffect>().Play(0.3f, true);
            return fallback;
        }

        GameObject effect = Instantiate(prefab, position, Quaternion.identity);
        EnemyDeathEffectController controller =
            effect.GetComponent<EnemyDeathEffectController>();
        if (controller == null)
            controller = effect.AddComponent<EnemyDeathEffectController>();
        controller.Configure();
        return effect;
    }

    public void Configure(
        float scale = 0.42f,
        float lifetime = 1.2f)
    {
        transform.localScale *= Mathf.Clamp(scale, 0.1f, 1f);
        Lifetime = Mathf.Clamp(lifetime, 0.2f, 1.3f);
        ActiveParticleSystemCount = 0;

        foreach (ParticleSystem particles in
                 GetComponentsInChildren<ParticleSystem>(true))
        {
            bool isHeavyDarkEffect =
                particles.name.Contains("Smoke") ||
                particles.name.Contains("Burn Mark");

            if (isHeavyDarkEffect)
            {
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.gameObject.SetActive(false);
                continue;
            }

            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.simulationSpeed = 1.45f;
            main.startLifetimeMultiplier *= 0.42f;
            main.startSizeMultiplier *= 0.68f;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTimeMultiplier *= 0.45f;
            ActiveParticleSystemCount++;
        }

        Destroy(gameObject, Lifetime);
    }
}
