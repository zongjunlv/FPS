# 专用服务器本地流程

全部流程通过 Unity CLI 或构建产物执行，不需要打开 Unity 编辑器。

## 构建专用服务器

```bash
Tools/Networking/build_dedicated_server.sh
```

也可以传入输出路径：

```bash
Tools/Networking/build_dedicated_server.sh /tmp/FPSDedicatedServer.x86_64
```

构建使用 Unity 的 `StandaloneBuildSubtarget.Server`，只包含 CityNew。默认生成
Linux x86_64 服务器到 `Builds/DedicatedServer/FPSDedicatedServer.x86_64`，适合
部署到常见 Linux 服务器或容器。也可以通过 `FPS_SERVER_PLATFORM=macos` 或
`FPS_SERVER_PLATFORM=windows` 生成对应平台产物，但需要先安装相应的 Unity
Dedicated Server Build Support 模块。

## 启动一台服务器和两个客户端

先准备客户端构建，然后运行：

```bash
Tools/Networking/launch_local_cluster.sh
```

可通过环境变量修改产物和战局参数：

- `FPS_SERVER_ARTIFACT`
- `FPS_CLIENT_ARTIFACT`
- `FPS_SERVER_BIN`（直接指定服务器可执行文件）
- `FPS_CLIENT_BIN`（直接指定客户端可执行文件）
- `FPS_SERVER_PORT`
- `FPS_MATCH_ID`
- `FPS_SERVER_SEED`
- `FPS_SERVER_VERSION`
- `FPS_SERVER_AUTH_SECRET`（至少 32 字节；未提供时本地脚本会生成一次性密钥）

服务器支持以下启动参数：

- `-server-map CityNew`
- `-server-port 17777`
- `-server-match local-two-client`
- `-server-max-players 2`
- `-server-seed 18018`
- `-server-version local-dev`
- `-server-tick-rate 60`
- `-server-diagnostics <json 路径>`

服务器日志包含 `INITIALIZING`、`LISTENING`、`READY`、`FAILED` 和
`SHUTDOWN` 状态。收到终止信号后会关闭网络并输出诊断 JSON。

连接审批要求客户端携带由可信账号服务签发的短时凭证。服务器会校验签名、
有效期、版本、重复账号、凭证重放和人数上限，再把账号身份绑定到 NGO 的
`clientId` 与权威模拟玩家槽位。生产环境应由独立后台持有签名密钥；
`launch_local_cluster.sh` 仅为本机联调，让服务器和两个测试客户端共享一次性
环境变量密钥，密钥及完整凭证不会写入日志。
