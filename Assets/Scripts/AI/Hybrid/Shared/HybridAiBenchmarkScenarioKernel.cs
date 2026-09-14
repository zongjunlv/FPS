using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FPS.AI.Hybrid.Shared
{
    [Flags]
    public enum HybridAiBenchmarkRole : byte
    {
        None = 0,
        Raider = 1 << 0,
        Suppressor = 1 << 1,
        Support = 1 << 2
    }

    public readonly struct HybridAiBenchmarkAgentState
    {
        public HybridAiBenchmarkAgentState(
            string stableId,
            int generation,
            Vector3 position,
            float healthRatio,
            HybridAiBenchmarkRole role,
            bool isAlive)
        {
            StableId = stableId ?? string.Empty;
            Generation = Mathf.Max(0, generation);
            Position = position;
            HealthRatio = Mathf.Clamp01(healthRatio);
            Role = role;
            IsAlive = isAlive;
        }

        public string StableId { get; }
        public int Generation { get; }
        public Vector3 Position { get; }
        public float HealthRatio { get; }
        public HybridAiBenchmarkRole Role { get; }
        public bool IsAlive { get; }
    }

    public readonly struct HybridAiBenchmarkPerception
    {
        public HybridAiBenchmarkPerception(
            Vector3 targetPosition,
            bool hasLineOfSight,
            bool targetInCover)
        {
            TargetPosition = targetPosition;
            HasLineOfSight = hasLineOfSight;
            TargetInCover = targetInCover;
        }

        public Vector3 TargetPosition { get; }
        public bool HasLineOfSight { get; }
        public bool TargetInCover { get; }
    }

    public readonly struct HybridAiBenchmarkNeighborhood
    {
        public HybridAiBenchmarkNeighborhood(
            int friendlyRaiderCount,
            int friendlySuppressorCount,
            int friendlySupportCount,
            bool hasSupportCoverage)
        {
            FriendlyRaiderCount = Mathf.Max(0, friendlyRaiderCount);
            FriendlySuppressorCount = Mathf.Max(
                0,
                friendlySuppressorCount);
            FriendlySupportCount = Mathf.Max(0, friendlySupportCount);
            HasSupportCoverage = hasSupportCoverage;
        }

        public int FriendlyRaiderCount { get; }
        public int FriendlySuppressorCount { get; }
        public int FriendlySupportCount { get; }
        public bool HasSupportCoverage { get; }
    }

    /// <summary>
    /// One deterministic A/B input generator. Both adapters project these
    /// values; UnityEngine.Random and adapter-specific facts are forbidden.
    /// </summary>
    public static class HybridAiBenchmarkScenarioKernel
    {
        public const float FriendlyRadius = 14f;

        public static HybridAiBenchmarkAgentState[] CreateAgents(
            int enemyCount,
            int seed)
        {
            int safeCount = Mathf.Max(0, enemyCount);
            var agents = new HybridAiBenchmarkAgentState[safeCount];

            for (int index = 0; index < safeCount; index++)
            {
                agents[index] = CreateAgent(index, seed);
            }

            return agents;
        }

        public static HybridAiBenchmarkAgentState CreateAgent(
            int index,
            int seed)
        {
            int safeIndex = Mathf.Max(0, index);
            uint hash = Hash(seed, safeIndex);
            float radius = 14f + safeIndex % 10 * 2f;
            double unitAngle = Fraction(
                safeIndex * 0.61803398875d +
                (seed & 0xFFFF) * 0.000001d);
            float angle = (float)(Math.PI * 2d * unitAngle);
            Vector3 position = new(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
            float healthRatio = 0.35f + hash % 6501u / 10000f;
            HybridAiBenchmarkRole role = (safeIndex % 3) switch
            {
                0 => HybridAiBenchmarkRole.Raider,
                1 => HybridAiBenchmarkRole.Suppressor,
                _ => HybridAiBenchmarkRole.Support
            };
            return new HybridAiBenchmarkAgentState(
                $"benchmark.enemy.{safeIndex:D4}",
                1,
                position,
                healthRatio,
                role,
                true);
        }

        public static HybridAiBenchmarkPerception SamplePerception(
            int agentIndex,
            int seed,
            int tick)
        {
            uint hash = Hash(seed, Mathf.Max(0, agentIndex));
            bool lineOfSight = (hash & 3u) != 0u;
            float phase = ((seed % 1024) + Mathf.Max(0, tick)) * 0.017f;
            Vector3 target = new(
                Mathf.Sin(phase) * 4f,
                0f,
                Mathf.Cos(phase) * 4f);
            return new HybridAiBenchmarkPerception(
                target,
                lineOfSight,
                !lineOfSight);
        }

        public static HybridAiBenchmarkNeighborhood CountNeighborhood(
            IReadOnlyList<HybridAiBenchmarkAgentState> agents,
            int sourceIndex,
            float radius = FriendlyRadius)
        {
            if (agents == null || sourceIndex < 0 ||
                sourceIndex >= agents.Count)
            {
                return default;
            }

            HybridAiBenchmarkAgentState source = agents[sourceIndex];
            float radiusSquared = Mathf.Max(0f, radius);
            radiusSquared *= radiusSquared;
            int raiders = 0;
            int suppressors = 0;
            int supporters = 0;
            bool supportCoverage = false;

            for (int index = 0; index < agents.Count; index++)
            {
                if (index == sourceIndex || !agents[index].IsAlive)
                {
                    continue;
                }

                HybridAiBenchmarkAgentState neighbor = agents[index];

                if ((neighbor.Position - source.Position).sqrMagnitude >
                    radiusSquared)
                {
                    continue;
                }

                if ((neighbor.Role & HybridAiBenchmarkRole.Raider) != 0)
                {
                    raiders++;
                }

                if ((neighbor.Role & HybridAiBenchmarkRole.Suppressor) != 0)
                {
                    suppressors++;
                }

                if ((neighbor.Role & HybridAiBenchmarkRole.Support) == 0)
                {
                    continue;
                }

                supporters++;
                supportCoverage = true;
            }

            return new HybridAiBenchmarkNeighborhood(
                raiders,
                suppressors,
                supporters,
                supportCoverage);
        }

        public static HybridAiWorldSnapshot ProjectWorld(
            in HybridAiBenchmarkAgentState agent,
            in HybridAiBenchmarkPerception perception,
            in HybridAiBenchmarkNeighborhood neighborhood,
            HybridAiExecutionPolicy policy,
            float actionCooldownRemaining = 0f,
            float actionCommitmentRemaining = 0f)
        {
            float distance = Vector3.Distance(
                agent.Position,
                perception.TargetPosition);
            var facts = new EnemyUtilityWorldFacts(
                distance,
                perception.HasLineOfSight,
                perception.TargetInCover,
                agent.HealthRatio,
                neighborhood.FriendlyRaiderCount,
                neighborhood.FriendlySuppressorCount,
                neighborhood.FriendlySupportCount,
                neighborhood.HasSupportCoverage,
                actionCooldownRemaining,
                actionCommitmentRemaining);
            return new HybridAiWorldSnapshot(
                agent.StableId,
                agent.Generation,
                agent.Position,
                perception.TargetPosition,
                facts,
                policy.Classify(distance),
                agent.IsAlive);
        }

        public static uint Hash(int seed, int index)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint value = offset;
            AppendInt(ref value, seed, prime);
            AppendInt(ref value, index, prime);
            return value;
        }

        private static void AppendInt(ref uint value, int input, uint prime)
        {
            uint bits = unchecked((uint)input);

            for (int shift = 0; shift < 32; shift += 8)
            {
                value ^= (byte)(bits >> shift);
                value *= prime;
            }
        }

        private static double Fraction(double value)
        {
            return value - Math.Floor(value);
        }
    }

    public static class HybridAiIntentDigestKernel
    {
        public static string Compute(
            IEnumerable<HybridAiActionIntent> intents)
        {
            HybridAiActionIntent[] ordered = (intents ??
                    Array.Empty<HybridAiActionIntent>())
                .OrderBy(value => value.AgentStableId,
                    StringComparer.Ordinal)
                .ToArray();
            var payload = new StringBuilder(ordered.Length * 96);

            for (int index = 0; index < ordered.Length; index++)
            {
                HybridAiActionIntent intent = ordered[index];
                payload.Append(intent.AgentStableId).Append('|')
                    .Append(intent.Generation.ToString(
                        CultureInfo.InvariantCulture)).Append('|')
                    .Append(intent.ActionId).Append('|')
                    .Append((int)intent.ActionKind).Append('|')
                    .Append((int)intent.RangeBand).Append('|')
                    .Append((int)intent.ExecutionOwner).Append('|')
                    .Append((int)intent.HandoffSignal).Append('\n');
            }

            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(
                Encoding.UTF8.GetBytes(payload.ToString()));
            var result = new StringBuilder(digest.Length * 2);

            for (int index = 0; index < digest.Length; index++)
            {
                result.Append(digest[index].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }

            return result.ToString();
        }
    }
}
