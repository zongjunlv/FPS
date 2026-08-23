# Unity 自动化质量门禁

Issue 29 为 Unity 6000.5.3f1 建立统一的编译、EditMode 与 PlayMode 门禁。本地和 GitHub Actions 使用同一个入口，避免 CI 与开发机执行方式分叉。

## 本地执行

先关闭正在使用该项目的 Unity Editor，然后在项目根目录运行：

```bash
./scripts/ci/run-unity-quality-gate.sh all
```

也可以只运行一个阶段：

```bash
./scripts/ci/run-unity-quality-gate.sh compile
./scripts/ci/run-unity-quality-gate.sh editmode
./scripts/ci/run-unity-quality-gate.sh playmode
```

脚本优先使用 `UNITY_EXECUTABLE`，否则按照 `ProjectSettings/ProjectVersion.txt` 自动查找 Unity。自定义路径示例：

```bash
UNITY_EXECUTABLE="/Users/jungle/UnityEditors/6000.5.3f1/Unity.app/Contents/MacOS/Unity" \
  ./scripts/ci/run-unity-quality-gate.sh all
```

默认报告目录为 `artifacts/quality-gate`，可以通过 `QUALITY_GATE_OUTPUT_DIR` 修改。

## 输出

- `compile.log`、`editmode.log`、`playmode.log`：Unity 原始日志。
- `editmode.xml`、`playmode.xml`：Unity Test Framework 机器可读报告。
- `compile.json`、`editmode.json`、`playmode.json`：带失败分类的阶段报告。
- `summary.json`：机器可读总结果。
- `summary.md`：供开发者和 GitHub Job Summary 阅读的总表。

任何必需阶段失败，入口脚本都会返回非零状态。编译失败后不会继续执行测试；EditMode 失败后仍会继续运行 PlayMode，以便一次收集完整测试信息。

## 失败分类

| 分类 | 含义 |
| --- | --- |
| `product-compilation` | C# 产品代码或测试代码无法编译 |
| `test-failure` | Unity 已生成有效 XML，但存在失败测试 |
| `environment-license` | Unity License 或 entitlement 无效 |
| `environment-import` | Asset、Package、Import Worker 或 Shader 导入失败 |
| `environment-unity` | Unity 未启动、异常退出、项目被其他 Editor 占用或编译探针未执行 |
| `environment-report` | 测试结束后缺少 XML，或 XML 无法解析 |

License、资源导入和报告异常即使 Unity 返回退出码 0，也会被判定为失败，不会伪装成绿色测试结果。

## GitHub Actions 自托管 Runner

工作流位于 `.github/workflows/unity-quality-gate.yml`，在 `main` 推送、Pull Request 和手动触发时运行。Runner 需要：

1. 标签包含 `self-hosted`、`macOS`、`unity`。
2. 安装项目指定的 Unity 6000.5.3f1，并完成有效 License 配置。
3. 运行在可创建图形设备的 macOS 登录会话中；PlayMode 不使用 `-nographics`。
4. 安装 Python 3。
5. 如 Unity 不在默认位置，在仓库 Actions Variables 中配置 `UNITY_EXECUTABLE`。

无论门禁成功或失败，工作流都会上传 XML、JSON、Markdown 和 Unity 日志，并把 `summary.md` 写入 GitHub Job Summary。
