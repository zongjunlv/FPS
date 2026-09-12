# FPS 程序集边界

Issue 27 将运行时代码从预定义程序集迁入显式的 `FPS.*` 程序集。当前采用渐进式边界：闭合的领域模型进入对应模块，仍存在跨模块耦合的 MonoBehaviour 与场景装配暂时进入最外层 `FPS.Composition`，由后续 Issue 继续下沉。

## 允许的依赖方向

```text
FPS.Core
├── FPS.GameplayEffects
├── FPS.Combat
│   ├── FPS.AI
│   └── FPS.Inventory
├── FPS.UI
└── FPS.Composition

FPS.Simulation ──> FPS.Composition
```

- `FPS.Core` 不依赖任何项目运行时程序集。
- `FPS.Simulation` 是固定 Tick 的权威战局内核，不依赖 Unity 或其他项目运行时程序集。
- `FPS.GameplayEffects` 只依赖 `FPS.Core`。
- `FPS.Combat` 可以依赖 `FPS.Core` 与 `FPS.GameplayEffects`。
- `FPS.AI`、`FPS.Inventory` 可以依赖 Core、Combat 与 GameplayEffects，但不得相互依赖，也不得依赖 UI。
- `FPS.UI` 可以读取各领域模块的公开状态，领域模块不得反向依赖 UI。
- `FPS.Composition` 是唯一允许同时引用全部模块的装配层，不承载新的领域规则。

## 首批归属

- Core：嵌套 Gameplay Lock 的纯状态与租约模型。
- Combat：伤害协议、生命与受击区域。
- GameplayEffects：Issue 31 使用的独立程序集锚点。
- AI：警觉、攻击与小队情报的纯状态模型。
- Inventory：背包、快捷栏、物品定义与确定性掉落模型。
- UI：安全区适配和 HUD 视觉配置。
- Simulation：波次、任务、玩家战局状态、稳定命令与有序战局事件。
- Composition：现有场景 Bootstrap、跨模块控制器和尚未解耦的表现层。

`AssemblyBoundaryTests` 会验证程序集存在、引用方向、无循环依赖、源码归属，以及运行时与测试代码不再硬编码旧程序集名称。

## 玩家战斗组合根

Issue 28 在 CityNew 玩家对象上引入 `PlayerCombatCompositionRoot`，作为玩家战斗切片唯一的运行时装配入口：

- 场景显式提供输入、移动控制、后坐力、角色动画、武器背包、战斗控制和曳光弹池。
- 组合根集中创建生命、运行时属性、准星、弹药 HUD、受击反馈和统一 HUD 入口，并向消费者注入引用。
- `CombatSoundEventChannel`、`CombatFeedbackAudioProfile` 和 `PlayerHudVisualProfile` 由场景序列化引用提供，消费者不再自行 `Resources.Load`。
- `PlayerCombatController` 与 `PlayerCombatFeedbackController` 不再通过全局查找或动态补组件完成初始化；配置不完整时组合根会列出缺失依赖并停止该切片。
- 任务、交互和背包仍作为兼容扩展由组合根集中挂载，后续 Issue 可按独立切片继续迁移。
