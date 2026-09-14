#!/usr/bin/env python3
"""Validate Issue 65 diagnostics reports without overstating fixture evidence."""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
from typing import Any


SCHEMA_VERSION = "issue65-network-diagnostics-v1"
REQUIRED_SCENARIOS = {
    "rtt-000-loss-00": (0, 0),
    "rtt-080-loss-00": (80, 0),
    "rtt-150-loss-00": (150, 0),
    "rtt-080-loss-05": (80, 500),
}


def validate(report: dict[str, Any], require_multiprocess: bool) -> list[str]:
    errors: list[str] = []
    if report.get("schemaVersion") != SCHEMA_VERSION:
        errors.append("schemaVersion 不匹配")

    metadata = report.get("metadata") or {}
    gate = report.get("gate") or {}
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
        errors.append("场景矩阵必须精确包含 0/80/150ms 与 80ms+5% 丢包")

    evidence_kind = metadata.get("evidenceKind")
    process_count = metadata.get("processCount")
    real = metadata.get("isRealMultiProcess")
    eligible = gate.get("acceptanceEligible")
    outcome = gate.get("outcome")

    if outcome == "Fail":
        errors.append("C# 硬门禁结论为 Fail")
    elif outcome == "FixtureOnly":
        if evidence_kind != "DeterministicFixture" or real is not False:
            errors.append("FixtureOnly 报告的证据元数据自相矛盾")
        if eligible is not False:
            errors.append("FixtureOnly 报告不得标记为可正式验收")
        if require_multiprocess:
            errors.append("CI 要求真实多进程证据，FixtureOnly 不满足")
    elif outcome == "Pass":
        if evidence_kind != "MultiProcessPlayer":
            errors.append("Pass 必须来自 MultiProcessPlayer 证据")
        if real is not True or not isinstance(process_count, int) or process_count < 2:
            errors.append("Pass 必须至少包含两个真实玩家进程")
        if eligible is not True:
            errors.append("Pass 报告必须标记 acceptanceEligible=true")
    else:
        errors.append("未知门禁结论")

    limitation = str(report.get("limitations", ""))
    if evidence_kind == "DeterministicFixture" and "not multi-process" not in limitation:
        errors.append("Fixture 报告必须明确声明不是多进程验收证据")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=pathlib.Path)
    parser.add_argument("--require-multiprocess", action="store_true")
    args = parser.parse_args()

    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"Issue65 report gate: 无法读取报告: {exc}", file=sys.stderr)
        return 2

    errors = validate(report, args.require_multiprocess)
    if errors:
        for error in errors:
            print(f"[FAIL] {error}", file=sys.stderr)
        return 1

    outcome = report["gate"]["outcome"]
    if outcome == "FixtureOnly":
        print("[FIXTURE ONLY] 指标管线门禁通过，但这不是正式多进程验收证据。")
    else:
        print("[PASS] 真实多进程网络诊断硬门禁通过。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
