import json
import sys
import tempfile
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from performance_gate import evaluate_reports  # noqa: E402


def passing_report():
    return {
        "enemyCount": 100,
        "configuredResolution": "1920x1080",
        "qualityLevel": "PC",
        "buildType": "Development",
        "averageFps": 72.0,
        "onePercentLowFps": 51.0,
        "p95FrameMs": 17.0,
        "p99FrameMs": 21.0,
        "averageGcBytesPerFrame": 64.0,
        "maximumConsecutiveGcSpikeFrames": 1,
        "stableSampleEnemyInstantiateCount": 0,
        "stableSampleTracerInstantiateCount": 0,
        "stableSampleEffectInstantiateCount": 0,
        "maximumNearSightResultDelayFrames": 2,
    }


class PerformanceGateTests(unittest.TestCase):
    def test_qualified_report_passes_all_hard_gates(self):
        result = evaluate_reports([passing_report()])

        self.assertEqual("passed", result["status"])
        self.assertEqual([], result["failures"])

    def test_low_fps_and_one_percent_low_fail(self):
        report = passing_report()
        report["averageFps"] = 59.9
        report["onePercentLowFps"] = 44.9

        result = evaluate_reports([report])

        self.assertEqual("failed", result["status"])
        self.assertEqual(
            {"minimumAverageFps", "minimumOnePercentLowFps"},
            {failure["rule"] for failure in result["failures"]},
        )

    def test_any_stable_sample_creation_fails(self):
        for field in (
            "stableSampleEnemyInstantiateCount",
            "stableSampleTracerInstantiateCount",
            "stableSampleEffectInstantiateCount",
        ):
            with self.subTest(field=field):
                report = passing_report()
                report[field] = 1
                result = evaluate_reports([report])
                self.assertIn(
                    field,
                    {failure["rule"] for failure in result["failures"]},
                )

    def test_continuous_gc_and_near_latency_fail_each_round(self):
        report = passing_report()
        report["maximumConsecutiveGcSpikeFrames"] = 3
        report["maximumNearSightResultDelayFrames"] = 3

        result = evaluate_reports([report])

        self.assertEqual("failed", result["status"])
        self.assertEqual(
            {
                "maximumConsecutiveGcSpikeFrames",
                "maximumNearSightResultDelayFrames",
            },
            {failure["rule"] for failure in result["failures"]},
        )

    def test_missing_metric_becomes_report_failure(self):
        report = passing_report()
        del report["p99FrameMs"]

        result = evaluate_reports([report])

        self.assertEqual("failed", result["status"])
        self.assertEqual("reportSchema", result["failures"][0]["rule"])


if __name__ == "__main__":
    unittest.main()
