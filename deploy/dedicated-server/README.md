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

## 2. 在远程主机保存密钥与持久数据

`FPS_SERVER_AUTH_SECRET` 必须由主机的 Secret Manager、systemd `EnvironmentFile` 或 CI Secret 注入，长度至少 32 字节。不要把真实值写入 `.env`、启动参数、日志或 Git。

```bash
export FPS_SERVER_AUTH_SECRET="$(security-tool read fps/server-ticket-key)"
```

账号、房间和令牌摘要存储在可配置的 `FPS_CONTROL_DATABASE`，默认示例为
`/srv/fps/data/fps-control.sqlite3`。数据库必须位于持久数据目录，绝不能放在
`/srv/fps/current` 或 `/srv/fps/releases/<release-id>` 中；发布目录可替换，数据库
不可随发布目录删除。服务使用非 root 的 `fps` 用户运行，数据库及其备份文件只允许
该用户读取。注册密码只存带独立盐的 PBKDF2-HMAC-SHA256 慢哈希；原密码、原始访问
令牌和战局签名密钥不得进入日志。

## 3. 正式拓扑：自建控制面与权威战斗进程

客户端先连接自建 HTTPS 服务，完成注册/登录、公开房间、角色选择与准备。
房主开始战斗时，服务端以数据库中真实的房间成员和准备状态为准，启动 Linux
Headless Dedicated Server，并把 1—2 名成员固定映射到权威模拟席位。每名客户端
再领取自己的短期 HMAC 入局票据，直接通过 UDP 连接 Dedicated Server。运行时
不需要 Unity Authentication、UGS Multiplayer Session、Lobby 或 Relay；NGO 与
Unity Transport 只用作自有客户端和游戏进程的通信库。

控制面使用 `fps-match-broker.service` 常驻运行。发布时要将
`fps_match_broker.py`、`fps_control_store.py`、`fps_control_backup.py` 和
`issue101_server_manager.py` 一起放到 `/srv/fps/current/tools/`。将
`broker.conf.example` 复制到 `/etc/fps/broker.conf` 并填写本机实际域名/IP、
数据库路径、TLS 证书和私钥路径；私钥权限设为 `0600`。客户端只保存 HTTPS
服务地址与可选证书指纹，不保存签名密钥；可以通过
`Application.persistentDataPath/CoopDedicatedServerSettings.json` 覆盖服务地址，
无需把某台机器的 IP 写死到客户端逻辑中。优先使用稳定域名和可信 CA 证书；
自签证书换签时必须同步更新客户端 pin。安全组需要同时允许 HTTPS TCP 端口和
游戏 UDP 端口，禁止 HTTP 明文降级。

```bash
sudo cp deploy/dedicated-server/fps-match-broker.service \
  /etc/systemd/system/fps-match-broker.service
sudo systemctl daemon-reload
sudo systemctl enable --now fps-match-broker.service
curl --cacert /srv/fps/data/tls/broker-cert.pem \
  https://127.0.0.1:80/healthz
```

`/healthz` 只证明控制面进程响应；还需使用正式客户端验证注册/登录、房间、
分配、双客户端直连和战局结算。旧 Unity 云账号没有可导出的密码，本自建账号
服务采用新账号库；旧云账号不删除，但玩家需要在新服务重新注册。

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

## 5. 在线备份与迁移服务器

账号库使用 SQLite WAL 模式。运行中**不能只复制主 `.sqlite3` 文件**；应使用
在线备份命令生成一致性快照并校验，然后将备份通过安全通道保存到另一台机器：

```bash
python3 /srv/fps/current/tools/fps_control_backup.py backup \
  --database /srv/fps/data/fps-control.sqlite3 \
  --output /srv/fps/data/backups/fps-control-YYYYMMDD.sqlite3
python3 /srv/fps/current/tools/fps_control_backup.py verify \
  --database /srv/fps/data/backups/fps-control-YYYYMMDD.sqlite3
```

正式切换前先停止旧主机接收新房间，等待当前战局结束或主动结束战局，随后停止
旧控制面所有写入并制作**最终备份**；在线预热备份不能代替这一份。在新主机
停止控制面服务后，保留原数据库作为回滚副本，再将**校验通过的最终备份**安装到
新主机配置的数据库路径，启动服务并再次校验账号数量和登录。切换主机同时需要
迁移或重新安全配置 `/etc/fps/server.env` 中的票据签名密钥、TLS 证书和
`broker.conf` 的公开地址；不能把它们提交到 Git。若签名密钥轮换，旧入局票据
会失效，因此应在战局结束后切换。

`broker-state.json`、游戏进程状态与日志是运行态，不保证跨主机热迁移正在进行
的战斗。可保留旧主机作为回滚点，先在新机器完成单房双客户端验收，再通过 DNS
或客户端外部配置切换入口。最终备份之后不能继续在旧主机注册新账号；若新主机
已产生新账号或房间数据，回滚前需停止新主机写入并迁回这些新增数据，不能直接
重新启动旧数据库。详细数据边界见
[自建控制面设计](../../docs/networking/self-hosted-control-plane.md)。

## 6. 诊断与停止

```bash
python3 scripts/networking/issue101_server_manager.py status \
  --state artifacts/networking/issue101/runtime/interview-demo-101/server-state.json

python3 scripts/networking/issue101_server_manager.py stop \
  --state artifacts/networking/issue101/runtime/interview-demo-101/server-state.json
```

诊断 JSON 每 5 秒原子刷新，包含连接数、配置/当前 Tick、收发总字节与速率、任务阶段、认证拒绝原因、异常和关闭原因。无人连接超过空闲租期后，战局自动退出并标记为 `recycled / idle-timeout`。

## 7. 两客户端公网验收

将 allocation 安全下载到客户端构建机，然后执行：

```bash
python3 scripts/networking/run_issue101_remote_clients.py \
  --allocation /secure/runtime/allocation.json \
  --build-manifest Builds/Issue100/build-manifest.json \
  --output artifacts/networking/issue101/public-demo
```

脚本启动两个独立客户端进程，完成加入、移动、射击、重连、波次、掉落、升级、终端和撤离流程；报告只记录账号 ID 和结果，不复制短期票据。
