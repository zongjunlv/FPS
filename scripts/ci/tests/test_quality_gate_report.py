import tempfile
import unittest
from pathlib import Path
import sys


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from quality_gate_report import evaluate  # noqa: E402


class QualityGateReportTests(unittest.TestCase):
    def setUp(self):
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)

    def tearDown(self):
        self.temporary_directory.cleanup()

    def write(self, name: str, content: str) -> Path:
        path = self.root / name
        path.write_text(content)
        return path

    def test_compile_probe_success_is_passed(self):
        log = self.write("compile.log", "[QUALITY_GATE_COMPILE_OK]")
        marker = self.write("compile.marker", "unity=6000.5.3f1")
        report = evaluate("compile", 0, log, marker_path=marker)
        self.assertEqual("passed", report["category"])

    def test_license_failure_has_environment_category(self):
        log = self.write("compile.log", "No valid Unity Editor license found")
        report = evaluate("compile", 1, log)
        self.assertEqual("environment-license", report["category"])

    def test_import_error_cannot_be_reported_as_success(self):
        log = self.write("compile.log", "Shader error in 'Broken/Shader'")
        marker = self.write("compile.marker", "unity=6000.5.3f1")
        report = evaluate("compile", 0, log, marker_path=marker)
        self.assertEqual("environment-import", report["category"])
        self.assertEqual("failed", report["status"])

    def test_failed_test_xml_has_test_failure_category(self):
        log = self.write("playmode.log", "Tests completed")
        results = self.write(
            "playmode.xml",
            '<test-run result="Failed(Child)" total="2" passed="1" '
            'failed="1" skipped="0" inconclusive="0" />',
        )
        report = evaluate("playmode", 2, log, results)
        self.assertEqual("test-failure", report["category"])
        self.assertEqual(1, report["counts"]["failed"])

    def test_passed_test_xml_is_passed(self):
        log = self.write("editmode.log", "Tests completed")
        results = self.write(
            "editmode.xml",
            '<test-run result="Passed" total="3" passed="3" '
            'failed="0" skipped="0" inconclusive="0" />',
        )
        report = evaluate("editmode", 0, log, results)
        self.assertEqual("passed", report["category"])


if __name__ == "__main__":
    unittest.main()
