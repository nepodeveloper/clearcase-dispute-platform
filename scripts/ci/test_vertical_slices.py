"""Structural check for feature slices; banking behavior is verified by .NET integration tests."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "apps/api/src/ClearCase.Api"
APPLICATION = ROOT / "apps/api/src/ClearCase.Application"


class VerticalSliceStructureTests(unittest.TestCase):
    def test_queries_are_feature_scoped(self):
        for category, name in [
            ("Disputes/ListDisputes", "ListDisputes"),
            ("Disputes/GetDispute", "GetDispute"),
            ("Disputes/GetDisputeTimeline", "GetDisputeTimeline"),
            ("Transactions/GetDisputeEligibility", "GetDisputeEligibility"),
        ]:
            self.assertTrue((API / f"Features/{category}/{name}Endpoint.cs").is_file())
        router = (API / "Endpoints/DisputeEndpoints.cs").read_text()
        for name in ("ListDisputes", "GetDispute", "GetDisputeTimeline"):
            self.assertIn(name + "Endpoint.Map(app, prefix, suffix)", router)

    def test_financial_commands_preserve_shared_domain_boundaries(self):
        command_paths = [
            ("Chargebacks/ProcessChargeback", "ChargebackProcessingHandler.cs"),
            ("DebitOrders/ProcessDebitOrder", "DebitOrderProcessingHandler.cs"),
            ("BankCharges/ProcessBankCharge", "BankChargeProcessingHandler.cs"),
        ]
        for category, name in command_paths:
            self.assertTrue((APPLICATION / f"Features/{category}/{name}").is_file())
        for category, name in [
            ("Chargebacks/SimulateChargeback", "ChargebackSimulationEndpoints.cs"),
            ("DebitOrders/SimulateDebitOrder", "DebitOrderSimulationEndpoints.cs"),
            ("BankCharges/SimulateBankCharge", "BankChargeSimulationEndpoints.cs"),
            ("AgentDisputes/ProcessAgentCommand", "AgentDisputeCommandEndpoints.cs"),
        ]:
            self.assertTrue((API / f"Features/{category}/{name}").is_file())
        # The canonical transition writer remains shared, outside HTTP slices.
        self.assertTrue((ROOT / "apps/api/src/ClearCase.Infrastructure/Disputes/DisputeTransitionPersistence.cs").is_file())

    def test_feature_local_validation_and_explicit_registration(self):
        validator = (API / "Features/Disputes/SubmitDispute/SubmitDisputeRequestValidator.cs").read_text()
        endpoint = (API / "Features/Disputes/SubmitDispute/SubmitDisputeEndpoint.cs").read_text()
        registration = (API / "Features/CustomerFeatureEndpointRegistration.cs").read_text()
        program = (API / "Program.cs").read_text()
        self.assertIn('Idempotency-Key', validator)
        self.assertIn('transactionId.invalid', validator)
        self.assertIn('description.length', validator)
        self.assertIn('SubmitDisputeRequestValidator.Validate', endpoint)
        self.assertIn('ApiProblem.Create', endpoint)
        self.assertIn('app.MapDisputeEndpoints()', registration)
        self.assertIn('app.MapTransactionEndpoints()', registration)
        self.assertIn('app.MapCustomerFeatures()', program)

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
