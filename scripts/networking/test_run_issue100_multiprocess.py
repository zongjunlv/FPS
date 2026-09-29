"""Replay process evidence through the real runner report/gate without Unity."""

from __future__ import annotations

import contextlib
import io
import json
import pathlib
import sys
import tempfile
import unittest
from unittest import mock

import run_issue100_multiprocess as runner
from issue100_report_gate import REQUIRED_STEPS


class Issue100DiagnosticReportTests(unittest.TestCase):
    def test_single_scenario_red_network_budget_is_reported_without_victory_expectations(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            record = self.scenario(root, "rtt-080-loss-00", 80, 0)
            # Captured diagnostic-80 result: twenty divergent comparisons of
            # 453, even though all three pressure-test processes exited well.
            record["metrics"]["stateComparisonCount"] = 453
            record["metrics"]["stateDivergenceCount"] = 20
            record["metrics"]["stateDivergenceRate"] = 20 / 453
            result, report, markdown, stdout, stderr = self.run_report(
                root, [record], scenario="rtt-080-loss-00")

            self.assertEqual(1, result,
                             "Process completion must not hide the existing 2% state-divergence budget.")
            self.assertEqual("DiagnosticFail", report["gate"]["outcome"])
            self.assertFalse(report["gate"]["acceptanceEligible"])
            self.assertFalse(report["gate"]["fullMatrixEvaluated"])
            self.assertTrue(any("状态分歧超预算" in reason for reason in report["gate"]["reasons"]))
            self.assertFalse(any("流程缺少通过证据" in reason or "必须精确包含" in reason
                                 for reason in report["gate"]["reasons"]),
                             "A single pressure diagnosis does not require full victory or all four scenarios.")
            self.assertIn("未执行完整四档门禁", markdown)
            self.assertIn("状态分歧超预算", markdown)
            self.assertNotIn("结论：**通过**", markdown)
            self.assertNotIn("全部硬门禁通过", markdown)
            self.assertNotIn("[PASS]", stdout)
            self.assertIn("状态分歧超预算", stderr)

    def test_single_scenario_within_budget_is_completed_but_acceptance_is_incomplete(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            record = self.scenario(root, "rtt-080-loss-00", 80, 0)
            result, report, markdown, stdout, stderr = self.run_report(
                root, [record], scenario="rtt-080-loss-00")

            self.assertEqual(0, result)
            self.assertEqual("DiagnosticIncomplete", report["gate"]["outcome"])
            self.assertFalse(report["gate"]["acceptanceEligible"])
            self.assertFalse(report["gate"]["fullMatrixEvaluated"])
            self.assertIn("单场景执行完成", markdown)
            self.assertIn("Incomplete", markdown)
            self.assertIn("未执行完整四档门禁", markdown)
            self.assertNotIn("结论：**通过**", markdown)
            self.assertNotIn("全部硬门禁通过", markdown)
            self.assertIn("[DIAGNOSTIC]", stdout)
            self.assertNotIn("[PASS]", stdout)
            self.assertEqual("", stderr)

    def test_default_full_matrix_retains_original_acceptance_success(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            records = [self.scenario(root, stable_id, rtt, loss)
                       for stable_id, rtt, loss in runner.SCENARIOS]
            result, report, markdown, stdout, stderr = self.run_report(
                root, records, flow=self.full_flow())

            self.assertEqual(0, result)
            self.assertEqual("Pass", report["gate"]["outcome"])
            self.assertTrue(report["gate"]["acceptanceEligible"])
            self.assertTrue(report["gate"]["fullMatrixEvaluated"])
            self.assertEqual([], report["gate"]["reasons"])
            self.assertIn("结论：**通过**", markdown)
            self.assertIn("全部硬门禁通过", markdown)
            self.assertIn("[PASS] Issue100 真实三进程验收通过", stdout)
            self.assertEqual("", stderr)

    def test_default_full_matrix_still_requires_victory_flow(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            records = [self.scenario(root, stable_id, rtt, loss)
                       for stable_id, rtt, loss in runner.SCENARIOS]
            result, report, markdown, _, _ = self.run_report(root, records)

            self.assertEqual(1, result)
            self.assertEqual("Fail", report["gate"]["outcome"])
            self.assertFalse(report["gate"]["acceptanceEligible"])
            self.assertTrue(report["gate"]["fullMatrixEvaluated"])
            self.assertIn("流程缺少通过证据：match.settlement", report["gate"]["reasons"])
            self.assertIn("结论：**失败**", markdown)

    def test_release_like_unapplied_delay_cannot_claim_pressure_matrix_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            records = [self.scenario(root, stable_id, rtt, loss)
                       for stable_id, rtt, loss in runner.SCENARIOS]
            # The full Release player completed real gameplay, but its
            # simulator was disabled: all nominal pressure runs stayed local.
            for record in records:
                record["metrics"]["meanTransportRttMilliseconds"] = 15.0
                record["metrics"]["p95HitFeedbackMilliseconds"] = 19.0
            result, report, markdown, stdout, _ = self.run_report(
                root, records, flow=self.full_flow())

            self.assertEqual(1, result)
            self.assertEqual("Fail", report["gate"]["outcome"])
            self.assertFalse(report["gate"]["acceptanceEligible"])
            self.assertEqual(3, sum("压力延迟证据无效" in reason
                                    for reason in report["gate"]["reasons"]))
            self.assertNotIn("[PASS]", stdout)
            self.assertNotIn("全部硬门禁通过", markdown)

    def test_normal_only_full_release_still_has_valid_local_diagnostic(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            record = self.scenario(root, "rtt-000-loss-00", 0, 0)
            record["metrics"]["meanTransportRttMilliseconds"] = 15.0
            result, report, _, _, _ = self.run_report(
                root, [record], scenario="rtt-000-loss-00", flow=self.full_flow())

            self.assertEqual(0, result)
            self.assertEqual("DiagnosticIncomplete", report["gate"]["outcome"])
            self.assertFalse(report["gate"]["acceptanceEligible"])

    def test_single_scenario_does_not_hide_a_recorded_failed_pressure_step(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            record = self.scenario(root, "rtt-080-loss-00", 80, 0)
            flow = [{"stepId": "combat.reload", "role": "client-a",
                     "scenario": "rtt-080-loss-00", "state": "failed"}]
            result, report, _, _, _ = self.run_report(
                root, [record], scenario="rtt-080-loss-00", flow=flow)

            self.assertEqual(1, result)
            self.assertEqual("DiagnosticFail", report["gate"]["outcome"])
            self.assertIn("流程步骤失败：combat.reload", report["gate"]["reasons"])

    def full_flow(self):
        return [{"stepId": step, "role": "client-a", "scenario": "rtt-000-loss-00",
                 "state": "passed", "authoritativeTick": index + 1}
                for index, step in enumerate(sorted(REQUIRED_STEPS))]

    def run_report(self, root, records, scenario=None, flow=None):
        manifest = root / "build-manifest.json"
        manifest.write_text(json.dumps({"serverOutput": sys.executable,
                                        "clientOutput": sys.executable}), encoding="utf-8")
        output = root / "output"
        arguments = ["run-issue100", "--project", str(root),
                     "--build-manifest", str(manifest), "--output", str(output)]
        if scenario:
            arguments.extend(["--scenario", scenario])
        if flow is None:
            flow = [{"stepId": step, "role": "client-a",
                     "scenario": records[0]["stableId"], "state": "passed",
                     "authoritativeTick": index + 1}
                    for index, step in enumerate(("movement.walk", "combat.fire", "combat.reload"))]
        # This seam replays the result of external Unity process execution;
        # validation, report serialization and Markdown remain real code.
        replay = [(record, flow if index == 0 else [], True)
                  for index, record in enumerate(records)]
        stdout = io.StringIO()
        stderr = io.StringIO()
        with mock.patch.object(sys, "argv", arguments), \
                mock.patch.object(runner, "run_scenario", side_effect=replay), \
                contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            result = runner.main()
        return (result, json.loads((output / "report.json").read_text(encoding="utf-8")),
                (output / "report.md").read_text(encoding="utf-8"), stdout.getvalue(), stderr.getvalue())

    def scenario(self, root, stable_id, rtt, loss):
        directory = root / stable_id
        directory.mkdir()
        merged = directory / "timeline.ndjson"
        merged.write_text("{}\n", encoding="utf-8")
        processes = []
        for index, role in enumerate(("server", "client-a", "client-b")):
            evidence = directory / role
            evidence.mkdir()
            process = {"role": role, "processId": 100 + index,
                       "startedUnixMilliseconds": 1000 + index * 10,
                       "endedUnixMilliseconds": 3000 - index * 10}
            for field, name in (("logPath", "player.log"), ("snapshotPath", "latest-snapshot.json"),
                                ("timelinePath", "timeline.ndjson")):
                artifact = evidence / name
                artifact.write_text("{}\n", encoding="utf-8")
                process[field] = str(artifact)
            processes.append(process)
        return {"stableId": stable_id, "roundTripLatencyMilliseconds": rtt,
                "packetLossBasisPoints": loss, "runnerPassed": True, "processes": processes,
                "timelinePath": str(merged), "metrics": {
                    "durationSeconds": 12.158866574201966, "hitFeedbackSampleCount": 40,
                    "p95HitFeedbackMilliseconds": rtt + 17.953875, "correctionCount": 20,
                    "correctionsPerMinute": 49.356065719561414,
                    "p95CorrectionMagnitude": 0.39304720679182226,
                    "maximumCorrectionMagnitude": 0.39304720679182226,
                    "uplinkBytes": 392940, "downlinkBytes": 2558979,
                    "uplinkBytesPerSecond": 16990.990144178082,
                    "downlinkBytesPerSecond": 105790.69949901351,
                    "stateComparisonCount": 453, "stateDivergenceCount": 1,
                    "stateDivergenceRate": 1 / 453,
                    "maximumStateDivergenceMagnitude": 0.39304720679182226,
                    "maximumStateDivergenceDurationMilliseconds": 452.1085839999999,
                    "sentCommandCount": 1344, "acceptedCommandCount": 1298,
                    "droppedCommandCount": 14, "rejectedCommandCount": 32,
                    "meanTransportRttMilliseconds": rtt + 5.3227815590086,
                }}


if __name__ == "__main__":
    unittest.main()
