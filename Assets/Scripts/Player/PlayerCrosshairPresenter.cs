using UnityEngine;

public class PlayerCrosshairPresenter : MonoBehaviour
{
    [Header("Hip Crosshair")]
    [SerializeField, Min(1f)] private float hipGap = 8f;
    [SerializeField, Min(1f)] private float hipLength = 10f;
    [SerializeField, Min(1f)] private float lineThickness = 2f;

    [Header("ADS Crosshair")]
    [SerializeField, Min(1f)] private float adsGap = 3f;
    [SerializeField, Min(1f)] private float adsLength = 4f;
    [SerializeField] private Color crosshairColor = Color.white;

    [Header("Dynamic Spread")]
    [SerializeField, Min(0f)] private float movementGapBonus = 6f;
    [SerializeField, Min(0f)] private float sprintGapBonus = 14f;
    [SerializeField, Min(0f)] private float fireBloomPerShot = 5f;
    [SerializeField, Min(0f)] private float maxFireBloom = 15f;
    [SerializeField, Min(0f)] private float bloomRecoverySpeed = 22f;

    [Header("Hit Feedback")]
    [SerializeField, Min(0.01f)] private float hitMarkerDuration = 0.32f;
    [SerializeField, Min(0.01f)] private float killMarkerDuration = 0.5f;
    [SerializeField] private Color normalHitColor = Color.white;
    [SerializeField] private Color headshotColor =
        new Color(1f, 0.75f, 0.1f, 1f);
    [SerializeField] private Color killColor =
        new Color(1f, 0.15f, 0.1f, 1f);

    private float aimBlend;
    private bool isVisible = true;
    private float movementAmount;
    private bool sprinting;
    private float fireBloom;
    private float hitFeedbackRemaining;

    public float CurrentGap { get; private set; }
    public HitFeedbackKind CurrentHitFeedback { get; private set; }
    public int HitFeedbackCount { get; private set; }

    public void SetState(float blend, bool visible)
    {
        aimBlend = Mathf.Clamp01(blend);
        isVisible = visible;
        RecalculateGap();
    }

    public void SetMotionState(float moveAmount, bool isSprinting)
    {
        movementAmount = Mathf.Clamp01(moveAmount);
        sprinting = isSprinting;
        RecalculateGap();
    }

    public void AddFireBloom()
    {
        fireBloom = Mathf.Min(
            maxFireBloom,
            fireBloom + fireBloomPerShot);
        RecalculateGap();
    }

    public void ShowHitFeedback(HitFeedbackKind kind)
    {
        if (kind == HitFeedbackKind.None)
        {
            return;
        }

        CurrentHitFeedback = kind;
        HitFeedbackCount++;
        hitFeedbackRemaining = kind == HitFeedbackKind.Kill
            ? killMarkerDuration
            : hitMarkerDuration;
    }

    private void Update()
    {
        fireBloom = Mathf.MoveTowards(
            fireBloom,
            0f,
            bloomRecoverySpeed * Time.unscaledDeltaTime);
        RecalculateGap();

        if (hitFeedbackRemaining <= 0f)
        {
            CurrentHitFeedback = HitFeedbackKind.None;
            return;
        }

        hitFeedbackRemaining -= Time.unscaledDeltaTime;
    }

    private void OnGUI()
    {
        if (!isVisible || Event.current.type != EventType.Repaint)
        {
            return;
        }

        float easedBlend = Mathf.SmoothStep(0f, 1f, aimBlend);
        float gap = CurrentGap;
        float length = Mathf.Lerp(hipLength, adsLength, easedBlend);
        Vector2 center = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f);
        Color previousColor = GUI.color;
        GUI.color = crosshairColor;

        DrawRect(new Rect(
            center.x - gap - length,
            center.y - lineThickness * 0.5f,
            length,
            lineThickness));
        DrawRect(new Rect(
            center.x + gap,
            center.y - lineThickness * 0.5f,
            length,
            lineThickness));
        DrawRect(new Rect(
            center.x - lineThickness * 0.5f,
            center.y - gap - length,
            lineThickness,
            length));
        DrawRect(new Rect(
            center.x - lineThickness * 0.5f,
            center.y + gap,
            lineThickness,
            length));

        if (aimBlend >= 0.5f)
        {
            DrawRect(new Rect(
                center.x - lineThickness * 0.5f,
                center.y - lineThickness * 0.5f,
                lineThickness,
                lineThickness));
        }

        DrawHitMarker(center);

        GUI.color = previousColor;
    }

    private void DrawHitMarker(Vector2 center)
    {
        if (CurrentHitFeedback == HitFeedbackKind.None)
        {
            return;
        }

        GUI.color = CurrentHitFeedback switch
        {
            HitFeedbackKind.Headshot => headshotColor,
            HitFeedbackKind.Kill => killColor,
            _ => normalHitColor
        };
        float inner = 7f;
        float outer = CurrentHitFeedback == HitFeedbackKind.Kill
            ? 32f
            : CurrentHitFeedback == HitFeedbackKind.Headshot
                ? 27f
                : 23f;
        float markerThickness = Mathf.Max(3f, lineThickness);
        DrawLine(
            center + new Vector2(-inner, -inner),
            center + new Vector2(-outer, -outer),
            markerThickness);
        DrawLine(
            center + new Vector2(inner, -inner),
            center + new Vector2(outer, -outer),
            markerThickness);
        DrawLine(
            center + new Vector2(-inner, inner),
            center + new Vector2(-outer, outer),
            markerThickness);
        DrawLine(
            center + new Vector2(inner, inner),
            center + new Vector2(outer, outer),
            markerThickness);

        GUIStyle markerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = CurrentHitFeedback == HitFeedbackKind.Kill
                ? 18
                : 14,
            fontStyle = FontStyle.Bold
        };
        markerStyle.normal.textColor = GUI.color;
        string markerText = CurrentHitFeedback switch
        {
            HitFeedbackKind.Headshot => "HEADSHOT",
            HitFeedbackKind.Kill => "KILL",
            _ => "HIT"
        };
        GUI.Label(
            new Rect(center.x - 70f, center.y + 28f, 140f, 24f),
            markerText,
            markerStyle);
    }

    private static void DrawLine(
        Vector2 start,
        Vector2 end,
        float thickness)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        float angle = Vector2.SignedAngle(Vector2.right, end - start);
        float length = Vector2.Distance(start, end);
        GUIUtility.RotateAroundPivot(angle, start);
        DrawRect(new Rect(start.x, start.y, length, thickness));
        GUI.matrix = previousMatrix;
    }

    private static void DrawRect(Rect rect)
    {
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }

    private void RecalculateGap()
    {
        float easedBlend = Mathf.SmoothStep(0f, 1f, aimBlend);
        CurrentGap = Mathf.Lerp(hipGap, adsGap, easedBlend) +
            movementGapBonus * movementAmount +
            (sprinting ? sprintGapBonus : 0f) +
            fireBloom;
    }
}
