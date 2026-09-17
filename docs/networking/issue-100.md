# Issue 100：真实服务器与双客户端多进程验收

## 验收目标

这套门禁不是 EditMode 夹具，也不是单进程内伪造三个角色。每个网络场景都会
启动三个独立操作系统进程：一台 Dedicated Server、Client A 和 Client B。
三者通过真实 Unity Transport Socket 通信，报告会校验 PID 不同、运行时间重叠、
日志/快照/时间线存在以及完整玩法流程通过。

账号注册、登录和房间步骤使用本地确定性的 `local-acceptance` 控制面；战局内
移动、战斗、掉落、背包、升级、任务、断线重连和结算全部走 NGO + Unity
Transport 的真实数据面。报告会明确记录这一区别，不把本地控制面冒充线上服务。

玩家移动、跳跃与射击共享同一条有序可靠输入流。三类命令共用领域序列号，统一
通道可避免丢包重传时离散动作被后续移动越序；命令吞吐和带宽仍由四档网络门禁
持续测量，后续若切换到输入冗余的不可靠序列协议，可复用同一套报告做回归对比。

## 静默构建与运行

全程使用 Unity CLI，不需要打开 Unity 编辑器，也不会播放声音。

```bash
Tools/Networking/build_issue100_acceptance.sh
Tools/Networking/run_issue100_multiprocess.sh
```

默认依次运行四档网络条件：

- 正常网络；
- 80 ms RTT；
- 150 ms RTT；
- 80 ms RTT + 5% 丢包。

正常网络档会由 Client A 离屏输出 1280×720、30 FPS 的连续演示录像。录像只有
视频轨，没有音轨；字幕读取正在执行的真实验收步骤，因此说明和操作共用同一
状态源。临时调试若不需要录像，可执行：

```bash
FPS_ISSUE100_SKIP_VIDEO=1 Tools/Networking/run_issue100_multiprocess.sh
```

构建器优先使用当前平台的 Unity Dedicated Server Build Support。若本机没有该
平台模块（例如只安装了 Linux Dedicated Server 模块、却要在 macOS 本机执行），
会回退到 `PlayerHeadlessFallback`：服务器仍是独立、无本地玩家、只运行权威
模拟的后台进程，并使用 `-fps-server -nographics` 启动；构建清单会明确记录该
回退，不会把它伪报成 Server 子目标。

## 硬门禁

报告只有同时满足以下条件才会通过：

- 每档恰好存在 `server`、`client-a`、`client-b` 三个不同 PID，且生命周期重叠；
- 四档网络条件完整，且每个进程均保留日志、末帧快照和时间线；
- 注册、登录、建房/入房、选角、准备、加载、移动、下蹲、跳跃、冲刺、ADS、
  射击、换弹、波次、掉落、背包、升级、终端、重连、撤离和结算均有通过证据；
- 命中反馈、预测校正、状态分歧、命令账目、RTT 和上下行带宽不超过预算；
- 正式运行的录像文件非空，恰好一条 1280×720 视频轨、无音轨且约 30 FPS。

## 产物

默认输出目录为 `artifacts/networking/issue100/<run-id>/`：

- `report.json`：机器可读门禁结论；
- `report.md`：面试与 CI 可直接阅读的汇总；
- `scenarios/<场景>/<角色>/player.log`：三个进程的独立日志；
- `latest-snapshot.json`、`timeline.ndjson`、`metrics.json`：运行态证据；
- `video/issue100-coop-demo.mp4`：正常网络档连续演示；
- `video/ffprobe.json`：分辨率、帧率、时长和音轨验证结果。

任何一步失败时，执行器仍保留已产生的三进程日志、快照和时间线，便于复现，
不会只留下一个笼统的退出码。
