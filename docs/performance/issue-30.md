# Issue 30：100敌人可复现压力场景与优化前基线

## 实验目的

本基线只记录100敌人同时运行时的真实性能事实，不包含 AI LOD、空间索引、Job/Burst 等后续优化。后续性能 Issue 必须保持相同实验条件，并与本页原始 JSON 比较。

## 固定实验条件

| 条件 | 值 |
| --- | --- |
| 采集日期 | 2026-08-23 |
| 设备 | MacBook Air（Apple M4，10核 CPU、8核 GPU、16 GB 内存） |
| 系统 | macOS 26.5.2 |
| Unity | 6000.5.3f1 |
| 构建 | macOS Development Player |
| 场景 | CityNew，真实 NavMesh、敌人动画、感知、追击和攻击 |
| 敌人数 | 100 |
| 分辨率 | 1920×1080 Windowed（配置值和实际值均校验） |
| 画质 | PC |
| 随机种子 | 30030 |
| 行为档案 | `alert-chase-fire-v1` |
| 感知预算 | 每帧4次昂贵视野检查 |
| 单轮时长 | 5秒预热＋20秒采样 |
| 轮数 | 连续3轮，最终取中位数 |

基准开始前停止正常波次，将敌人池一次性预热至100。正式采样期间如果 `stableSampleInstantiateCount` 不为0，汇总工具会直接拒绝该轮数据。

## 三轮原始结果

| 轮次 | 平均 FPS | 1% Low | 平均帧时间 | P95 | P99 | 主线程平均 | 平均 GC/帧 | 最大感知延迟 | 采样期创建 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 52.55 | 4.49 | 19.31 ms | 98.51 ms | 184.91 ms | 19.02 ms | 41,239 B | 482.77 ms | 0 |
| 2 | 29.25 | 3.73 | 34.71 ms | 138.29 ms | 234.86 ms | 34.18 ms | 41,515 B | 867.70 ms | 0 |
| 3 | 25.60 | 3.37 | 39.74 ms | 169.59 ms | 259.14 ms | 39.06 ms | 41,499 B | 993.54 ms | 0 |
| **中位数** | **29.25** | **3.73** | **34.71 ms** | **138.29 ms** | **234.86 ms** | **34.18 ms** | **41,499 B** | **867.70 ms** | **0** |

三轮均生成100个池对象，复用100次，采样期没有扩容或创建。平均 FPS 和长帧存在明显轮次波动，因此后续优化不能只比较单轮平均值，必须继续执行至少3轮并比较中位数。当前1% Low 与 P95/P99 表明100敌人下存在严重长帧，这是后续优化的首要痛点。

原始数据：

- [`fps-issue30-baseline-round-1.json`](raw/fps-issue30-baseline-round-1.json)
- [`fps-issue30-baseline-round-2.json`](raw/fps-issue30-baseline-round-2.json)
- [`fps-issue30-baseline-round-3.json`](raw/fps-issue30-baseline-round-3.json)
- [`fps-issue30-baseline-summary.json`](raw/fps-issue30-baseline-summary.json)

## 执行方式

先关闭 Unity Editor，并生成 Development Build：

```bash
/Users/jungle/UnityEditors/6000.5.3f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "/Users/jungle/GameProject/Unity/FPS/My project" \
  -executeMethod Issue1Build.BuildMacDevelopment \
  -logFile /tmp/fps-issue30-build.log
```

然后在项目根目录执行：

```bash
./scripts/performance/run-issue30-baseline.sh
```

默认输出到 `artifacts/performance/issue30/`：

- `raw/round-*.json`：每轮完整原始数据；
- `logs/round-*.log`：Player 日志；
- `summary.json`：机器可读中位数；
- `summary.md`：人工可读汇总表。

可使用 `FPS_BENCHMARK_PLAYER` 指定 Player 路径；也可通过 `ISSUE30_ROUNDS`、`ISSUE30_WARMUP_SECONDS` 和 `ISSUE30_SAMPLE_SECONDS` 做短链路验证，但这类结果不能替代本页正式基线。
