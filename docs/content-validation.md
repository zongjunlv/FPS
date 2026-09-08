# 内容资产校验

## Unity 中使用

打开 `FPS > Content > Validate Content`，点击“扫描正式内容”。窗口显示错误、警告、资产路径；支持“仅错误”和文本搜索。点击“定位资产”会选中对应资产，并在 Project 中高亮。

扫描是只读操作，不会自动修复、改写或保存场景。修复后重新扫描即可。

## 无界面入口

在项目根目录执行：

```bash
UNITY_EXECUTABLE="/Users/jungle/UnityEditors/6000.5.3f1/Unity.app/Contents/MacOS/Unity" \
  bash scripts/ci/run-content-validation.sh
```

报告和日志位于 `artifacts/content-validation/run.*/`。每次运行使用独立目录，防止误读上次的成功报告。现有 GitHub Actions 已在测试和性能检查前加入这一关；需要可用的 Unity 自托管运行器及授权。

也可直接调用：

```bash
Unity -batchmode -nographics -quit \
  -projectPath "/path/to/project" \
  -executeMethod ContentValidationCli.Run \
  -contentValidationReport "/tmp/content-report.json" \
  -logFile "/tmp/content-validation.log"
```

可选 `-contentValidationRoot Assets/某个目录` 指定扫描范围，主要用于自动化测试。默认扫描正式内容及其领域依赖，不扫描第三方美术包中的示例配置。

退出码：`0` 校验通过；`1` 内容存在错误；`2` 执行失败。Unity 自身编译或授权失败也可能返回非零码。警告不阻断流程，错误必须修复。

## 验收方式

1. 扫描当前正式内容，确认结果与资产路径可读。
2. 复制一个正式内容资产（保留原 ID），重扫应报告重复 ID，并可定位副本。删除测试副本后恢复通过。
3. 可临时清空副本的图标、引用或填写无效模板地址，验证错误提示。不要保存对原始正式资产的试验改动。

完整异常场景由 EditMode 的 `Issue49` 测试覆盖。
