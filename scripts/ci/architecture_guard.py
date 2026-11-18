"""Static ClearCase vertical-slice and dependency-direction quality gate.

No runtime imports, network requests, or source execution. This supplements .NET
compiler/integration tests; it cannot replace architecture and ownership tests.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path("apps/api/src")
REQUIRED = (
    "ClearCase.Api/Features/Disputes/SubmitDispute/SubmitDisputeEndpoint.cs",
    "ClearCase.Api/Features/Disputes/SubmitDispute/SubmitDisputeRequestValidator.cs",
    "ClearCase.Api/Features/Disputes/ListDisputes/ListDisputesEndpoint.cs",
    "ClearCase.Api/Features/Disputes/GetDispute/GetDisputeEndpoint.cs",
    "ClearCase.Api/Features/Disputes/GetDisputeTimeline/GetDisputeTimelineEndpoint.cs",
    "ClearCase.Api/Features/Transactions/GetDisputeEligibility/GetDisputeEligibilityEndpoint.cs",
    "ClearCase.Api/Features/CustomerFeatureEndpointRegistration.cs",
    "ClearCase.Application/Features/Disputes/SubmitDispute/SubmitDispute.cs",
    "ClearCase.Application/Features/Disputes/ListDisputes/ListDisputesHandler.cs",
    "ClearCase.Application/Features/Disputes/GetDispute/GetDisputeHandler.cs",
    "ClearCase.Application/Features/Transactions/GetDisputeEligibility/GetDisputeEligibilityHandler.cs",
    "ClearCase.Application/Features/Chargebacks/ProcessChargeback/ChargebackProcessingHandler.cs",
    "ClearCase.Application/Features/DebitOrders/ProcessDebitOrder/DebitOrderProcessingHandler.cs",
    "ClearCase.Application/Features/BankCharges/ProcessBankCharge/BankChargeProcessingHandler.cs",
)

CUSTOMER_ENDPOINTS = (
    "ClearCase.Api/Features/Disputes/SubmitDispute/SubmitDisputeEndpoint.cs",
    "ClearCase.Api/Features/Disputes/ListDisputes/ListDisputesEndpoint.cs",
    "ClearCase.Api/Features/Disputes/GetDispute/GetDisputeEndpoint.cs",
    "ClearCase.Api/Features/Disputes/GetDisputeTimeline/GetDisputeTimelineEndpoint.cs",
    "ClearCase.Api/Features/Transactions/GetDisputeEligibility/GetDisputeEligibilityEndpoint.cs",
)

def verify(root: Path = ROOT) -> list[str]:
    errors = []
    source = root / SOURCE
    for path in REQUIRED:
        if not (source / path).is_file():
            errors.append(f"Required feature slice is missing: {path}")
    for layer, forbidden in (
        ("ClearCase.Domain", ("ClearCase.Application", "ClearCase.Infrastructure", "ClearCase.Api",
                              "Microsoft.EntityFrameworkCore")),
        ("ClearCase.Application", ("ClearCase.Infrastructure", "ClearCase.Api", "Microsoft.EntityFrameworkCore")),
    ):
        folder = source / layer
        for file in folder.rglob("*.cs"):
            content = file.read_text(encoding="utf-8")
            for namespace in forbidden:
                if re.search(r"^\s*(?:global\s+)?using\s+" + re.escape(namespace) + r"(?:\.|\s*;)", content, re.M):
                    errors.append(f"Forbidden {layer} dependency on {namespace}: {file.relative_to(root)}")
    for relative in CUSTOMER_ENDPOINTS:
        file = source / relative
        if file.is_file() and ".RequireAuthorization(" not in file.read_text(encoding="utf-8"):
            errors.append(f"Customer endpoint lacks server authorization: {relative}")
    submit = source / REQUIRED[0]
    if submit.is_file():
        body = submit.read_text(encoding="utf-8")
        if ".RequireCsrfProtection()" not in body:
            errors.append("Dispute submission lost CSRF requirement")
        if "SubmitDisputeRequestValidator.Validate(" not in body:
            errors.append("Dispute submission bypasses feature validator")
        if "ApiProblem.Create(" not in body:
            errors.append("Dispute submission bypasses common problem response")
    transition = source / "ClearCase.Infrastructure/Disputes/DisputeTransitionPersistence.cs"
    if transition.is_file():
        body = transition.read_text(encoding="utf-8")
        for requirement in ("DisputeEvents.Add(", "OutboxMessages.Add(", "SaveChangesAsync("):
            if requirement not in body:
                errors.append(f"Shared financial transition writer lost {requirement}")
    else:
        errors.append("Canonical financial transition writer not found")
    return errors


if __name__ == "__main__":
    for violation in verify():
        print("ARCHITECTURE VIOLATION:", violation)
    raise SystemExit(1 if verify() else 0)
