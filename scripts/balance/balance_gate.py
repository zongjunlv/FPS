#!/usr/bin/env python3
"""Evaluate deterministic offline encounter reports against balance gates."""

from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Iterable


DEFAULT_THRESHOLDS: dict[str, float] = {
    "minimumSeedCount": 1000,
    "minimumVictoryRate": 0.80,
    "maximumResourceExhaustionRate": 0.10,
    "maximumDurationP99Ticks": 18000,
    "maximumTtkP95Ticks": 1800,
    "maximumVictoryRateDrop": 0.03,
    "maximumResourceExhaustionIncrease": 0.02,
    "maximumDurationP99Multiplier": 1.20,
    "maximumTtkP95Multiplier": 1.15,
    "maximumDistributionJensenShannon": 0.05,
    "maximumCategoryShareShift": 0.05,
}


def percentile(values: Iterable[float], quantile: float) -> float:
    ordered = sorted(float(value) for value in values)
    if not ordered:
        return 0.0
    position = max(0.0, min(1.0, quantile)) * (len(ordered) - 1)
    lower = int(math.floor(position))
    upper = int(math.ceil(position))
    if lower == upper:
        return ordered[lower]
    weight = position - lower
    return ordered[lower] * (1.0 - weight) + ordered[upper] * weight


def _counter(value: Any) -> Counter[str]:
    if isinstance(value, dict):
        return Counter({str(key): int(count) for key, count in value.items()})
    result: Counter[str] = Counter()
    if isinstance(value, list):
        for item in value:
            if isinstance(item, dict):
                key = item.get("key", item.get("id", item.get("name", "")))
                result[str(key)] += int(item.get("count", 0))
            elif item is not None:
                result[str(item)] += 1
    return result


def _shares(counter: Counter[str]) -> dict[str, float]:
    total = sum(max(0, count) for count in counter.values())
    if total <= 0:
        return {}
    return {key: max(0, count) / total for key, count in counter.items()}


def jensen_shannon(left: Counter[str], right: Counter[str]) -> tuple[float, float]:
    p = _shares(left)
    q = _shares(right)
    keys = set(p) | set(q)
    if not keys:
        return 0.0, 0.0
    divergence = 0.0
    maximum_shift = 0.0
    for key in keys:
        a = p.get(key, 0.0)
        b = q.get(key, 0.0)
        maximum_shift = max(maximum_shift, abs(a - b))
        midpoint = (a + b) * 0.5
        if a > 0.0:
            divergence += 0.5 * a * math.log(a / midpoint, 2)
        if b > 0.0:
            divergence += 0.5 * b * math.log(b / midpoint, 2)
    return divergence, maximum_shift


def summarize(report: dict[str, Any]) -> dict[str, Any]:
    results = report.get("results") or []
    victories = [item for item in results if item.get("outcome") == "Victory"]
    exhausted = [
        item for item in results
        if item.get("failureReason") == "ResourceExhausted"
        or "ResourceExhausted" in (item.get("anomalyCodes") or [])
    ]
    ttks = [
        float(value)
        for item in results
        for value in (item.get("enemyTtkTicks") or [])
    ]
    roles: Counter[str] = Counter()
    encounters: Counter[str] = Counter()
    upgrades: Counter[str] = Counter()
    failures: Counter[str] = Counter()
    for item in results:
        roles.update(_counter(item.get("roleCounts")))
        encounters.update(_counter(item.get("encounterCounts")))
        upgrades.update(str(value) for value in item.get("upgradeSelections") or [])
        failures[str(item.get("failureReason") or "None")] += 1
    count = len(results)
    return {
        "seedCount": count,
        "victoryRate": len(victories) / count if count else 0.0,
        "resourceExhaustionRate": len(exhausted) / count if count else 0.0,
        "durationP50Ticks": percentile(
            (item.get("durationTicks", 0) for item in results), 0.50),
        "durationP95Ticks": percentile(
            (item.get("durationTicks", 0) for item in results), 0.95),
        "durationP99Ticks": percentile(
            (item.get("durationTicks", 0) for item in results), 0.99),
        "ttkP50Ticks": percentile(ttks, 0.50),
        "ttkP95Ticks": percentile(ttks, 0.95),
        "meanHealthDamage": statistics.fmean(
            float(item.get("healthDamage", 0)) for item in results) if count else 0.0,
        "meanArmorDamage": statistics.fmean(
            float(item.get("armorDamage", 0)) for item in results) if count else 0.0,
        "meanAmmoSpent": statistics.fmean(
            float(item.get("ammoSpent", 0)) for item in results) if count else 0.0,
        "roleDistribution": dict(sorted(roles.items())),
        "encounterDistribution": dict(sorted(encounters.items())),
        "upgradeDistribution": dict(sorted(upgrades.items())),
        "failureDistribution": dict(sorted(failures.items())),
    }


def evaluate_report(
    report: dict[str, Any],
    thresholds: dict[str, float] | None = None,
    baseline: dict[str, Any] | None = None,
) -> dict[str, Any]:
    limits = dict(DEFAULT_THRESHOLDS)
    limits.update(thresholds or {})
    failures: list[dict[str, Any]] = []
    required = (
        "schemaVersion", "rulesVersion", "policyVersion", "contentVersion",
        "contentFingerprint", "environment", "results",
    )
    missing = [field for field in required if field not in report]
    if missing:
        failures.append({"rule": "schema", "message": "缺少字段：" + ", ".join(missing)})
        return {"status": "failed", "thresholds": limits, "failures": failures}

    results = report.get("results") or []
    seeds = [item.get("seed") for item in results]
    invalid_numbers = any(
        not math.isfinite(float(item.get(field, 0)))
        for item in results
        for field in ("durationTicks", "healthDamage", "armorDamage", "ammoSpent")
    )
    if len(results) < int(limits["minimumSeedCount"]):
        failures.append({
            "rule": "minimumSeedCount", "actual": len(results),
            "expected": int(limits["minimumSeedCount"]),
        })
    if len(set(seeds)) != len(seeds) or None in seeds:
        failures.append({"rule": "uniqueSeeds", "message": "Seed 缺失或重复。"})
    if invalid_numbers:
        failures.append({"rule": "finiteMetrics", "message": "报告包含 NaN 或 Infinity。"})
    if any(not item.get("deterministicDigest") for item in results):
        failures.append({"rule": "deterministicDigest", "message": "存在缺少确定性摘要的 Seed。"})
    hard_failures = [
        item for item in results
        if item.get("failureReason") in {
            "UnsolvableRoster", "Stalled", "MaximumTicks", "TickLimitExceeded"
        }
    ]
    if hard_failures:
        failures.append({
            "rule": "hardSimulationFailure", "actual": len(hard_failures), "expected": 0,
        })

    summary = summarize(report)
    absolute_checks = (
        ("minimumVictoryRate", summary["victoryRate"], limits["minimumVictoryRate"], lambda a, b: a < b),
        ("maximumResourceExhaustionRate", summary["resourceExhaustionRate"], limits["maximumResourceExhaustionRate"], lambda a, b: a > b),
        ("maximumDurationP99Ticks", summary["durationP99Ticks"], limits["maximumDurationP99Ticks"], lambda a, b: a > b),
        ("maximumTtkP95Ticks", summary["ttkP95Ticks"], limits["maximumTtkP95Ticks"], lambda a, b: a > b),
    )
    for rule, actual, expected, failed in absolute_checks:
        if failed(actual, expected):
            failures.append({"rule": rule, "actual": actual, "expected": expected})

    baseline_summary = None
    distribution_comparisons: dict[str, Any] = {}
    if baseline:
        baseline_summary = baseline.get("summary") or summarize(baseline)
        if summary["victoryRate"] < baseline_summary["victoryRate"] - limits["maximumVictoryRateDrop"]:
            failures.append({"rule": "victoryRateRegression", "actual": summary["victoryRate"], "expected": baseline_summary["victoryRate"]})
        if summary["resourceExhaustionRate"] > baseline_summary["resourceExhaustionRate"] + limits["maximumResourceExhaustionIncrease"]:
            failures.append({"rule": "resourceExhaustionRegression", "actual": summary["resourceExhaustionRate"], "expected": baseline_summary["resourceExhaustionRate"]})
        if summary["durationP99Ticks"] > max(
            baseline_summary["durationP99Ticks"] * limits["maximumDurationP99Multiplier"],
            baseline_summary["durationP99Ticks"] + 300,
        ):
            failures.append({"rule": "durationP99Regression", "actual": summary["durationP99Ticks"], "expected": baseline_summary["durationP99Ticks"]})
        if summary["ttkP95Ticks"] > max(
            baseline_summary["ttkP95Ticks"] * limits["maximumTtkP95Multiplier"],
            baseline_summary["ttkP95Ticks"] + 60,
        ):
            failures.append({"rule": "ttkP95Regression", "actual": summary["ttkP95Ticks"], "expected": baseline_summary["ttkP95Ticks"]})
        for field in ("roleDistribution", "encounterDistribution", "upgradeDistribution"):
            divergence, shift = jensen_shannon(
                _counter(summary[field]), _counter(baseline_summary[field]))
            distribution_comparisons[field] = {
                "jensenShannon": divergence, "maximumShareShift": shift,
            }
            if divergence > limits["maximumDistributionJensenShannon"] and shift > limits["maximumCategoryShareShift"]:
                failures.append({"rule": field + "Regression", "actual": divergence, "expected": limits["maximumDistributionJensenShannon"]})

    return {
        "status": "passed" if not failures else "failed",
        "thresholds": limits,
        "summary": summary,
        "baselineSummary": baseline_summary,
        "distributionComparisons": distribution_comparisons,
        "failures": failures,
    }


def write_markdown(result: dict[str, Any], report: dict[str, Any], path: Path) -> None:
    summary = result.get("summary") or {}
    lines = [
        "# Issue 63 离线遭遇平衡门禁", "",
        f"总体状态：**{result['status'].upper()}**", "",
        f"规则版本：`{report.get('rulesVersion', '—')}`  ",
        f"策略版本：`{report.get('policyVersion', '—')}`  ",
        f"内容指纹：`{report.get('contentFingerprint', '—')}`", "",
        "| 指标 | 结果 |", "| --- | ---: |",
        f"| Seed 数 | {summary.get('seedCount', 0)} |",
        f"| 通关率 | {summary.get('victoryRate', 0):.2%} |",
        f"| 资源枯竭率 | {summary.get('resourceExhaustionRate', 0):.2%} |",
        f"| 战斗时长 P99 | {summary.get('durationP99Ticks', 0):.1f} Tick |",
        f"| TTK P95 | {summary.get('ttkP95Ticks', 0):.1f} Tick |", "",
        "## 失败项", "",
    ]
    if result["failures"]:
        for failure in result["failures"]:
            lines.append(
                f"- `{failure['rule']}`：实际 {failure.get('actual', failure.get('message', '—'))}，阈值 {failure.get('expected', '—')}"
            )
    else:
        lines.append("- 无")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("--thresholds", type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output-json", required=True, type=Path)
    parser.add_argument("--output-markdown", required=True, type=Path)
    args = parser.parse_args()
    report = json.loads(args.report.read_text(encoding="utf-8"))
    thresholds = json.loads(args.thresholds.read_text(encoding="utf-8")) if args.thresholds else None
    baseline = json.loads(args.baseline.read_text(encoding="utf-8")) if args.baseline and args.baseline.exists() else None
    result = evaluate_report(report, thresholds, baseline)
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_markdown(result, report, args.output_markdown)
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
