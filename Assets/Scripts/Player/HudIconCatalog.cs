using System.Collections.Generic;
using UnityEngine;

public enum HudIconId
{
    Health,
    Armor,
    Rifle,
    Handgun,
    Ammo
}

public sealed class HudIconCatalog
{
    private readonly Dictionary<HudIconId, Sprite> icons = new();
    private Sprite placeholder;

    public HudIconCatalog()
    {
        Load(HudIconId.Health, "UI/Icons/health");
        Load(HudIconId.Armor, "UI/Icons/armor");
        LoadCropped(
            HudIconId.Rifle,
            "UI/Icons/weapon-rifle",
            new Rect(17f, 98f, 456f, 132f));
        LoadCropped(
            HudIconId.Handgun,
            "UI/Icons/weapon-handgun",
            new Rect(145f, 120f, 156f, 107f));
        Load(HudIconId.Ammo, "UI/Icons/ammo");
    }

    public Sprite Get(HudIconId iconId)
    {
        return icons.TryGetValue(iconId, out Sprite icon) && icon != null
            ? icon
            : GetPlaceholder();
    }

    public bool UsesPlaceholder(HudIconId iconId)
    {
        return !icons.TryGetValue(iconId, out Sprite icon) || icon == null;
    }

    public Sprite GetWeaponIcon(string weaponName)
    {
        bool isHandgun = !string.IsNullOrEmpty(weaponName) &&
            (weaponName.ToLowerInvariant().Contains("handgun") ||
             weaponName.ToLowerInvariant().Contains("pistol"));
        return Get(isHandgun ? HudIconId.Handgun : HudIconId.Rifle);
    }

    private void Load(HudIconId iconId, string resourcePath)
    {
        icons[iconId] = Resources.Load<Sprite>(resourcePath);
    }

    private void LoadCropped(
        HudIconId iconId,
        string resourcePath,
        Rect visibleBounds)
    {
        Sprite source = Resources.Load<Sprite>(resourcePath);

        if (source == null || source.texture == null)
        {
            icons[iconId] = null;
            return;
        }

        Rect textureBounds = new Rect(
            0f,
            0f,
            source.texture.width,
            source.texture.height);
        float xMin = Mathf.Clamp(
            visibleBounds.xMin,
            textureBounds.xMin,
            textureBounds.xMax - 1f);
        float yMin = Mathf.Clamp(
            visibleBounds.yMin,
            textureBounds.yMin,
            textureBounds.yMax - 1f);
        float xMax = Mathf.Clamp(
            visibleBounds.xMax,
            xMin + 1f,
            textureBounds.xMax);
        float yMax = Mathf.Clamp(
            visibleBounds.yMax,
            yMin + 1f,
            textureBounds.yMax);
        Sprite cropped = Sprite.Create(
            source.texture,
            Rect.MinMaxRect(xMin, yMin, xMax, yMax),
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        cropped.name = source.name + "_HUD_Cropped";
        cropped.hideFlags = HideFlags.HideAndDontSave;
        icons[iconId] = cropped;
    }

    private Sprite GetPlaceholder()
    {
        if (placeholder != null)
        {
            return placeholder;
        }

        const int size = 32;
        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false)
        {
            name = "HUD Icon Placeholder",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color clear = new Color(1f, 1f, 1f, 0f);
        Color white = Color.white;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool border = x < 3 || x >= size - 3 ||
                    y < 3 || y >= size - 3;
                bool cross = Mathf.Abs(x - size / 2) < 2 ||
                    Mathf.Abs(y - size / 2) < 2;
                texture.SetPixel(x, y, border || cross ? white : clear);
            }
        }

        texture.Apply(false, true);
        placeholder = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        placeholder.name = "HUD Icon Placeholder";
        placeholder.hideFlags = HideFlags.HideAndDontSave;
        return placeholder;
    }
}
