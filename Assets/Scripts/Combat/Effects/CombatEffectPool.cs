using System;
using UnityEngine;

public sealed class CombatEffectPool : MonoBehaviour
{
    private const int ImpactCapacity = 48;
    private const int SparkCapacity = 24;
    private const int AudioCapacity = 32;

    private RuntimeGameObjectPool concretePool;
    private RuntimeGameObjectPool metalPool;
    private RuntimeGameObjectPool sparkPool;
    private RuntimeGameObjectPool audioPool;
    private Transform effectRoot;
    private CombatFeedbackAudioProfile audioProfile;
    private Material metalSparkMaterial;

    public int ConcreteCapacity => concretePool?.Capacity ?? 0;
    public int ConcreteActiveCount => concretePool?.ActiveCount ?? 0;
    public int MetalCapacity => metalPool?.Capacity ?? 0;
    public int MetalActiveCount => metalPool?.ActiveCount ?? 0;
    public int SparkCapacityValue => sparkPool?.Capacity ?? 0;
    public int AudioCapacityValue => audioPool?.Capacity ?? 0;
    public int AudioActiveCount => audioPool?.ActiveCount ?? 0;

    public static CombatEffectPool Ensure(
        Transform owner,
        GameObject concretePrefab)
    {
        Transform safeOwner = owner != null ? owner : null;
        CombatEffectPool pool = safeOwner != null
            ? safeOwner.GetComponent<CombatEffectPool>()
            : null;

        if (pool == null)
        {
            GameObject host = safeOwner != null
                ? safeOwner.gameObject
                : new GameObject("Combat Effect Pool");
            pool = host.AddComponent<CombatEffectPool>();
        }

        pool.Configure(concretePrefab);
        return pool;
    }

    public void Configure(GameObject concretePrefab)
    {
        EnsureSharedPools();

        if (concretePool == null && concretePrefab != null)
        {
            concretePool = new RuntimeGameObjectPool(
                ImpactCapacity,
                index => CreateConcreteImpact(
                    concretePrefab,
                    index));
        }
    }

    public void PresentImpact(ShotResult result)
    {
        if (!result.DidHit)
        {
            return;
        }

        EnsureSharedPools();
        SurfaceImpactStyle style =
            SurfaceImpactStyle.For(result.Surface);

        if (result.Surface == SurfaceType.Metal)
        {
            PlayMetalImpact(result, style);
        }
        else
        {
            PlayConcreteImpact(result, style);
        }

        AudioClip clip = ResolveClip(result.Surface);
        PlayAudio(
            result.Point,
            clip,
            0.85f,
            style.AudioPitch);
    }

    public bool PlayAudio(
        Vector3 position,
        AudioClip clip,
        float volume = 1f,
        float pitch = 1f)
    {
        if (clip == null)
        {
            return false;
        }

        EnsureSharedPools();
        GameObject voiceObject = audioPool.Rent();

        if (voiceObject == null)
        {
            return false;
        }

        voiceObject.GetComponent<PooledAudioVoice>().Play(
            audioPool,
            clip,
            position,
            volume,
            pitch);
        return true;
    }

    public void ReturnAll()
    {
        concretePool?.ReturnAll();
        metalPool?.ReturnAll();
        sparkPool?.ReturnAll();
        audioPool?.ReturnAll();
    }

    public long MeasureSteadyStateManagedAllocation(
        ShotResult result,
        int iterations)
    {
        int safeIterations = Mathf.Max(1, iterations);
        PresentImpact(result);
        ReturnAll();
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < safeIterations; index++)
        {
            PresentImpact(result);
            ReturnAll();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private void EnsureSharedPools()
    {
        if (effectRoot == null)
        {
            GameObject rootObject =
                new GameObject("Prewarmed Combat Effects");
            rootObject.transform.SetParent(transform, false);
            effectRoot = rootObject.transform;
            audioProfile =
                Resources.Load<CombatFeedbackAudioProfile>(
                    "CombatFeedbackAudio");
            PrepareSparkMaterial();
        }

        metalPool ??= new RuntimeGameObjectPool(
            ImpactCapacity,
            CreateMetalImpact);
        sparkPool ??= new RuntimeGameObjectPool(
            SparkCapacity,
            CreateMetalSpark);
        audioPool ??= new RuntimeGameObjectPool(
            AudioCapacity,
            CreateAudioVoice);
    }

    private GameObject CreateConcreteImpact(
        GameObject prefab,
        int index)
    {
        GameObject effect = Instantiate(prefab, effectRoot);
        effect.name = $"Concrete Pool {index + 1:00}";
        ImpactEffectController legacyLifetime =
            effect.GetComponent<ImpactEffectController>();

        if (legacyLifetime != null)
        {
            legacyLifetime.enabled = false;
        }

        AudioSource[] legacySources =
            effect.GetComponentsInChildren<AudioSource>(true);

        foreach (AudioSource legacySource in legacySources)
        {
            legacySource.Stop();
            legacySource.enabled = false;
        }

        SurfaceImpactVisualMarker marker =
            effect.GetComponent<SurfaceImpactVisualMarker>();

        if (marker == null)
        {
            marker = effect.AddComponent<SurfaceImpactVisualMarker>();
        }

        marker.Configure(SurfaceType.Concrete);
        PooledEffectInstance pooled =
            effect.AddComponent<PooledEffectInstance>();
        pooled.Prepare();
        return effect;
    }

    private GameObject CreateMetalImpact(int index)
    {
        GameObject effect =
            new GameObject($"Metal Impact Pool {index + 1:00}");
        effect.transform.SetParent(effectRoot, false);
        effect.AddComponent<SurfaceImpactVisualMarker>()
            .Configure(SurfaceType.Metal);
        effect.AddComponent<MetalImpactVisualController>()
            .Configure(SurfaceImpactStyle.For(SurfaceType.Metal));
        PooledEffectInstance pooled =
            effect.AddComponent<PooledEffectInstance>();
        pooled.Prepare();
        return effect;
    }

    private GameObject CreateMetalSpark(int index)
    {
        GameObject sparkObject =
            new GameObject($"Metal Spark Pool {index + 1:00}");
        sparkObject.transform.SetParent(effectRoot, false);
        sparkObject.AddComponent<SurfaceImpactVisualMarker>()
            .Configure(SurfaceType.Metal);
        ParticleSystem sparks =
            sparkObject.AddComponent<ParticleSystem>();
        sparks.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);
        SurfaceImpactStyle style =
            SurfaceImpactStyle.For(SurfaceType.Metal);
        ParticleSystem.MainModule main = sparks.main;
        main.duration = 0.12f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime =
            new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 15f);
        main.startSize =
            new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = style.Color;
        main.gravityModifier = 0.55f;
        ParticleSystem.EmissionModule emission = sparks.emission;
        emission.rateOverTime = 0f;
        emission.burstCount = 1;
        emission.SetBurst(
            0,
            new ParticleSystem.Burst(
                0f,
                (short)style.SparkCount));
        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.015f;
        ParticleSystemRenderer renderer =
            sparkObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.8f;
        renderer.sharedMaterial = metalSparkMaterial;
        PooledEffectInstance pooled =
            sparkObject.AddComponent<PooledEffectInstance>();
        pooled.Prepare();
        return sparkObject;
    }

    private GameObject CreateAudioVoice(int index)
    {
        GameObject voiceObject =
            new GameObject($"Combat Audio Voice {index + 1:00}");
        voiceObject.transform.SetParent(effectRoot, false);
        voiceObject.AddComponent<AudioSource>();
        voiceObject.AddComponent<PooledAudioVoice>().Prepare();
        return voiceObject;
    }

    private void PlayConcreteImpact(
        ShotResult result,
        SurfaceImpactStyle style)
    {
        if (concretePool == null)
        {
            return;
        }

        GameObject effect = concretePool.Rent();

        if (effect == null)
        {
            return;
        }

        effect.transform.SetPositionAndRotation(
            result.Point + result.Normal * 0.002f,
            Quaternion.LookRotation(result.Normal));
        effect.name = "Concrete(Clone)";
        effect.GetComponent<PooledEffectInstance>().Play(
            concretePool,
            style.UniqueMarkerLifetime);
    }

    private void PlayMetalImpact(
        ShotResult result,
        SurfaceImpactStyle style)
    {
        GameObject effect = metalPool.Rent();

        if (effect != null)
        {
            effect.name = "Metal Impact(Clone)";
            effect.transform.SetPositionAndRotation(
                result.Point + result.Normal * 0.012f,
                Quaternion.LookRotation(result.Normal));
            effect.GetComponent<MetalImpactVisualController>().Play();
            effect.GetComponent<PooledEffectInstance>().Play(
                metalPool,
                style.UniqueMarkerLifetime);
        }

        GameObject sparks = sparkPool.Rent();

        if (sparks == null)
        {
            return;
        }

        sparks.name = "Metal Spark Burst";
        sparks.transform.SetPositionAndRotation(
            result.Point + result.Normal * 0.01f,
            Quaternion.LookRotation(result.Normal));
        sparks.GetComponent<PooledEffectInstance>().Play(
            sparkPool,
            1.2f);
    }

    private AudioClip ResolveClip(SurfaceType surface)
    {
        if (audioProfile == null)
        {
            return null;
        }

        return surface == SurfaceType.Metal
            ? audioProfile.MetalImpact
            : audioProfile.ConcreteImpact;
    }

    private void PrepareSparkMaterial()
    {
        CombatFeedbackVisualProfile profile =
            Resources.Load<CombatFeedbackVisualProfile>(
                "CombatFeedbackVisual");

        if (profile == null || profile.MetalSparkMaterial == null)
        {
            return;
        }

        metalSparkMaterial = new Material(
            profile.MetalSparkMaterial);
        Color color =
            SurfaceImpactStyle.For(SurfaceType.Metal).Color;
        metalSparkMaterial.color = color;

        if (metalSparkMaterial.HasProperty("_BaseColor"))
        {
            metalSparkMaterial.SetColor("_BaseColor", color);
        }
    }

    private void OnDestroy()
    {
        if (metalSparkMaterial != null)
        {
            Destroy(metalSparkMaterial);
        }
    }
}
