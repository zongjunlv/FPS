using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class ShotTracerController : MonoBehaviour
{
    private const float TracerLength = 1.8f;
    private const float FadeDuration = 0.06f;

    public float Speed { get; private set; }

    private ShotTracerPool owner;
    private LineRenderer line;
    private Vector3 startPoint;
    private Vector3 direction;
    private float totalDistance;
    private float traveledDistance;
    private float fadeElapsed;
    private bool reachedEnd;

    internal void Prepare(
        ShotTracerPool tracerPool,
        Material material)
    {
        owner = tracerPool;
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.025f;
        line.endWidth = 0.008f;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.sharedMaterial = material;
        line.enabled = false;
    }

    internal void Activate(
        Vector3 start,
        Vector3 end,
        float speed)
    {
        startPoint = start;
        Vector3 offset = end - start;
        totalDistance = offset.magnitude;
        direction = totalDistance > 0.0001f
            ? offset / totalDistance
            : Vector3.forward;
        Speed = Mathf.Max(200f, speed);
        traveledDistance = Mathf.Min(0.25f, totalDistance);
        fadeElapsed = 0f;
        reachedEnd = totalDistance <= traveledDistance;

        line.positionCount = 2;
        line.startColor = new Color(
            1f,
            0.78f,
            0.2f,
            1f);
        line.endColor = new Color(
            1f,
            0.25f,
            0.04f,
            0.15f);
        UpdateLine();
        line.enabled = true;
        gameObject.SetActive(true);
    }

    internal void Deactivate()
    {
        line.enabled = false;
        gameObject.SetActive(false);
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
        line.startColor = new Color(
            1f,
            0.78f,
            0.2f,
            alpha);
        line.endColor = new Color(
            1f,
            0.25f,
            0.04f,
            alpha * 0.15f);

        if (fadeElapsed >= FadeDuration)
        {
            owner.Release(this);
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
}
