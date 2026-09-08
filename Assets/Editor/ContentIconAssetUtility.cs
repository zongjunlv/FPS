using System;
using UnityEditor;
using UnityEngine;

public static class ContentIconAssetUtility
{
    public static Sprite Get(HudIconId id)
    {
        string name = id switch
        {
            HudIconId.Health => "health", HudIconId.Armor => "armor",
            HudIconId.Ammo => "ammo", HudIconId.Rifle => "weapon-rifle",
            HudIconId.Handgun => "weapon-handgun",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        Sprite source = Resources.Load<Sprite>("UI/Icons/" + name);
        if (source == null) throw new InvalidOperationException("Missing source icon: " + name);
        if (id != HudIconId.Rifle && id != HudIconId.Handgun) return source;
        string path = "Assets/Resources/UI/Icons/" + name + "-content.asset";
        Sprite persistent = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (persistent != null) return persistent;
        Rect rect = id == HudIconId.Rifle ? new Rect(17, 98, 456, 132) : new Rect(145, 120, 156, 107);
        persistent = Sprite.Create(source.texture, rect, Vector2.one * 0.5f, source.pixelsPerUnit);
        persistent.name = name + "-content";
        AssetDatabase.CreateAsset(persistent, path);
        return persistent;
    }

    [MenuItem("FPS/Content/Repair Missing Weapon Content Icons")]
    public static void RepairMissingIcons()
    {
        var targets = new (string Path, HudIconId Icon)[]
        {
            ("Items/RifleAmmo", HudIconId.Rifle), ("Items/HandgunAmmo", HudIconId.Handgun),
            ("Upgrades/WeakpointAnalysis", HudIconId.Rifle), ("Upgrades/OverchargedCore", HudIconId.Handgun),
            ("Upgrades/RapidCycling", HudIconId.Rifle), ("Upgrades/QuickHands", HudIconId.Handgun),
            ("Upgrades/RecoilDampening", HudIconId.Rifle)
        };
        foreach (var target in targets)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Resources/Content/CityNew/" + target.Path + ".asset");
            if (asset == null) continue;
            using var data = new SerializedObject(asset);
            var icon = data.FindProperty("icon");
            if (icon.objectReferenceValue != null) continue;
            icon.objectReferenceValue = Get(target.Icon);
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();
    }
}
