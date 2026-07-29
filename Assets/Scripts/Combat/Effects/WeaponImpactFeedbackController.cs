using UnityEngine;

public sealed class WeaponImpactFeedbackController : MonoBehaviour
{
    private WeaponController weapon;
    private GameObject impactPrefab;
    private CombatFeedbackAudioProfile audioProfile;

    public int FeedbackCount { get; private set; }
    public SurfaceType LastSurface { get; private set; }

    public void Configure(
        WeaponController sourceWeapon,
        GameObject effectPrefab)
    {
        if (weapon != null)
        {
            weapon.ShotResolved -= Present;
        }

        weapon = sourceWeapon;
        impactPrefab = effectPrefab;
        audioProfile = Resources.Load<CombatFeedbackAudioProfile>(
            "CombatFeedbackAudio");

        if (weapon != null)
        {
            weapon.ShotResolved += Present;
        }
    }

    private void OnDestroy()
    {
        if (weapon != null)
        {
            weapon.ShotResolved -= Present;
        }
    }

    public void Present(ShotResult result)
    {
        if (!result.DidHit)
        {
            return;
        }

        FeedbackCount++;
        LastSurface = result.Surface;
        SurfaceImpactStyle style =
            SurfaceImpactStyle.For(result.Surface);
        GameObject effect = null;

        if (result.Surface == SurfaceType.Metal)
        {
            effect = CreateMetalImpact(
                result.Point,
                result.Normal,
                style);
            CreateMetalSparkBurst(
                result.Point,
                result.Normal,
                style);
        }
        else if (impactPrefab != null)
        {
            effect = Instantiate(
                impactPrefab,
                result.Point + result.Normal * 0.002f,
                Quaternion.LookRotation(result.Normal));
            effect.name = $"{impactPrefab.name}(Clone)";
            effect.AddComponent<SurfaceImpactVisualMarker>()
                .Configure(result.Surface);
        }

        AudioClip clip = ResolveClip(result.Surface);

        if (clip == null)
        {
            return;
        }

        GameObject audioObject = effect != null
            ? effect
            : new GameObject($"{result.Surface} Impact Audio");
        audioObject.transform.position = result.Point;
        AudioSource source = audioObject.GetComponent<AudioSource>();

        if (source == null)
        {
            source = audioObject.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = 1f;
        source.maxDistance = 35f;
        source.volume = 0.85f;
        source.pitch = style.AudioPitch;
        source.PlayOneShot(clip);

        if (effect == null)
        {
            Destroy(audioObject, clip.length + 0.1f);
        }
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

    private static GameObject CreateMetalImpact(
        Vector3 point,
        Vector3 normal,
        SurfaceImpactStyle style)
    {
        GameObject effect = new GameObject("Metal Impact(Clone)");
        effect.transform.SetPositionAndRotation(
            point + normal * 0.012f,
            Quaternion.LookRotation(normal));
        effect.AddComponent<SurfaceImpactVisualMarker>()
            .Configure(SurfaceType.Metal);
        effect.AddComponent<MetalImpactVisualController>()
            .Configure(style);
        return effect;
    }

    private static void CreateMetalSparkBurst(
        Vector3 point,
        Vector3 normal,
        SurfaceImpactStyle style)
    {
        GameObject sparkObject =
            new GameObject("Metal Spark Burst");
        sparkObject.transform.SetPositionAndRotation(
            point + normal * 0.01f,
            Quaternion.LookRotation(normal));
        SurfaceImpactVisualMarker marker =
            sparkObject.AddComponent<SurfaceImpactVisualMarker>();
        marker.Configure(SurfaceType.Metal);
        ParticleSystem sparks =
            sparkObject.AddComponent<ParticleSystem>();
        sparks.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = sparks.main;
        main.duration = 0.12f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = style.Color;
        main.gravityModifier = 0.55f;
        ParticleSystem.EmissionModule emission = sparks.emission;
        emission.rateOverTime = 0f;
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
        CombatFeedbackVisualProfile profile =
            Resources.Load<CombatFeedbackVisualProfile>(
                "CombatFeedbackVisual");

        if (profile != null && profile.MetalSparkMaterial != null)
        {
            Material material =
                new Material(profile.MetalSparkMaterial);
            material.color = style.Color;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", style.Color);
            }

            renderer.material = material;
        }

        sparks.Play();
        Destroy(sparkObject, 1.2f);
    }
}
