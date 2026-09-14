# Issue 64：Hybrid ECS 群体 AI 对照实验

## 目标

在同一硬件、同一画质与同一输入事实下，对比传统 GameObject（GO）和
Hybrid ECS 两条群体 AI 适配器路径。实验只在语义一致与生命周期
正确的前提下讨论性能，不允许用减少敌人、降低决策质量或跳过回收流程换取结果。

当前自动化基准测量的是纯 AI adapter `Step`，明确排除渲染、动画、物理和
Editor repaint；它回答“这段 ECS 纵向切片是否值得继续迁移”，不冒充完整游戏
帧率或 CityNew 场景压力测试。

## 固定实验矩阵

每轮必须完整包含以下六个 case：

| 敌人数 | GO | ECS |
| ---: | :---: | :---: |
| 100 | 必测 | 必测 |
| 300 | 必测 | 必测 |
| 500 | 必测 | 必测 |

每个 case 使用相同 Seed、相同固定步长、相同预热帧数和采样帧数。每档至少运行
3 轮，GO/ECS 交错执行，避免温升和后台任务只影响单一实现。报告按轮次中位数
判定，原始报告不可删除。

报告必须记录：

- Unity 与 Entities 版本；
- 操作系统、CPU、GPU、系统内存和显存；
- 画质等级、分辨率、窗口模式、VSync 与目标帧率；
- Development/Release、场景、内容版本；
- Seed、固定步长、预热帧、采样帧和每档轮数。

以上字段共同形成 `comparisonKey`。任意报告的 key 不一致，结论直接为 STOP，
不得把不同机器或不同画质的数据混算。

## 采集指标

每帧采集并输出：

- 帧时间：平均值、P95、P99；
- 主线程耗时：平均值、P95、P99；
- GC：平均每帧分配、单帧峰值；
- 内存：平均已分配内存、峰值已分配内存；
- AI 决策延迟：平均值、P95、P99；
- 当前活跃敌人数、池回收后幽灵对象/实体数；
- 按稳定敌人 ID 排序生成的命令意图摘要。

GO 与 ECS 的意图摘要必须完全一致。摘要建议每帧按 `AgentStableId` 排序，依次
纳入 `AgentStableId|Generation|ActionId|ActionKind|RangeBand|ExecutionOwner|HandoffSignal`，
再计算稳定 SHA-256。呈现文本和墙钟计时不得进入摘要。

## Continue / Stop 硬门禁

只有全部条件同时满足才输出 **CONTINUE**：

- 100、300、500 三档数据齐全，GO/ECS Seed、轮次与采样数成对；
- 三档意图摘要均一致，活跃数量达到配置值，幽灵数为 0；
- 100 敌人时，ECS 平均帧时间与主线程耗时相对 GO 的回退均不超过 5%；
- 300 和 500 敌人时，ECS 平均帧时间与主线程耗时均至少降低 15%；
- 300 和 500 敌人时，ECS 决策 P95 延迟至少降低 25%；
- 任意档 ECS P99 帧时间相对 GO 的回退不超过 5%；
- 任意档 ECS 平均 GC/帧不高于 GO；
- 任意档 ECS 峰值内存相对 GO 增幅不超过 20%。

任一条件未满足即输出 **STOP**，保留 GO 为默认路径；失败原因必须带敌人档位
与具体指标，不能只给出“性能不足”。

## 运行与门禁

关闭已打开的 Unity Editor 后，可用独立 batch 进程完成整套采集与门禁：

```bash
./scripts/performance/issue64_run_ab.sh
```

可通过 `ISSUE64_ROUNDS`、`ISSUE64_WARMUP_FRAMES`、
`ISSUE64_SAMPLE_FRAMES`、`ISSUE64_SEED`、`ISSUE64_QUALITY`、
`ISSUE64_WIDTH`、`ISSUE64_HEIGHT` 和 `ISSUE64_CONTENT_VERSION` 固定条件。
外层 runner 会等待 GO/ECS 工厂注册，逐轮交错执行两种模式；缺少任一适配器时
立即失败，不生成看似完整的结果。

本实验的 `averageFrameMilliseconds` 是纯 AI 适配器 `Step` 的单调时钟耗时，
明确排除渲染、动画、物理与 Editor repaint；适配器快照另外报告主线程、GC、
内存和决策延迟。它回答的是“同一决策工作负载是否值得迁移 ECS”，不能替代
完整游戏 Player 的端到端 FPS 验收。

每轮会得到一份
`issue64-hybrid-ai-performance-v1` JSON。将同一环境的全部轮次交给门禁：

```bash
./scripts/performance/issue64_gate.sh \
  artifacts/performance/issue64/raw/round-1.json \
  artifacts/performance/issue64/raw/round-2.json \
  artifacts/performance/issue64/raw/round-3.json
```

输出：

- `artifacts/performance/issue64/gate.json`：机器可读结论、阈值和中位数；
- `artifacts/performance/issue64/summary.md`：面试演示和人工审核用表格。

报告/门禁自身的 fixture 测试：

```bash
python3 scripts/performance/issue64_gate_test.py
```

## 当前结论

2026-09-13 在 Apple M4、Unity 6000.5.3f1、Entities 6.5.0、PC 画质下完成
3 轮交错顺序实测。每个 case 预热 30 帧、采样 180 帧；GO/ECS 全部意图摘要
一致，活跃数量正确，幽灵数为 0。

| 敌人 | GO 平均 Step | ECS 平均 Step | ECS 变化 | GO 主线程 | ECS 主线程 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 2.05 ms | 2.34 ms | +14.1% | 2.04 ms | 2.33 ms |
| 300 | 11.51 ms | 7.93 ms | -31.1% | 11.48 ms | 7.90 ms |
| 500 | 28.69 ms | 16.14 ms | -43.7% | 28.66 ms | 16.11 ms |

量化结论为 **STOP**：100 敌人时固定 ECS 开销导致平均 Step 和主线程回退超过
5%，且 300/500 敌人的单体决策 P95 降幅没有达到 25% 硬门禁。当前默认玩法
继续使用 GO adapter；保留可替换 ECS 纵向切片、生命周期防重与报告工具，后续
只有在降低低密度开销或采用“高密度才启用”的分段策略后，才重新评估扩大迁移。
