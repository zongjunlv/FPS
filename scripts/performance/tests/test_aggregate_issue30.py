import tempfile
import unittest
from pathlib import Path
import sys


SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIR))

from aggregate_issue30 import aggregate, validate_reports  # noqa: E402


def report(round_offset: float = 0.0) -> dict:
    result = {
        "unityVersion": "6000.5.3f1",
        "buildType": "Development",
        "operatingSystem": "macOS",
        "processor": "Apple M4",
        "graphicsDevice": "Apple M4",
        "configuredResolution": "1920x1080",
        "resolution": "1920x1080",
        "screenMode": "Windowed",
        "qualityLevel": "PC",
        "enemyCount": 100,
        "warmupSeconds": 5.0,
        "configuredSampleSeconds": 20.0,
        "seed": 30030,
        "behaviorProfile": "alert-chase-fire-v1",
        "perceptionChecksPerFrame": 4,
        "stableSampleInstantiateCount": 0,
    }
    metrics = (
        "averageFps",
        "onePercentLowFps",
        "averageFrameMs",
        "p95FrameMs",
        "p99FrameMs",
        "averageMainThreadMs",
        "p95MainThreadMs",
        "p99MainThreadMs",
        "averageGcBytesPerFrame",
        "p95GcBytesPerFrame",
        "maximumGcBytesInFrame",
        "maximumPerceptionLatencyFrames",
        "maximumPerceptionLatencyMs",
        "enemyPoolObjects",
        "enemyPoolReuseCount",
        "enemyPoolExpansionCount",
    )

    for index, field in enumerate(metrics, start=1):
        result[field] = index + round_offset

    return result


class Issue30AggregateTests(unittest.TestCase):
    def test_three_comparable_rounds_use_median(self) -> None:
        reports = [report(10.0), report(0.0), report(20.0)]
        paths = [Path(f"round-{index}.json") for index in range(3)]
        result = aggregate(paths, reports, 100)
        self.assertEqual(result["roundCount"], 3)
        self.assertEqual(result["medians"]["averageFps"], 11.0)

    def test_mismatched_seed_is_rejected(self) -> None:
        reports = [report(), report()]
        reports[1]["seed"] = 1
        with self.assertRaisesRegex(ValueError, "seed differs"):
            validate_reports(reports, 100)

    def test_stable_sample_expansion_is_rejected(self) -> None:
        candidate = report()
        candidate["stableSampleInstantiateCount"] = 1
        with self.assertRaisesRegex(ValueError, "stable sampling"):
            validate_reports([candidate], 100)


if __name__ == "__main__":
    unittest.main()
