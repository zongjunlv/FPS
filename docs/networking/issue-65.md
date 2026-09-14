# Issue 65：服务器权威双人合作切片——网络诊断与验收

## 游戏内入口

- 单人模式默认不启动网络，也不会创建传输连接。
- 进入 `CityNew` 后按 `F6` 打开“双人合作切片”面板。
- 主机填写一个本机身份（同机双开时两边必须不同），点击“创建两人战局”，把面板显示的加入码交给第二个实例。
- 第二个实例按 `F6`，填写不同的本机身份和加入码，点击“加入战局”。
- 连接完成后，面板会显示玩家数、权威 Tick、目标生命、波次状态和本地预测校正幅度。当前视角正前方会生成红色权威目标；两枪击杀后波次变为 `Completed`。
- 合作模式下原单机武器伤害输入会被暂停，鼠标左键只提交网络射击命令；离开合作战局后自动恢复单机战斗。

如需绕过在线会话服务做本机多进程回归，可给两个实例分别传入：

```text
-issue65-role host -issue65-port 7777
-issue65-role client -issue65-port 7777
```

该入口适合 Multiplayer Play Mode 或本机构建自动化；只有显式传入参数时才会启动。

## 自动化覆盖

- `Issue65NetworkingDomainTests`：固定 Tick、回溯命中、反重放、非法移动/视角/射速、伤害/掉落/波次、预测与插值。
- `Issue65NetcodeVerticalSliceTests`：默认离线、真实 UTP Host 启停、网络 Prefab Spawn、Host/Client 命令同 Tick、身份伪造拦截、断线清理。
- `Issue65SessionRuntimeTests`：从可选会话入口启动完整本地 Host，验证权威战局、玩家和目标生成。
- `Issue65NetworkDiagnosticsTests`：固定网络条件矩阵、统计聚合、报告稳定性和硬门禁。

进程内测试用于快速回归；最终网络表现仍按下文的真实双进程证据边界验收。

## 证据边界

网络诊断分为两种证据，结论不得混用：

- `DeterministicFixture`：单进程、内存内的确定性指标管线 Fixture。它不创建真实 Socket，不启动独立 Host/Client 玩家进程，也不覆盖传输序列化、进程调度、渲染或物理成本。它只能验证采样、聚合、序列化和硬门禁本身，不能作为 Issue 65 的真实联机验收证据。
- `MultiProcessPlayer`：至少两个独立玩家构建进程产生的实测证据。只有这种证据且所有指标门禁通过时，报告才允许输出 `Pass` 和 `acceptanceEligible=true`。

因此，编辑器菜单和默认命令行入口输出的 `FixtureOnly` 是预期结果，不代表网络实现验收通过。

## 固定场景矩阵

每份正式报告必须精确包含以下四个场景，不能缺项、改名或用平均值代替单场景结果：

| 稳定 ID | 往返延迟 | 抖动 | 受控丢包 | 用途 |
|---|---:|---:|---:|---|
| `rtt-000-loss-00` | 0ms | 0ms | 0% | 本地理想网络基准 |
| `rtt-080-loss-00` | 80ms | ±8ms | 0% | 常见跨区域网络 |
| `rtt-150-loss-00` | 150ms | ±15ms | 0% | 高延迟网络 |
| `rtt-080-loss-05` | 80ms | ±8ms | 5% | 受控丢包恢复能力 |

丢包使用万分比记录，5% 等于 `500 basis points`。场景种子固定，便于复现，不使用运行时随机数掩盖回归。

## 采样定义

- 命中反馈 RTT：客户端发出射击命令至收到服务器权威命中/拒绝反馈的毫秒数；报告样本数、平均值、P95、P99。
- 预测校正：客户端预测状态与权威快照不一致并实际应用校正的事件；报告次数、每分钟次数、平均/P95/最大位移幅度。
- 流量：按客户端视角分别累计上行和下行有效载荷字节，并按场景实测时长换算 B/s。
- 状态分歧：同一权威 Tick 的客户端预测快照与服务器状态比较；报告比较次数、分歧次数/比例、最大位置幅度、最长持续时间。
- 命令账目：每个已发送命令必须且只能归入接受、网络条件丢弃或服务器拒绝之一。
- 拒绝原因：正式验收至少覆盖 `TooOld`、`Duplicate`、`IllegalState`；任何 `Unknown` 原因都会使门禁失败。Netcode/Domain 适配层负责把更细的服务器拒绝枚举稳定映射到这些报告类别。

`INetworkDiagnosticsSink` 是采样端口。传输、服务器模拟和客户端预测适配器应在事件真实发生的位置上报，不得用报告层反推或补造指标。

## 硬门禁

所有阈值逐场景执行：

| 指标 | 硬门禁 |
|---|---:|
| 命中反馈样本数 | ≥ 20 |
| 命中反馈 P95 | ≤ 场景 RTT + 100ms |
| 校正频率 | ≤ 60 次/分钟 |
| 校正 P95 幅度 | ≤ 0.75m |
| 单次最大校正 | ≤ 2m |
| 状态分歧比例 | ≤ 2% |
| 最大状态分歧幅度 | ≤ 1m |
| 最长状态分歧持续 | ≤ 500ms |
| 客户端上行 | ≤ 65,536 B/s |
| 客户端下行 | ≤ 131,072 B/s |

此外，场景矩阵不完整、命令账目不守恒、缺少三类拒绝覆盖、出现未知拒绝原因，都会直接输出 `Fail`。

## 报告格式

报告 Schema 为 `issue65-network-diagnostics-v1`，同时输出稳定排序的 JSON 和 Markdown。JSON 的场景按稳定 ID 排序，拒绝原因按枚举值排序，所有数值使用 invariant culture，适合 CI 比较。

报告必须包含：

- `evidenceKind`、`processCount`、`isRealMultiProcess`；
- `measurementScope` 和 `limitations`；
- `metricThresholdsPassed` 和 `acceptanceEligible`；
- 四个网络场景的完整指标与拒绝原因；
- 排序稳定的门禁失败原因。

## 编辑器与 CI 入口

编辑器验证指标管线：

1. 打开 `Tools/FPS/Networking/Issue 65/运行诊断 Fixture`。
2. 查看 `artifacts/networking/issue65/fixture-report.json` 和 `fixture-report.md`。
3. 预期门禁结论为 `FixtureOnly`，且 `acceptanceEligible=false`。

命令行验证：

```bash
scripts/networking/issue65_diagnostics.sh
```

默认脚本允许 Fixture 完成，以验证报告基础设施。正式 CI 必须设置：

```bash
ISSUE65_REQUIRE_MULTIPROCESS=1 scripts/networking/issue65_diagnostics.sh
```

此模式会明确拒绝 Fixture 报告。真实多进程 Runner 应把 Host 与 Client 的采样汇总为同一 Schema，再执行：

```bash
python3 scripts/networking/issue65_report_gate.py path/to/report.json --require-multiprocess
```

只有真实多进程报告满足全部硬门禁时，命令才返回成功。
