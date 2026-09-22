using System.Collections.Generic;
using UnityEngine;

internal static class TacticalUiTheme
{
    public static readonly Color Background =
        new(0.006f, 0.014f, 0.024f, 1f);
    public static readonly Color Surface =
        new(0.022f, 0.047f, 0.063f, 0.98f);
    public static readonly Color SurfaceRaised =
        new(0.032f, 0.071f, 0.088f, 0.98f);
    public static readonly Color SurfaceSoft =
        new(0.02f, 0.09f, 0.105f, 0.82f);
    public static readonly Color Cyan =
        new(0.25f, 0.91f, 0.84f, 1f);
    public static readonly Color Blue =
        new(0.29f, 0.64f, 1f, 1f);
    public static readonly Color Amber =
        new(1f, 0.69f, 0.24f, 1f);
    public static readonly Color Green =
        new(0.37f, 0.92f, 0.62f, 1f);
    public static readonly Color Red =
        new(1f, 0.4f, 0.35f, 1f);
    public static readonly Color TextPrimary =
        new(0.94f, 0.98f, 1f, 1f);
    public static readonly Color TextSecondary =
        new(0.57f, 0.68f, 0.74f, 1f);

    private const string IconRoot = "UI/Kenney/GameIcons/";
    private static readonly Dictionary<string, Sprite> Icons = new();

    public static Sprite LoadIcon(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return null;
        if (Icons.TryGetValue(iconName, out Sprite cached)) return cached;
        Sprite icon = Resources.Load<Sprite>(IconRoot + iconName);
        Icons[iconName] = icon;
        return icon;
    }

    public static UnityEngine.UI.Image CreateIcon(
        string objectName,
        Transform parent,
        string iconName,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size,
        Vector2 position)
    {
        RectTransform rect = ModeUiFactory.CreateRect(objectName, parent);
        ModeUiFactory.SetRect(rect, anchorMin, anchorMax, pivot, size, position);
        UnityEngine.UI.Image image =
            rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = LoadIcon(iconName);
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = image.sprite != null;
        return image;
    }

    public static void AddSurfaceChrome(RectTransform rect, Color accent,
        bool shadow = true)
    {
        if (shadow)
        {
            UnityEngine.UI.Shadow shadowEffect =
                rect.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadowEffect.effectColor = new Color(0f, 0f, 0f, 0.48f);
            shadowEffect.effectDistance = new Vector2(0f, -9f);
            shadowEffect.useGraphicAlpha = true;
        }

        UnityEngine.UI.Outline outline =
            rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.42f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;

        RectTransform topLine = ModeUiFactory.CreateRect("顶部信号线", rect);
        ModeUiFactory.SetRect(topLine, new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 3f), Vector2.zero);
        UnityEngine.UI.Image line =
            topLine.gameObject.AddComponent<UnityEngine.UI.Image>();
        line.color = new Color(accent.r, accent.g, accent.b, 0.9f);
        line.raycastTarget = false;

        AddCorner(rect, "左上角标", new Vector2(0f, 1f),
            new Vector2(0f, 1f), accent, false);
        AddCorner(rect, "右下角标", new Vector2(1f, 0f),
            new Vector2(1f, 0f), accent, true);
    }

    public static RectTransform CreatePill(Transform parent, string name,
        Vector2 size, Vector2 position, Color color)
    {
        RectTransform rect = ModeUiFactory.CreateRect(name, parent);
        ModeUiFactory.SetRect(rect, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            size, position);
        UnityEngine.UI.Image image =
            rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    public static void AddScanLines(RectTransform canvas, int count = 12)
    {
        for (int index = 0; index < count; index++)
        {
            RectTransform line = ModeUiFactory.CreateRect(
                $"背景扫描线 {index + 1}", canvas);
            float y = (index + 0.5f) / count;
            ModeUiFactory.SetRect(line, new Vector2(0f, y),
                new Vector2(1f, y), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 1f), Vector2.zero);
            UnityEngine.UI.Image image =
                line.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.2f, 0.7f, 0.72f,
                index % 3 == 0 ? 0.055f : 0.022f);
            image.raycastTarget = false;
        }
    }

    private static void AddCorner(RectTransform parent, string name,
        Vector2 anchor, Vector2 pivot, Color color, bool inverted)
    {
        RectTransform horizontal = ModeUiFactory.CreateRect(name, parent);
        ModeUiFactory.SetRect(horizontal, anchor, anchor, pivot,
            new Vector2(34f, 3f), inverted
                ? new Vector2(-10f, 10f)
                : new Vector2(10f, -10f));
        UnityEngine.UI.Image horizontalImage =
            horizontal.gameObject.AddComponent<UnityEngine.UI.Image>();
        horizontalImage.color = color;
        horizontalImage.raycastTarget = false;

        RectTransform vertical = ModeUiFactory.CreateRect(name + "竖线", parent);
        ModeUiFactory.SetRect(vertical, anchor, anchor, pivot,
            new Vector2(3f, 34f), inverted
                ? new Vector2(-10f, 10f)
                : new Vector2(10f, -10f));
        UnityEngine.UI.Image verticalImage =
            vertical.gameObject.AddComponent<UnityEngine.UI.Image>();
        verticalImage.color = color;
        verticalImage.raycastTarget = false;
    }
}
