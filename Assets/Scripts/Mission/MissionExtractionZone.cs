using UnityEngine;

public sealed class MissionExtractionZone : MonoBehaviour
{
    private CityNewMissionController mission;
    private GameObject player;
    private Renderer zoneRenderer;
    private Material zoneMaterial;
    private Light beaconLight;

    public bool IsAvailable { get; private set; }
    public float Radius { get; private set; } = 3f;

    public void Configure(
        CityNewMissionController configuredMission,
        GameObject configuredPlayer,
        float radius)
    {
        mission = configuredMission;
        player = configuredPlayer;
        Radius = Mathf.Max(1f, radius);
        CreateTrigger();
        CreateVisual();
        SetAvailable(false);
    }

    public void SetAvailable(bool available)
    {
        IsAvailable = available;

        if (zoneRenderer != null)
        {
            zoneRenderer.enabled = available;
        }

        if (beaconLight != null)
        {
            beaconLight.enabled = available;
        }
    }

    public bool TryEnter(GameObject actor)
    {
        return IsAvailable &&
            mission != null &&
            actor == player &&
            mission.TryEnterExtraction(actor);
    }

    private void Update()
    {
        if (IsAvailable && beaconLight != null)
        {
            beaconLight.intensity =
                2.4f + Mathf.Sin(Time.unscaledTime * 3f) * 0.7f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryEnter(ResolveActor(other));
    }

    private void OnTriggerStay(Collider other)
    {
        TryEnter(ResolveActor(other));
    }

    private static GameObject ResolveActor(Collider other)
    {
        if (other == null)
        {
            return null;
        }

        PlayerController controller =
            other.GetComponentInParent<PlayerController>();
        return controller != null ? controller.gameObject : null;
    }

    private void CreateTrigger()
    {
        SphereCollider trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = Radius;
        Rigidbody body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void CreateVisual()
    {
        GameObject ring =
            GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Extraction Ring";
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = Vector3.up * 0.04f;
        ring.transform.localScale =
            new Vector3(Radius * 2f, 0.025f, Radius * 2f);
        Collider ringCollider = ring.GetComponent<Collider>();

        if (ringCollider != null)
        {
            Destroy(ringCollider);
        }

        zoneRenderer = ring.GetComponent<Renderer>();
        Shader shader = Shader.Find(
            "Universal Render Pipeline/Unlit");
        shader ??= Shader.Find("Unlit/Color");

        if (shader != null)
        {
            zoneMaterial = new Material(shader);
            zoneMaterial.color = new Color(0.08f, 0.95f, 0.72f);
            zoneRenderer.material = zoneMaterial;
        }

        GameObject beacon = new GameObject("Extraction Beacon");
        beacon.transform.SetParent(transform, false);
        beacon.transform.localPosition = Vector3.up * 2.2f;
        beaconLight = beacon.AddComponent<Light>();
        beaconLight.type = LightType.Point;
        beaconLight.color = new Color(0.1f, 1f, 0.72f);
        beaconLight.range = 12f;
        beaconLight.intensity = 3f;
    }

    private void OnDestroy()
    {
        if (zoneMaterial != null)
        {
            Destroy(zoneMaterial);
        }
    }
}
