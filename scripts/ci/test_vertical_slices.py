"""Structural check for feature slices; banking behavior is verified by .NET integration tests."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "apps/api/src/ClearCase.Api"
APPLICATION = ROOT / "apps/api/src/ClearCase.Application"


class VerticalSliceStructureTests(unittest.TestCase):
    def test_submit_dispute_is_a_feature_slice(self):
        endpoint = (API / "Features/Disputes/SubmitDispute/SubmitDisputeEndpoint.cs").read_text()
        handler = (APPLICATION / "Features/Disputes/SubmitDispute/SubmitDispute.cs").read_text()
        router = (API / "Endpoints/DisputeEndpoints.cs").read_text()
        self.assertIn('RequireCsrfProtection()', endpoint)
        self.assertIn('Idempotency-Key', endpoint)
        self.assertIn('SubmitDisputeEndpoint.Map(app, prefix, suffix)', router)
        self.assertIn('SubmitDisputeHandler', handler)
        self.assertIn('ResolveSubmissionAsync', handler)
        self.assertNotIn('private static async Task<Results<Created<DisputeResponse>', router)


if __name__ == "__main__":
    unittest.main()
