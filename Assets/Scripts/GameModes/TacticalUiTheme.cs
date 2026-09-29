using System.Collections.Generic;
using UnityEngine;

internal static class TacticalUiTheme
{
    public static readonly Color Background =
        new(0.047f, 0.055f, 0.063f, 1f);
    public static readonly Color Surface =
        new(0.086f, 0.098f, 0.109f, 0.98f);
    public static readonly Color SurfaceRaised =
        new(0.114f, 0.129f, 0.141f, 0.98f);
    public static readonly Color SurfaceSoft =
        new(0.098f, 0.124f, 0.133f, 0.92f);
    public static readonly Color Cyan =
        new(0.32f, 0.76f, 0.73f, 1f);
    public static readonly Color Blue =
        new(0.44f, 0.67f, 0.78f, 1f);
    public static readonly Color Amber =
        new(0.85f, 0.68f, 0.38f, 1f);
    public static readonly Color Green =
        new(0.46f, 0.75f, 0.58f, 1f);
    public static readonly Color Red =
        new(0.91f, 0.43f, 0.40f, 1f);
    public static readonly Color TextPrimary =
        new(0.94f, 0.96f, 0.96f, 1f);
    public static readonly Color TextSecondary =
        new(0.68f, 0.73f, 0.75f, 1f);

    private const string IconRoot = "UI/Kenney/GameIcons/";
    private const string MenuArtRoot = "UI/TacticalMenu/";
    private static readonly Dictionary<string, Sprite> Icons = new();
    private static readonly Dictionary<string, Sprite> MenuArt = new();

    public static Sprite LoadMenuArt(string artName)
    {
        if (string.IsNullOrWhiteSpace(artName)) return null;
        if (MenuArt.TryGetValue(artName, out Sprite cached)) return cached;
        Sprite art = Resources.Load<Sprite>(MenuArtRoot + artName);
        MenuArt[artName] = art;
        return art;
    }

    public static void ApplyMenuArt(UnityEngine.UI.Image image,
        string artName, Color tint, bool sliced = true)
    {
        if (image == null) return;
        Sprite art = LoadMenuArt(artName);
        if (art == null) return;
        image.sprite = art;
        image.type = sliced
            ? UnityEngine.UI.Image.Type.Sliced
            : UnityEngine.UI.Image.Type.Simple;
        image.color = tint;
    }

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
            shadowEffect.effectColor = new Color(0f, 0f, 0f, 0.26f);
            shadowEffect.effectDistance = new Vector2(0f, -5f);
            shadowEffect.useGraphicAlpha = true;
        }

        UnityEngine.UI.Outline outline =
            rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0.27f, 0.32f, 0.34f, 0.7f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;

        RectTransform topLine = ModeUiFactory.CreateRect("顶部强调线", rect);
        ModeUiFactory.SetRect(topLine, new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 2f), Vector2.zero);
        UnityEngine.UI.Image line =
            topLine.gameObject.AddComponent<UnityEngine.UI.Image>();
        line.color = new Color(accent.r, accent.g, accent.b, 0.5f);
        line.raycastTarget = false;
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
}
