# Issue 63：离线遭遇模拟与自动平衡门禁

Issue 63 用固定 Tick 的确定性模拟批量检查 CityNew 战局规则。它的目标不是替代人工试玩，而是在内容或数值变更进入主分支前，尽早发现大面积不可通关、战斗拖沓、资源枯竭和分布漂移。

## 模型边界

离线模拟复用正式战局的 Seed、波次/遭遇选择、导演决策、伤害、资源和升级规则，并把 Unity 场景资产先投影成不可变输入，再并行运行纯规则核心。因此，同一规则版本、内容指纹与 Seed 必须得到相同结果，且结果不能随输入顺序或并行度变化。

离线模型不会运行以下表现层或空间层内容：

- NavMesh 寻路、碰撞、射线和真实命中位置；
- Animator、特效、音频、相机、HUD 与渲染性能；
- 玩家实时操作水平、掩体选择和关卡几何产生的战术差异。

所以，门禁通过代表“规则和资源供需在模型假设下健康”，不代表画面、手感、寻路或实际关卡难度已经验收。这些仍由 PlayMode、性能场景和人工试玩覆盖。

## 报告内容

默认每轮运行连续 1000 个 Seed。`report.json` 记录规则版本、策略版本、内容版本、内容指纹、运行环境，以及每个 Seed 的：

- 通关或失败结果、失败原因和总 Tick 数；
- 各敌人 TTK、角色/遭遇分布和导演事件；
- 生命与护甲损失、弹药消耗与补给；
- 升级选择、异常代码与确定性摘要。

聚合门禁在 `gate.json` 中保留完整判断结果，并生成便于阅读的 `summary.md`。固定 Tick 是规则时间单位；需要换算秒数时，应使用同一报告对应的固定 Tick 频率，不要把 Tick 当作渲染帧。

## 本地运行

先关闭正在使用该项目的 Unity Editor，然后在项目根目录运行：

```bash
./scripts/balance/run-issue63-balance-gate.sh
```

常用覆盖参数：

```bash
./scripts/balance/run-issue63-balance-gate.sh \
  --seed-start 10000 \
  --seed-count 2000 \
  --parallelism 8 \
  --output-dir artifacts/balance/issue63-local
```

Unity 不在默认目录时设置 `UNITY_EXECUTABLE`。也可用 `ISSUE63_SEED_START`、`ISSUE63_SEED_COUNT`、`ISSUE63_PARALLELISM`、`ISSUE63_OUTPUT_DIR`、`ISSUE63_THRESHOLDS` 和 `ISSUE63_BASELINE` 环境变量覆盖参数。

只想用新阈值重新判断已有 `report.json` 时运行：

```bash
./scripts/balance/run-issue63-balance-gate.sh \
  --output-dir artifacts/balance/issue63 \
  --skip-simulation
```

默认输出目录 `artifacts/balance/issue63` 包含：

| 文件 | 用途 |
| --- | --- |
| `report.json` | Unity 生成的逐 Seed 机器报告 |
| `gate.json` | Python 门禁结果与失败规则 |
| `summary.md` | 中文聚合摘要 |
| `unity.log` | Unity 批处理日志 |
| `reproductions/` | 异常 Seed 的复现包 |

## 异常与复现

异常复现包至少固定 Seed、内容版本/指纹、规则与策略版本、模拟命令流、期望摘要及运行环境。开发入口加载复现包后，应按记录的命令顺序重放同一离线规则；这是一键复现“规则异常”，不是录制玩家画面，也不承诺还原 NavMesh、物理或动画现场。

“异常”和“回放分歧”是两件事：

- 稳定重现的不可通关、资源枯竭或极端时长属于业务异常；如果再次运行摘要一致，它没有回放分歧。
- 只有相同输入重放得到不同状态或摘要时，才记录首个分歧 Tick、命令索引、期望值和实际值。

不得为稳定异常伪造“首个分歧”。复现包进入缺陷单时应与生成它的内容指纹一起保存，否则内容修改后无法证明是同一测试条件。

## 确定性与并行

每个 Seed 必须拥有独立的命名随机流和不可变场景快照，禁止共享 `UnityEngine.Random` 或跨任务写入运行状态。报告按 Seed 稳定排序，摘要只依赖 Seed 内结果。自动测试会比较顺序执行与不同并行度的确定性摘要；任何不一致都应视为实现缺陷，而不是允许的统计噪声。

以下任一变化意味着结果不可直接与旧报告比较：

- `rulesVersion` 或 `policyVersion` 改变；
- `contentVersion` 或 `contentFingerprint` 改变；
- 影响浮点或线程语义的运行环境改变。

## 阈值与基线

硬阈值由 `scripts/balance/thresholds.json` 管理，目前检查：

- 至少 1000 个唯一 Seed，且数值有限、确定性摘要完整；
- 最低通关率与最高资源枯竭率；
- 战斗时长 P99 与敌人 TTK P95；
- 不可解阵容、停滞或 Tick 上限等硬失败必须为零；
- 启用基线时，检查通关率、资源枯竭、时长、TTK 以及角色/遭遇/升级分布回归。

阈值修改必须说明对应的玩法目标，不能只为让失败构建变绿而放宽。基线不是“最近一次运行结果”：只有在同一批 Seed 上完成规则审查、人工试玩和异常复核后，才可将候选报告提升为新基线。内容版本或规则版本变化时，先保留旧基线作差异分析，再由评审明确接受新分布；禁止在同一个改动中无解释地同时修改玩法和覆盖基线。

## CI 行为

`.github/workflows/unity-quality-gate.yml` 会执行 1000 Seed 离线模拟和 Python 门禁，把 `summary.md` 写入 GitHub Job Summary，并在成功或失败时上传报告、日志和复现包。门禁返回非零状态会阻止质量检查通过；复现证据仍通过 artifact 保留。
