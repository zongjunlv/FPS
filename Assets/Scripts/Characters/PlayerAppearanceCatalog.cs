using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerAppearanceCatalog",
    menuName = "FPS/Characters/Player Appearance Catalog")]
public sealed class PlayerAppearanceCatalog : ScriptableObject
{
    public const string DefaultAssetPath =
        "Assets/Resources/Content/Characters/PlayerAppearances/PlayerAppearanceCatalog.asset";
    public const string ResourcesPath =
        "Content/Characters/PlayerAppearances/PlayerAppearanceCatalog";
    public const string HostPrefabAssetPath =
        "Assets/Resources/Content/Characters/PlayerAppearances/PlayerAppearanceHost.prefab";
    public const string PreviewSceneAssetPath =
        "Assets/Scenes/CharacterCalibration/PlayerAppearancePreview.unity";

    [SerializeField] private string defaultAppearanceId;
    [SerializeField] private PlayerAppearanceDefinition[] definitions =
        Array.Empty<PlayerAppearanceDefinition>();

    public string DefaultAppearanceId => defaultAppearanceId;
    public IReadOnlyList<PlayerAppearanceDefinition> Definitions =>
        definitions ?? Array.Empty<PlayerAppearanceDefinition>();
    public PlayerAppearanceDefinition DefaultDefinition =>
        Resolve(defaultAppearanceId, out _);

    public void Configure(string defaultId,
        IEnumerable<PlayerAppearanceDefinition> appearanceDefinitions)
    {
        defaultAppearanceId = defaultId?.Trim() ?? string.Empty;
        definitions = appearanceDefinitions?.Where(value => value != null)
            .ToArray() ?? Array.Empty<PlayerAppearanceDefinition>();
    }

    public bool TryGetExact(string stableId,
        out PlayerAppearanceDefinition definition)
    {
        definition = definitions?.FirstOrDefault(value => value != null &&
            string.Equals(value.StableId, stableId,
                StringComparison.Ordinal));
        return definition != null;
    }

    public PlayerAppearanceDefinition Resolve(string requestedId,
        out bool usedFallback)
    {
        if (TryGetExact(requestedId, out PlayerAppearanceDefinition exact))
        {
            usedFallback = false;
            return exact;
        }
        usedFallback = true;
        return definitions?.FirstOrDefault(value => value != null &&
            string.Equals(value.StableId, defaultAppearanceId,
                StringComparison.Ordinal));
    }

    public bool TryValidate(out string error)
    {
        if (definitions == null || definitions.Length < 3)
        {
            error = "玩家外观目录至少需要三个角色定义。";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlayerAppearanceDefinition definition in definitions)
        {
            if (definition == null)
            {
                error = "玩家外观目录包含空定义。";
                return false;
            }
            if (!definition.TryValidate(out error))
            {
                return false;
            }
            if (!ids.Add(definition.StableId))
            {
                error = $"玩家外观 ID 重复：{definition.StableId}";
                return false;
            }
        }
        if (!ids.Contains(defaultAppearanceId))
        {
            error = "玩家外观目录的安全默认角色不存在。";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
