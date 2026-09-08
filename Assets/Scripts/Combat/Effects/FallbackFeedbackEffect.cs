using UnityEngine;

/// <summary>A small, short-lived glint when an optional effect is missing.</summary>
public sealed class FallbackFeedbackEffect : MonoBehaviour
{
    private LineRenderer cue;
    private Material material;
    private float remaining;
    private float duration;

    public static GameObject Create(Transform parent = null)
    {
        GameObject host = new("Fallback Feedback");
        host.transform.SetParent(parent, false);
        host.AddComponent<FallbackFeedbackEffect>();
        return host;
    }

    private void Awake()
    {
        cue = gameObject.AddComponent<LineRenderer>();
        cue.useWorldSpace = false;
        cue.alignment = LineAlignment.View;
        cue.widthMultiplier = 0.025f;
        cue.positionCount = 5;
        cue.SetPositions(new[]
        {
            new Vector3(-0.14f, -0.14f, 0f), new Vector3(0.14f, 0.14f, 0f),
            Vector3.zero, new Vector3(-0.14f, 0.14f, 0f), new Vector3(0.14f, -0.14f, 0f)
        });
        cue.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cue.receiveShadows = false;
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            material = new Material(shader) { name = "Fallback Feedback Material" };
            cue.sharedMaterial = material;
        }
        cue.enabled = false;
    }

    public void Play(float lifetime = 0.2f, bool destroyOnCompletion = false)
    {
        duration = Mathf.Clamp(lifetime, 0.05f, 0.4f);
        remaining = duration;
        cue.startColor = cue.endColor = new Color(1f, 0.66f, 0.26f, 1f);
        cue.enabled = material != null;
        if (destroyOnCompletion) Destroy(gameObject, duration);
    }

    private void Update()
    {
        remaining = Mathf.Max(0f, remaining - Time.deltaTime);
        float alpha = duration > 0f ? remaining / duration : 0f;
        cue.startColor = cue.endColor = new Color(1f, 0.66f, 0.26f, alpha);
        cue.enabled = remaining > 0f && material != null;
    }

    private void OnDisable()
    {
        remaining = 0f;
        if (cue != null) cue.enabled = false;
    }

    private void OnDestroy()
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material);
        else DestroyImmediate(material);
    }
}
