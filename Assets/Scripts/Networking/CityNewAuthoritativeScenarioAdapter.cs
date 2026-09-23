using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using UnityEngine;

namespace FPS.Networking
{
    /// <summary>
    /// Converts the exact CityNew ScriptableObject catalog used by solo play
    /// into an immutable, deterministic network scenario. This is the sole
    /// content source for host tests and Linux dedicated servers.
    /// </summary>
    public static class CityNewAuthoritativeScenarioAdapter
    {
        private const int NetworkTickRate = 60;
        private const float CooperativeEnemyDamageMultiplier = 0.25f;
        private const float CooperativeStartingArmor = 50f;
        private static readonly Vector3 PlayerOrigin =
            new(49.761f, 0.16f, 59.719f);
        private static readonly Vector3 EncounterCenter =
            new(49.761f, 0.16f, 74.719f);

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            CoopScenarioRegistry.RegisterCityNewFactory(Build);
            CoopEnvironmentReadinessRegistry.RegisterCityNewEnvironment(
                RuntimeNavMeshBootstrap.EnsureForActiveScene,
                () => RuntimeNavMeshBootstrap.IsSceneReady);
            CoopEnvironmentReadinessRegistry.RegisterCityNewContentIsolation(
                () => CoopSceneContentIsolation.DisableLegacySceneEnemies());
        }

        public static CoopScenarioConfiguration Build(
            int seed,
            int maximumPlayers)
        {
            CityNewContentCatalog catalog =
                CityNewContentCatalog.LoadDefault();
            if (catalog == null)
                throw new InvalidOperationException(
                    "找不到 CityNewContentCatalog 资源。");
            if (!catalog.TryValidate(out string validationError))
                throw new InvalidOperationException(validationError);

            int playerCount = Mathf.Clamp(maximumPlayers, 1, 2);
            var players = new CoopPlayerSpawnDefinition[playerCount];
            float center = (playerCount - 1) * 0.5f;
            for (int index = 0; index < players.Length; index++)
            {
                players[index] = new CoopPlayerSpawnDefinition
                {
                    PlayerId = index + 1,
                    Position = PlayerOrigin +
                        Vector3.right * ((index - center) * 2.5f),
                    Health = 100f,
                    Armor = CooperativeStartingArmor,
                    MaximumArmor = 100f
                };
            }

            WaveSequenceDefinition sequence = catalog.WaveSequence;
            var targets = new List<CoopTargetSpawnDefinition>();
            var waves = new CoopWaveDefinition[sequence.WaveCount];
            var loot = new DeterministicLootResolver(seed);
            int targetId = 1;
            for (int stageIndex = 0;
                 stageIndex < sequence.WaveCount;
                 stageIndex++)
            {
                WaveStageDefinition stage = sequence.GetStage(stageIndex);
                WaveDefinition wave = stage.Wave;
                wave.PrepareForRun(seed);
                int waveIndex = stageIndex + 1;
                int count = Mathf.Max(1, wave.TotalEnemyCount);
                waves[stageIndex] = new CoopWaveDefinition
                {
                    WaveIndex = waveIndex,
                    MaximumAlive = Mathf.Clamp(
                        wave.MaximumAliveCount, 1, count),
                    SpawnIntervalTicks = Mathf.Max(0,
                        Mathf.RoundToInt(wave.SpawnInterval *
                                         NetworkTickRate)),
                    IntermissionTicks = Mathf.Max(0,
                        Mathf.RoundToInt(stage.IntermissionAfterSeconds *
                                         NetworkTickRate)),
                    WaveRewards = ConvertDrops(loot.Resolve(
                        catalog.LootDropTable,
                        new LootRewardContext("*", waveIndex,
                            LootRewardTier.WaveClear, -waveIndex))),
                    FinalRewards = ConvertDrops(loot.Resolve(
                        catalog.LootDropTable,
                        new LootRewardContext("*", waveIndex,
                            LootRewardTier.FinalWave,
                            int.MinValue + waveIndex)))
                };

                for (int spawnIndex = 0;
                     spawnIndex < count;
                     spawnIndex++)
                {
                    WaveEnemyEntry entry = wave.GetEntry(spawnIndex);
                    EnemyArchetypeDefinition archetype = entry?.Archetype;
                    if (archetype == null)
                        throw new InvalidOperationException(
                            $"波次 {wave.StableId} 的敌人 {spawnIndex + 1} " +
                            "缺少 Archetype。");

                    AuthoritativeEnemyRole role = ToRole(archetype.RoleTag);
                    IReadOnlyList<LootDropStack> rolled = loot.Resolve(
                        catalog.LootDropTable,
                        new LootRewardContext(
                            archetype.EnemyTypeId,
                            waveIndex,
                            archetype.RewardTier,
                            targetId));
                    LootDropStack drop = rolled.Count > 0
                        ? rolled[0]
                        : default;
                    CoopLootDropDefinition[] allDrops =
                        ConvertDrops(rolled);
                    float angle = DeterministicAngle(seed, targetId);
                    float radius = Mathf.Lerp(
                        wave.MinimumSpawnRadius,
                        wave.MaximumSpawnRadius,
                        DeterministicUnit(seed, targetId * 31));
                    Vector3 position = EncounterCenter + new Vector3(
                        Mathf.Sin(angle) * radius,
                        0f,
                        Mathf.Cos(angle) * radius);
                    EnemyDefinition enemy = catalog.DefaultEnemy;
                    float healthMultiplier = role ==
                        AuthoritativeEnemyRole.Elite ? 2.25f : 1f;
                    targets.Add(new CoopTargetSpawnDefinition
                    {
                        TargetId = targetId,
                        Position = position,
                        Radius = role == AuthoritativeEnemyRole.Elite
                            ? 1.15f
                            : 0.8f,
                        Health = Mathf.Max(1f, enemy.HP * healthMultiplier),
                        DropDefinitionId = drop.ItemStableId,
                        DropQuantity = Mathf.Max(1, drop.Quantity),
                        LootDrops = allDrops,
                        HeadOffset = new Vector3(0f,
                            role == AuthoritativeEnemyRole.Elite
                                ? 1.05f
                                : 0.75f,
                            0f),
                        HeadRadius = role == AuthoritativeEnemyRole.Elite
                            ? 0.38f
                            : 0.3f,
                        Role = role,
                        MoveSpeed = BaseMoveSpeed(role),
                        AttackRange = AttackRange(role),
                        AttackDamage = Mathf.Max(1f,
                            enemy.Attack * DamageScale(role) *
                            CooperativeEnemyDamageMultiplier),
                        AttackIntervalTicks = AttackInterval(role),
                        RewardExperience = role ==
                            AuthoritativeEnemyRole.Elite
                                ? enemy.RewardExperience * 3
                                : enemy.RewardExperience,
                        ArchetypeId = archetype.StableId,
                        PresentationAddress = archetype.TemplateAddress,
                        WaveIndex = waveIndex,
                        SpawnOrder = spawnIndex,
                        // Sequenced simulations force all targets into the
                        // pool initially. This value remains non-zero to make
                        // older diagnostics clearly identify scheduled data.
                        SpawnTick = 1
                    });
                    targetId++;
                }
            }

            var mission = new AuthoritativeMissionDefinition(
                new NetVector3(51.059917d, 0.766349d, 72.16028d),
                new NetVector3(48.414d, 0.05d, 41.41d),
                terminalRadius: 3d,
                extractionRadius: 5d,
                reviveRadius: 2.5d,
                terminalHoldTicks: 150,
                extractionHoldTicks: 120,
                reviveHoldTicks: 90,
                revivedHealth: 60d);
            return new CoopScenarioConfiguration(
                players,
                targets.ToArray(),
                waves,
                mission,
                seed,
                catalog.StableId);
        }

        private static CoopLootDropDefinition[] ConvertDrops(
            IReadOnlyList<LootDropStack> rolled)
        {
            var result = new CoopLootDropDefinition[rolled?.Count ?? 0];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new CoopLootDropDefinition
                {
                    ItemId = rolled[index].ItemStableId,
                    Quantity = Mathf.Max(1, rolled[index].Quantity)
                };
            }
            return result;
        }

        private static AuthoritativeEnemyRole ToRole(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "raider" => AuthoritativeEnemyRole.Raider,
                "support" => AuthoritativeEnemyRole.Support,
                "suppressor" => AuthoritativeEnemyRole.Suppressor,
                "elite" => AuthoritativeEnemyRole.Elite,
                _ => AuthoritativeEnemyRole.Assault
            };
        }

        private static float BaseMoveSpeed(AuthoritativeEnemyRole role) =>
            role switch
            {
                AuthoritativeEnemyRole.Raider => 3.2f,
                AuthoritativeEnemyRole.Support => 2.1f,
                AuthoritativeEnemyRole.Suppressor => 1.9f,
                AuthoritativeEnemyRole.Elite => 2.5f,
                _ => 2.35f
            };

        private static float AttackRange(AuthoritativeEnemyRole role) =>
            role == AuthoritativeEnemyRole.Suppressor ? 13f : 1.8f;

        private static float DamageScale(AuthoritativeEnemyRole role) =>
            role switch
            {
                AuthoritativeEnemyRole.Support => 0.2f,
                AuthoritativeEnemyRole.Suppressor => 0.3f,
                AuthoritativeEnemyRole.Elite => 0.4f,
                _ => 0.24f
            };

        private static int AttackInterval(AuthoritativeEnemyRole role) =>
            role == AuthoritativeEnemyRole.Suppressor ? 90 : 60;

        private static float DeterministicAngle(int seed, int value)
        {
            return DeterministicUnit(seed, value) * Mathf.PI * 2f;
        }

        private static float DeterministicUnit(int seed, int value)
        {
            unchecked
            {
                uint hash = (uint)seed;
                hash ^= (uint)value + 0x9e3779b9u + (hash << 6) +
                        (hash >> 2);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                return (hash & 0x00ffffffu) / 16777215f;
            }
        }
    }
}
