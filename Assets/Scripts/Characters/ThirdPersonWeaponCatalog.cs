using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "ThirdPersonWeaponCatalog",
    menuName = "FPS/Characters/Third Person Weapon Catalog")]
public sealed class ThirdPersonWeaponCatalog : ScriptableObject
{
    public const string DefaultAssetPath =
        "Assets/Resources/Content/Weapons/ThirdPerson/" +
        "ThirdPersonWeaponCatalog.asset";
    public const string ResourcesPath =
        "Content/Weapons/ThirdPerson/ThirdPersonWeaponCatalog";

    [SerializeField] private string defaultWeaponId;
    [SerializeField] private ThirdPersonWeaponDefinition[] definitions =
        Array.Empty<ThirdPersonWeaponDefinition>();

    public string DefaultWeaponId => defaultWeaponId;
    public IReadOnlyList<ThirdPersonWeaponDefinition> Definitions =>
        definitions ?? Array.Empty<ThirdPersonWeaponDefinition>();

    public void Configure(string defaultId,
        IEnumerable<ThirdPersonWeaponDefinition> entries)
    {
        defaultWeaponId = defaultId?.Trim() ?? string.Empty;
        definitions = entries?.Where(value => value != null).ToArray() ??
                      Array.Empty<ThirdPersonWeaponDefinition>();
    }

    public ThirdPersonWeaponDefinition Resolve(
        string requestedId,
        out bool usedFallback)
    {
        ThirdPersonWeaponDefinition selected = definitions?.FirstOrDefault(
            value => value != null && string.Equals(value.StableId,
                requestedId?.Trim(), StringComparison.Ordinal));
        usedFallback = selected == null;
        if (selected != null) return selected;
        return definitions?.FirstOrDefault(value => value != null &&
            string.Equals(value.StableId, defaultWeaponId,
                StringComparison.Ordinal));
    }

    public bool TryValidate(out string error)
    {
        if (definitions == null || definitions.Length < 2)
        {
            error = "第三人称武器目录至少需要步枪和手枪两条定义。";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ThirdPersonWeaponDefinition definition in definitions)
        {
            if (definition == null)
            {
                error = "武器目录包含空定义。";
                return false;
            }
            if (!ids.Add(definition.StableId))
            {
                error = $"武器目录包含重复 ID：{definition.StableId}";
                return false;
            }
            if (!definition.TryValidate(out error)) return false;
        }
        if (!ids.Contains(defaultWeaponId))
        {
            error = "默认武器 ID 不在目录中。";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
