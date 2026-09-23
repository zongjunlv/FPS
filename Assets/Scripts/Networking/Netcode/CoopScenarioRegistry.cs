using System;
using FPS.Networking.Domain;

namespace FPS.Networking.Netcode
{
    [Serializable]
    public struct CoopWaveDefinition
    {
        public int WaveIndex;
        public int MaximumAlive;
        public int SpawnIntervalTicks;
        public int IntermissionTicks;
        public CoopLootDropDefinition[] WaveRewards;
        public CoopLootDropDefinition[] FinalRewards;

        public AuthoritativeWaveDefinition ToDomain()
        {
            return new AuthoritativeWaveDefinition(
                WaveIndex,
                MaximumAlive,
                SpawnIntervalTicks,
                IntermissionTicks,
                WaveRewards == null ? null :
                Array.ConvertAll(WaveRewards, value => value.ToDomain()),
                FinalRewards == null ? null :
                Array.ConvertAll(FinalRewards, value => value.ToDomain()));
        }
    }

    public sealed class CoopScenarioConfiguration
    {
        public CoopScenarioConfiguration(
            CoopPlayerSpawnDefinition[] players,
            CoopTargetSpawnDefinition[] targets,
            CoopWaveDefinition[] waves,
            AuthoritativeMissionDefinition mission,
            int runSeed,
            string contentId)
        {
            Players = players ?? throw new ArgumentNullException(nameof(players));
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            Waves = waves ?? Array.Empty<CoopWaveDefinition>();
            Mission = mission ?? AuthoritativeMissionDefinition.Default;
            RunSeed = runSeed;
            ContentId = string.IsNullOrWhiteSpace(contentId)
                ? "unknown"
                : contentId.Trim();
        }

        public CoopPlayerSpawnDefinition[] Players { get; }
        public CoopTargetSpawnDefinition[] Targets { get; }
        public CoopWaveDefinition[] Waves { get; }
        public AuthoritativeMissionDefinition Mission { get; }
        public int RunSeed { get; }
        public string ContentId { get; }
        public int RequiredKills => Targets.Length;
    }

    /// <summary>
    /// Composition owns Unity content assets, while networking owns the
    /// authoritative runtime. This registry is the dependency-inversion seam
    /// between the two assemblies and prevents Session/Dedicated paths from
    /// growing separate hard-coded encounters again.
    /// </summary>
    public static class CoopScenarioRegistry
    {
        public const string CityNewScenarioId = "city_new.coop.authoritative";

        private static Func<int, int, CoopScenarioConfiguration> cityNewFactory;

        public static bool HasCityNewFactory => cityNewFactory != null;

        public static void RegisterCityNewFactory(
            Func<int, int, CoopScenarioConfiguration> factory)
        {
            cityNewFactory = factory ?? throw new ArgumentNullException(
                nameof(factory));
        }

        public static bool TryCreateCityNew(
            int seed,
            int maximumPlayers,
            out CoopScenarioConfiguration scenario,
            out string error)
        {
            scenario = null;
            if (cityNewFactory == null)
            {
                error = "CityNew 权威内容适配器尚未注册。";
                return false;
            }

            try
            {
                scenario = cityNewFactory(seed, maximumPlayers);
                if (scenario == null || scenario.Players.Length == 0 ||
                    scenario.Targets.Length == 0 || scenario.Waves.Length == 0)
                {
                    scenario = null;
                    error = "CityNew 权威战局定义为空或不完整。";
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                scenario = null;
                error = $"构建 CityNew 权威战局失败：{exception.Message}";
                return false;
            }
        }

#if UNITY_INCLUDE_TESTS
        public static void ResetForTests()
        {
            cityNewFactory = null;
        }
#endif
    }
}
