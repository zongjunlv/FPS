#!/usr/bin/env python3
from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from issue100_report_gate import (
    REQUIRED_SCENARIOS,
    REQUIRED_STEPS,
    SCHEMA_VERSION,
    validate,
)


class Issue100ReportGateTests(unittest.TestCase):
    def test_complete_real_three_process_report_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            self.assertEqual([], validate(report))

    def test_duplicate_pid_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            report["scenarios"][0]["processes"][1]["processId"] = 100
            errors = validate(report)
            self.assertTrue(any("PID 必须不同" in error for error in errors))

    def test_missing_flow_step_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            report["flow"] = [item for item in report["flow"]
                              if item["stepId"] != "reconnect.restore"]
            errors = validate(report)
            self.assertIn("流程缺少通过证据：reconnect.restore", errors)

    def test_fixture_metadata_cannot_claim_acceptance(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            report["metadata"]["evidenceKind"] = "DeterministicFixture"
            self.assertIn("证据类型必须是 MultiProcessPlayer", validate(report))

    def test_hit_feedback_budget_uses_observed_transport_rtt(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            metrics = report["scenarios"][3]["metrics"]
            metrics["meanTransportRttMilliseconds"] = 130
            metrics["p95HitFeedbackMilliseconds"] = 307
            self.assertEqual([], validate(report))

    def test_hit_feedback_above_observed_transport_budget_is_rejected(
            self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            metrics = report["scenarios"][3]["metrics"]
            metrics["meanTransportRttMilliseconds"] = 130
            metrics["p95HitFeedbackMilliseconds"] = 311
            errors = validate(report)
            self.assertTrue(any("射击反馈 P95 超预算" in error
                                for error in errors))

    def test_sparse_corrections_use_maximum_not_unstable_p95(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            metrics = report["scenarios"][3]["metrics"]
            metrics["correctionCount"] = 5
            metrics["p95CorrectionMagnitude"] = 1.0
            metrics["maximumCorrectionMagnitude"] = 1.1
            self.assertEqual([], validate(report))

    def test_correction_p95_is_enforced_with_twenty_samples(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report = self._report(pathlib.Path(temporary))
            metrics = report["scenarios"][3]["metrics"]
            metrics["correctionCount"] = 20
            metrics["p95CorrectionMagnitude"] = 1.0
            errors = validate(report)
            self.assertTrue(any("校正幅度超预算" in error
                                for error in errors))

    def _report(self, root: pathlib.Path) -> dict:
        flow = [{
            "stepId": step,
            "role": "client-a",
            "scenario": "rtt-000-loss-00",
            "state": "passed",
            "authoritativeTick": index + 1,
        } for index, step in enumerate(sorted(REQUIRED_STEPS))]
        scenarios = []
        for scenario_index, (stable_id, (rtt, loss)) in enumerate(
                REQUIRED_SCENARIOS.items()):
            scenario_root = root / stable_id
            scenario_root.mkdir()
            merged = scenario_root / "timeline.ndjson"
            merged.write_text("{}\n", encoding="utf-8")
            processes = []
            for role_index, role in enumerate(("server", "client-a", "client-b")):
                process_root = scenario_root / role
                process_root.mkdir()
                log = process_root / "player.log"
                snapshot = process_root / "latest-snapshot.json"
                timeline = process_root / "timeline.ndjson"
                log.write_text("ready\n", encoding="utf-8")
                snapshot.write_text("{}\n", encoding="utf-8")
                timeline.write_text("{}\n", encoding="utf-8")
                processes.append({
                    "role": role,
                    "processId": 100 + scenario_index * 10 + role_index,
                    "startedUnixMilliseconds": 1000 + role_index * 10,
                    "endedUnixMilliseconds": 3000 - role_index * 10,
                    "logPath": str(log),
                    "snapshotPath": str(snapshot),
                    "timelinePath": str(timeline),
                })
            dropped = 5 if loss else 0
            scenarios.append({
                "stableId": stable_id,
                "roundTripLatencyMilliseconds": rtt,
                "packetLossBasisPoints": loss,
                "processes": processes,
                "timelinePath": str(merged),
                "metrics": {
                    "durationSeconds": 60,
                    "hitFeedbackSampleCount": 24,
                    "p95HitFeedbackMilliseconds": rtt + 20,
                    "correctionCount": 2,
                    "correctionsPerMinute": 2,
                    "p95CorrectionMagnitude": 0.1,
                    "maximumCorrectionMagnitude": 0.2,
                    "uplinkBytes": 10000,
                    "downlinkBytes": 30000,
                    "uplinkBytesPerSecond": 200,
                    "downlinkBytesPerSecond": 500,
                    "stateComparisonCount": 500,
                    "stateDivergenceCount": 1,
                    "stateDivergenceRate": 0.002,
                    "maximumStateDivergenceMagnitude": 0.1,
                    "maximumStateDivergenceDurationMilliseconds": 30,
                    "sentCommandCount": 100,
                    "acceptedCommandCount": 100 - dropped,
                    "droppedCommandCount": dropped,
                    "rejectedCommandCount": 0,
                    "meanTransportRttMilliseconds": rtt,
                },
            })
        return {
            "schemaVersion": SCHEMA_VERSION,
            "metadata": {
                "evidenceKind": "MultiProcessPlayer",
                "topology": "dedicated-server-plus-two-clients",
                "processesPerScenario": 3,
                "controlPlane": "local-acceptance",
                "dataPlane": "UnityTransport",
            },
            "scenarios": scenarios,
            "flow": flow,
            "artifacts": {"videoPath": ""},
        }


if __name__ == "__main__":
    unittest.main()
