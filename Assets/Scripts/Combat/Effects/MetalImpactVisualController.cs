using UnityEngine;

public sealed class MetalImpactVisualController : MonoBehaviour
{
    private Light impactLight;
    private float lightRemaining;
    private Material surfaceMaterial;
    private Material silverMaterial;
    private Material coreMaterial;
    private bool initialized;

    public float Lifetime { get; private set; }

    public void Configure(SurfaceImpactStyle style)
    {
        if (initialized)
        {
            Play();
            return;
        }

        initialized = true;
        CombatFeedbackVisualProfile profile =
            Resources.Load<CombatFeedbackVisualProfile>(
                "CombatFeedbackVisual");
        surfaceMaterial = profile != null
            ? profile.SurfaceMaterial
            : null;
        Lifetime = style.UniqueMarkerLifetime;
        CreateDent(
            "Silver Metal Dent",
            style.UniqueMarkerScale,
            new Color(0.16f, 0.19f, 0.22f, 1f),
            false);
        CreateDent(
            "Hot Impact Core",
            style.UniqueMarkerScale * 0.38f,
            new Color(1f, 0.32f, 0.04f, 1f),
            true);
        impactLight = gameObject.AddComponent<Light>();
        impactLight.type = LightType.Point;
        impactLight.color = new Color(1f, 0.45f, 0.08f);
        impactLight.intensity = 2.8f;
        impactLight.range = 1.2f;
        Play();
    }

    public void Play()
    {
        if (impactLight == null)
        {
            return;
        }

        impactLight.enabled = true;
        lightRemaining = 0.11f;
    }

    private void Update()
    {
        if (impactLight == null || !impactLight.enabled)
        {
            return;
        }

        lightRemaining -= Time.deltaTime;

        if (lightRemaining <= 0f)
        {
            impactLight.enabled = false;
        }
    }

    private void CreateDent(
        string objectName,
        float scale,
        Color color,
        bool emissive)
    {
        GameObject dent =
            GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dent.name = objectName;
        dent.transform.SetParent(transform, false);
        dent.transform.localPosition =
            emissive ? new Vector3(0f, 0f, 0.012f) : Vector3.zero;
        dent.transform.localScale =
            new Vector3(scale, scale, 0.018f);
        Collider collider = dent.GetComponent<Collider>();
        collider.enabled = false;
        Renderer renderer = dent.GetComponent<Renderer>();
        if (surfaceMaterial == null)
        {
            return;
        }

        Material material = new Material(surfaceMaterial);
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", emissive ? 0.2f : 0.95f);
        material.SetFloat("_Smoothness", emissive ? 0.35f : 0.8f);

        if (emissive)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 3f);
        }

        renderer.material = material;

        if (emissive)
        {
            coreMaterial = material;
        }
        else
        {
            silverMaterial = material;
        }
    }

    private void OnDisable()
    {
        if (impactLight != null)
        {
            impactLight.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (silverMaterial != null)
        {
            Destroy(silverMaterial);
        }

        if (coreMaterial != null)
        {
            Destroy(coreMaterial);
        }
    }
}
