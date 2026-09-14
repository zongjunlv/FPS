using System;
using System.Collections.Generic;
using System.Linq;
using FPS.AI.Hybrid.Shared;
using FPS.AI.HybridEcs;
using FPS.Performance.HybridAi;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FPS.Tests.Issue64Ecs
{
    public sealed class Issue64EcsVerticalSliceTests
    {
        private World world;
        private EntityManager manager;
        private HybridEcsEntityRegistry registry;
        private HybridEcsPerceptionNeighborhoodSystem perceptionSystem;
        private HybridEcsUtilityIntentSystem utilitySystem;
        private EnemyUtilityProfileDefinition profile;

        [SetUp]
        public void SetUp()
        {
            world = new World("Issue64 ECS Tests");
            manager = world.EntityManager;
            registry = new HybridEcsEntityRegistry(manager);
            perceptionSystem = world.CreateSystemManaged<
                HybridEcsPerceptionNeighborhoodSystem>();
            utilitySystem = world.CreateSystemManaged<
                HybridEcsUtilityIntentSystem>();
            profile = Resources.Load<EnemyUtilityProfileDefinition>(
                "Content/CityNew/Enemies/Utility/RaiderUtility");
            Assert.That(profile, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            registry?.Dispose();
            registry = null;

            if (world != null && world.IsCreated)
            {
                world.Dispose();
            }

            world = null;
        }

        [Test]
        public void EntitiesPackageRunsTheFormalEcsWorld()
        {
            Entity entity = manager.CreateEntity(typeof(HybridEcsAgent));

            Assert.That(manager.Exists(entity), Is.True);
            Assert.That(typeof(Entity).Assembly.GetName().Name,
                Is.EqualTo("Unity.Entities"));
        }

        [Test]
        public void NeighborhoodBatchCountsRolesAndIgnoresDeadAgents()
        {
            HybridEcsAgentHandle source = Register(
                "enemy.source",
                1,
                new float3(20f, 0f, 0f),
                HybridEcsRoleFlags.Raider);
            Register("enemy.raider", 1, new float3(21f, 0f, 0f),
                HybridEcsRoleFlags.Raider);
            Register("enemy.suppressor", 1, new float3(22f, 0f, 0f),
                HybridEcsRoleFlags.Suppressor);
            Register("enemy.support", 1, new float3(23f, 0f, 0f),
                HybridEcsRoleFlags.Support);
            Register("enemy.dead-support", 1, new float3(24f, 0f, 0f),
                HybridEcsRoleFlags.Support, false);

            UpdateSystems();

            HybridEcsNeighborhoodFacts facts = manager.GetComponentData<
                HybridEcsNeighborhoodFacts>(source.Entity);
            Assert.That(facts.FriendlyRaiderCount, Is.EqualTo(1));
            Assert.That(facts.FriendlySuppressorCount, Is.EqualTo(1));
            Assert.That(facts.FriendlySupportCount, Is.EqualTo(1));
            Assert.That(facts.HasSupportCoverage, Is.EqualTo(1));
            Assert.That(perceptionSystem.LastProcessedCount, Is.EqualTo(5));
        }

        [Test]
        public void FarAgentUsesSharedUtilityRulesAndProducesMovementIntent()
        {
            HybridEcsAgentHandle handle = Register(
                "enemy.far",
                1,
                new float3(32f, 0f, 0f),
                HybridEcsRoleFlags.Raider);

            UpdateSystems();

            Assert.That(registry.TryReadIntent(handle, out var intent), Is.True);
            Assert.That(intent.IsValid, Is.EqualTo(1));
            Assert.That(intent.ActionId.ToString(), Is.Not.Empty);
            Assert.That(intent.ExecutionOwner,
                Is.EqualTo(HybridAiExecutionOwner.Batch));
            Assert.That(intent.HandoffSignal,
                Is.EqualTo(HybridAiHandoffSignal.ActivateBatch));
            Assert.That(intent.ShouldExecuteInEcs, Is.EqualTo(1));
            Assert.That(utilitySystem.LastDecisionCount, Is.EqualTo(1));
        }

        [Test]
        public void NearAgentHandsExecutionToGameObjectWithoutEcsMovement()
        {
            HybridEcsAgentHandle handle = Register(
                "enemy.near",
                1,
                new float3(20f, 0f, 0f),
                HybridEcsRoleFlags.Raider);

            UpdateSystems();
            Assert.That(registry.Synchronize(
                handle,
                new HybridEcsAgent
                {
                    StableId = new FixedString64Bytes("enemy.near"),
                    Generation = 1,
                    Position = new float3(5f, 0f, 0f),
                    HealthRatio = 1f,
                    RoleFlags = (byte)HybridEcsRoleFlags.Raider,
                    IsAlive = 1
                },
                new HybridEcsPerceptionInput
                {
                    TargetPosition = float3.zero,
                    HasLineOfSight = 1
                }), Is.True);
            UpdateSystems();

            Assert.That(registry.TryReadIntent(handle, out var intent), Is.True);
            Assert.That(intent.IsValid, Is.EqualTo(1));
            Assert.That(intent.ActionId.ToString(), Is.Not.Empty);
            Assert.That(intent.ShouldExecuteInEcs, Is.Zero);
            Assert.That(intent.ExecutionOwner,
                Is.EqualTo(HybridAiExecutionOwner.GameObject));
            Assert.That(intent.HandoffSignal,
                Is.EqualTo(HybridAiHandoffSignal.ActivateGameObject));
            Assert.That(manager.IsComponentEnabled<HybridEcsHandoffPending>(
                handle.Entity), Is.True);
            Assert.That(registry.AcknowledgeHandoff(handle), Is.True);
            Assert.That(manager.IsComponentEnabled<HybridEcsHandoffPending>(
                handle.Entity), Is.False);
            Assert.That(utilitySystem.LastDecisionCount, Is.EqualTo(1));
            Assert.That(utilitySystem.LastNearHandoffCount, Is.EqualTo(1));
        }

        [Test]
        public void CreationOrderDoesNotChangeNeighborhoodOrIntentDigest()
        {
            string forward = RunOrderedScenario(false);
            string reversed = RunOrderedScenario(true);

            Assert.That(reversed, Is.EqualTo(forward));
        }

        [Test]
        public void PoolReuseAndSceneResetLeaveNoDuplicateOrGhostEntity()
        {
            HybridEcsAgentHandle first = Register(
                "enemy.reused",
                1,
                new float3(20f, 0f, 0f),
                HybridEcsRoleFlags.Raider);
            HybridEcsAgentHandle second = Register(
                "enemy.reused",
                2,
                new float3(21f, 0f, 0f),
                HybridEcsRoleFlags.Raider);

            Assert.That(manager.Exists(first.Entity), Is.False);
            Assert.That(manager.Exists(second.Entity), Is.True);
            Assert.That(registry.RegisteredCount, Is.EqualTo(1));
            Assert.That(registry.CountGhostEntities(), Is.Zero);
            Assert.That(registry.Despawn("enemy.reused", 1), Is.False);
            Assert.That(registry.Despawn("enemy.reused", 2), Is.True);
            Assert.That(registry.RegisteredCount, Is.Zero);

            Entity externalGhost = manager.CreateEntity(
                typeof(HybridEcsAgent));
            manager.SetComponentData(externalGhost, new HybridEcsAgent
            {
                StableId = new FixedString64Bytes("enemy.ghost"),
                Generation = 1,
                IsAlive = 1
            });
            Assert.That(registry.CountGhostEntities(), Is.EqualTo(1));
            Assert.That(registry.DestroyAll(), Is.EqualTo(1));
            Assert.That(manager.Exists(externalGhost), Is.False);
            Assert.That(registry.CountGhostEntities(), Is.Zero);
        }

        [Test]
        public void BenchmarkAdapterRegistersAndProducesStableSharedDigest()
        {
            Issue64EcsBenchmarkAdapter.RegisterFactory();
            Assert.That(Issue64BenchmarkAdapterRegistry.TryCreate(
                HybridAiBenchmarkMode.Ecs,
                out IIssue64BenchmarkAdapter first), Is.True);

            string firstDigest;
            using (first)
            {
                first.Configure(100, 640064);
                first.Step(1f / 60f);
                Issue64AdapterMetricsSnapshot metrics = first.CaptureMetrics();
                Assert.That(metrics.ActiveCount, Is.EqualTo(100));
                Assert.That(metrics.GhostCount, Is.Zero);
                Assert.That(metrics.IntentDigest, Has.Length.EqualTo(64));
                firstDigest = metrics.IntentDigest;
            }

            using var second = new Issue64EcsBenchmarkAdapter();
            second.Configure(100, 640064);
            second.Step(1f / 60f);
            Issue64AdapterMetricsSnapshot repeated = second.CaptureMetrics();
            Assert.That(repeated.GhostCount, Is.Zero);
            Assert.That(repeated.IntentDigest, Is.EqualTo(firstDigest));
        }

        [Test]
        public void GameObjectAndEcsConcreteAdaptersMatchForConsecutiveTicks()
        {
            using var gameObjectAdapter =
                new Issue64GameObjectBenchmarkAdapter();
            using var ecsAdapter = new Issue64EcsBenchmarkAdapter();
            gameObjectAdapter.Configure(12, 640012);
            ecsAdapter.Configure(12, 640012);

            for (int tick = 0; tick < 3; tick++)
            {
                gameObjectAdapter.Step(1f / 60f);
                ecsAdapter.Step(1f / 60f);
                Assert.That(
                    ecsAdapter.CaptureMetrics().IntentDigest,
                    Is.EqualTo(gameObjectAdapter.CaptureMetrics().IntentDigest),
                    $"Intent digest diverged at tick {tick}.");
            }
        }

        private HybridEcsAgentHandle Register(
            string stableId,
            int generation,
            float3 position,
            HybridEcsRoleFlags role,
            bool alive = true)
        {
            return registry.Register(
                new HybridEcsAgent
                {
                    StableId = new FixedString64Bytes(stableId),
                    Generation = generation,
                    Position = position,
                    HealthRatio = alive ? 1f : 0f,
                    RoleFlags = (byte)role,
                    IsAlive = alive ? (byte)1 : (byte)0
                },
                new HybridEcsPerceptionInput
                {
                    TargetPosition = float3.zero,
                    HasLineOfSight = 1,
                    TargetInCover = 0
                },
                new HybridEcsDecisionConfig
                {
                    FriendlyScanRadius = 14f,
                    NearMaximumDistance = 12f,
                    MidMaximumDistance = 28f,
                    HandoffHysteresis = 2f,
                    DecisionSeed = 640057L
                },
                profile);
        }

        private void UpdateSystems()
        {
            world.SetTime(new TimeData(1d, 1f / 60f));
            perceptionSystem.Update();
            utilitySystem.Update();
        }

        private string RunOrderedScenario(bool reverse)
        {
            string[] ids =
            {
                "enemy.alpha",
                "enemy.bravo",
                "enemy.charlie",
                "enemy.delta"
            };
            IEnumerable<string> orderedIds = reverse ? ids.Reverse() : ids;

            registry.DestroyAll();
            foreach (string id in orderedIds)
            {
                int canonical = Array.IndexOf(ids, id);
                Register(
                    id,
                    1,
                    new float3(18f + canonical * 2f, 0f, canonical),
                    (HybridEcsRoleFlags)(1 << (canonical % 3)));
            }

            UpdateSystems();
            using EntityQuery query = manager.CreateEntityQuery(
                ComponentType.ReadOnly<HybridEcsAgent>(),
                ComponentType.ReadOnly<HybridEcsNeighborhoodFacts>(),
                ComponentType.ReadOnly<HybridEcsActionIntent>());
            using NativeArray<Entity> entities = query.ToEntityArray(
                Allocator.Temp);
            var rows = new List<string>(entities.Length);

            for (int index = 0; index < entities.Length; index++)
            {
                Entity entity = entities[index];
                HybridEcsAgent agent = manager.GetComponentData<
                    HybridEcsAgent>(entity);
                HybridEcsNeighborhoodFacts neighborhood =
                    manager.GetComponentData<HybridEcsNeighborhoodFacts>(
                        entity);
                HybridEcsActionIntent intent = manager.GetComponentData<
                    HybridEcsActionIntent>(entity);
                rows.Add(string.Join(
                    "|",
                    agent.StableId.ToString(),
                    neighborhood.FriendlyRaiderCount,
                    neighborhood.FriendlySuppressorCount,
                    neighborhood.FriendlySupportCount,
                    intent.ActionId.ToString(),
                    intent.Kind,
                    intent.RangeBand,
                    intent.ExecutionOwner));
            }

            rows.Sort(StringComparer.Ordinal);
            return string.Join("\n", rows);
        }
    }
}
