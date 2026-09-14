using System;
using System.Collections.Generic;
using System.Diagnostics;
using FPS.AI.Hybrid.Shared;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FPS.AI.HybridEcs
{
    /// <summary>
    /// 对 ECS 中的事实做确定性顺序的 Utility 批处理。真正的分数、冷却、承诺、
    /// 滞回与 fallback 全部调用共享 adapter；本系统只负责 ECS 数据编排。
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(HybridEcsPerceptionNeighborhoodSystem))]
    public partial class HybridEcsUtilityIntentSystem : SystemBase
    {
        private readonly List<OrderedEntity> ordered = new(512);
        private readonly List<double> decisionLatencies = new(512);
        private EntityQuery decisionQuery;

        public int LastDecisionCount { get; private set; }
        public int LastNearHandoffCount { get; private set; }
        public int LastHandoffSignalCount { get; private set; }
        public IReadOnlyList<double> LastDecisionLatencies =>
            decisionLatencies;

        protected override void OnCreate()
        {
            decisionQuery = GetEntityQuery(
                ComponentType.ReadOnly<HybridEcsAgent>(),
                ComponentType.ReadOnly<HybridEcsPerceptionInput>(),
                ComponentType.ReadOnly<HybridEcsNeighborhoodFacts>(),
                ComponentType.ReadOnly<HybridEcsPerceptionFacts>(),
                ComponentType.ReadOnly<HybridEcsDecisionConfig>(),
                ComponentType.ReadWrite<HybridEcsActionIntent>(),
                ComponentType.ReadOnly<HybridEcsDecisionRuntime>(),
                ComponentType.ReadOnly<HybridEcsBatchOwned>());
        }

        protected override void OnUpdate()
        {
            LastDecisionCount = 0;
            LastNearHandoffCount = 0;
            LastHandoffSignalCount = 0;
            ordered.Clear();
            decisionLatencies.Clear();

            using NativeArray<Entity> entities = decisionQuery.ToEntityArray(
                Allocator.Temp);

            for (int index = 0; index < entities.Length; index++)
            {
                Entity entity = entities[index];
                HybridEcsAgent agent = EntityManager.GetComponentData<
                    HybridEcsAgent>(entity);
                ordered.Add(new OrderedEntity(
                    entity,
                    agent.StableId.ToString(),
                    agent.Generation));
            }

            ordered.Sort(OrderedEntityComparer.Instance);
            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);

            for (int index = 0; index < ordered.Count; index++)
            {
                EvaluateEntity(ordered[index].Entity, deltaTime);
            }
        }

        private void EvaluateEntity(Entity entity, float deltaTime)
        {
            HybridEcsAgent agent = EntityManager.GetComponentData<
                HybridEcsAgent>(entity);
            HybridEcsPerceptionInput input = EntityManager.GetComponentData<
                HybridEcsPerceptionInput>(entity);
            HybridEcsNeighborhoodFacts neighbors =
                EntityManager.GetComponentData<HybridEcsNeighborhoodFacts>(
                    entity);
            HybridEcsPerceptionFacts perception =
                EntityManager.GetComponentData<HybridEcsPerceptionFacts>(
                    entity);
            HybridEcsDecisionConfig config = EntityManager.GetComponentData<
                HybridEcsDecisionConfig>(entity);
            HybridEcsDecisionRuntime runtime =
                EntityManager.GetComponentObject<HybridEcsDecisionRuntime>(
                    entity);
            long decisionStarted = Stopwatch.GetTimestamp();
            var policy = new HybridAiExecutionPolicy(
                true,
                config.NearMaximumDistance,
                config.MidMaximumDistance,
                config.HandoffHysteresis);
            HybridAiExecutionState execution = runtime.Handoff.Evaluate(
                perception.TargetDistance,
                policy);
            var facts = new EnemyUtilityWorldFacts(
                perception.TargetDistance,
                input.HasLineOfSight != 0,
                input.TargetInCover != 0,
                agent.HealthRatio,
                neighbors.FriendlyRaiderCount,
                neighbors.FriendlySuppressorCount,
                neighbors.FriendlySupportCount,
                neighbors.HasSupportCoverage != 0,
                input.ActionCooldownRemaining,
                input.ActionCommitmentRemaining);
            var world = new HybridAiWorldSnapshot(
                agent.StableId.ToString(),
                agent.Generation,
                ToVector3(agent.Position),
                ToVector3(input.TargetPosition),
                facts,
                perception.RangeBand,
                agent.IsAlive != 0);
            HybridAiActionIntent intent;

            intent = runtime.Decision.Evaluate(
                world,
                execution,
                deltaTime,
                config.DecisionSeed);
            LastDecisionCount++;

            if (execution.Owner == HybridAiExecutionOwner.GameObject)
            {
                // 仍计算同一份玩法意图，保证 GO/ECS A/B 摘要和 commitment
                // 连续；ShouldExecuteInEcs=0 保证近距只交给 GO 执行。
                LastNearHandoffCount++;
            }

            decisionLatencies.Add(
                (Stopwatch.GetTimestamp() - decisionStarted) * 1000d /
                Stopwatch.Frequency);

            EntityManager.SetComponentData(
                entity,
                ConvertIntent(intent));

            if (intent.HandoffSignal != HybridAiHandoffSignal.None)
            {
                EntityManager.SetComponentEnabled<HybridEcsHandoffPending>(
                    entity,
                    true);
                LastHandoffSignalCount++;
            }

        }

        private static HybridEcsActionIntent ConvertIntent(
            in HybridAiActionIntent intent)
        {
            return new HybridEcsActionIntent
            {
                ActionId = new FixedString64Bytes(intent.ActionId ?? string.Empty),
                DecisionReason = new FixedString64Bytes(
                    intent.DecisionReason ?? string.Empty),
                Kind = intent.ActionKind,
                AnchorPosition = ToFloat3(intent.AnchorPosition),
                MovementSpeedMultiplier = intent.MovementSpeedMultiplier,
                RangeBand = intent.RangeBand,
                ExecutionOwner = intent.ExecutionOwner,
                HandoffSignal = intent.HandoffSignal,
                Changed = intent.Changed ? (byte)1 : (byte)0,
                IsValid = intent.HasSelection ? (byte)1 : (byte)0,
                ShouldExecuteInEcs = intent.ExecutionOwner ==
                    HybridAiExecutionOwner.Batch && intent.HasSelection
                    ? (byte)1
                    : (byte)0
            };
        }

        private static Vector3 ToVector3(float3 value) =>
            new(value.x, value.y, value.z);

        private static float3 ToFloat3(Vector3 value) =>
            new(value.x, value.y, value.z);

        private readonly struct OrderedEntity
        {
            public OrderedEntity(Entity entity, string stableId, int generation)
            {
                Entity = entity;
                StableId = stableId ?? string.Empty;
                Generation = generation;
            }

            public Entity Entity { get; }
            public string StableId { get; }
            public int Generation { get; }
        }

        private sealed class OrderedEntityComparer : IComparer<OrderedEntity>
        {
            public static readonly OrderedEntityComparer Instance = new();

            public int Compare(OrderedEntity left, OrderedEntity right)
            {
                int stableId = string.CompareOrdinal(
                    left.StableId,
                    right.StableId);
                return stableId != 0
                    ? stableId
                    : left.Generation.CompareTo(right.Generation);
            }
        }
    }
}
