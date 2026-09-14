#!/usr/bin/env python3
import copy
import unittest

from issue64_gate import evaluate_reports


def metrics(frame, main, decision, gc=100.0, memory=1000.0):
    return {
        "averageFrameMilliseconds": frame,
        "p95FrameMilliseconds": frame * 1.2,
        "p99FrameMilliseconds": frame * 1.4,
        "averageMainThreadMilliseconds": main,
        "p95MainThreadMilliseconds": main * 1.2,
        "p99MainThreadMilliseconds": main * 1.4,
        "averageGcBytesPerFrame": gc,
        "peakGcBytesPerFrame": gc * 2,
        "averageMemoryBytes": memory * 0.9,
        "peakMemoryBytes": memory,
        "averageDecisionLatencyMilliseconds": decision * 0.75,
        "p95DecisionLatencyMilliseconds": decision,
        "p99DecisionLatencyMilliseconds": decision * 1.2,
    }


def run(mode, count, frame, main, decision, gc=100.0, memory=1000.0):
    return {
        "mode": mode,
        "enemyCount": count,
        "seed": 64064,
        "runIndex": 0,
        "sampleCount": 3600,
        "activeCount": count,
        "ghostCount": 0,
        "intentDigest": "same-intents",
        "metrics": metrics(frame, main, decision, gc, memory),
    }


def passing_report():
    comparisons = []
    values = {
        100: (10.0, 10.2, 8.0, 8.2, 4.0, 4.0),
        300: (20.0, 16.0, 18.0, 14.0, 8.0, 5.0),
        500: (30.0, 22.0, 27.0, 20.0, 12.0, 8.0),
    }
    for count, value in values.items():
        go_frame, ecs_frame, go_main, ecs_main, go_decision, ecs_decision = value
        comparisons.append({
            "enemyCount": count,
            "intentDigestMatches": True,
            "gameObject": run(
                "GameObject", count, go_frame, go_main, go_decision,
            ),
            "ecs": run(
                "Ecs", count, ecs_frame, ecs_main, ecs_decision,
                gc=90.0, memory=1150.0,
            ),
        })
    return {
        "schemaVersion": "issue64-hybrid-ai-performance-v1",
        "environment": {
            "comparisonKey": "same-machine-and-quality",
            "unityVersion": "6000.0.60f1",
            "entitiesVersion": "1.3.14",
            "processor": "Apple M2",
            "graphicsDevice": "Apple M2",
            "qualityLevel": "PC",
            "width": 1920,
            "height": 1080,
            "runsPerCase": 1,
        },
        "comparisons": comparisons,
    }


class Issue64GateTests(unittest.TestCase):
    def test_complete_same_condition_ab_report_continues(self):
        result = evaluate_reports([passing_report()])

        self.assertEqual("Continue", result["outcome"])
        self.assertEqual([], result["reasons"])
        self.assertEqual([100, 300, 500], [
            item["enemyCount"] for item in result["comparisons"]
        ])

    def test_environment_mismatch_and_round_count_stop(self):
        first = passing_report()
        first["environment"]["runsPerCase"] = 2
        second = copy.deepcopy(first)
        second["environment"]["comparisonKey"] = "other-gpu"

        result = evaluate_reports([first, second])

        self.assertEqual("Stop", result["outcome"])
        self.assertTrue(any("硬件/画质/采样条件不一致" in reason
                            for reason in result["reasons"]))

    def test_missing_density_stops(self):
        report = passing_report()
        report["comparisons"].pop()

        result = evaluate_reports([report])

        self.assertEqual("Stop", result["outcome"])
        self.assertTrue(any("100、300、500" in reason
                            for reason in result["reasons"]))

    def test_intent_ghost_gc_memory_and_tail_regressions_stop(self):
        report = passing_report()
        pair = report["comparisons"][2]
        pair["intentDigestMatches"] = False
        pair["ecs"]["intentDigest"] = "different"
        pair["ecs"]["ghostCount"] = 1
        pair["ecs"]["metrics"]["p99FrameMilliseconds"] = 50.0
        pair["ecs"]["metrics"]["averageGcBytesPerFrame"] = 101.0
        pair["ecs"]["metrics"]["peakMemoryBytes"] = 1250.0

        result = evaluate_reports([report])

        joined = "\n".join(result["reasons"])
        self.assertEqual("Stop", result["outcome"])
        self.assertIn("命令意图摘要不一致", joined)
        self.assertIn("幽灵", joined)
        self.assertIn("P99", joined)
        self.assertIn("GC", joined)
        self.assertIn("峰值内存", joined)

    def test_missing_metric_is_schema_failure_not_exception(self):
        report = passing_report()
        del report["comparisons"][0]["ecs"]["metrics"][
            "p95DecisionLatencyMilliseconds"
        ]

        result = evaluate_reports([report])

        self.assertEqual("Stop", result["outcome"])
        self.assertTrue(any("缺少指标" in reason
                            for reason in result["reasons"]))


if __name__ == "__main__":
    unittest.main()
