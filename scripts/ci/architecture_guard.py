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


def _mask(text: str) -> str:
    return "".join(character if character in "\r\n" else " " for character in text)


def csharp_source_code(source: str) -> str:
    """Remove comments and literals while preserving positions and line breaks."""
    output = []
    index = 0
    length = len(source)
    while index < length:
        if source.startswith("//", index):
            end = source.find("\n", index)
            if end == -1:
                end = length
            output.append(_mask(source[index:end]))
            index = end
            continue
        if source.startswith("/*", index):
            end = source.find("*/", index + 2)
            end = length if end == -1 else end + 2
            output.append(_mask(source[index:end]))
            index = end
            continue

        quote_index = index
        while quote_index < length and source[quote_index] == "$":
            quote_index += 1
        if quote_index < length and source[quote_index] == "@":
            quote_index += 1
            while quote_index < length and source[quote_index] == "$":
                quote_index += 1
        verbatim = "@" in source[index:quote_index]
        is_character = source[index] == "'"
        if is_character or (quote_index < length and source[quote_index] == '"'):
            delimiter = "'"
            if not is_character:
                delimiter_start = quote_index
                while delimiter_start < length and source[delimiter_start] == '"':
                    delimiter_start += 1
                delimiter = (
                    source[quote_index:delimiter_start]
                    if delimiter_start - quote_index >= 3
                    else '"'
                )
            cursor = (index if is_character else quote_index) + len(delimiter)
            if len(delimiter) >= 3:
                end = source.find(delimiter, cursor)
                end = length if end == -1 else end + len(delimiter)
            elif verbatim:
                while cursor < length:
                    if source.startswith('""', cursor):
                        cursor += 2
                        continue
                    if source[cursor] == '"':
                        cursor += 1
                        break
                    cursor += 1
                end = cursor
            else:
                while cursor < length:
                    if source[cursor] == "\\":
                        cursor += 2
                        continue
                    if source[cursor] == delimiter:
                        cursor += 1
                        break
                    cursor += 1
                end = min(cursor, length)
            output.append(_mask(source[index:end]))
            index = end
            continue

        output.append(source[index])
        index += 1
    return "".join(output)


def route_registration_statements(source: str) -> list[str]:
    registrations = []
    starts = re.finditer(r"\bapp\s*\.\s*Map(?:Get|Post|Put|Patch|Delete)\s*\(", source)
    for match in starts:
        parentheses = braces = brackets = 0
        for index in range(match.start(), len(source)):
            character = source[index]
            if character == "(":
                parentheses += 1
            elif character == ")":
                parentheses -= 1
            elif character == "{":
                braces += 1
            elif character == "}":
                braces -= 1
            elif character == "[":
                brackets += 1
            elif character == "]":
                brackets -= 1
            elif character == ";" and parentheses == braces == brackets == 0:
                registrations.append(source[match.start():index + 1])
                break
    return registrations


def contains_invocation(source: str, invocation_pattern: str) -> bool:
    return re.search(invocation_pattern, source, re.DOTALL) is not None


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
            content = csharp_source_code(file.read_text(encoding="utf-8"))
            for namespace in forbidden:
                qualified_name = r"\s*\.\s*".join(
                    re.escape(segment) for segment in namespace.split(".")
                )
                if re.search(
                    rf"(?<![A-Za-z0-9_])(?:global\s*::\s*)?{qualified_name}"
                    r"(?![A-Za-z0-9_])",
                    content,
                ):
                    errors.append(f"Forbidden {layer} dependency on {namespace}: {file.relative_to(root)}")
    for relative in CUSTOMER_ENDPOINTS:
        file = source / relative
        if not file.is_file():
            continue
        content = csharp_source_code(file.read_text(encoding="utf-8"))
        registrations = route_registration_statements(content)
        if not registrations:
            errors.append(f"Customer endpoint has no route registration: {relative}")
        for index, registration in enumerate(registrations, start=1):
            if not contains_invocation(
                registration,
                r"\.\s*RequireAuthorization\s*\(\s*AuthenticationPolicies"
                r"\s*\.\s*CustomerAccess\s*\)",
            ):
                errors.append(
                    f"Customer endpoint route registration {index} lacks "
                    f"CustomerAccess authorization: {relative}"
                )
    submit = source / REQUIRED[0]
    if submit.is_file():
        body = csharp_source_code(submit.read_text(encoding="utf-8"))
        if not contains_invocation(body, r"\.\s*RequireCsrfProtection\s*\(\s*\)"):
            errors.append("Dispute submission lost CSRF requirement")
        if not contains_invocation(
            body,
            r"\bSubmitDisputeRequestValidator\s*\.\s*Validate\s*\(",
        ):
            errors.append("Dispute submission bypasses feature validator")
        if not contains_invocation(body, r"\bApiProblem\s*\.\s*Create\s*\("):
            errors.append("Dispute submission bypasses common problem response")
    transition = source / "ClearCase.Infrastructure/Disputes/DisputeTransitionPersistence.cs"
    if transition.is_file():
        body = csharp_source_code(transition.read_text(encoding="utf-8"))
        requirements = (
            (
                "case status assignment",
                r"\brow\s*\.\s*CaseStatus\s*=\s*projected\s*\.\s*Next\s*\.\s*CaseStatus\s*;",
            ),
            (
                "decision status assignment",
                r"\brow\s*\.\s*DecisionStatus\s*=\s*projected\s*\.\s*Next"
                r"\s*\.\s*DecisionStatus\s*;",
            ),
            (
                "financial status assignment",
                r"\brow\s*\.\s*FinancialStatus\s*=\s*projected\s*\.\s*Next"
                r"\s*\.\s*FinancialStatus\s*;",
            ),
            (
                "case version assignment",
                r"\brow\s*\.\s*Version\s*=\s*projected\s*\.\s*Next\s*\.\s*Version\s*;",
            ),
            (
                "transaction begin",
                r"\bcontext\s*\.\s*Database\s*\.\s*BeginTransactionAsync\s*\(",
            ),
            (
                "transition event write",
                r"\bcontext\s*\.\s*DisputeEvents\s*\.\s*Add\s*\(",
            ),
            (
                "outbox write",
                r"\bcontext\s*\.\s*OutboxMessages\s*\.\s*Add\s*\(",
            ),
            ("save changes", r"\bcontext\s*\.\s*SaveChangesAsync\s*\("),
            (
                "transaction commit",
                r"\btransaction\s*\.\s*CommitAsync\s*\(",
            ),
        )
        for label, requirement in requirements:
            if not contains_invocation(body, requirement):
                errors.append(f"Shared financial transition writer lost {label}")
    else:
        errors.append("Canonical financial transition writer not found")
    return errors


if __name__ == "__main__":
    violations = verify()
    for violation in violations:
        print("ARCHITECTURE VIOLATION:", violation)
    raise SystemExit(1 if violations else 0)
