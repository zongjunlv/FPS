# 权威战局仿真内核

Issue 58 将波次、敌人生命周期、玩家战局状态与任务阶段收口到
`FPS.Simulation`。该程序集开启 `noEngineReferences`，不能读取
`Time`、`GameObject`、`NavMesh`、输入系统或 UI。

## 运行边界

```text
Unity 输入 / Health / Enemy / Terminal
              │ 稳定命令
              ▼
      RunSimulationKernel（30 Tick/s）
              │ 有序事件 + 权威快照
      ┌───────┼────────┬─────────┐
      ▼       ▼        ▼         ▼
   场景表现   HUD     存档适配    回放/诊断
```

- Unity 帧时间只在 `WaveDirector` 中累积并换算成整数 Tick。
- 内核只接受带权威 Tick 的 `SimulationCommand`；过期或超前命令会被拒绝。
- 每个 `SimulationEvent` 都包含 Tick 和连续 Sequence，可稳定排序并序列化。
- 波次完成后，任务阶段由内核直接从“清敌”推进到“接入终端”，不由 UI 推断。
- 暂停不推进 Tick；`Step` 可以单步；`FastForward` 可在无场景环境快速验证。

## 存档兼容

当前存档继续保留原有 Wave/Mission 投影，并可选保存固定 Tick、事件序号和
玩家战局数值。Issue 58 以前的 schema-v2 存档没有该扩展块，加载时会从旧
Wave/Mission 字段重建内核，因此历史校验和与旧存档仍然有效。

## 确定性验证

`SimulationCommandCodec` 保存命令流，`SimulationEventCodec` 保存事件流。
相同 Seed、配置和命令顺序必须得到完全一致的 Tick、Sequence、事件类型与
实体编号；中途快照恢复后，事件序号从保存值继续递增。
