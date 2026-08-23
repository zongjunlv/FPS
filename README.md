# FPS

基于 Unity 6.5、URP 和 Input System 开发的单人 FPS 项目。

## 当前可运行基线

- 玩法场景：`CityNew`
- 玩家：移动、前向奔跑、跳跃、平滑下蹲、鼠标/手柄视角和瞄准状态
- 战斗：AR/手枪 Hitscan、视觉曳光、枪口焰、弹孔、材质反馈和后坐力
- 敌人：Spider 感知、协同警报、分散搜索、近战、受击与死亡反馈
- 性能：固定容量对象池、感知预算分帧和可重复 Player 压力测试

## 操作

| 操作 | 键位 |
| --- | --- |
| 移动 | WASD |
| 观察 | 鼠标移动 |
| 跳跃 | Space |
| 奔跑 | Left Shift |
| 下蹲/站立 | C |
| 射击 | 鼠标左键 |
| 瞄准状态 | 鼠标右键 |
| 暂停/继续并释放或锁定鼠标 | Esc |
| 交互（预留） | E |

手柄支持左摇杆移动、右摇杆观察、按下左摇杆奔跑、东键下蹲，以及 Start 暂停。

## 自动化质量门禁

```bash
./scripts/ci/run-unity-quality-gate.sh all
```

同一入口会依次执行 Unity 编译检查、EditMode 与 PlayMode 测试，保留 XML、JSON、Markdown 和原始日志，并区分产品代码、测试、License、资源导入及 Unity 环境失败。完整的本地与 GitHub Actions 自托管 Runner 配置见 [`docs/quality-gate.md`](docs/quality-gate.md)。

测试会验证：

- `CityNew` 已加入 Build Settings 并能够加载。
- 玩家包含角色控制、输入、战斗及 CharacterController。
- 场景包含武器和敌人。
- 场景中不存在 Missing Script。
- 已废弃的第三人称视角空壳组件已移除。
- 武器能够开火并生成高速视觉曳光，敌人受到致命伤害后会销毁。
- 下蹲会平滑调整碰撞体和相机高度，且始终保持脚底对齐。
- 低矮顶棚会阻止站起，移除障碍后可以恢复站立。
- 斜向移动不会加速，后退不能冲刺，蹲伏会覆盖冲刺速度。
- 暂停会冻结游戏时间并释放鼠标。
- 鼠标与手柄使用各自的视角灵敏度，支持反转 Y 轴。
- 对象池能够预热、拒绝超容量借出，并保护重复/外部归还。
- 24 名敌人的视野检查由固定预算轮转调度，不会集中在同一帧。

## 运行时架构

- `RuntimeGameObjectPool`：固定容量预热模块。容量耗尽时返回空值，不扩容、不抢占活跃对象；正常、重复和外部归还均有明确结果。
- `CombatEffectPool`：玩家级战斗反馈模块。统一管理 48 个混凝土弹孔/命中特效、48 个金属标记、24 个金属火花和 32 个空间音频 voice。
- `ShotTracerPool`：固定 16 条高速视觉曳光；完成后停用并复用。
- `MuzzleFlashController`：复用武器层级预置粒子和灯光，不逐发创建对象。
- `EnemyPerceptionScheduler`：场景级圆环调度模块，默认每帧最多执行 4 次昂贵视野查询；敌人的状态推进和导航仍按帧运行。
- `PerformanceDisplayBootstrap`：独立 Player 全屏时保持显示比例并限制为约 1920×1080 的像素预算，避免 4K Retina 原生渲染拖垮帧率；可用 `-native-resolution` 关闭限制。
- 武器和敌人射线使用 `RaycastNonAlloc` 与实例级复用命中缓存；协同搜索复用 `List<Vector3>` 和 `NavMeshPath`。

## Issue 13 性能压力测试

先按下文生成 Development Build，再运行：

```bash
"/Users/jungle/GameProject/Unity/FPS/My project/Builds/Issue1/FPS.app/Contents/MacOS/My project" \
  -fps-benchmark \
  -benchmark-enemies 24 \
  -benchmark-warmup 5 \
  -benchmark-duration 20 \
  -benchmark-seed 13013 \
  -benchmark-width 1920 \
  -benchmark-height 1080 \
  -benchmark-fullscreen \
  -benchmark-variant local \
  -benchmark-output /tmp/fps-issue13-local.json \
  -logFile /tmp/fps-issue13-local.log
```

压力场景会在 `CityNew` 创建固定 24 名高血量敌人，强制持续感知、追击和攻击；玩家自动连续开火、换弹且不会在采样期间死亡。报告记录设备、系统、Unity 版本、实际分辨率、全屏模式、画质、敌人数量、随机种子、CPU 帧时间、GC Alloc、内存、平均 FPS、P95/P99 和 1% Low。不要添加 `-nographics` 才能测量真实图形输出；添加该参数只适合隔离 CPU/GC。

独立 Player 默认将高分辨率全屏限制在约 1080p 像素量。测试原生 4K 可添加 `-native-resolution`；自定义预算可添加 `-fullscreen-pixel-budget <像素数>`。敌人调试浮层在 Player 中默认关闭，可用 `-enemy-debug-overlay` 临时开启。

真实性能结果与限制见 [`docs/performance/issue-13.md`](docs/performance/issue-13.md)；简历只能引用其中已有原始 JSON 支持的数据。

## Issue 30：100敌人可复现基线

生成最新 Development Build 后，在项目根目录运行：

```bash
./scripts/performance/run-issue30-baseline.sh
```

入口会固定100敌人、1920×1080、PC画质、随机种子、感知预算、5秒预热和20秒采样，连续执行3轮并校验实验条件一致性，最后输出中位数报告。正式优化前结果与原始 JSON 见 [`docs/performance/issue-30.md`](docs/performance/issue-30.md)。

## Gameplay Effect

生命强化卡牌已接入可追踪、可移除和可回滚的最小 Gameplay Effect 闭环，支持 Add、Multiply、Override 的确定性聚合；医疗包和护甲包也通过即时 Effect 完成预检、执行与库存失败回滚。设计与回滚规则见 [`docs/architecture/gameplay-effects.md`](docs/architecture/gameplay-effects.md)。

## macOS Development Build

```bash
/Users/jungle/UnityEditors/6000.5.3f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "/Users/jungle/GameProject/Unity/FPS/My project" \
  -executeMethod Issue1Build.BuildMacDevelopment \
  -logFile /tmp/fps-build.log
```

构建产物位于 `Builds/Issue1/FPS.app`，`Builds/` 已由 Git 忽略。

## 已知限制

- `CityNew` 使用的旧 LightmapSnapshot 与当前 Unity 版本不兼容，运行和构建时会出现 GI 警告；后续应在确定最终关卡光照后重新烘焙。
- 旧 TerrainData 中的两个 DetailPrototype 会导致 Unity 6.5 在 macOS Player Build 阶段原生崩溃；当前基线已移除这些草地细节，保留地形高度图和材质层。
- 弹匣、换弹、双武器、通用伤害、敌人 AI、任务与撤离等功能由后续 GitHub Issues 继续实现。
