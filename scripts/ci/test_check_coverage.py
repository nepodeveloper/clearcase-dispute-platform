import tempfile
import unittest
from pathlib import Path

import check_coverage

REPORT = """<coverage><packages>
<package name="ClearCase.Domain"><classes><class filename="A.cs"><lines>
<line number="1" hits="{a}"/><line number="2" hits="0"/>
</lines></class></classes></package>
</packages></coverage>"""


class CoverageGateTests(unittest.TestCase):
    def write(self, directory: str, name: str, hits: int) -> Path:
        path = Path(directory) / name
        path.write_text(REPORT.format(a=hits), encoding="utf-8")
        return path

    def test_lines_are_unioned_across_reports(self):
        with tempfile.TemporaryDirectory() as directory:
            first = self.write(directory, "a.xml", 0)
            second = self.write(directory, "b.xml", 3)
            merged = check_coverage.merge([first, second])
        self.assertEqual(merged["ClearCase.Domain"], (1, 2))

    def test_assembly_below_minimum_fails_closed(self):
        errors = check_coverage.verify({"ClearCase.Domain": (1, 2)}, {"ClearCase.Domain": 80.0}, 0.0)
        self.assertEqual(len(errors), 1)
        self.assertIn("below", errors[0])

    def test_missing_assembly_and_empty_reports_fail(self):
        self.assertTrue(check_coverage.verify({}, {"ClearCase.Domain": 80.0}, 0.0))
        self.assertTrue(check_coverage.verify({}, {}, 0.0))

    def test_passing_thresholds_report_no_errors(self):
        self.assertEqual(check_coverage.verify({"ClearCase.Domain": (9, 10)}, {"ClearCase.Domain": 85.0}, 80.0), [])


if __name__ == "__main__":
    unittest.main()
