using UnityEngine;

public sealed class FeedbackTestRangeController : MonoBehaviour
{
    private CombatFeedbackVisualProfile visualProfile;

    public GameObject RangeRoot { get; private set; }

    private void Start()
    {
        if (GameObject.Find("Combat Feedback Test Range") != null)
        {
            return;
        }

        visualProfile = Resources.Load<CombatFeedbackVisualProfile>(
            "CombatFeedbackVisual");
        BuildRange();
    }

    private void BuildRange()
    {
        Transform cameraTransform = GetComponent<PlayerController>()
            ?.AimCamera?.transform;
        Vector3 forward = cameraTransform != null
            ? cameraTransform.forward
            : transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 center = transform.position + forward * 12f;
        center.y = transform.position.y;

        RangeRoot = new GameObject("Combat Feedback Test Range");
        RangeRoot.transform.position = center;
        RangeRoot.transform.rotation =
            Quaternion.LookRotation(-forward, Vector3.up);

        CreateSurfacePanel(
            "Concrete Feedback Panel",
            new Vector3(-2.25f, 1.2f, 0f),
            SurfaceType.Concrete,
            new Color(0.55f, 0.5f, 0.42f));
        CreateDamageDummy(
            "Damage Feedback Dummy",
            new Vector3(0f, 0f, 0f));
        CreateSurfacePanel(
            "Metal Feedback Panel",
            new Vector3(2.25f, 1.2f, 0f),
            SurfaceType.Metal,
            new Color(0.25f, 0.4f, 0.55f));
        CreateLabel(
            "CONCRETE",
            new Vector3(-2.25f, 2.4f, -0.05f));
        CreateLabel(
            "BODY / HEAD",
            new Vector3(0f, 2.75f, -0.05f));
        CreateLabel(
            "METAL",
            new Vector3(2.25f, 2.4f, -0.05f));
        CreateDamagePad();
    }

    private void CreateSurfacePanel(
        string objectName,
        Vector3 localPosition,
        SurfaceType surface,
        Color color)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = objectName;
        panel.transform.SetParent(RangeRoot.transform, false);
        panel.transform.localPosition = localPosition;
        panel.transform.localScale = new Vector3(1.5f, 2.2f, 0.25f);
        panel.AddComponent<SurfaceDescriptor>().Configure(surface);
        SetColor(panel, color);
    }

    private void CreateDamageDummy(
        string objectName,
        Vector3 localPosition)
    {
        GameObject dummy = new GameObject(objectName);
        dummy.transform.SetParent(RangeRoot.transform, false);
        dummy.transform.localPosition = localPosition;
        Health health = dummy.AddComponent<Health>();
        health.Initialize(100f);

        CreateHitboxPart(
            dummy.transform,
            health,
            "Body Hitbox",
            new Vector3(0f, 1f, 0f),
            new Vector3(1.15f, 1.6f, 0.45f),
            1f,
            HitRegion.Body,
            new Color(0.3f, 0.55f, 0.3f));
        CreateHitboxPart(
            dummy.transform,
            health,
            "Head Hitbox",
            new Vector3(0f, 2.05f, 0f),
            new Vector3(0.7f, 0.6f, 0.45f),
            2f,
            HitRegion.Head,
            new Color(0.75f, 0.3f, 0.25f));
    }

    private void CreateHitboxPart(
        Transform parent,
        Health health,
        string objectName,
        Vector3 localPosition,
        Vector3 scale,
        float multiplier,
        HitRegion region,
        Color color)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        part.AddComponent<DamageHitbox>()
            .ConfigureRegion(health, multiplier, region);
        SetColor(part, color);
    }

    private void CreateLabel(string text, Vector3 localPosition)
    {
        GameObject labelObject = new GameObject($"{text} Label");
        labelObject.transform.SetParent(RangeRoot.transform, false);
        labelObject.transform.localPosition = localPosition;
        labelObject.transform.localRotation =
            Quaternion.Euler(0f, 180f, 0f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = text;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.08f;
        label.fontSize = 40;
        label.color = Color.white;
    }

    private void CreateDamagePad()
    {
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "Player Damage Audio Pad";
        pad.transform.SetParent(RangeRoot.transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.08f, 5.5f);
        pad.transform.localScale = new Vector3(2.2f, 0.15f, 2.2f);
        BoxCollider collider = pad.GetComponent<BoxCollider>();
        MeshRenderer renderer = pad.GetComponent<MeshRenderer>();
        ApplyColor(renderer, new Color(0.75f, 0.08f, 0.08f));
        pad.AddComponent<Rigidbody>();
        pad.AddComponent<PlayerDamageTestPad>();
        collider.isTrigger = true;
        CreateLabel(
            "DAMAGE AUDIO PAD",
            new Vector3(0f, 0.25f, 5.5f));
    }

    private void SetColor(GameObject target, Color color)
    {
        Renderer renderer = target.GetComponent<Renderer>();

        if (renderer != null)
        {
            ApplyColor(renderer, color);
        }
    }

    private void ApplyColor(Renderer renderer, Color color)
    {
        if (visualProfile != null &&
            visualProfile.SurfaceMaterial != null)
        {
            renderer.material =
                new Material(visualProfile.SurfaceMaterial);
        }

        renderer.material.color = color;

        if (renderer.material.HasProperty("_BaseColor"))
        {
            renderer.material.SetColor("_BaseColor", color);
        }
    }
}
