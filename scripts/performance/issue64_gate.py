#!/usr/bin/env python3
"""Validate and aggregate Issue 64 GO/ECS paired benchmark reports."""

from __future__ import annotations

import argparse
import json
import statistics
import sys
from pathlib import Path
from typing import Any


REQUIRED_COUNTS = (100, 300, 500)
REQUIRED_METRICS = (
    "averageFrameMilliseconds",
    "p95FrameMilliseconds",
    "p99FrameMilliseconds",
    "averageMainThreadMilliseconds",
    "p95MainThreadMilliseconds",
    "p99MainThreadMilliseconds",
    "averageGcBytesPerFrame",
    "peakGcBytesPerFrame",
    "averageMemoryBytes",
    "peakMemoryBytes",
    "averageDecisionLatencyMilliseconds",
    "p95DecisionLatencyMilliseconds",
    "p99DecisionLatencyMilliseconds",
)
DEFAULT_THRESHOLDS = {
    "maximumLowDensityRegressionRatio": 1.05,
    "maximumP99RegressionRatio": 1.05,
    "maximumGcRatio": 1.00,
    "maximumMemoryRatio": 1.20,
    "requiredHighDensityFrameRatio": 0.85,
    "requiredHighDensityMainThreadRatio": 0.85,
    "requiredHighDensityDecisionRatio": 0.75,
}


def evaluate_reports(
    reports: list[dict[str, Any]],
    thresholds: dict[str, float] | None = None,
) -> dict[str, Any]:
    limits = dict(DEFAULT_THRESHOLDS)
    limits.update(thresholds or {})
    reasons: list[str] = []

    if not reports:
        return _result("Stop", ["至少需要一份 A/B 报告。"], limits, [])

    environment = reports[0].get("environment", {})
    comparison_key = environment.get("comparisonKey", "")
    if not comparison_key:
        reasons.append("报告缺少环境 comparisonKey。")

    expected_rounds = int(environment.get("runsPerCase", 0) or 0)
    if expected_rounds != len(reports):
        reasons.append(
            f"固定条件要求每档 {expected_rounds} 轮，实际收到 {len(reports)} 轮。"
        )

    by_count: dict[int, dict[str, list[dict[str, Any]]]] = {
        count: {"GameObject": [], "Ecs": []}
        for count in REQUIRED_COUNTS
    }

    for round_index, report in enumerate(reports):
        if report.get("schemaVersion") != "issue64-hybrid-ai-performance-v1":
            reasons.append(f"第 {round_index + 1} 轮报告 schemaVersion 不支持。")
        current_environment = report.get("environment", {})
        if current_environment.get("comparisonKey", "") != comparison_key:
            reasons.append(f"第 {round_index + 1} 轮硬件/画质/采样条件不一致。")

        comparisons = report.get("comparisons", [])
        counts = sorted(item.get("enemyCount") for item in comparisons)
        if counts != list(REQUIRED_COUNTS):
            reasons.append(
                f"第 {round_index + 1} 轮必须包含且仅包含 100、300、500 敌人。"
            )
            continue

        for comparison in comparisons:
            count = int(comparison["enemyCount"])
            go = comparison.get("gameObject", {})
            ecs = comparison.get("ecs", {})
            _validate_pair(count, go, ecs, comparison, round_index, reasons)
            if _has_metrics(go) and _has_metrics(ecs):
                by_count[count]["GameObject"].append(go)
                by_count[count]["Ecs"].append(ecs)

    aggregate: list[dict[str, Any]] = []
    for count in REQUIRED_COUNTS:
        go_runs = by_count[count]["GameObject"]
        ecs_runs = by_count[count]["Ecs"]
        if len(go_runs) != len(reports) or len(ecs_runs) != len(reports):
            reasons.append(f"{count} 敌人：缺少完整 GO/ECS 配对数据。")
            continue

        go_metrics = _median_metrics(go_runs)
        ecs_metrics = _median_metrics(ecs_runs)
        ratios = {
            "averageFrame": _ratio(
                ecs_metrics["averageFrameMilliseconds"],
                go_metrics["averageFrameMilliseconds"],
            ),
            "p99Frame": _ratio(
                ecs_metrics["p99FrameMilliseconds"],
                go_metrics["p99FrameMilliseconds"],
            ),
            "averageMainThread": _ratio(
                ecs_metrics["averageMainThreadMilliseconds"],
                go_metrics["averageMainThreadMilliseconds"],
            ),
            "averageGc": _ratio(
                ecs_metrics["averageGcBytesPerFrame"],
                go_metrics["averageGcBytesPerFrame"],
                zero_over_zero=1.0,
            ),
            "peakMemory": _ratio(
                ecs_metrics["peakMemoryBytes"],
                go_metrics["peakMemoryBytes"],
            ),
            "p95Decision": _ratio(
                ecs_metrics["p95DecisionLatencyMilliseconds"],
                go_metrics["p95DecisionLatencyMilliseconds"],
            ),
        }
        aggregate.append({
            "enemyCount": count,
            "gameObject": go_metrics,
            "ecs": ecs_metrics,
            "ecsToGoRatios": ratios,
        })
        _apply_thresholds(count, ratios, limits, reasons)

    return _result(
        "Continue" if not reasons else "Stop",
        reasons,
        limits,
        aggregate,
        environment,
    )


def _validate_pair(
    count: int,
    go: dict[str, Any],
    ecs: dict[str, Any],
    comparison: dict[str, Any],
    round_index: int,
    reasons: list[str],
) -> None:
    label = f"第 {round_index + 1} 轮 / {count} 敌人"
    if go.get("mode") != "GameObject" or ecs.get("mode") != "Ecs":
        reasons.append(f"{label}：模式标签错误。")
    for run in (go, ecs):
        if int(run.get("enemyCount", -1)) != count:
            reasons.append(f"{label}：运行结果敌人数不匹配。")
        missing = [field for field in REQUIRED_METRICS
                   if field not in run.get("metrics", {})]
        if missing:
            reasons.append(f"{label}：缺少指标 {', '.join(missing)}。")
    if any(go.get(field) != ecs.get(field)
           for field in ("seed", "runIndex", "sampleCount")):
        reasons.append(f"{label}：GO/ECS 种子、轮次或采样数不一致。")
    if int(go.get("activeCount", -1)) != count or int(
        ecs.get("activeCount", -1)
    ) != count:
        reasons.append(f"{label}：活跃数量未达到配置值。")
    if int(go.get("ghostCount", -1)) != 0 or int(
        ecs.get("ghostCount", -1)
    ) != 0:
        reasons.append(f"{label}：池回收后出现幽灵对象/实体。")
    if not comparison.get("intentDigestMatches", False) or go.get(
        "intentDigest", ""
    ) != ecs.get("intentDigest", ""):
        reasons.append(f"{label}：GO/ECS 命令意图摘要不一致。")


def _has_metrics(run: dict[str, Any]) -> bool:
    metrics = run.get("metrics", {})
    return all(field in metrics for field in REQUIRED_METRICS)


def _median_metrics(runs: list[dict[str, Any]]) -> dict[str, float]:
    return {
        field: statistics.median(float(run["metrics"][field]) for run in runs)
        for field in REQUIRED_METRICS
    }


def _apply_thresholds(
    count: int,
    ratios: dict[str, float],
    limits: dict[str, float],
    reasons: list[str],
) -> None:
    if ratios["p99Frame"] > limits["maximumP99RegressionRatio"]:
        reasons.append(f"{count} 敌人：ECS P99 帧时间回退超过 5%。")
    if ratios["averageGc"] > limits["maximumGcRatio"]:
        reasons.append(f"{count} 敌人：ECS 平均 GC/帧高于 GO。")
    if ratios["peakMemory"] > limits["maximumMemoryRatio"]:
        reasons.append(f"{count} 敌人：ECS 峰值内存增幅超过 20%。")
    if count == 100:
        if ratios["averageFrame"] > limits[
            "maximumLowDensityRegressionRatio"
        ]:
            reasons.append("100 敌人：ECS 平均帧时间回退超过 5%。")
        if ratios["averageMainThread"] > limits[
            "maximumLowDensityRegressionRatio"
        ]:
            reasons.append("100 敌人：ECS 主线程回退超过 5%。")
        return
    if ratios["averageFrame"] > limits["requiredHighDensityFrameRatio"]:
        reasons.append(f"{count} 敌人：ECS 平均帧时间降幅不足 15%。")
    if ratios["averageMainThread"] > limits[
        "requiredHighDensityMainThreadRatio"
    ]:
        reasons.append(f"{count} 敌人：ECS 主线程降幅不足 15%。")
    if ratios["p95Decision"] > limits[
        "requiredHighDensityDecisionRatio"
    ]:
        reasons.append(f"{count} 敌人：ECS 决策 P95 延迟降幅不足 25%。")


def _ratio(
    numerator: float,
    denominator: float,
    zero_over_zero: float = float("inf"),
) -> float:
    if denominator <= 0.0:
        return zero_over_zero if numerator <= 0.0 else float("inf")
    return numerator / denominator


def _result(
    outcome: str,
    reasons: list[str],
    thresholds: dict[str, float],
    comparisons: list[dict[str, Any]],
    environment: dict[str, Any] | None = None,
) -> dict[str, Any]:
    return {
        "schemaVersion": "issue64-hybrid-ai-gate-v1",
        "outcome": outcome,
        "environment": environment or {},
        "thresholds": thresholds,
        "comparisons": comparisons,
        "reasons": reasons,
    }


def write_markdown(result: dict[str, Any], path: Path) -> None:
    environment = result.get("environment", {})
    lines = [
        "# Issue 64：Hybrid ECS 群体 AI A/B 门禁", "",
        f"结论：**{result['outcome'].upper()}**", "",
        "## 环境", "",
        f"- Unity / Entities：{environment.get('unityVersion', '缺失')} / "
        f"{environment.get('entitiesVersion', '缺失')}",
        f"- CPU / GPU：{environment.get('processor', '缺失')} / "
        f"{environment.get('graphicsDevice', '缺失')}",
        f"- 画质 / 分辨率：{environment.get('qualityLevel', '缺失')} / "
        f"{environment.get('width', '?')}×{environment.get('height', '?')}",
        "", "## 中位数结果", "",
        "| 敌人 | GO平均帧 | ECS平均帧 | ECS变化 | GO主线程 | ECS主线程 | "
        "GO决策P95 | ECS决策P95 |",
        "| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]
    for item in result.get("comparisons", []):
        go = item["gameObject"]
        ecs = item["ecs"]
        ratio = item["ecsToGoRatios"]["averageFrame"]
        lines.append(
            f"| {item['enemyCount']} | {go['averageFrameMilliseconds']:.2f} ms | "
            f"{ecs['averageFrameMilliseconds']:.2f} ms | {(ratio - 1) * 100:+.1f}% | "
            f"{go['averageMainThreadMilliseconds']:.2f} ms | "
            f"{ecs['averageMainThreadMilliseconds']:.2f} ms | "
            f"{go['p95DecisionLatencyMilliseconds']:.2f} ms | "
            f"{ecs['p95DecisionLatencyMilliseconds']:.2f} ms |"
        )
    lines.extend(["", "## 门禁原因", ""])
    lines.extend(
        [f"- {reason}" for reason in result["reasons"]]
        if result["reasons"] else ["- 全部硬门禁通过。"]
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reports", nargs="+", type=Path)
    parser.add_argument("--output-json", required=True, type=Path)
    parser.add_argument("--output-markdown", required=True, type=Path)
    args = parser.parse_args()
    reports = [
        json.loads(path.read_text(encoding="utf-8"))
        for path in args.reports
    ]
    result = evaluate_reports(reports)
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_markdown(result, args.output_markdown)
    return 0 if result["outcome"] == "Continue" else 1


if __name__ == "__main__":
    sys.exit(main())
