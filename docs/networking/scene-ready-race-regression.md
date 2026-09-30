# CityNew 场景就绪竞态回归

## 原因与修复

旧客户端存在以下可复现顺序：

1. `StartLobbyGameAsync` 分配战局后仍在等待房间 GET，普通房间操作锁保持 busy。
2. 并行轮询先读到 `loading`，协调器完成 CityNew 加载。
3. `ReportSceneReadyAsync` 误用普通操作门控，未发送 `readyEpoch`；协调器不再重试，错误也没有显示在加载遮罩上。
4. 房主加载屏障在 30 秒后取消战局，客户端退回房间。此阶段尚未启动战斗 UDP 连接，不能据此归因于 UDP 丢包。

修复将就绪确认与普通按钮操作锁分离；用房间、加载轮次、操作代次、连接及取消状态守卫迟到响应。临时失败串行退避重试，不并发重复发送，加载阶段本地重试上限仍为 30 秒，不延长原房主屏障。成功必须确认本地玩家的 `readyEpoch`；迟到开局 GET 可以合法观察到同一分配的 `battle`。离房重置同步清理旧 busy 锁。

加载遮罩区分正常加载、就绪确认重试、准备失败与 UDP 连接阶段。正式账号服务、路由、认证、战斗端点与协议版本未改变。

服务器现场记录中，房主没有上报本轮就绪，取消原因为“等待成员加载 CityNew 超时，战局已取消”；战斗进程已经监听，但没有客户端连接。该记录与复现相符；本机回归不等同于还原用户设备上每条请求的完整时序。

## 自动化测试

使用 Unity 插件 CLI，连接已启动的 `-batchmode -nographics -disable-audio` 后台编辑器，不抢前台焦点。测试过滤器需提供完整类名称；命令返回测试结果路径后，还必须确认 XML 的 `total > 0`、`failed = 0`。

```sh
/Users/jungle/.unity/bin/unity command coop_repair_tests \
  --caller plugin --skill unity-cli \
  --project-path '/Users/jungle/GameProject/Unity/FPS/My project' \
  --filter 'FPS.Tests.Architecture.CoopSceneReadyRaceTests;FPS.Tests.Architecture.Issue88SceneLoadBarrierTests;FPS.Tests.Architecture.Issue87CoopLobbyTests;FPS.Tests.Architecture.Issue99ReconnectAdmissionTests' \
  --output '/private/tmp/fps-scene-ready-editmode.xml' \
  --mode EditMode --format json
```

新增测试覆盖 busy 期间上报、缺少确认、重试成功、旧轮次／房间／操作代次回包、取消和退出、新轮次 UI 清理，以及离房后的旧锁释放。相邻 PlayMode 回归覆盖生成屏障、加载取消后光标恢复及 ESC 退出。

无 GUI 的合成键盘测试临时使用独立的 InputSettings 克隆忽略 Game View 焦点、启用自身的合成键盘，并临时允许后台处理；结束与异常时恢复原设置和后台状态。不改正式输入配置，不绕过真实按键事件和生产 Update。

## 真实本机 HTTPS 调用链

`scripts/editor/CoopSceneReadyRaceAcceptance.cs` 使用本机 TLS 替身服务，真实调用生产的分配、GET、轮询、场景协调器和就绪回报。三种顺序分别为：开局 GET 尚未结束时场景就绪、正常顺序、迟到开局 GET 已观察到 battle。

运行前用 OpenSSL 生成一次性 localhost 自签证书（包含 localhost 与 127.0.0.1 SAN），导出兼容 Unity Mono 的 legacy PKCS#12，密码设为非秘密测试值 `repro-only`。证书仅由脚本针对本机目标固定校验，不安装系统 CA，不禁用生产 TLS 校验。完成后移除自身生成的临时证书与私钥。

```sh
/Users/jungle/.unity/bin/unity command run_script \
  --caller plugin --skill unity-cli \
  --project-path '/Users/jungle/GameProject/Unity/FPS/My project' \
  --file scripts/editor/CoopSceneReadyRaceAcceptance.cs \
  --entry CoopSceneReadyRaceAcceptance.Run \
  --args '["/private/tmp/fps-loading-ready-local-cert.pfx","/private/tmp/fps-scene-ready-race.json"]' \
  --timeout_ms 90000 --timeout 100 --format json
```

验收 JSON 必须为 `expectedBehaviorPassed = true`，且三种顺序均真正发送并确认一次就绪回报，不返回 lobby。该脚本拒绝交互式编辑器、未保存场景和已有登录账号；只使用内存合成身份，不访问公网，不启动实际多人 UDP 服务器。

## 验证边界

旧门控已先复现失败，再以相同请求顺序验证修复。自动化通过仅证明所覆盖的代码路径；公网双客户端进入同一战局并完整通关仍需正式客户端验证。源码修复不会自动更新已经下载的安装包。

2026-09-29 修复回归：EditMode 44／44 通过（含新增 24 条）、PlayMode 16／16 通过。完整本机 HTTPS 三种请求顺序通过；旧 busy 门控及退出旧锁的回归均保留先失败、后通过的记录。未以这些本机结果宣称公网完整对局已验收。
