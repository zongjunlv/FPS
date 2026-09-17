#!/usr/bin/env python3
"""Strict gate for Issue100 dedicated-server plus two-client evidence."""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
from typing import Any, Iterable


SCHEMA_VERSION = "issue100-multiprocess-acceptance-v1"
REQUIRED_SCENARIOS = {
    "rtt-000-loss-00": (0, 0),
    "rtt-080-loss-00": (80, 0),
    "rtt-150-loss-00": (150, 0),
    "rtt-080-loss-05": (80, 500),
}
REQUIRED_ROLES = {"server", "client-a", "client-b"}
REQUIRED_STEPS = {
    "account.register",
    "account.login",
    "room.create",
    "room.join",
    "character.select",
    "lobby.ready",
    "scene.load",
    "movement.walk",
    "movement.crouch",
    "movement.jump",
    "movement.sprint",
    "combat.aim",
    "combat.fire",
    "combat.reload",
    "wave.complete",
    "drop.spawn",
    "inventory.pickup",
    "inventory.use",
    "upgrade.select",
    "mission.terminal",
    "reconnect.restore",
    "mission.extraction",
    "match.settlement",
}


def validate(report: dict[str, Any], require_video: bool = False) -> list[str]:
    errors: list[str] = []
    if report.get("schemaVersion") != SCHEMA_VERSION:
        errors.append("schemaVersion 不匹配")

    metadata = report.get("metadata") or {}
    if metadata.get("evidenceKind") != "MultiProcessPlayer":
        errors.append("证据类型必须是 MultiProcessPlayer")
    if metadata.get("topology") != "dedicated-server-plus-two-clients":
        errors.append("拓扑必须是 dedicated-server-plus-two-clients")
    if metadata.get("processesPerScenario") != 3:
        errors.append("每档场景必须恰好三个进程")
    if metadata.get("controlPlane") != "local-acceptance":
        errors.append("必须如实标记本地验收控制面")
    if metadata.get("dataPlane") != "UnityTransport":
        errors.append("数据面必须是 UnityTransport")

    scenarios = report.get("scenarios") or []
    observed = {
        item.get("stableId"): (
            item.get("roundTripLatencyMilliseconds"),
            item.get("packetLossBasisPoints"),
        )
        for item in scenarios
        if isinstance(item, dict)
    }
    if observed != REQUIRED_SCENARIOS:
        errors.append("必须精确包含正常、80ms、150ms 和 80ms+5% 丢包")

    for scenario in scenarios:
        if not isinstance(scenario, dict):
            errors.append("场景记录格式无效")
            continue
        _validate_scenario(scenario, errors)

    passed_steps = {
        item.get("stepId")
        for item in report.get("flow", [])
        if isinstance(item, dict) and item.get("state") == "passed"
    }
    for step in sorted(REQUIRED_STEPS - passed_steps):
        errors.append(f"流程缺少通过证据：{step}")
    for item in report.get("flow", []):
        if isinstance(item, dict) and item.get("state") == "failed":
            errors.append(f"流程步骤失败：{item.get('stepId', 'unknown')}")

    artifacts = report.get("artifacts") or {}
    video_path = str(artifacts.get("videoPath") or "")
    if require_video:
        if not video_path:
            errors.append("正式验收缺少连续演示录像")
        elif not pathlib.Path(video_path).is_file():
            errors.append("连续演示录像文件不存在")
        elif pathlib.Path(video_path).stat().st_size <= 0:
            errors.append("连续演示录像为空")

    return sorted(set(errors))


def _validate_scenario(scenario: dict[str, Any], errors: list[str]) -> None:
    stable_id = str(scenario.get("stableId") or "unknown")
    prefix = stable_id + ": "
    processes = scenario.get("processes") or []
    roles = {item.get("role") for item in processes if isinstance(item, dict)}
    if len(processes) != 3 or roles != REQUIRED_ROLES:
        errors.append(prefix + "必须包含 server/client-a/client-b 三种角色")
    pids = [item.get("processId") for item in processes if isinstance(item, dict)]
    if len(pids) != 3 or any(not isinstance(pid, int) or pid <= 0 for pid in pids):
        errors.append(prefix + "进程 PID 无效")
    elif len(set(pids)) != 3:
        errors.append(prefix + "三个 PID 必须不同")

    starts = [item.get("startedUnixMilliseconds", 0) for item in processes]
    ends = [item.get("endedUnixMilliseconds", 0) for item in processes]
    if (len(starts) != 3 or any(not isinstance(value, int) or value <= 0 for value in starts)
            or any(not isinstance(value, int) or value <= 0 for value in ends)):
        errors.append(prefix + "进程起止时间无效")
    elif max(starts) >= min(ends):
        errors.append(prefix + "三个进程没有真实重叠运行")

    for process in processes:
        if not isinstance(process, dict):
            continue
        for field in ("logPath", "snapshotPath", "timelinePath"):
            path = str(process.get(field) or "")
            if not path or not pathlib.Path(path).is_file():
                errors.append(prefix + f"{process.get('role')} 缺少 {field}")

    timeline = str(scenario.get("timelinePath") or "")
    if not timeline or not pathlib.Path(timeline).is_file():
        errors.append(prefix + "缺少合并时间线")
    _validate_metrics(stable_id, scenario.get("metrics") or {}, errors)


def _validate_metrics(stable_id: str, metrics: dict[str, Any], errors: list[str]) -> None:
    prefix = stable_id + ": metrics "
    required_numeric = (
        "durationSeconds",
        "hitFeedbackSampleCount",
        "p95HitFeedbackMilliseconds",
        "correctionCount",
        "correctionsPerMinute",
        "p95CorrectionMagnitude",
        "maximumCorrectionMagnitude",
        "uplinkBytes",
        "downlinkBytes",
        "uplinkBytesPerSecond",
        "downlinkBytesPerSecond",
        "stateComparisonCount",
        "stateDivergenceCount",
        "stateDivergenceRate",
        "maximumStateDivergenceMagnitude",
        "maximumStateDivergenceDurationMilliseconds",
        "sentCommandCount",
        "acceptedCommandCount",
        "droppedCommandCount",
        "rejectedCommandCount",
        "meanTransportRttMilliseconds",
    )
    if any(not isinstance(metrics.get(field), (int, float)) for field in required_numeric):
        errors.append(prefix + "字段不完整")
        return
    configured_rtt = REQUIRED_SCENARIOS.get(stable_id, (0, 0))[0]
    if metrics["durationSeconds"] <= 0:
        errors.append(prefix + "持续时间为空")
    if metrics["hitFeedbackSampleCount"] < 20:
        errors.append(prefix + "射击反馈样本少于 20")
    observed_rtt = max(
        configured_rtt,
        metrics["meanTransportRttMilliseconds"],
    )
    retransmit_budget = configured_rtt if REQUIRED_SCENARIOS.get(
        stable_id, (0, 0))[1] > 0 else 0
    if (metrics["p95HitFeedbackMilliseconds"] >
            observed_rtt + retransmit_budget + 100):
        errors.append(prefix + "射击反馈 P95 超预算")
    if metrics["correctionsPerMinute"] > 60:
        errors.append(prefix + "校正频率超预算")
    if ((metrics["correctionCount"] >= 20 and
            metrics["p95CorrectionMagnitude"] > 0.751)
            or metrics["maximumCorrectionMagnitude"] > 2):
        errors.append(prefix + "校正幅度超预算")
    if metrics["stateComparisonCount"] <= 0:
        errors.append(prefix + "缺少状态比较")
    if (metrics["stateDivergenceRate"] > 0.02
            or metrics["maximumStateDivergenceMagnitude"] > 1.25
            or metrics["maximumStateDivergenceDurationMilliseconds"] > 500):
        errors.append(prefix + "状态分歧超预算")
    if metrics["uplinkBytes"] <= 0 or metrics["downlinkBytes"] <= 0:
        errors.append(prefix + "缺少双向流量")
    if metrics["uplinkBytesPerSecond"] > 65536 or metrics["downlinkBytesPerSecond"] > 131072:
        errors.append(prefix + "带宽超预算")
    accounted = (metrics["acceptedCommandCount"] + metrics["droppedCommandCount"]
                 + metrics["rejectedCommandCount"])
    if metrics["sentCommandCount"] <= 0 or accounted != metrics["sentCommandCount"]:
        errors.append(prefix + "命令账目不守恒")


def to_markdown(report: dict[str, Any], errors: Iterable[str]) -> str:
    failures = list(errors)
    metadata = report.get("metadata") or {}
    lines = [
        "# Issue 100 真实多进程验收报告",
        "",
        f"- 结论：**{'通过' if not failures else '失败'}**",
        "- 拓扑：真实 Dedicated Server + 两个独立 Player 进程",
        f"- Commit：`{metadata.get('commit', '')}`",
        f"- 构建哈希：`{metadata.get('buildHash', '')}`",
        "- 控制面：`local-acceptance`（本地确定性验收服务）",
        "- 数据面：`UnityTransport`（真实 Socket/NGO/UTP）",
        "",
        "## 网络场景",
        "",
        "| 场景 | RTT/丢包 | 实测 RTT | 命中 P95 | 校正/分钟 | 分歧率 | 上/下行 B/s |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for scenario in report.get("scenarios", []):
        metrics = scenario.get("metrics") or {}
        lines.append(
            f"| {scenario.get('stableId')} | "
            f"{scenario.get('roundTripLatencyMilliseconds')}ms / "
            f"{scenario.get('packetLossBasisPoints', 0) / 100:.0f}% | "
            f"{metrics.get('meanTransportRttMilliseconds', 0):.1f}ms | "
            f"{metrics.get('p95HitFeedbackMilliseconds', 0):.1f}ms | "
            f"{metrics.get('correctionsPerMinute', 0):.1f} | "
            f"{metrics.get('stateDivergenceRate', 0) * 100:.2f}% | "
            f"{metrics.get('uplinkBytesPerSecond', 0):.0f} / "
            f"{metrics.get('downlinkBytesPerSecond', 0):.0f} |"
        )
    lines.extend(["", "## 完整流程证据", "", "| 步骤 | 角色 | 场景 | Tick | 结果 |", "|---|---|---|---:|---|"])
    for item in report.get("flow", []):
        if item.get("state") != "passed":
            continue
        lines.append(
            f"| {item.get('stepId')} | {item.get('role')} | "
            f"{item.get('scenario')} | {item.get('authoritativeTick', 0)} | 通过 |"
        )
    lines.extend(["", "## 门禁失败原因", ""])
    lines.extend([f"- `{failure}`" for failure in failures] or ["- 无；全部硬门禁通过。"])
    video = (report.get("artifacts") or {}).get("videoPath")
    if video:
        lines.extend(["", "## 连续演示录像", "", f"- `{video}`"])
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=pathlib.Path)
    parser.add_argument("--require-video", action="store_true")
    parser.add_argument("--write-markdown", type=pathlib.Path)
    args = parser.parse_args()
    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"[FAIL] 无法读取 Issue100 报告：{exc}", file=sys.stderr)
        return 2
    errors = validate(report, args.require_video)
    if args.write_markdown:
        args.write_markdown.parent.mkdir(parents=True, exist_ok=True)
        args.write_markdown.write_text(to_markdown(report, errors), encoding="utf-8")
    if errors:
        for error in errors:
            print(f"[FAIL] {error}", file=sys.stderr)
        return 1
    print("[PASS] Dedicated Server + 两个独立客户端真实多进程门禁通过。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
