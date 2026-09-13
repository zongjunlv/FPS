import importlib.util
import math
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "balance_gate.py"
SPEC = importlib.util.spec_from_file_location("balance_gate", MODULE_PATH)
balance_gate = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(balance_gate)


def seed_result(seed: int, *, outcome: str = "Victory", reason: str = "None", duration: int = 600):
    return {
        "seed": seed,
        "outcome": outcome,
        "failureReason": reason,
        "durationTicks": duration,
        "enemyTtkTicks": [30, 40],
        "healthDamage": 10,
        "armorDamage": 15,
        "ammoSpent": 20,
        "ammoGranted": 5,
        "roleCounts": [{"key": "assault", "count": 2}],
        "encounterCounts": [{"key": "ambush", "count": 1}],
        "upgradeSelections": ["damage"],
        "anomalyCodes": [],
        "deterministicDigest": f"digest-{seed}",
    }


def report(count: int = 1000):
    return {
        "schemaVersion": 1,
        "rulesVersion": "rules-v1",
        "policyVersion": "policy-v1",
        "contentVersion": 1,
        "contentFingerprint": "abc",
        "environment": {"runtime": "test"},
        "results": [seed_result(index) for index in range(count)],
    }


class BalanceGateTests(unittest.TestCase):
    def test_valid_thousand_seed_report_passes(self):
        self.assertEqual(balance_gate.evaluate_report(report())["status"], "passed")

    def test_missing_duplicate_and_non_finite_data_fail(self):
        candidate = report(999)
        candidate["results"][1]["seed"] = 0
        candidate["results"][2]["healthDamage"] = math.inf
        rules = {item["rule"] for item in balance_gate.evaluate_report(candidate)["failures"]}
        self.assertTrue({"minimumSeedCount", "uniqueSeeds", "finiteMetrics"} <= rules)

    def test_hard_failure_and_resource_regression_fail(self):
        candidate = report()
        for item in candidate["results"][:201]:
            item["outcome"] = "Defeat"
            item["failureReason"] = "ResourceExhausted"
        candidate["results"][0]["failureReason"] = "UnsolvableRoster"
        rules = {item["rule"] for item in balance_gate.evaluate_report(candidate)["failures"]}
        self.assertIn("hardSimulationFailure", rules)
        self.assertIn("maximumResourceExhaustionRate", rules)

    def test_duration_ttk_and_distribution_regressions_fail_against_baseline(self):
        baseline = report()
        candidate = report()
        for item in candidate["results"]:
            item["durationTicks"] = 1200
            item["enemyTtkTicks"] = [120, 140]
            item["roleCounts"] = [{"key": "elite", "count": 2}]
        limits = {
            "minimumSeedCount": 1,
            "maximumDurationP99Ticks": 99999,
            "maximumTtkP95Ticks": 99999,
        }
        rules = {
            item["rule"]
            for item in balance_gate.evaluate_report(candidate, limits, baseline)["failures"]
        }
        self.assertIn("durationP99Regression", rules)
        self.assertIn("ttkP95Regression", rules)
        self.assertIn("roleDistributionRegression", rules)


if __name__ == "__main__":
    unittest.main()
