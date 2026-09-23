#!/usr/bin/env python3
"""Classify Unity quality-gate outcomes and produce JSON/Markdown reports."""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as element_tree
from pathlib import Path
from typing import Any


LICENSE_PATTERNS = (
    r"no valid unity editor license",
    r"license is not active",
    r"failed to (?:activate|update).*license",
    r"license.*(?:activation|entitlement).*(?:failed|error)",
    r"entitlement.*(?:failed|not found)",
)
IMPORT_PATTERNS = (
    r"shader error in",
    r"asset import failed",
    r"failed to import (?:asset|package)",
    r"import worker.*(?:crash|terminated|failed)",
    r"assetdatabase.*(?:corrupt|failure)",
)
COMPILATION_PATTERNS = (
    r"error CS\d{4}",
    r"scripts have compiler errors",
    r"script compilation failed",
    r"compilation failed",
)
CATEGORY_EXIT_CODES = {
    "passed": 0,
    "product-compilation": 10,
    "test-failure": 11,
    "environment-license": 20,
    "environment-import": 21,
    "environment-unity": 22,
    "environment-report": 23,
}


def _matches(patterns: tuple[str, ...], text: str) -> bool:
    return any(re.search(pattern, text, re.IGNORECASE) for pattern in patterns)


def _test_counts(results_path: Path) -> tuple[dict[str, int], str]:
    root = element_tree.parse(results_path).getroot()
    counts = {
        "total": int(root.attrib.get("total", 0)),
        "passed": int(root.attrib.get("passed", 0)),
        "failed": int(root.attrib.get("failed", 0)),
        "skipped": int(root.attrib.get("skipped", 0)),
        "inconclusive": int(root.attrib.get("inconclusive", 0)),
    }
    return counts, root.attrib.get("result", "Unknown")


def evaluate(
    phase: str,
    exit_code: int,
    log_path: Path,
    results_path: Path | None = None,
    marker_path: Path | None = None,
) -> dict[str, Any]:
    log_text = log_path.read_text(errors="replace") if log_path.exists() else ""
    report: dict[str, Any] = {
        "phase": phase,
        "status": "failed",
        "category": "environment-unity",
        "message": "Unity process failed for an unclassified reason.",
        "exit_code": exit_code,
        "log": str(log_path),
    }

    if results_path is not None:
        report["results"] = str(results_path)

    if _matches(LICENSE_PATTERNS, log_text):
        report.update(
            category="environment-license",
            message="Unity License or entitlement validation failed.",
        )
        return report

    if _matches(IMPORT_PATTERNS, log_text):
        report.update(
            category="environment-import",
            message="Unity asset or shader import failed.",
        )
        return report

    if _matches(COMPILATION_PATTERNS, log_text):
        report.update(
            category="product-compilation",
            message="Product scripts did not compile.",
        )
        return report

    if phase in {"editmode", "playmode", "playmode-input"}:
        if results_path is None or not results_path.exists():
            report.update(
                category="environment-report",
                message="Unity did not produce the required XML test report.",
            )
            return report

        try:
            counts, result = _test_counts(results_path)
        except (element_tree.ParseError, OSError, ValueError) as error:
            report.update(
                category="environment-report",
                message=f"Unity XML test report is invalid: {error}",
            )
            return report

        report["counts"] = counts
        report["unity_result"] = result

        if counts["failed"] > 0 or not result.startswith("Passed"):
            report.update(
                category="test-failure",
                message=(
                    f"{phase} tests failed: {counts['failed']} failed of "
                    f"{counts['total']}."
                ),
            )
            return report

    if exit_code != 0:
        return report

    if phase == "compile" and (marker_path is None or not marker_path.exists()):
        report.update(
            category="environment-unity",
            message="Unity exited without executing the compile probe.",
        )
        return report

    report.update(
        status="passed",
        category="passed",
        message=f"{phase} quality gate passed.",
    )
    return report


def _write_phase_report(report: dict[str, Any], output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    phase = report["phase"]
    (output_dir / f"{phase}.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    )
    counts = report.get("counts", {})
    count_text = (
        f"{counts.get('passed', 0)}/{counts.get('total', 0)}"
        if counts
        else "—"
    )
    (output_dir / f"{phase}.md").write_text(
        "\n".join(
            (
                f"## {phase}",
                "",
                "| 状态 | 分类 | 测试通过数 | 说明 |",
                "| --- | --- | ---: | --- |",
                f"| {report['status']} | `{report['category']}` | "
                f"{count_text} | {report['message']} |",
                "",
            )
        )
    )


def aggregate(output_dir: Path) -> dict[str, Any]:
    order = {"compile": 0, "editmode": 1, "playmode": 2,
             "playmode-input": 3}
    reports = []

    for path in output_dir.glob("*.json"):
        if path.name == "summary.json":
            continue
        reports.append(json.loads(path.read_text()))

    reports.sort(key=lambda item: order.get(item["phase"], 99))
    failed = [report for report in reports if report["status"] != "passed"]
    summary = {
        "status": "failed" if failed else "passed",
        "failed_categories": [report["category"] for report in failed],
        "phases": reports,
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n"
    )
    lines = [
        "# Unity 自动化质量门禁",
        "",
        f"总体状态：**{summary['status'].upper()}**",
        "",
        "| 阶段 | 状态 | 分类 | 通过/总数 | 说明 |",
        "| --- | --- | --- | ---: | --- |",
    ]

    for report in reports:
        counts = report.get("counts", {})
        count_text = (
            f"{counts.get('passed', 0)}/{counts.get('total', 0)}"
            if counts
            else "—"
        )
        lines.append(
            f"| {report['phase']} | {report['status']} | "
            f"`{report['category']}` | {count_text} | {report['message']} |"
        )

    (output_dir / "summary.md").write_text("\n".join(lines) + "\n")
    return summary


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    evaluate_parser = subparsers.add_parser("evaluate")
    evaluate_parser.add_argument("--phase", required=True)
    evaluate_parser.add_argument("--exit-code", required=True, type=int)
    evaluate_parser.add_argument("--log", required=True, type=Path)
    evaluate_parser.add_argument("--results", type=Path)
    evaluate_parser.add_argument("--marker", type=Path)
    evaluate_parser.add_argument("--output-dir", required=True, type=Path)
    aggregate_parser = subparsers.add_parser("aggregate")
    aggregate_parser.add_argument("--output-dir", required=True, type=Path)
    return parser


def main() -> int:
    arguments = _parser().parse_args()

    if arguments.command == "aggregate":
        summary = aggregate(arguments.output_dir)
        return 0 if summary["status"] == "passed" else 1

    report = evaluate(
        arguments.phase,
        arguments.exit_code,
        arguments.log,
        arguments.results,
        arguments.marker,
    )
    _write_phase_report(report, arguments.output_dir)
    print(f"{report['phase']}: {report['category']} - {report['message']}")
    return CATEGORY_EXIT_CODES[report["category"]]


if __name__ == "__main__":
    sys.exit(main())
