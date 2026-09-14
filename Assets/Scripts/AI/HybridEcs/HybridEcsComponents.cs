using FPS.AI.Hybrid.Shared;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FPS.AI.HybridEcs
{
    /// <summary>
    /// ECS 中唯一标识一个远中距离敌人的权威数据。StableId 与 Generation
    /// 组成生命周期键，避免对象池复用后旧实体继续参与决策。
    /// </summary>
    public struct HybridEcsAgent : IComponentData
    {
        public FixedString64Bytes StableId;
        public int Generation;
        public float3 Position;
        public float HealthRatio;
        public byte RoleFlags;
        public byte IsAlive;
    }

    /// <summary>
    /// GameObject adapter 每次同步给 ECS 的只读感知输入。ECS 不执行射线、
    /// NavMesh 或物理查询，LOS/掩体等事实由现有表现层负责采样。
    /// </summary>
    public struct HybridEcsPerceptionInput : IComponentData
    {
        public float3 TargetPosition;
        public byte HasLineOfSight;
        public byte TargetInCover;
        public float ActionCooldownRemaining;
        public float ActionCommitmentRemaining;
    }

    /// <summary>
    /// 确定性邻域批处理的结果，是共享 Utility 规则所消费的世界事实子集。
    /// </summary>
    public struct HybridEcsNeighborhoodFacts : IComponentData
    {
        public int FriendlyRaiderCount;
        public int FriendlySuppressorCount;
        public int FriendlySupportCount;
        public byte HasSupportCoverage;
    }

    public struct HybridEcsPerceptionFacts : IComponentData
    {
        public float TargetDistance;
        public HybridAiRangeBand RangeBand;
    }

    /// <summary>
    /// ECS 只输出移动意图与交接信号，不直接承接 NavMesh、Animator、碰撞或表现。
    /// </summary>
    public struct HybridEcsActionIntent : IComponentData
    {
        public FixedString64Bytes ActionId;
        public FixedString64Bytes DecisionReason;
        public EnemyUtilityActionKind Kind;
        public float3 AnchorPosition;
        public float MovementSpeedMultiplier;
        public HybridAiRangeBand RangeBand;
        public HybridAiExecutionOwner ExecutionOwner;
        public HybridAiHandoffSignal HandoffSignal;
        public byte Changed;
        public byte IsValid;
        public byte ShouldExecuteInEcs;
    }

    public struct HybridEcsDecisionConfig : IComponentData
    {
        public float FriendlyScanRadius;
        public float NearMaximumDistance;
        public float MidMaximumDistance;
        public float HandoffHysteresis;
        public long DecisionSeed;
    }

    /// <summary>
    /// 只在远中距离启用。近距离移除该标签后，实体不会再参与 ECS 批处理。
    /// </summary>
    public struct HybridEcsBatchOwned : IComponentData, IEnableableComponent
    {
    }

    public struct HybridEcsHandoffPending : IComponentData, IEnableableComponent
    {
    }

    [System.Flags]
    public enum HybridEcsRoleFlags : byte
    {
        None = 0,
        Raider = 1 << 0,
        Suppressor = 1 << 1,
        Support = 1 << 2
    }
}
