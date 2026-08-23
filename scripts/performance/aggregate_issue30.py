#!/usr/bin/env python3
"""Validate and aggregate comparable Issue 30 benchmark reports."""

from __future__ import annotations

import argparse
import json
import statistics
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


CONFIGURATION_FIELDS = (
    "unityVersion",
    "buildType",
    "operatingSystem",
    "processor",
    "graphicsDevice",
    "configuredResolution",
    "resolution",
    "screenMode",
    "qualityLevel",
    "enemyCount",
    "warmupSeconds",
    "configuredSampleSeconds",
    "seed",
    "behaviorProfile",
    "perceptionChecksPerFrame",
)

METRIC_FIELDS = (
    "averageFps",
    "onePercentLowFps",
    "averageFrameMs",
    "p95FrameMs",
    "p99FrameMs",
    "averageMainThreadMs",
    "p95MainThreadMs",
    "p99MainThreadMs",
    "averageGcBytesPerFrame",
    "p95GcBytesPerFrame",
    "maximumGcBytesInFrame",
    "maximumPerceptionLatencyFrames",
    "maximumPerceptionLatencyMs",
    "enemyPoolObjects",
    "enemyPoolReuseCount",
    "enemyPoolExpansionCount",
    "stableSampleInstantiateCount",
)


def load_reports(paths: list[Path]) -> list[dict[str, Any]]:
    reports: list[dict[str, Any]] = []

    for path in paths:
        with path.open(encoding="utf-8") as stream:
            report = json.load(stream)

        if not isinstance(report, dict):
            raise ValueError(f"{path} does not contain a JSON object")

        reports.append(report)

    return reports


def validate_reports(
    reports: list[dict[str, Any]], expected_enemies: int
) -> dict[str, Any]:
    if not reports:
        raise ValueError("at least one raw report is required")

    first = reports[0]
    missing = [field for field in CONFIGURATION_FIELDS if field not in first]

    if missing:
        raise ValueError(f"first report is missing fields: {', '.join(missing)}")

    configuration = {field: first[field] for field in CONFIGURATION_FIELDS}

    if configuration["enemyCount"] != expected_enemies:
        raise ValueError(
            f"expected {expected_enemies} enemies, "
            f"got {configuration['enemyCount']}"
        )

    for index, report in enumerate(reports[1:], start=2):
        for field, expected in configuration.items():
            actual = report.get(field)

            if actual != expected:
                raise ValueError(
                    f"round {index} field {field} differs: "
                    f"expected {expected!r}, got {actual!r}"
                )

    for index, report in enumerate(reports, start=1):
        missing_metrics = [field for field in METRIC_FIELDS if field not in report]

        if missing_metrics:
            raise ValueError(
                f"round {index} is missing metrics: "
                + ", ".join(missing_metrics)
            )

        if report["stableSampleInstantiateCount"] != 0:
            raise ValueError(
                f"round {index} instantiated enemies during stable sampling"
            )

    return configuration


def aggregate(
    paths: list[Path], reports: list[dict[str, Any]], expected_enemies: int
) -> dict[str, Any]:
    configuration = validate_reports(reports, expected_enemies)
    medians = {
        field: statistics.median(float(report[field]) for report in reports)
        for field in METRIC_FIELDS
    }
    return {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "roundCount": len(reports),
        "configuration": configuration,
        "sourceFiles": [path.name for path in paths],
        "medians": medians,
    }


def write_markdown(result: dict[str, Any], path: Path) -> None:
    config = result["configuration"]
    metrics = result["medians"]
    lines = [
        "# Issue 30：100敌人优化前基线",
        "",
        f"原始轮数：**{result['roundCount']}**",
        "",
        "## 固定实验条件",
        "",
        "| 条件 | 值 |",
        "| --- | --- |",
    ]

    for field in CONFIGURATION_FIELDS:
        lines.append(f"| `{field}` | {config[field]} |")

    lines.extend(
        [
            "",
            "## 中位数结果",
            "",
            "| 指标 | 中位数 |",
            "| --- | ---: |",
        ]
    )

    for field in METRIC_FIELDS:
        lines.append(f"| `{field}` | {metrics[field]:.3f} |")

    lines.extend(["", "原始 JSON 保留在同目录的 `raw/` 中。", ""])
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reports", nargs="+", type=Path)
    parser.add_argument("--expected-enemies", type=int, default=100)
    parser.add_argument("--output-json", required=True, type=Path)
    parser.add_argument("--output-markdown", required=True, type=Path)
    args = parser.parse_args()

    reports = load_reports(args.reports)
    result = aggregate(args.reports, reports, args.expected_enemies)
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_markdown(result, args.output_markdown)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
