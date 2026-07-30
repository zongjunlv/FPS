# Issue 13 性能量化报告

## 结论

在同一台设备、同一场景和相同随机种子下，以 48 个敌人进行无图形持续交火 CPU/GC 压力测试。为降低 MacBook Air 无风扇设备温升造成的顺序偏差，正式测试按 `基线 → 优化 → 优化 → 基线 → 基线 → 优化` 交错执行 3 轮，并采用中位数作为结论。

| 指标 | 基线中位数 | 优化后中位数 | 变化 |
| --- | ---: | ---: | ---: |
| 平均 FPS | 165.93 | 204.59 | +23.3% |
| 1% Low FPS | 37.95 | 47.45 | +25.0% |
| P95 帧时间 | 16.10 ms | 13.79 ms | -14.4% |
| 主线程平均耗时 | 6.02 ms | 4.88 ms | -18.9% |
| 每帧 GC 分配 | 28,825.81 B | 22,263.24 B | -22.8% |
| Unity 峰值内存 | 166.80 MB | 166.43 MB | -0.2% |

该表来自 `-nographics`，用于隔离 CPU、AI 和 GC，不能代表全屏真实图形帧率。Player 仍存在来自引擎、AI、动画及其他系统的每帧分配；本次可以确认的是已纳入对象池的战斗特效稳定路径不再依赖反复 `Instantiate/Destroy`，而不是“整个游戏零 GC”。

## 全屏图形诊断

用户在 4096×2304 外接屏全屏运行时反馈明显不流畅。使用真实 Metal 图形输出、3 个敌人、相同场景和脚本，仅改变分辨率进行最小化复现：

| 分辨率 | 平均 FPS | 1% Low FPS | P95 帧时间 | 主线程平均耗时 |
| --- | ---: | ---: | ---: | ---: |
| 4096×2304 全屏 | 59.53 | 54.54 | 17.87 ms | 16.74 ms |
| 1920×1080 全屏 | 220.56 | 165.96 | 5.60 ms | 4.53 ms |

1080p 平均帧率约为原生 4K 的 3.7 倍，证明该设备上的全屏卡顿主要受 4K Retina 像素吞吐限制。该对照是单轮根因诊断，不用于替代三轮正式统计。

修复措施：

- 独立 Player 全屏时保持宽高比，并将默认渲染分辨率限制在约 1920×1080 像素量。
- 低于预算的分辨率保持不变。
- `-native-resolution` 可关闭限制；`-fullscreen-pixel-budget <像素数>` 可自定义预算。
- Player 默认关闭敌人 `OnGUI` 调试浮层，避免每个敌人每帧创建调试字符串。
- 修复失效小队警报来源导致的 `OnGUI` 每帧 `NullReferenceException`。

诊断原始数据：

- [`fps-issue13-graphics-4k.json`](raw/fps-issue13-graphics-4k.json)
- [`fps-issue13-graphics-1080p.json`](raw/fps-issue13-graphics-1080p.json)

## 测试环境与方法

- 设备：Apple M4，10 核 CPU，16 GB 内存，集成 Apple M4 GPU
- 系统：macOS 26.5.2
- Unity：6000.5.3f1
- 构建：macOS Development Build，画质 `PC`
- 分辨率：逻辑设置为 1280 × 720，关闭垂直同步及目标帧率限制
- 图形模式：`-batchmode -nographics`，只用于隔离 CPU/GC
- 场景：CityNew
- 敌人数量：48
- 每轮预热：5 秒
- 每轮采样：15 秒
- 随机种子：13013
- 压力行为：自动连续开火、自动换弹；敌人视野设为 360° 并保持高生命值
- 采集：`ProfilerRecorder` 记录 Main Thread 和 `GC Allocated In Frame`；同时记录 Unity 内存、帧时间、开枪/命中次数、感知调度次数
- 基线：提交 `b232e72` 的游戏代码，加同一份只负责驱动和采集的 benchmark harness
- 优化后：Issue 13 实现代码

## 每轮原始结果

| 变体 | 平均 FPS | 1% Low | P95 帧时间 | 主线程平均 | 每帧 GC | 峰值内存 | 开枪/命中 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline-48-1 | 290.98 | 62.06 | 4.51 ms | 3.43 ms | 25,635.85 B | 165.98 MB | 149 / 149 |
| optimized-48-1 | 248.53 | 60.25 | 10.95 ms | 4.02 ms | 22,238.46 B | 166.46 MB | 150 / 150 |
| optimized-48-2 | 204.59 | 47.45 | 13.79 ms | 4.88 ms | 22,263.24 B | 166.43 MB | 149 / 149 |
| baseline-48-2 | 165.93 | 37.24 | 16.10 ms | 6.02 ms | 28,825.81 B | 166.80 MB | 145 / 145 |
| baseline-48-3 | 159.41 | 37.95 | 17.20 ms | 6.26 ms | 29,644.75 B | 166.93 MB | 142 / 142 |
| optimized-48-3 | 154.32 | 40.30 | 17.23 ms | 6.46 ms | 22,315.97 B | 166.43 MB | 147 / 147 |

原始数据：

- [`fps-issue13-baseline-48-1.json`](raw/fps-issue13-baseline-48-1.json)
- [`fps-issue13-baseline-48-2.json`](raw/fps-issue13-baseline-48-2.json)
- [`fps-issue13-baseline-48-3.json`](raw/fps-issue13-baseline-48-3.json)
- [`fps-issue13-optimized-48-1.json`](raw/fps-issue13-optimized-48-1.json)
- [`fps-issue13-optimized-48-2.json`](raw/fps-issue13-optimized-48-2.json)
- [`fps-issue13-optimized-48-3.json`](raw/fps-issue13-optimized-48-3.json)

正式统计采用每个变体三轮的逐指标中位数，不使用最佳单轮数据。

## 实现说明

### 战斗特效对象池

- 弹道继续使用已有的固定容量 `ShotTracerPool`。
- 枪口火焰复用武器上的持久 ParticleSystem 和 Light，不在射击时创建对象。
- Concrete 弹孔/受击效果容量 48。
- Metal 弹孔/火花组合容量 48，额外粒子容量 24。
- 高频临时音源容量 32。
- 对象在启动时预热；容量耗尽时安全降级为跳过该次次要表现，不突破硬上限。
- 回收时校验对象归属并拒绝重复回收，避免池状态损坏。
- PlayMode 分配探针连续执行 128 次“借出—播放—回收”，稳定路径托管分配不超过 256 B。

### AI 感知与缓存

- 新增全局感知调度器，每帧最多处理 4 个敌人的视觉检测，采用轮询保证敌人最终都能获得检查机会。
- 视觉检测使用平方距离和点积进行前置过滤。
- 遮挡检测复用固定 `RaycastHit[32]`，不在每次检测时创建集合。
- 路径探测复用 `NavMeshPath`。
- 小队搜索点广播复用列表，避免每次广播创建新集合。

### 可重复压测入口

Development Build 可使用：

```bash
"/path/to/My project.app/Contents/MacOS/My project" \
  -fps-benchmark \
  -benchmark-enemies 48 \
  -benchmark-warmup 5 \
  -benchmark-duration 15 \
  -benchmark-seed 13013 \
  -benchmark-variant local \
  -benchmark-output /tmp/fps-issue13-local.json \
  -batchmode -nographics
```

benchmark 会固定分辨率、关闭 VSync、生成指定数量敌人、自动持续交火，并在结束后写出 JSON。

真实图形输出不要添加 `-nographics`，并可使用
`-benchmark-width`、`-benchmark-height` 和
`-benchmark-fullscreen` 指定显示条件。

## 限制与废弃数据

- Development Build 与正式 Release Build 的绝对性能不同。
- 48 敌人三轮 A/B 数据使用 `-nographics`，不能作为真实全屏 GPU 帧率。
- 测试期间未连接外部 Profiler，以免改变结果；数据来自 Player 内部 `ProfilerRecorder`。
- 无风扇设备存在明显温升降频，因此各轮绝对 FPS 波动较大。本报告通过交错顺序和中位数降低偏差，但不能完全消除。
- 早期 24 敌人的“连续跑完全部基线，再跑全部优化”数据存在严重顺序温升偏差，已判定无效，不用于结论。
- 对象池分配探针只覆盖池化特效路径；Player 总 GC 指标还包含其他游戏与引擎系统。

## 简历可引用结论

可以基于本报告写为：

> 为 FPS 战斗表现建立固定容量、预热与异常回收保护的对象池，并将敌人视觉感知改为预算化分帧调度；在 Apple M4、48 敌人无图形持续交火 Development Build 压测中，三轮交错 A/B 测试中位数显示 CPU 模拟吞吐提升 23.3%，主线程平均耗时下降 18.9%，每帧 GC 分配下降 22.8%；针对 4K Retina 全屏瓶颈增加等比例 1080p 像素预算。

不要写“零 GC”“所有设备提升 23.3%”或未由原始 JSON 支持的结论。
