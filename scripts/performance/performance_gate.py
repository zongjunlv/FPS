#!/usr/bin/env python3
"""Evaluate reproducible 100-enemy benchmark reports against hard gates."""

from __future__ import annotations

import argparse
import json
import statistics
import sys
from pathlib import Path
from typing import Any


DEFAULT_THRESHOLDS = {
    "minimumAverageFps": 60.0,
    "minimumOnePercentLowFps": 45.0,
    "maximumAverageGcBytesPerFrame": 8192.0,
    "maximumConsecutiveGcSpikeFrames": 2,
    "maximumNearSightResultDelayFrames": 2,
}
REQUIRED_FIELDS = (
    "enemyCount",
    "configuredResolution",
    "qualityLevel",
    "buildType",
    "averageFps",
    "onePercentLowFps",
    "p95FrameMs",
    "p99FrameMs",
    "averageGcBytesPerFrame",
    "maximumConsecutiveGcSpikeFrames",
    "stableSampleEnemyInstantiateCount",
    "stableSampleTracerInstantiateCount",
    "stableSampleEffectInstantiateCount",
    "maximumNearSightResultDelayFrames",
)


def evaluate_reports(
    reports: list[dict[str, Any]],
    thresholds: dict[str, float] | None = None,
) -> dict[str, Any]:
    limits = dict(DEFAULT_THRESHOLDS)
    limits.update(thresholds or {})
    failures: list[dict[str, Any]] = []

    if not reports:
        return {
            "status": "failed",
            "thresholds": limits,
            "failures": [{
                "rule": "reports",
                "message": "至少需要一份性能报告。",
            }],
        }

    for index, report in enumerate(reports, start=1):
        missing = [field for field in REQUIRED_FIELDS if field not in report]
        if missing:
            return {
                "status": "failed",
                "roundCount": len(reports),
                "thresholds": limits,
                "failures": [{
                    "rule": "reportSchema",
                    "round": index,
                    "message": "缺少字段：" + ", ".join(missing),
                }],
            }
        if int(report["enemyCount"]) != 100 or report[
            "configuredResolution"
        ] != "1920x1080":
            return {
                "status": "failed",
                "roundCount": len(reports),
                "thresholds": limits,
                "failures": [{
                    "rule": "benchmarkConfiguration",
                    "round": index,
                    "message": "门禁要求100敌人与1920x1080配置。",
                }],
            }

    medians = {
        field: statistics.median(float(report[field]) for report in reports)
        for field in (
            "averageFps",
            "onePercentLowFps",
            "p95FrameMs",
            "p99FrameMs",
            "averageGcBytesPerFrame",
        )
    }

    if medians["averageFps"] < limits["minimumAverageFps"]:
        failures.append({
            "rule": "minimumAverageFps",
            "actual": medians["averageFps"],
            "expected": limits["minimumAverageFps"],
        })
    if medians["onePercentLowFps"] < limits["minimumOnePercentLowFps"]:
        failures.append({
            "rule": "minimumOnePercentLowFps",
            "actual": medians["onePercentLowFps"],
            "expected": limits["minimumOnePercentLowFps"],
        })
    if medians["averageGcBytesPerFrame"] > limits[
        "maximumAverageGcBytesPerFrame"
    ]:
        failures.append({
            "rule": "maximumAverageGcBytesPerFrame",
            "actual": medians["averageGcBytesPerFrame"],
            "expected": limits["maximumAverageGcBytesPerFrame"],
        })

    invariant_fields = (
        "stableSampleEnemyInstantiateCount",
        "stableSampleTracerInstantiateCount",
        "stableSampleEffectInstantiateCount",
    )
    for index, report in enumerate(reports, start=1):
        for field in invariant_fields:
            if int(report[field]) != 0:
                failures.append({
                    "rule": field,
                    "round": index,
                    "actual": int(report[field]),
                    "expected": 0,
                })
        if int(report["maximumConsecutiveGcSpikeFrames"]) > limits[
            "maximumConsecutiveGcSpikeFrames"
        ]:
            failures.append({
                "rule": "maximumConsecutiveGcSpikeFrames",
                "round": index,
                "actual": int(report["maximumConsecutiveGcSpikeFrames"]),
                "expected": limits["maximumConsecutiveGcSpikeFrames"],
            })
        if int(report["maximumNearSightResultDelayFrames"]) > limits[
            "maximumNearSightResultDelayFrames"
        ]:
            failures.append({
                "rule": "maximumNearSightResultDelayFrames",
                "round": index,
                "actual": int(report["maximumNearSightResultDelayFrames"]),
                "expected": limits["maximumNearSightResultDelayFrames"],
            })

    return {
        "status": "passed" if not failures else "failed",
        "roundCount": len(reports),
        "thresholds": limits,
        "medians": medians,
        "failures": failures,
    }


def write_markdown(result: dict[str, Any], path: Path) -> None:
    metrics = result.get("medians", {})
    lines = [
        "# 100敌人性能回退门禁", "",
        f"总体状态：**{result['status'].upper()}**", "",
    ]
    if metrics:
        lines.extend([
            "| 指标 | 中位数 |",
            "| --- | ---: |",
            f"| 平均 FPS | {metrics['averageFps']:.2f} |",
            f"| 1% Low | {metrics['onePercentLowFps']:.2f} |",
            f"| P95 帧时间 | {metrics['p95FrameMs']:.2f} ms |",
            f"| P99 帧时间 | {metrics['p99FrameMs']:.2f} ms |",
            f"| 平均 GC/帧 | {metrics['averageGcBytesPerFrame']:.2f} B |",
            "",
        ])
    lines.extend(["## 失败项", ""])
    if result["failures"]:
        for failure in result["failures"]:
            lines.append(
                f"- `{failure['rule']}`：实际 "
                f"{failure.get('actual', '—')}，阈值 "
                f"{failure.get('expected', '—')}"
            )
    else:
        lines.append("- 无")
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
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
