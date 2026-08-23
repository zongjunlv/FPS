# Gameplay Effect 最小闭环

Issue 31 以“生命强化”卡牌作为第一条 tracer bullet，把升级卡牌的选择入口与属性计算解耦。现有卡牌 ID、候选生成和 UI 不变；选中后由 `GameplayEffectDefinition`、`GameplayEffectContext`、`GameplayEffectInstance` 与 `GameplayEffectRuntime` 完成应用和追踪。

## 聚合规则

同一属性固定按照以下顺序聚合：

```text
(Base + Add总和) × (1 + Multiply总和)
```

如果存在 `Override`，最终值由 Override 直接替代。多个 Override 先比较 `Priority`，再按 Effect `StableId`，最后按实例 ID 决胜，因此相同输入和应用顺序始终得到相同结果。

生命强化每层提供 `MaximumHealth / Multiply / 0.2`，100点基础生命选择两层后得到140点，与旧卡牌数值保持一致。

## 实例追踪

每次应用都会创建独立实例并记录：

- 全局唯一实例 ID；
- Effect 数据定义；
- 来源稳定 ID；
- 来源对象；
- 目标对象。

实例可以按 ID 单独移除，也可以在局内结算时统一清空。

## 回滚规则

最大生命应用和移除都通过 `Health.SetMaximumHealth` 完成。上限变化时同步增加或扣除相同的当前生命差值，从而始终保留玩家已经损失的生命量。

示例：基础 `100/100`，受到20点伤害后为 `80/100`；选择两层生命强化变为 `120/140`；移除一层变为 `100/120`；结算清空后回到 `80/100`。

场景重载会销毁运行时容器，胜利、失败和主动重开都会先调用 `PlayerUpgradeController.EndRun` 清空实例，因此不会把局内 Effect 带入下一局。
