#!/usr/bin/env python3
import copy
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from issue65_report_gate import REQUIRED_SCENARIOS, validate


def base_report():
    return {
        "schemaVersion": "issue65-network-diagnostics-v1",
        "limitations": "fixture is not multi-process acceptance evidence",
        "metadata": {
            "evidenceKind": "DeterministicFixture",
            "processCount": 1,
            "isRealMultiProcess": False,
        },
        "gate": {
            "outcome": "FixtureOnly",
            "acceptanceEligible": False,
        },
        "scenarios": [
            {
                "stableId": stable_id,
                "roundTripLatencyMilliseconds": condition[0],
                "packetLossBasisPoints": condition[1],
            }
            for stable_id, condition in REQUIRED_SCENARIOS.items()
        ],
    }


class Issue65ReportGateTests(unittest.TestCase):
    def test_fixture_is_valid_for_pipeline_but_not_acceptance(self):
        self.assertEqual([], validate(base_report(), False))
        self.assertTrue(validate(base_report(), True))

    def test_fixture_cannot_claim_pass(self):
        report = base_report()
        report["gate"]["outcome"] = "Pass"
        report["gate"]["acceptanceEligible"] = True
        self.assertTrue(validate(report, False))

    def test_real_multiprocess_can_pass(self):
        report = base_report()
        report["limitations"] = "applies only to recorded build"
        report["metadata"] = {
            "evidenceKind": "MultiProcessPlayer",
            "processCount": 2,
            "isRealMultiProcess": True,
        }
        report["gate"] = {
            "outcome": "Pass",
            "acceptanceEligible": True,
        }
        self.assertEqual([], validate(report, True))

    def test_missing_scenario_fails(self):
        report = copy.deepcopy(base_report())
        report["scenarios"].pop()
        self.assertTrue(validate(report, False))


if __name__ == "__main__":
    unittest.main()
