#!/usr/bin/env python3
import argparse
import json
import statistics
from pathlib import Path

METRICS = (
    "averageFps", "averageFrameMs", "p95FrameMs",
    "averageMainThreadMs", "p95MainThreadMs",
    "maximumPerceptionLatencyFrames", "stableSampleInstantiateCount",
    "aiDecisionTicks", "aiSkippedDecisionTicks",
    "maximumAiDecisionLatencyFrames",
)


def median(reports, field):
    return statistics.median(float(report[field]) for report in reports)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("reports", nargs="+", type=Path)
    parser.add_argument("--output-json", required=True, type=Path)
    parser.add_argument("--output-markdown", required=True, type=Path)
    args = parser.parse_args()
    groups = {False: [], True: []}

    for path in args.reports:
        report = json.loads(path.read_text(encoding="utf-8"))
        if report["enemyCount"] != 100:
            raise ValueError(f"{path}: expected 100 enemies")
        if report["stableSampleInstantiateCount"] != 0:
            raise ValueError(f"{path}: instantiated during sampling")
        groups[bool(report["aiLodEnabled"])].append(report)

    if not groups[False] or not groups[True]:
        raise ValueError("both disabled and enabled reports are required")

    result = {
        "schemaVersion": 1,
        "disabled": {field: median(groups[False], field) for field in METRICS},
        "enabled": {field: median(groups[True], field) for field in METRICS},
    }
    result["changePercent"] = {
        field: ((result["enabled"][field] - result["disabled"][field]) /
                result["disabled"][field] * 100.0)
        if result["disabled"][field] else 0.0
        for field in METRICS
    }
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8")
    lines = [
        "# Issue 42：AI LOD A/B 性能对比", "",
        "| 指标 | 关闭 LOD | 开启 LOD | 变化 |",
        "| --- | ---: | ---: | ---: |",
    ]
    for field in METRICS:
        lines.append(
            f"| `{field}` | {result['disabled'][field]:.3f} | "
            f"{result['enabled'][field]:.3f} | "
            f"{result['changePercent'][field]:+.2f}% |")
    args.output_markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
