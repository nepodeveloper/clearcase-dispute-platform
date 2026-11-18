"""Test that the static architecture gate is fail-closed on harmful regressions."""
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from architecture_guard import ROOT, SOURCE, verify


class ArchitectureBoundaryTests(unittest.TestCase):
    def test_current_feature_and_dependency_graph(self):
        self.assertEqual([], verify(ROOT))

    def test_forbidden_application_infrastructure_reference(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            file = root / SOURCE / "ClearCase.Application/Dummy.cs"
            file.parent.mkdir(parents=True)
            file.write_text("using ClearCase.Infrastructure.Disputes;\nclass Dummy {}\n")
            self.assertTrue(any("Forbidden ClearCase.Application dependency" in issue
                                for issue in verify(root)))

    def test_forbidden_domain_api_reference(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            file = root / SOURCE / "ClearCase.Domain/Dummy.cs"
            file.parent.mkdir(parents=True)
            file.write_text("using ClearCase.Api.Endpoints;\nclass Dummy {}\n")
            self.assertTrue(any("Forbidden ClearCase.Domain dependency" in issue
                                for issue in verify(root)))


if __name__ == "__main__":
    unittest.main()
