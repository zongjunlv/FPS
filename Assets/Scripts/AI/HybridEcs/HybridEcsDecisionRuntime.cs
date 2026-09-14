using FPS.AI.Hybrid.Shared;
using Unity.Entities;

namespace FPS.AI.HybridEcs
{
    /// <summary>
    /// 每个实体独享的 Utility 冷却、承诺和交接状态。规则计算委托给两条路径
    /// 共用的 HybridAiSharedDecisionAdapter，不在 ECS 中复制第二套评分逻辑。
    /// </summary>
    public sealed class HybridEcsDecisionRuntime : IComponentData
    {
        public HybridEcsDecisionRuntime()
        {
            Reset(null);
        }

        public HybridEcsDecisionRuntime(EnemyUtilityProfileDefinition profile)
        {
            Reset(profile);
        }

        public HybridAiSharedDecisionAdapter Decision { get; } = new();
        public HybridAiHandoffStateMachine Handoff { get; } = new();
        public EnemyUtilityProfileDefinition Profile { get; private set; }

        public void Reset(EnemyUtilityProfileDefinition profile)
        {
            Profile = profile;
            Decision.Reset(profile);
            Handoff.Reset(0f, HybridAiExecutionPolicy.GameObjectOnly);
        }
    }
}
