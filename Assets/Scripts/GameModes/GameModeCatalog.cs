using System;
using System.Collections.Generic;
using FPS.Core.GameModes;
using UnityEngine;

public static class GameModeScenePaths
{
    public const string Entry = "Assets/Scenes/Modes/ModeEntry.unity";
    public const string Tutorial = "Assets/Scenes/Modes/Tutorial.unity";
    public const string BattlePreparation =
        "Assets/Scenes/Modes/BattlePreparation.unity";
    public const string CoopLogin = "Assets/Scenes/Modes/CoopLogin.unity";
    public const string CityNew =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";
}

[Serializable]
public sealed class GameModeDefinition
{
    [SerializeField] private GameModeId mode;
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private string entryScenePath;
    [SerializeField] private GameModeStage entryStage;
    [SerializeField] private string gameplayScenePath;
    [SerializeField] private GameModeStage gameplayStage;

    public GameModeId Mode => mode;
    public string StableId => stableId;
    public string DisplayName => displayName;
    public string Description => description;
    public string EntryScenePath => entryScenePath;
    public GameModeStage EntryStage => entryStage;
    public string GameplayScenePath => gameplayScenePath;
    public GameModeStage GameplayStage => gameplayStage;
    public bool HasGameplayRoute =>
        !string.IsNullOrWhiteSpace(gameplayScenePath);

    public void Configure(
        GameModeId configuredMode,
        string configuredDisplayName,
        string configuredDescription,
        string configuredEntryScenePath,
        GameModeStage configuredEntryStage,
        string configuredGameplayScenePath = null,
        GameModeStage configuredGameplayStage = GameModeStage.Entry)
    {
        mode = configuredMode;
        stableId = GameModeIds.ToStableId(configuredMode);
        displayName = configuredDisplayName?.Trim() ?? string.Empty;
        description = configuredDescription?.Trim() ?? string.Empty;
        entryScenePath = configuredEntryScenePath?.Trim() ?? string.Empty;
        entryStage = configuredEntryStage;
        gameplayScenePath = configuredGameplayScenePath?.Trim() ?? string.Empty;
        gameplayStage = configuredGameplayStage;
    }
}

[CreateAssetMenu(
    fileName = "GameModeCatalog",
    menuName = "FPS/Game Modes/Catalog")]
public sealed class GameModeCatalog : ScriptableObject
{
    public const string ResourcesPath = "GameModes/GameModeCatalog";

    [SerializeField] private string entryScenePath;
    [SerializeField] private GameModeDefinition[] modes =
        Array.Empty<GameModeDefinition>();

    public string EntryScenePath => entryScenePath;
    public IReadOnlyList<GameModeDefinition> Modes => modes;

    public static GameModeCatalog LoadDefault()
    {
        return Resources.Load<GameModeCatalog>(ResourcesPath);
    }

    public void Configure(
        string configuredEntryScenePath,
        GameModeDefinition[] configuredModes)
    {
        entryScenePath = configuredEntryScenePath?.Trim() ?? string.Empty;
        modes = configuredModes ?? Array.Empty<GameModeDefinition>();
    }

    public bool TryGet(
        GameModeId mode,
        out GameModeDefinition definition)
    {
        for (int index = 0; index < modes.Length; index++)
        {
            if (modes[index] != null && modes[index].Mode == mode)
            {
                definition = modes[index];
                return true;
            }
        }

        definition = null;
        return false;
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(entryScenePath))
        {
            error = "模式入口场景路径不能为空。";
            return false;
        }

        var discovered = new HashSet<GameModeId>();
        GameModeId[] required =
        {
            GameModeId.Tutorial,
            GameModeId.SoloBattle,
            GameModeId.Coop
        };

        for (int index = 0; index < modes.Length; index++)
        {
            GameModeDefinition definition = modes[index];
            if (definition == null || definition.Mode == GameModeId.None)
            {
                error = $"模式配置第 {index + 1} 项无效。";
                return false;
            }

            if (!discovered.Add(definition.Mode))
            {
                error = $"模式 {definition.Mode} 重复配置。";
                return false;
            }

            if (definition.StableId !=
                    GameModeIds.ToStableId(definition.Mode) ||
                string.IsNullOrWhiteSpace(definition.DisplayName) ||
                string.IsNullOrWhiteSpace(definition.EntryScenePath))
            {
                error = $"模式 {definition.Mode} 的稳定标识、名称或场景无效。";
                return false;
            }

            if (definition.Mode == GameModeId.SoloBattle &&
                (!definition.HasGameplayRoute ||
                 definition.GameplayStage != GameModeStage.Battle))
            {
                error = "单人战斗模式必须配置正式战斗场景。";
                return false;
            }

            if (definition.HasGameplayRoute &&
                !IsGameplayStageValid(
                    definition.Mode,
                    definition.GameplayStage))
            {
                error = $"模式 {definition.Mode} 的玩法阶段无效。";
                return false;
            }
        }

        for (int index = 0; index < required.Length; index++)
        {
            if (!discovered.Contains(required[index]))
            {
                error = $"缺少模式配置：{required[index]}。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsGameplayStageValid(
        GameModeId mode,
        GameModeStage stage)
    {
        return mode switch
        {
            GameModeId.Tutorial => stage == GameModeStage.Tutorial,
            GameModeId.SoloBattle => stage == GameModeStage.Battle,
            GameModeId.Coop => stage == GameModeStage.CoopLobby ||
                               stage == GameModeStage.CoopBattle,
            _ => false
        };
    }
}
