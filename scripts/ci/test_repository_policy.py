"""Focused unit tests for CI governance fail-closed controls."""
import json
import tempfile
import unittest
from pathlib import Path

from repository_policy import REQUIRED, ROOT, validate


class RepositoryPolicyTests(unittest.TestCase):
    def test_actual_repository_policy(self):
        self.assertEqual([], validate(ROOT))

    def test_pipeline_ai_review_must_remain_disabled(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in REQUIRED:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("NuGetAuditLevel=high npm audit --omit=dev --audit-level=high test:e2e Migration coverage.cobertura.xml")
            config = root / ".ai/ai-review.config.json"
            config.parent.mkdir(parents=True, exist_ok=True)
            settings = {
                "runAIReview": True,
                "executionMode": "pipeline",
                "requireChangedLineEvidence": True,
                "requireBatchTokenVerification": True,
                "oversizeFallback": "skip",
                "maxFilesPerBatch": 8,
                "maxChangedLinesPerBatch": 400
            }
            config.write_text(json.dumps(settings))
            failures = validate(root)
            self.assertTrue(any("sign-off" in message for message in failures))

    def test_missing_mandatory_gate_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in REQUIRED:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("")
            config = root / ".ai/ai-review.config.json"
            config.parent.mkdir(parents=True, exist_ok=True)
            config.write_text((ROOT / ".ai/ai-review.config.json").read_text())
            self.assertTrue(any("Missing mandatory GitHub CI evidence gate" in x for x in validate(root)))


if __name__ == "__main__":
    unittest.main()
