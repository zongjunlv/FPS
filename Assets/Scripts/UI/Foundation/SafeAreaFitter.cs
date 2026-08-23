using UnityEngine;

public readonly struct SafeAreaAnchors
{
    public SafeAreaAnchors(Vector2 minimum, Vector2 maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public Vector2 Minimum { get; }
    public Vector2 Maximum { get; }
}

[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaFitter : MonoBehaviour
{
    private RectTransform target;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;

    private void Awake()
    {
        target = (RectTransform)transform;
        Apply(Screen.safeArea, Screen.width, Screen.height);
    }

    private void Update()
    {
        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);

        if (safeArea == lastSafeArea && screenSize == lastScreenSize)
        {
            return;
        }

        Apply(safeArea, screenSize.x, screenSize.y);
    }

    public void Apply(Rect safeArea, int screenWidth, int screenHeight)
    {
        SafeAreaAnchors anchors = Calculate(
            safeArea,
            screenWidth,
            screenHeight);
        target ??= (RectTransform)transform;
        target.anchorMin = anchors.Minimum;
        target.anchorMax = anchors.Maximum;
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;
        lastSafeArea = safeArea;
        lastScreenSize = new Vector2Int(screenWidth, screenHeight);
    }

    public static SafeAreaAnchors Calculate(
        Rect safeArea,
        int screenWidth,
        int screenHeight)
    {
        float width = Mathf.Max(1, screenWidth);
        float height = Mathf.Max(1, screenHeight);
        Vector2 minimum = new Vector2(
            Mathf.Clamp01(safeArea.xMin / width),
            Mathf.Clamp01(safeArea.yMin / height));
        Vector2 maximum = new Vector2(
            Mathf.Clamp01(safeArea.xMax / width),
            Mathf.Clamp01(safeArea.yMax / height));
        maximum.x = Mathf.Max(minimum.x, maximum.x);
        maximum.y = Mathf.Max(minimum.y, maximum.y);
        return new SafeAreaAnchors(minimum, maximum);
    }
}
