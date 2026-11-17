# ClearCase release evidence and rollback checklist

This document distinguishes **implemented and tested** features from target designs.
Passing GitHub Actions demonstrates reproducible local assessment code, not production
banking readiness or regulatory certification.

## Required CI gates

| Gate | Command / artifact | Decision |
| --- | --- | --- |
| .NET restore + NuGet advisory check | `dotnet restore apps/api/ClearCase.slnx -p:NuGetAudit=true -p:NuGetAuditLevel=high -warnaserror:NU1903,NU1904` | High/critical vulnerabilities fail restore |
| .NET analysis + build | `dotnet build apps/api/ClearCase.slnx -c Release --no-restore` | Compiler errors, nullable safety gate fail |
| Integration tests | `dotnet test apps/api/ClearCase.slnx -c Release --no-build --collect:"XPlat Code Coverage" --logger trx` | Failed test fails gate |
| Migration model and upgrade checks | `dotnet test apps/api/ClearCase.slnx -c Release --no-build --filter "FullyQualifiedName~Migration"` | Migration or snapshot drift fails |
| Coverage evidence | GitHub Actions artifact `backend-release-evidence` | Cobertura XML and TRX must be present; no numeric baseline gate yet |
| Web dependency security | From `apps/web`: `npm audit --omit=dev --audit-level=high` | High/critical production dependency vulnerabilities fail |
| Frontend/build/E2E | `npm ci`, `npm run test:ci`, `npm run build`, `npm run test:e2e` | Build/browser regressions fail |
| Browser audit evidence | Artifact `browser-evidence` | Reports and screenshots attached |

Keep artifacts for the configured CI retention period. A merged commit must reference the
CI run URL, build SHA, test results, migration result, and any approved deviations.
A successful smoke check alone never substitutes for a failed release gate.

## Migration sequence and data protection

- Before PR-33: `20261117063000_EnforceSingleActiveEscalation` (15 migrations).
- PR-33: `20261117090000_AddOutboxDeliveryLeases` (16), additive lease and retry fields.
- PR-34: `20261117091000_AddKafkaQuarantine` (17), quarantine metadata only.
- PR-35: `20261117092000_AddFinancialOperationIntents` (18), durable financial intent state.

The latter three migrations are **pending while their PRs remain unmerged**. Never
assume schema changes are present in `main` before the corresponding PR is merged.

Before a non-disposable SQLite migration, stop writers and backup the database with
its sidecars or a consistent SQLite online backup. Run migration tests on a restored
copy. Check for active-escalation duplicates before the unique-index migration.
Do not downgrade an outbox/intent migration with unacknowledged work or unresolved
financial outcomes: export durable recovery metadata first and obtain an explicit
release decision. Never mark operations financially `Confirmed` based on a timeout.

## Production-readiness exclusions

The local application uses synthetic identities and mock financial adapters.
The optional Kafka transport is not managed production infrastructure; no cloud Kafka
deployment, live ledger integration, production financial reconciliation, external
identity provider, MFA, private evidence storage or production-grade deployment
infrastructure is claimed implemented. POPIA-sensitive logging, retention, privileged
operations and infrastructure reviews remain release blockers for real customer data.

## Reconciling documentation

The root README describes current local behavior; older PR-specific sections are
historical. A statement marked deferred in an old phase must not be read as a current
status without checking later sections and the corresponding merged tests.
`docs/target-design` is an architecture target, not proof of deployed infrastructure.
Update this checklist whenever a release gate, database migration or integration changes.
