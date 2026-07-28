# FPS

基于 Unity 6.5、URP 和 Input System 开发的单人 FPS 项目。

## 当前可运行基线

- 玩法场景：`CityNew`
- 玩家：移动、奔跑、跳跃、鼠标视角和瞄准状态
- 战斗：AR 连续射击、射速限制、实体子弹、枪口焰和后坐力
- 敌人：Spider 受击、生命值扣除、死亡爆炸

## 操作

| 操作 | 键位 |
| --- | --- |
| 移动 | WASD |
| 观察 | 鼠标移动 |
| 跳跃 | Space |
| 奔跑 | Left Shift |
| 射击 | 鼠标左键 |
| 瞄准状态 | 鼠标右键 |
| 交互（预留） | F |

## PlayMode 测试

```bash
/Users/jungle/UnityEditors/6000.5.3f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -nographics \
  -projectPath "/Users/jungle/GameProject/Unity/FPS/My project" \
  -runTests \
  -testPlatform PlayMode \
  -testResults /tmp/fps-playmode.xml \
  -logFile /tmp/fps-playmode.log
```

测试会验证：

- `CityNew` 已加入 Build Settings 并能够加载。
- 玩家包含角色控制、输入、战斗及 CharacterController。
- 场景包含武器和敌人。
- 场景中不存在 Missing Script。
- 已废弃的第三人称视角空壳组件已移除。
- 武器能够开火并生成实体子弹，敌人受到致命伤害后会销毁。

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
