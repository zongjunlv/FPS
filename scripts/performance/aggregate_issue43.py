#!/usr/bin/env python3
import argparse
import json
import statistics
from pathlib import Path

METRICS = (
    "averageFps", "averageFrameMs", "p95FrameMs",
    "averageMainThreadMs", "p95MainThreadMs",
    "averageGcBytesPerFrame", "neighborQueryCount",
    "neighborCandidateVisits", "neighborQueryMilliseconds",
    "averageNeighborQueryMicroseconds", "spatialCellMoveCount",
    "spatialPeakRegisteredCount", "stableSampleInstantiateCount",
)
CONFIG_FIELDS = (
    "unityVersion", "operatingSystem", "processor", "processorCount",
    "systemMemoryMb", "graphicsDevice", "graphicsMemoryMb",
    "configuredResolution", "screenMode", "qualityLevel", "buildType",
    "behaviorProfile", "aiLodEnabled", "perceptionChecksPerFrame",
    "enemyCount", "warmupSeconds", "configuredSampleSeconds", "seed",
)


def median(reports, field):
    return statistics.median(float(report[field]) for report in reports)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("reports", nargs="+", type=Path)
    parser.add_argument("--output-json", required=True, type=Path)
    parser.add_argument("--output-markdown", required=True, type=Path)
    parser.add_argument("--expected-rounds", type=int, default=4)
    args = parser.parse_args()
    groups = {False: [], True: []}
    baseline_config = None

    for path in args.reports:
        report = json.loads(path.read_text(encoding="utf-8"))
        if report["enemyCount"] != 100:
            raise ValueError(f"{path}: expected 100 enemies")
        if report["stableSampleInstantiateCount"] != 0:
            raise ValueError(f"{path}: instantiated during sampling")
        config = {field: report[field] for field in CONFIG_FIELDS}
        if baseline_config is None:
            baseline_config = config
        elif config != baseline_config:
            mismatches = [
                field for field in CONFIG_FIELDS
                if config[field] != baseline_config[field]
            ]
            raise ValueError(
                f"{path}: benchmark configuration mismatch: {mismatches}")
        groups[bool(report["spatialIndexEnabled"])].append(report)

    if not groups[False] or not groups[True]:
        raise ValueError("both disabled and enabled reports are required")
    for enabled, reports in groups.items():
        if len(reports) != args.expected_rounds:
            label = "enabled" if enabled else "disabled"
            raise ValueError(
                f"{label}: expected {args.expected_rounds} reports, "
                f"found {len(reports)}")

    result = {
        "schemaVersion": 1,
        "roundsPerGroup": args.expected_rounds,
        "configuration": baseline_config,
        "disabled": {field: median(groups[False], field) for field in METRICS},
        "enabled": {field: median(groups[True], field) for field in METRICS},
    }
    result["changePercent"] = {
        field: ((result["enabled"][field] - result["disabled"][field]) /
                result["disabled"][field] * 100.0)
        if result["disabled"][field] else 0.0
        for field in METRICS
    }
    ordered_fps = [
        float(json.loads(path.read_text(encoding="utf-8"))["averageFps"])
        for path in args.reports
    ]
    result["thermalDrift"] = {
        "firstFps": ordered_fps[0],
        "lastFps": ordered_fps[-1],
        "maxToMinRatio": max(ordered_fps) / min(ordered_fps),
        "wholeFrameMetricsReliable": max(ordered_fps) / min(ordered_fps) <= 1.25,
    }
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8")
    lines = [
        "# Issue 43：空间索引 A/B 性能对比", "",
        f"每组 `{args.expected_rounds}` 轮；采用 ABBA 对称顺序并在轮次间冷却。", "",
        "| 指标 | 朴素扫描 | 空间哈希 | 变化 |",
        "| --- | ---: | ---: | ---: |",
    ]
    for field in METRICS:
        lines.append(
            f"| `{field}` | {result['disabled'][field]:.3f} | "
            f"{result['enabled'][field]:.3f} | "
            f"{result['changePercent'][field]:+.2f}% |")
    if not result["thermalDrift"]["wholeFrameMetricsReliable"]:
        lines.extend([
            "",
            "> 本轮设备存在显著热衰减；FPS、帧时间和主线程指标仅作原始记录，",
            "> 不用于归因空间索引收益。验收以同缓存查询正确性、零分配和单次查询耗时为准。",
        ])
    args.output_markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
