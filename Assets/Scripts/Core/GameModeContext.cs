using System;
using UnityEngine;

namespace FPS.Core.GameModes
{
    public enum GameModeId
    {
        None = 0,
        Tutorial = 10,
        SoloBattle = 20,
        Coop = 30
    }

    public enum GameModeStage
    {
        Entry = 0,
        Tutorial = 10,
        BattlePreparation = 20,
        Battle = 21,
        CoopLogin = 30,
        CoopLobby = 31,
        CoopBattle = 32
    }

    public static class GameModeIds
    {
        public const string Tutorial = "tutorial";
        public const string SoloBattle = "solo-battle";
        public const string Coop = "coop";

        public static string ToStableId(GameModeId mode)
        {
            return mode switch
            {
                GameModeId.None => string.Empty,
                GameModeId.Tutorial => Tutorial,
                GameModeId.SoloBattle => SoloBattle,
                GameModeId.Coop => Coop,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        public static bool TryParse(string stableId, out GameModeId mode)
        {
            mode = stableId switch
            {
                Tutorial => GameModeId.Tutorial,
                SoloBattle => GameModeId.SoloBattle,
                Coop => GameModeId.Coop,
                _ => GameModeId.None
            };
            return mode != GameModeId.None;
        }
    }

    public static class GameModeContext
    {
        private static GameModeId currentMode;
        private static GameModeStage currentStage = GameModeStage.Entry;
        private static GameModeId pendingMode;
        private static GameModeStage pendingStage = GameModeStage.Entry;
        private static int epoch;

        public static GameModeId CurrentMode => currentMode;
        public static GameModeStage CurrentStage => currentStage;
        public static GameModeId RequestedMode =>
            IsTransitioning ? pendingMode : currentMode;
        public static GameModeStage RequestedStage =>
            IsTransitioning ? pendingStage : currentStage;
        public static int Epoch => epoch;
        public static bool IsTransitioning { get; private set; }
        public static string LastFailure { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration()
        {
            ResetForTests();
        }

        public static int BeginTransition(
            GameModeId mode,
            GameModeStage stage)
        {
            if (!IsValidPair(mode, stage))
            {
                throw new ArgumentException(
                    $"Invalid game mode route: {mode}/{stage}.");
            }

            epoch++;
            pendingMode = mode;
            pendingStage = stage;
            IsTransitioning = true;
            LastFailure = string.Empty;
            return epoch;
        }

        public static bool TryActivate(
            GameModeId mode,
            GameModeStage stage,
            out string error)
        {
            if (!IsValidPair(mode, stage))
            {
                error = $"无效的模式场景标识：{mode}/{stage}。";
                LastFailure = error;
                return false;
            }

            if (IsTransitioning &&
                (pendingMode != mode || pendingStage != stage))
            {
                error =
                    $"模式加载结果不匹配，期望 {pendingMode}/{pendingStage}，" +
                    $"实际 {mode}/{stage}。";
                LastFailure = error;
                return false;
            }

            currentMode = mode;
            currentStage = stage;
            pendingMode = GameModeId.None;
            pendingStage = GameModeStage.Entry;
            IsTransitioning = false;
            LastFailure = string.Empty;
            error = string.Empty;
            return true;
        }

        public static void FailTransition(string error)
        {
            pendingMode = GameModeId.None;
            pendingStage = GameModeStage.Entry;
            IsTransitioning = false;
            LastFailure = string.IsNullOrWhiteSpace(error)
                ? "模式加载失败。"
                : error.Trim();
        }

        public static bool IsActive(
            GameModeId mode,
            GameModeStage stage)
        {
            return currentMode == mode && currentStage == stage;
        }

        public static void ResetForTests()
        {
            currentMode = GameModeId.None;
            currentStage = GameModeStage.Entry;
            pendingMode = GameModeId.None;
            pendingStage = GameModeStage.Entry;
            epoch = 0;
            IsTransitioning = false;
            LastFailure = string.Empty;
        }

        private static bool IsValidPair(
            GameModeId mode,
            GameModeStage stage)
        {
            return mode switch
            {
                GameModeId.None => stage == GameModeStage.Entry,
                GameModeId.Tutorial => stage == GameModeStage.Tutorial,
                GameModeId.SoloBattle =>
                    stage == GameModeStage.BattlePreparation ||
                    stage == GameModeStage.Battle,
                GameModeId.Coop =>
                    stage == GameModeStage.CoopLogin ||
                    stage == GameModeStage.CoopLobby ||
                    stage == GameModeStage.CoopBattle,
                _ => false
            };
        }
    }
}
