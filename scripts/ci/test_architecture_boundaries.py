"""Test that the static architecture gate is fail-closed on harmful regressions."""
from pathlib import Path
import re
from tempfile import TemporaryDirectory
import unittest

from architecture_guard import ROOT, SOURCE, verify


class ArchitectureBoundaryTests(unittest.TestCase):
    def assert_guard_detects(self, relative, body, message):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            file = root / SOURCE / relative
            file.parent.mkdir(parents=True)
            file.write_text(body, encoding="utf-8")
            self.assertTrue(any(message in issue for issue in verify(root)))

    def source_body(self, relative):
        return (ROOT / SOURCE / relative).read_text(encoding="utf-8")

    def test_current_feature_and_dependency_graph(self):
        self.assertEqual([], verify(ROOT))

    def test_forbidden_dependency_forms(self):
        references = {
            "using alias": "using Context = Microsoft.EntityFrameworkCore.DbContext;",
            "static import": "using static Microsoft.EntityFrameworkCore.EF;",
            "qualified type": "sealed class Dummy : Microsoft.EntityFrameworkCore.DbContext { }",
        }
        for name, body in references.items():
            with self.subTest(name=name):
                self.assert_guard_detects(
                    "ClearCase.Application/Dummy.cs",
                    body + "\n",
                    "Forbidden ClearCase.Application dependency",
                )

    def test_forbidden_application_infrastructure_reference(self):
        self.assert_guard_detects(
            "ClearCase.Application/Dummy.cs",
            "using ClearCase.Infrastructure.Disputes;\nclass Dummy { }\n",
            "Forbidden ClearCase.Application dependency",
        )

    def test_forbidden_domain_api_reference(self):
        self.assert_guard_detects(
            "ClearCase.Domain/Dummy.cs",
            "using ClearCase.Api.Endpoints;\nclass Dummy { }\n",
            "Forbidden ClearCase.Domain dependency",
        )

    def test_comments_and_literals_do_not_create_dependencies(self):
        with TemporaryDirectory() as temp:
            root = Path(temp)
            file = root / SOURCE / "ClearCase.Application/Dummy.cs"
            file.parent.mkdir(parents=True)
            file.write_text(
                '// using ClearCase.Infrastructure.Disputes;\n'
                'const string Name = "Microsoft.EntityFrameworkCore.DbContext";\n'
                'class Dummy { }\n',
                encoding="utf-8",
            )
            self.assertFalse(any("Forbidden ClearCase.Application dependency" in issue
                                 for issue in verify(root)))

    def test_empty_string_does_not_hide_a_dependency(self):
        self.assert_guard_detects(
            "ClearCase.Application/Dummy.cs",
            'const string Empty = ""; '
            "using static Microsoft.EntityFrameworkCore.EF;\n",
            "Forbidden ClearCase.Application dependency",
        )

    def test_each_eligibility_route_requires_authorization(self):
        relative = (
            "ClearCase.Api/Features/Transactions/GetDisputeEligibility/"
            "GetDisputeEligibilityEndpoint.cs"
        )
        body = self.source_body(relative)
        for route in (
            "/api/v1/transactions/{id}/dispute-eligibility",
            "/v1/transactions/{id}/dispute-eligibility",
        ):
            with self.subTest(route=route):
                start = body.index(f'app.MapGet("{route}"')
                end = body.index(";", start)
                registration = body[start:end]
                without_authorization = registration.replace(
                    ".RequireAuthorization(AuthenticationPolicies.CustomerAccess)",
                    "; // .RequireAuthorization(AuthenticationPolicies.CustomerAccess)",
                )
                self.assertNotEqual(registration, without_authorization)
                self.assert_guard_detects(
                    relative,
                    body[:start] + without_authorization + body[end + 1:],
                    "lacks CustomerAccess authorization",
                )

    def test_each_eligibility_route_requires_customer_policy(self):
        relative = (
            "ClearCase.Api/Features/Transactions/GetDisputeEligibility/"
            "GetDisputeEligibilityEndpoint.cs"
        )
        body = self.source_body(relative)
        for route in (
            "/api/v1/transactions/{id}/dispute-eligibility",
            "/v1/transactions/{id}/dispute-eligibility",
        ):
            with self.subTest(route=route):
                start = body.index(f'app.MapGet("{route}"')
                end = body.index(";", start)
                registration = body[start:end]
                without_policy = registration.replace(
                    ".RequireAuthorization(AuthenticationPolicies.CustomerAccess)",
                    ".RequireAuthorization()",
                )
                self.assertNotEqual(registration, without_policy)
                self.assert_guard_detects(
                    relative,
                    body[:start] + without_policy + body[end:],
                    "lacks CustomerAccess authorization",
                )

    def test_commented_csrf_marker_does_not_pass(self):
        relative = (
            "ClearCase.Api/Features/Disputes/SubmitDispute/"
            "SubmitDisputeEndpoint.cs"
        )
        body = self.source_body(relative)
        mutation = body.replace(
            ".RequireCsrfProtection();",
            "; // .RequireCsrfProtection()",
        )
        self.assertNotEqual(body, mutation)
        self.assert_guard_detects(
            relative, mutation, "Dispute submission lost CSRF requirement"
        )

    def test_commented_validation_marker_does_not_pass(self):
        relative = (
            "ClearCase.Api/Features/Disputes/SubmitDispute/"
            "SubmitDisputeEndpoint.cs"
        )
        body = self.source_body(relative)
        mutation = body.replace(
            "var validation = SubmitDisputeRequestValidator.Validate(request, context.Request.Headers);",
            "/* SubmitDisputeRequestValidator.Validate(request, context.Request.Headers) */",
        )
        self.assertNotEqual(body, mutation)
        self.assert_guard_detects(
            relative, mutation, "Dispute submission bypasses feature validator"
        )

    def test_transition_requires_every_protected_operation(self):
        relative = (
            "ClearCase.Infrastructure/Disputes/"
            "DisputeTransitionPersistence.cs"
        )
        body = self.source_body(relative)
        operations = (
            ("row.CaseStatus = projected.Next.CaseStatus;", "case status assignment"),
            ("row.DecisionStatus = projected.Next.DecisionStatus;", "decision status assignment"),
            ("row.FinancialStatus = projected.Next.FinancialStatus;", "financial status assignment"),
            ("row.Version = projected.Next.Version;", "case version assignment"),
            ("context.Database.BeginTransactionAsync(cancellationToken)", "transaction begin"),
            ("transaction.CommitAsync(cancellationToken)", "transaction commit"),
        )
        for operation, message in operations:
            with self.subTest(operation=operation):
                mutation = body.replace(operation, "/* removed protected operation */")
                self.assertNotEqual(body, mutation)
                self.assert_guard_detects(relative, mutation, message)

    def test_commented_outbox_write_does_not_pass(self):
        relative = (
            "ClearCase.Infrastructure/Disputes/"
            "DisputeTransitionPersistence.cs"
        )
        body = self.source_body(relative)
        mutation = re.sub(
            r"context\.OutboxMessages\.Add\(PersistedOutboxMessage\.Transitioned\(\n"
            r"\s+row, command, occurredAt, projected\.EnvelopeEventType, correlationId\)\);",
            lambda match: "/* " + match.group(0).replace("*/", "") + " */",
            body,
        )
        self.assertNotEqual(body, mutation)
        self.assert_guard_detects(relative, mutation, "outbox")

    def test_string_literal_outbox_write_does_not_pass(self):
        relative = (
            "ClearCase.Infrastructure/Disputes/"
            "DisputeTransitionPersistence.cs"
        )
        body = self.source_body(relative)
        mutation = re.sub(
            r"context\.OutboxMessages\.Add\(PersistedOutboxMessage\.Transitioned\(\n"
            r"\s+row, command, occurredAt, projected\.EnvelopeEventType, correlationId\)\);",
            'const string ignored = "context.OutboxMessages.Add(";',
            body,
        )
        self.assertNotEqual(body, mutation)
        self.assert_guard_detects(relative, mutation, "outbox")


if __name__ == "__main__":
    unittest.main()
