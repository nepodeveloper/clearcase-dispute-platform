# 01 | Design changes and baseline gap analysis

## Executive decision

Retain a **modular monolith** as one independently deployable ASP.NET Core service with separate Domain, Application, Infrastructure and API projects. Add workers only for real asynchronous behavior. Treat Kafka, AWS EKS and distributed service extraction as target-state capabilities unless actually built and verified. This is more credible in an assessment than six empty microservices.

## Mapping the newly established engineering standards

| Standard or principle | Baseline | v2 design decision | Proof / acceptance gate |
|---|---|---|---|
| REST resource convention | `/api/disputes` and `/api/transactions` | `/api/v1/disputes`, `/api/v1/transactions`, noun/plural resources; GUID IDs; real hierarchy only | OpenAPI schema lint + endpoint contract tests |
| HTTP semantics | Some valid codes, unversioned | GET safe; POST create; PUT replace; PATCH partial; DELETE remove; POST explicit commands for non-CRUD actions | 201 + Location; deterministic replays; 409 and 422 examples |
| JSON conventions | Basic DTOs | camelCase, stable DTOs, omitted optional nulls, empty collections `[]` where in contract | Consumer contract tests |
| Error standard | ASP.NET Problem Details | RFC 7807/9457-style Problem Details; JSON Pointer validation errors; trace/correlation ID; no sensitive internals | Standardised negative-path tests |
| Pagination | Basic lists | cursor or keyset paging, capped `pageSize`, stable `(postedAt,id)` ordering, metadata and continuation cursor | Large-dataset pagination tests |
| SOLID, DI, Clean Architecture | API and EF/domain share project | Domain pure; use cases use focused ports; infrastructure adapters; API thin | Architecture dependency tests; isolated unit tests |
| Naming, encapsulation, SRP | Mixed | intention-revealing names, private invariants, small interfaces, no needless parameters | PR checklist and static analysis |
| TypeScript standards | Angular TypeScript | strict mode, `async/await` for sequential async, strict equality, explicit null treatment, single quotes, consistent formatting | ESLint, Prettier, TS strict compilation |
| Exception/logging | Select catches and correlation | Catch only where translated/recovered; structured contextual logs; propagate or map failures; never swallow | Fault injection + log assertions |
| TDD and quality | Unit and selected integration tests | AAA and test pyramid, domain/application/integration/contract/E2E/chaos/perf/security | CI gates and documented evidence |
| Failure handling | Mock workflow only | timeouts, retries+jitter, circuit breaker, durable pending state, reconciliation | Dependency down/up tests |
| Consistency | SQLite and simple uniqueness | ACID dispute + timeline + idempotency + outbox; inbox, optimistic concurrency | Crash/race tests |
| Cloud and IaC | Azure outline | AWS deployment target: RDS PostgreSQL, S3, OIDC, EKS (if justified), MSK (if justified), CloudWatch; modular IaC with naming, parameters, least privilege, tags and diagnostics | IaC validation, security checks, environment smoke tests |
| Governance | A few architecture notes | ADRs, threat model, sequence diagrams, change control and runbooks | Docs in repo, reviewed during PR |

**Scope distinction:** The earlier Bicep/Key Vault/managed-identity conventions are Azure-specific. Preserve their underlying principles (modular IaC, parameterised environments, naming standards, RBAC, secret management, tagging and diagnostics), but implement the equivalent AWS-native controls. Do not add Azure infrastructure to an AWS role assessment simply to satisfy technology names.

## Actual starter findings

Read from the supplied starter ZIP, not inferred:

1. `TransactionKind` supports only `CardPurchase` and `BankFee`. There is no `DebitOrder`.
2. `DisputeWorkflow` has `SentToProcessor` then `MerchantResponded`; chargeback, representment, outcome and settlement are not separately represented.
3. `Dispute` stores `IdempotencyKey` and timeline but not a dedicated durable request/response idempotency record, version, processor reference, reversals or outbox.
4. EF Core uses SQLite with `EnsureCreated` in development; there are no PostgreSQL migrations.
5. The API uses `/api/...` routes without major-version path segments.
6. Evidence is submitted as text notes, not scanned private document uploads.
7. The README lists only selected unit/integration/contract tests; verified Kubernetes, Kafka, load, security and DR tests are absent.
8. Demo-only simulation operations exist and must be physically excluded from production builds/routes, not merely hidden by UI.

## Priority changes

**P0 — essential assessment completeness**: Add debit-order transaction type and policy strategy; correct chargeback workflow; versioned API; PostgreSQL migrations; application/domain separation; authoritative money-outcome vocabulary; C# and TS lint/quality gates; customer isolation.

**P1 — reliability differentiator**: Durable outbox/inbox, business idempotency, concurrency, workflow worker, external adapter, reconciliation, resilience and adversarial tests.

**P2 — operational/cloud proof**: Docker Compose, Kafka local, structured telemetry, S3 evidence with scanning, AWS deployment plans, CI security scans, measured k6 tests and disaster-recovery design.

## Anti-patterns we will not introduce

- One microservice per table or technical layer.
- Synchronous database write followed by unprotected Kafka publish.
- Automatic blind retries of financial commands when the remote outcome is unknown.
- Customer-supplied `customerId` as authorization source.
- Per-request distributed transactions across PostgreSQL, Kafka and the bank ledger.
- A mutable single status string used as the only audit record.
- Claiming a R2,000 amount as a regulatory debit-order ceiling.
- Asserting a case was refunded before authoritative ledger confirmation.
- Reusing Azure-specific IaC in an AWS target without a deliberate reason.
