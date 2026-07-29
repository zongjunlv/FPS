using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class ShotTracerController : MonoBehaviour
{
    private const float TracerLength = 1.8f;
    private const float FadeDuration = 0.06f;

    public float Speed { get; private set; }

    private static Material sharedMaterial;

    private LineRenderer line;
    private Vector3 startPoint;
    private Vector3 endPoint;
    private Vector3 direction;
    private float totalDistance;
    private float traveledDistance;
    private float fadeElapsed;
    private bool reachedEnd;

    public static ShotTracerController Play(
        Vector3 start,
        Vector3 end,
        float speed)
    {
        var tracerObject = new GameObject("ShotTracer");
        LineRenderer lineRenderer =
            tracerObject.AddComponent<LineRenderer>();
        ShotTracerController tracer =
            tracerObject.AddComponent<ShotTracerController>();
        tracer.Initialize(lineRenderer, start, end, speed);
        return tracer;
    }

    private void Initialize(
        LineRenderer lineRenderer,
        Vector3 start,
        Vector3 end,
        float speed)
    {
        line = lineRenderer;
        startPoint = start;
        endPoint = end;
        Vector3 offset = end - start;
        totalDistance = offset.magnitude;
        direction = totalDistance > 0.0001f
            ? offset / totalDistance
            : Vector3.forward;
        Speed = Mathf.Max(200f, speed);

        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.025f;
        line.endWidth = 0.008f;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.material = GetSharedMaterial();
        line.startColor = new Color(1f, 0.78f, 0.2f, 1f);
        line.endColor = new Color(1f, 0.25f, 0.04f, 0.15f);

        traveledDistance = Mathf.Min(0.25f, totalDistance);
        UpdateLine();
    }

    private void Update()
    {
        if (!reachedEnd)
        {
            traveledDistance = Mathf.Min(
                totalDistance,
                traveledDistance + Speed * Time.deltaTime);
            UpdateLine();

            if (traveledDistance >= totalDistance)
            {
                reachedEnd = true;
            }

            return;
        }

        fadeElapsed += Time.deltaTime;
        float alpha = 1f - Mathf.Clamp01(
            fadeElapsed / FadeDuration);
        line.startColor = new Color(1f, 0.78f, 0.2f, alpha);
        line.endColor = new Color(1f, 0.25f, 0.04f, alpha * 0.15f);

        if (fadeElapsed >= FadeDuration)
        {
            Destroy(gameObject);
        }
    }

    private void UpdateLine()
    {
        float tailDistance = Mathf.Max(
            0f,
            traveledDistance - TracerLength);
        line.SetPosition(
            0,
            startPoint + direction * tailDistance);
        line.SetPosition(
            1,
            startPoint + direction * traveledDistance);
    }

    private static Material GetSharedMaterial()
    {
        if (sharedMaterial != null)
        {
            return sharedMaterial;
        }

        // Sprites/Default multiplies the material color by the
        // LineRenderer vertex colors, so the orange gradient and fade remain
        // visible in both Built-in and URP projects.
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        sharedMaterial = new Material(shader)
        {
            name = "Runtime Shot Tracer Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedMaterial;
    }
}
