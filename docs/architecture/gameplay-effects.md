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

## 即时消耗品效果

Issue 32 在同一套定义上增加 `Instant` 生命周期。医疗包和护甲包分别以 `CurrentHealth / Add`、`CurrentArmor / Add` 描述恢复量；即时效果只执行一次，不进入持久实例列表。

执行分为两个阶段：

1. `PreviewInstant` 读取目标属性并计算钳制后的实际变化，不修改角色；满生命或满护甲会返回 `NoChange`。
2. `ExecuteInstant` 先计算全部属性，再按属性 ID 稳定提交；目标拒绝任一写入时，已提交值恢复到执行前快照。

`PlayerInventoryController` 继续使用 `InventoryState.TryConsumeAt`：先暂扣一个物品，在 Effect 返回成功后提交库存、冷却和成功反馈；Effect 失败则恢复原槽位。因此背包、快捷栏、HUD 和提示只会看到最终成功状态，不需要各自实现补偿逻辑。

## 燃烧持续效果

Issue 33 增加 `Timed` 生命周期。燃烧定义包含 Tick 伤害、Tick 间隔、持续时间、叠层上限和刷新策略。可选策略包括不刷新、刷新全部层持续时间、在叠层上限时替换最旧层。

同一目标上的同 ID 燃烧合并为一个运行时实例，但每层独立保存施加来源和剩余时间。每次 Tick 按层分别产生 `DamageInfo`，伤害类型为 `StatusEffect`，因此最后一层造成击杀时，`Health.LastAppliedDamage` 和波次奖励仍能获得正确来源。

武器命中存活敌人后施加一层燃烧。存活敌人头顶常驻实时血条，血量降低时按绿、黄、红变化；燃烧期间追加橙色火星和 `BURN ×层数` 状态行。状态到期只隐藏状态行，死亡或 `PrepareForPool` 会隐藏整组信息，`ResetForSpawn` 恢复干净满血条，避免对象池复用污染下一代敌人。

## 击杀事件触发效果

Issue 34 建立 `GameplayEffectEventContext` 与玩家级事件流。只有 `PlayerCombatEventRouter` 订阅权威的 `WaveDirector.EnemyDied`；它校验最后伤害来源属于玩家，并按波次与生成编号去重，再把与敌人、波次系统解耦的 `EnemyKilled` 上下文交给效果订阅者。

自动装填效果由一个可移除的 Persistent Effect 实例表示。实例存在时订阅事件流，每次有效击杀为当前弹匣补充3发，不消耗备弹且不超过弹匣上限；实例移除时立即退订。切枪过程中当前武器归属尚未完成，因此该次击杀明确忽略，不延迟结算；切换完成后的后续击杀只补充新装备武器。

`WeaponController.AddMagazineAmmo` 仅在实际增加子弹时发布 `AmmoChanged`，因此旧弹药界面与统一 HUD 同帧刷新；满弹时不会产生伪刷新。
