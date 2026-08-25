using UnityEngine;

public sealed class ShotTracerPool : MonoBehaviour
{
    [SerializeField, Min(1)] private int capacity = 16;

    public int Capacity =>
        tracers != null ? tracers.Length : capacity;

    public int ActiveCount
    {
        get
        {
            int count = 0;

            foreach (ShotTracerController tracer in tracers)
            {
                if (tracer.gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private ShotTracerController[] tracers;
    private Material tracerMaterial;
    private int saturatedReuseIndex;

    private void Awake()
    {
        CreateMaterial();
        Prewarm();
    }

    public ShotTracerController Play(
        Vector3 start,
        Vector3 end,
        float speed)
    {
        ShotTracerController tracer = FindAvailableTracer();
        tracer.Activate(start, end, speed);
        return tracer;
    }

    public ShotTracerController Play(
        Vector3 start,
        Vector3 end,
        float speed,
        Color startColor,
        Color endColor)
    {
        ShotTracerController tracer = FindAvailableTracer();
        tracer.Activate(start, end, speed, startColor, endColor);
        return tracer;
    }

    internal void Release(ShotTracerController tracer)
    {
        if (tracer != null && tracer.gameObject.activeSelf)
        {
            tracer.Deactivate();
        }
    }

    private void OnDisable()
    {
        if (tracers == null)
        {
            return;
        }

        foreach (ShotTracerController tracer in tracers)
        {
            if (tracer != null && tracer.gameObject.activeSelf)
            {
                tracer.Deactivate();
            }
        }
    }

    private void OnDestroy()
    {
        if (tracerMaterial != null)
        {
            Destroy(tracerMaterial);
        }
    }

    private void CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader = Shader.Find(
                "Universal Render Pipeline/Unlit");
        }

        tracerMaterial = new Material(shader)
        {
            name = "Runtime Shot Tracer Material",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void Prewarm()
    {
        capacity = Mathf.Max(1, capacity);
        tracers = new ShotTracerController[capacity];

        for (int index = 0; index < capacity; index++)
        {
            var tracerObject = new GameObject(
                $"ShotTracer {index + 1:00}");
            tracerObject.transform.SetParent(transform, false);
            tracerObject.AddComponent<LineRenderer>();
            ShotTracerController tracer =
                tracerObject.AddComponent<ShotTracerController>();
            tracer.Prepare(this, tracerMaterial);
            tracer.Deactivate();
            tracers[index] = tracer;
        }
    }

    private ShotTracerController FindAvailableTracer()
    {
        foreach (ShotTracerController tracer in tracers)
        {
            if (!tracer.gameObject.activeSelf)
            {
                return tracer;
            }
        }

        ShotTracerController saturatedTracer =
            tracers[saturatedReuseIndex];
        saturatedReuseIndex =
            (saturatedReuseIndex + 1) % tracers.Length;
        saturatedTracer.Deactivate();
        return saturatedTracer;
    }
}
