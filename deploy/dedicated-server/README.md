# FPS 专用服务器远程部署

本目录只保存部署说明；构建产物、运行状态、连接票据和签名密钥均不进入仓库。

## 1. 构建 Linux Server

在构建机使用 Unity CLI 调用 `Issue85DedicatedServerBuild.Build`，并指定应用、协议和内容版本：

```bash
/path/to/Unity -batchmode -nographics -disable-audio \
  -projectPath /path/to/project \
  -executeMethod Issue85DedicatedServerBuild.Build \
  -serverBuildTarget linux \
  -buildOutput /srv/fps/build/FPSDedicatedServer.x86_64 \
  -serverProtocolVersion 1 \
  -serverContentVersion citynew-v1 \
  -logFile /tmp/fps-server-build.log \
  -quit
```

同目录的 `dedicated-server-build.json` 会记录产品版本、协议版本、内容版本、Unity 版本、产物大小和 SHA-256，可用于发布校验。

## 2. 在远程主机保存密钥

`FPS_SERVER_AUTH_SECRET` 必须由主机的 Secret Manager、systemd `EnvironmentFile` 或 CI Secret 注入，长度至少 32 字节。不要把真实值写入 `.env`、启动参数、日志或 Git。

```bash
export FPS_SERVER_AUTH_SECRET="$(security-tool read fps/server-ticket-key)"
```

## 3. 正式拓扑：房间服务与战斗服务器分离

客户端先通过 Unity Authentication 登录，并使用 UGS Multiplayer Session 完成公开房间、角色选择与准备。房主开始战斗时，不再启动 Relay Host；客户端改为调用 HTTPS Match Broker。Broker 校验 Unity 登录 JWT 后启动腾讯云上的 Linux Headless Dedicated Server，并把 1—2 名房间成员固定映射到权威模拟席位。每名客户端再领取自己的短期 HMAC 连接票据，直接通过 UDP 连接 Dedicated Server。

Match Broker 使用 `fps-match-broker.service` 常驻运行。将
`broker.conf.example` 复制到 `/etc/fps/broker.conf`，将 TLS 证书与私钥放到
`/srv/fps/data/tls`，私钥权限设为 `0600`。客户端只保存 Broker 地址与证书
SHA-256 指纹，不保存服务端签名密钥。安全组需要同时允许 Broker 的 TCP
端口和游戏 UDP 端口。

```bash
sudo cp deploy/dedicated-server/fps-match-broker.service \
  /etc/systemd/system/fps-match-broker.service
sudo systemctl daemon-reload
sudo systemctl enable --now fps-match-broker.service
curl --cacert /srv/fps/data/tls/broker-cert.pem \
  https://127.0.0.1:80/healthz
```

## 4. 手工启动两人战局（仅部署诊断）

```bash
python3 scripts/networking/issue101_server_manager.py start \
  --binary /srv/fps/build/FPSDedicatedServer.x86_64 \
  --public-host game.example.com \
  --port 17777 \
  --match-id interview-demo-101 \
  --player player-a=operative-alpha \
  --player player-b=operative-bravo \
  --application-version 1.0.0 \
  --protocol-version 1 \
  --content-version citynew-v1 \
  --idle-timeout 120 \
  --allocation-output /secure/runtime/allocation.json
```

返回的 allocation 包含公网地址、端口、战局 ID、三层版本和两名玩家各自的一次性连接/重连票据，不包含签名密钥。安全组只需开放对应 UDP 端口。

生产主机应使用仓库中的 `fps-server.service`，让 systemd 监管前台
`run` 命令及其 Unity 子进程。把 `server.conf.example` 复制到
`/etc/fps/server.conf` 并填写非敏感的公网地址、端口、版本和战局参数；
签名密钥仍只保存在权限为 `0600` 的 `/etc/fps/server.env`。发布目录放在
`/srv/fps/releases/<release-id>`，`/srv/fps/current` 只指向当前版本，便于
校验后原子切换和回滚。

正式客户端不会读取该 allocation 文件；它通过 Match Broker 获取只属于当前登录账号的票据。手工启动方式仅用于服务器部署诊断。

## 5. 诊断与停止

```bash
python3 scripts/networking/issue101_server_manager.py status \
  --state artifacts/networking/issue101/runtime/interview-demo-101/server-state.json

python3 scripts/networking/issue101_server_manager.py stop \
  --state artifacts/networking/issue101/runtime/interview-demo-101/server-state.json
```

诊断 JSON 每 5 秒原子刷新，包含连接数、配置/当前 Tick、收发总字节与速率、任务阶段、认证拒绝原因、异常和关闭原因。无人连接超过空闲租期后，战局自动退出并标记为 `recycled / idle-timeout`。

## 6. 两客户端公网验收

将 allocation 安全下载到客户端构建机，然后执行：

```bash
python3 scripts/networking/run_issue101_remote_clients.py \
  --allocation /secure/runtime/allocation.json \
  --build-manifest Builds/Issue100/build-manifest.json \
  --output artifacts/networking/issue101/public-demo
```

脚本启动两个独立客户端进程，完成加入、移动、射击、重连、波次、掉落、升级、终端和撤离流程；报告只记录账号 ID 和结果，不复制短期票据。
