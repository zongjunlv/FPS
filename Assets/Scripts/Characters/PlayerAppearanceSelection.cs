using System;
using UnityEngine;

public static class PlayerAppearanceSelection
{
    public const string PlayerPrefsKey = "fps.player.appearance.selected.v1";

    public static string CurrentAppearanceId { get; private set; } = string.Empty;
    public static string LastWarning { get; private set; } = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        CurrentAppearanceId = string.Empty;
        LastWarning = string.Empty;
    }

    public static PlayerAppearanceDefinition LoadOrDefault(
        PlayerAppearanceCatalog catalog, out bool usedFallback)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        string storedId = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        PlayerAppearanceDefinition definition = catalog.Resolve(storedId,
            out usedFallback);
        if (definition == null)
            throw new InvalidOperationException("玩家外观目录缺少安全默认角色。");

        CurrentAppearanceId = definition.StableId;
        LastWarning = usedFallback
            ? string.IsNullOrWhiteSpace(storedId)
                ? "尚未选择角色，已为你选中安全默认角色。"
                : $"上次选择的角色 '{storedId}' 已失效，已回退安全默认角色。"
            : string.Empty;
        return definition;
    }

    public static bool TrySave(PlayerAppearanceCatalog catalog,
        string requestedId, out PlayerAppearanceDefinition definition,
        out string message)
    {
        if (catalog == null)
        {
            definition = null;
            message = "角色资源目录加载失败，无法开始战斗。";
            return false;
        }

        definition = catalog.Resolve(requestedId, out bool usedFallback);
        string validationError = "安全默认角色不可用。";
        if (definition == null ||
            !definition.TryValidate(out validationError))
        {
            message = string.IsNullOrWhiteSpace(validationError)
                ? "角色资源加载失败，且安全默认角色不可用。"
                : $"角色资源加载失败：{validationError}";
            return false;
        }

        CurrentAppearanceId = definition.StableId;
        PlayerPrefs.SetString(PlayerPrefsKey, CurrentAppearanceId);
        PlayerPrefs.Save();
        LastWarning = usedFallback
            ? $"所选角色 '{requestedId}' 不可用，已改用安全默认角色。"
            : string.Empty;
        message = LastWarning;
        return true;
    }
}
