"""No-cloud release controller verification: promotion, approval, failure and recovery."""
import tempfile
from pathlib import Path
import unittest

from release_simulator import BASELINE, ReleaseGateError, approve, deploy, load, prepare

SHA = "a" * 40


class ReleaseSimulatorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.state = Path(self.tmp.name) / "release.json"

    def pipeline_to_uat(self):
        prepare(self.state, SHA)
        deploy(self.state, "dev")
        deploy(self.state, "uat")

    def test_successful_promotion_preserves_candidate(self):
        self.pipeline_to_uat()
        approve(self.state, "Demo Release Manager", "CAB-DEMO-01")
        state = deploy(self.state, "prod")
        self.assertEqual("healthy", state["stages"]["prod"])
        self.assertEqual(state["artifactDigest"], state["production"]["currentDigest"])
        self.assertEqual(100, state["production"]["trafficPercentNew"])
        self.assertFalse(state["alerts"])

    def test_production_cannot_run_before_approval(self):
        self.pipeline_to_uat()
        with self.assertRaises(ReleaseGateError):
            deploy(self.state, "prod")
        self.assertEqual(BASELINE, load(self.state)["production"]["currentDigest"])

    def test_cannot_approve_before_healthy_uat(self):
        prepare(self.state, SHA)
        with self.assertRaises(ReleaseGateError):
            approve(self.state, "Demo Manager", "CAB-1")

    def test_failed_canary_rolls_back_and_raises_alert(self):
        self.pipeline_to_uat()
        approve(self.state, "Demo Manager", "CAB-2")
        state = deploy(self.state, "prod", "telemetry-failure")
        self.assertEqual("rolled_back", state["stages"]["prod"])
        self.assertEqual(BASELINE, state["production"]["currentDigest"])
        self.assertEqual(0, state["production"]["trafficPercentNew"])
        self.assertEqual("telemetry", state["alerts"][0]["reason"])
        readings = state["measurements"]["prod"]
        self.assertGreater(readings["metrics"]["http5xxPercent"], readings["limits"]["http5xxPercent"])
        self.assertTrue(any(e["event"] == "traffic_rolled_back" for e in state["events"]))

    def test_earlier_stage_failure_blocks_promotion(self):
        prepare(self.state, SHA)
        state = deploy(self.state, "dev", "readiness-failure")
        self.assertEqual("failed", state["stages"]["dev"])
        with self.assertRaises(ReleaseGateError):
            deploy(self.state, "uat")

    def test_candidate_is_immutable_and_attempt_not_reused(self):
        prepare(self.state, SHA)
        with self.assertRaises(ReleaseGateError):
            prepare(self.state, "b" * 40)
        deploy(self.state, "dev")
        with self.assertRaises(ReleaseGateError):
            deploy(self.state, "dev")


if __name__ == "__main__":
    unittest.main()
