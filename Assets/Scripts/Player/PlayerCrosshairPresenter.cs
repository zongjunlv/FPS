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

    private float aimBlend;
    private bool isVisible = true;

    public void SetState(float blend, bool visible)
    {
        aimBlend = Mathf.Clamp01(blend);
        isVisible = visible;
    }

    private void OnGUI()
    {
        if (!isVisible || Event.current.type != EventType.Repaint)
        {
            return;
        }

        float easedBlend = Mathf.SmoothStep(0f, 1f, aimBlend);
        float gap = Mathf.Lerp(hipGap, adsGap, easedBlend);
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

        GUI.color = previousColor;
    }

    private static void DrawRect(Rect rect)
    {
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }
}
