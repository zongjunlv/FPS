# Issue 26：敌人对象池性能验收

## 实现口径

- `WaveDirector` 继续只依赖 `IEnemyFactory`，波次规则未包含对象创建细节。
- `CityNewWaveBootstrap` 默认使用 `PooledEnemyFactory`，预热 4 个敌人，最大容量 64。
- 回收延迟到帧末执行，避免在 `Health.Died` 事件通知经验、掉落期间复活同一对象。
- 每次复用会重置生命、护甲、最后伤害、死亡表现、导航路径、巡逻点、警戒/搜索、攻击冷却、动画、音频、碰撞体与受击闪烁。
- 感知调度器和小队协调器依赖 `OnEnable/OnDisable` 精确注册、注销；重复注册由容器去重。
- 池化敌人的代际计数会使上一代仍在播放的附着弹痕自动回收。

## 自动化门禁

`Issue26EnemyPoolTests` 覆盖：

1. 连续波次使用同一批预热实例，稳定阶段 `InstantiateCount` 不增长。
2. 复用后的敌人恢复满血、非死亡状态，且生成代际递增。
3. 玩家失败后所有租约在帧末归还，感知调度注册数归零。

## 24 / 48 敌人性能采集

基准工具仍使用 `-fps-benchmark`，但敌人压力组现在通过 `PooledEnemyFactory` 租借，不再直接 `Instantiate`。报告新增：

- `enemyPoolObjects`
- `enemyPoolReuseCount`
- `enemyPoolExpansionCount`
- `stableSampleInstantiateCount`（稳定采样阶段必须为 0）
- `maximumPerceptionLatencyFrames`
- `totalGcBytes`
- `maximumGcBytesInFrame`

同时，`onePercentLowFps` 已修正为“最慢 1% 帧时间均值”的倒数。

```bash
Builds/Issue1/FPS.app/Contents/MacOS/FPS \
  -fps-benchmark -batchmode -nographics \
  -benchmark-enemies 24 -benchmark-warmup 5 \
  -benchmark-duration 20 -benchmark-seed 26024 \
  -benchmark-variant issue26-pool-24 \
  -benchmark-output /tmp/issue26-pool-24.json

Builds/Issue1/FPS.app/Contents/MacOS/FPS \
  -fps-benchmark -batchmode -nographics \
  -benchmark-enemies 48 -benchmark-warmup 5 \
  -benchmark-duration 20 -benchmark-seed 26048 \
  -benchmark-variant issue26-pool-48 \
  -benchmark-output /tmp/issue26-pool-48.json
```

## 48 敌人回退线

沿用 Issue 13 同设备、同画质、同分辨率的优化后基线，并允许最多 10% 回退：

| 指标 | Issue 13 基线 | Issue 26 门禁 |
|---|---:|---:|
| 平均帧时间 | 4.88 ms | ≤ 5.37 ms |
| 主线程平均 | 4.88 ms | ≤ 5.37 ms |
| 1% Low | 47.45 FPS | ≥ 42.70 FPS |
| P95 帧时间 | 13.79 ms | ≤ 15.17 ms |

24 敌人的旧数据受温升顺序影响，不用于相对比较；本轮重新采集后只作为后续版本基线。

## 本轮短采样结果

2026-08-15 在 Apple M4、Unity 6000.5.3f1、Development Player、`-nographics` 下完成 2 秒预热 + 5 秒短采样：

| 敌人数 | 平均帧时间 | P95 | P99 | 1% Low | 主线程平均 | 平均 GC/帧 | 最大感知间隔 | 稳定期创建 |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 24 | 0.421 ms | 0.521 ms | 0.707 ms | 742.09 FPS | 0.420 ms | 655 B | 6 帧 | 0 |
| 48 | 0.460 ms | 0.515 ms | 0.715 ms | 195.16 FPS | 0.460 ms | 659 B | 12 帧 | 0 |

两档均在预热阶段扩容到目标数量，正式采样阶段 `stableSampleInstantiateCount = 0`。该短采样用于验证性能采集链和稳定期零创建，不替代 5 秒预热、20 秒采样、三轮交错取中位数的正式基准。
