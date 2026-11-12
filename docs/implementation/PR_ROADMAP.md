# ClearCase — small PR delivery roadmap

This is the execution plan, **not** a claim that the features already exist. All 21 PRs must remain independently mergeable and green. Current baseline is the `card-dispute-portal-starter` (.NET single API project, Angular frontend, SQLite, unversioned `/api` routes). Target is the v2 design.

## Release cut lines

- **Demo A (after PR-05):** current customer journey maintained, versioned API, PostgreSQL, CI.
- **Demo B (after PR-10):** all three supported dispute types simulated with truthful case/finance statuses and timelines.
- **Demo C (after PR-15):** idempotency, crash recovery, outbox/Kafka, reconciliation evidenced by tests.
- **Demo D (after PR-19):** security, evidence and operational tests.
- **Submission candidate (after PR-21):** reproducible deployment blueprint and verified walkthrough.

## PR table

| Batch | PR | Outcome | Gate |
|---|---|---|---|
| Batch 01 | PR-01 | Establish reproducible baseline | Running baseline README and CI both executes real backend build/tests and frontend build/tests |
| Batch 01 | PR-02 | Extract pure domain assembly | Domain project has no infrastructure dependency |
| Batch 01 | PR-03 | Introduce application use-case seams | No direct EF access in refactored dispute endpoints |
| Batch 01 | PR-04 | Version API and normalize contracts | Angular uses `/api/v1` end to end |
| Batch 01 | PR-05 | PostgreSQL with real migrations | Clean database can migrate and seed |
| Batch 02 | PR-06 | Model three dispute types and statuses | Transaction category contract exposed |
| Batch 02 | PR-07 | Debit order eligibility policy | Day 1/60/61 cases tested |
| Batch 02 | PR-08 | Debit order case processing | Eligible EFT dispute reaches requested state |
| Batch 02 | PR-09 | Card chargeback workflow | Acceptance and representment branches tested |
| Batch 02 | PR-10 | Bank fee dispute and case timeline | Bank fee cannot enter card network flow |
| Batch 03 | PR-11 | Concurrency-safe submission idempotency | Same key+payload returns original case |
| Batch 03 | PR-12 | Optimistic concurrency and immutable history | Stale update fails safely |
| Batch 03 | PR-13 | Transactional outbox and local publisher | Case+timeline+outbox commit or roll back together |
| Batch 03 | PR-14 | Kafka transport and consumer inbox | Duplicate event produces one side effect per consumer |
| Batch 03 | PR-15 | Unknown-outcome reconciliation and recovery | Timeout after remote success does not cause second reversal |
| Batch 04 | PR-16 | Ownership and security hardening | User A cannot read/change user B case/evidence |
| Batch 04 | PR-17 | Evidence upload behind private adapter | Only owner/authorized agent can retrieve |
| Batch 04 | PR-18 | Customer UX and E2E quality | Customers can raise each supported dispute |
| Batch 04 | PR-19 | Observability and controlled load tests | Can trace dispute from HTTP to event worker |
| Batch 05 | PR-20 | Container and Kubernetes deployment blueprint | Images build locally |
| Batch 05 | PR-21 | Release readiness and interview demo | Clean-clone setup is reproducible |

## Dependencies and sequencing

- PR-01 is first. PR-02→03 should avoid runtime behavior changes. PR-04 carries temporary route aliases while Angular switches. PR-05 does not use `EnsureCreated`.
- PR-06 creates business types; PR-07 policy; PR-08 debit flow; PR-09 card flow; PR-10 bank fee and customer projections. Tests for finance statuses must precede workflows.
- PR-11 and PR-12 harden concurrency before asynchronous external side effects. PR-13 outbox works without Kafka; PR-14 adds Kafka behind a port; PR-15 hardens external operations.
- PR-16 security hardening must also be continually enforced in previous PRs; it is a dedicated negative-test sweep, **not** the first time security is implemented.
- PR-17 storage is optional for the minimum brief but adds credible secure evidence. PR-18 UX/E2E, PR-19 measured operations.
- PR-20 and PR-21 are deployment/release work; no paid AWS provisioning without explicit authorization.

## Detailed acceptance criteria

### PR-01 — Establish reproducible baseline

**Outcome:** Make the existing starter compile/test reproducibly without changing business behavior.

**Allowed implementation:** Read current .NET/Angular repo; fix only demonstrated build/test blockers; commit frontend lockfile if missing; ensure CI runs actual builds and tests; document exact commands and known limitations.

**Acceptance:**
- [ ] Running baseline README and CI both executes real backend build/tests and frontend build/tests
- [ ] No broken or silently skipped checks
- [ ] Legacy login → transactions → dispute → timeline still works manually

**Not in scope:** No domain refactor, database replacement, feature work, Kafka or AWS.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-02 — Extract pure domain assembly

**Outcome:** Move transaction/dispute enums, state rules and value objects into `ClearCase.Domain` while preserving API behavior.

**Allowed implementation:** Create domain project with no EF/HTTP dependencies; change references and tests; add architecture dependency check; maintain DTO compatibility.

**Acceptance:**
- [ ] Domain project has no infrastructure dependency
- [ ] Existing endpoint and workflow tests stay green
- [ ] New domain transition tests cover allowed and forbidden paths

**Not in scope:** Do not rename all models, add new dispute types or change DB schema.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-03 — Introduce application use-case seams

**Outcome:** Move transaction list/get and create-dispute orchestration out of endpoint methods behind focused application handlers.

**Allowed implementation:** Create `ClearCase.Application`; typed commands/queries, customer context port and repository seam; initially preserve existing EF adapter. Keep auth endpoints unchanged.

**Acceptance:**
- [ ] No direct EF access in refactored dispute endpoints
- [ ] Ownership still enforced server-side
- [ ] Behavior and payloads unchanged with tests

**Not in scope:** Do not refactor login, move persistence, or introduce a mediator framework.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-04 — Version API and normalize contracts

**Outcome:** Introduce `/api/v1` resource routes with typed Angular client and versioned OpenAPI, retaining temporary aliases for legacy routes.

**Allowed implementation:** Add consistent DTOs, correct HTTP codes, error envelope with field JSON pointers/correlation ID, contract tests. Alias old URLs temporarily; centralize UI base URL.

**Acceptance:**
- [ ] Angular uses `/api/v1` end to end
- [ ] OpenAPI reflects routes
- [ ] 401, 403, 404, 409, validation error shape tested
- [ ] Legacy API aliases still work

**Not in scope:** No deletion of old endpoints or new domain business rules.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-05 — PostgreSQL with real migrations

**Outcome:** Switch persistence to PostgreSQL via explicit EF migrations; preserve existing customer demo experience.

**Allowed implementation:** Docker Compose Postgres with env-based connection; EF migration from current schema; local seed only in development; remove `EnsureCreated`; add Testcontainers or disposable Postgres integration tests.

**Acceptance:**
- [ ] Clean database can migrate and seed
- [ ] Existing data read/write tests run against PostgreSQL
- [ ] App starts and demos with documented local commands
- [ ] No secret in repo

**Not in scope:** Do not add Kafka, S3 or AWS deployments; do not claim automatic production migrations are safe.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-06 — Model three dispute types and statuses

**Outcome:** Introduce `cardPurchase`, `debitOrder`, `bankCharge` and separate case/decision/financial statuses.

**Allowed implementation:** Add only required domain types and migration; policy result and clock abstractions; safe compatibility mapping of existing states; seeded read-only data for each type.

**Acceptance:**
- [ ] Transaction category contract exposed
- [ ] Legacy records preserved or migrated
- [ ] No financial status inferred from case approval
- [ ] Type/serialization tests pass

**Not in scope:** No external gateway calls or new customer screens yet.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-07 — Debit order eligibility policy

**Outcome:** Make effective-dated EFT/RM/DebiCheck eligibility decisions independently testable.

**Allowed implementation:** Implement injected policy/clock, mandate facts, reason codes; proposed 60-day window for appropriate collections with date boundaries; configured ≤R2,000 standard vs >R2,000 enhanced **review tier**, with complaint/referral path.

**Acceptance:**
- [ ] Day 1/60/61 cases tested
- [ ] Before/after effective-policy dates tested
- [ ] Valid matching authorized DebiCheck not auto-reversed
- [ ] R2,001 not automatically refused
- [ ] No real ledger posting

**Not in scope:** Do not claim policy is statutory legal advice or automate a bank-core transfer.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-08 — Debit order case processing

**Outcome:** Execute debit-order state transitions via a deterministic mock mandate and reversal adapter.

**Allowed implementation:** Add case handler, idempotent mock operation reference, pending/review/decision states and timeline events, negative path and mismatch handling.

**Acceptance:**
- [ ] Eligible EFT dispute reaches requested state
- [ ] Unknown/mismatch case cannot be shown as reversed
- [ ] Manual-review path works
- [ ] Integration tests cover rejection and duplicate action

**Not in scope:** No real reversal integration, Kafka or general resilience framework.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-09 — Card chargeback workflow

**Outcome:** Represent issuer → network → acquirer/merchant → merchant acceptance or representment → issuer resolution.

**Allowed implementation:** Add `Chargeback` domain record, mock gateway state transitions, scheme reference and example evidence metadata; protect from invalid jumps.

**Acceptance:**
- [ ] Acceptance and representment branches tested
- [ ] Chargeback initiated not equal money credited
- [ ] Merchant rejection is not automatically final customer loss
- [ ] Timeline records events

**Not in scope:** No real Visa/Mastercard API, arbitration claims or hardcoded universal scheme deadlines.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-10 — Bank fee dispute and case timeline

**Outcome:** Complete internal bank-fee review flow and expose unified customer case + financial timeline.

**Allowed implementation:** Add fee policy and mock adjustment, customer timeline projection and minimal Angular type/status rendering for three cases; include accessible empty/loading/error states.

**Acceptance:**
- [ ] Bank fee cannot enter card network flow
- [ ] Three seeded cases visible and accurately labelled
- [ ] Finance pending distinct from confirmed
- [ ] Keyboard-accessible timeline

**Not in scope:** No extra dashboard redesign or document upload feature.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-11 — Concurrency-safe submission idempotency

**Outcome:** Guarantee safe replay of POST dispute requests with same idempotency key and body.

**Allowed implementation:** Persist customer-scoped key + canonical payload fingerprint + response reference; PostgreSQL uniqueness; detect conflicting body; handle simultaneous submissions and eligible active-case uniqueness.

**Acceptance:**
- [ ] Same key+payload returns original case
- [ ] Same key+different payload 409
- [ ] Concurrent requests produce one case
- [ ] No duplicate active case per business rule

**Not in scope:** No Kafka integration, generic idempotency framework or cross-bank transfer.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-12 — Optimistic concurrency and immutable history

**Outcome:** Make case transitions race-safe and timeline/audit records append-only.

**Allowed implementation:** Version column/row guard, service-level transition transaction, immutable timeline sequence with actor/time/reason/correlation; integration tests for racing updates.

**Acceptance:**
- [ ] Stale update fails safely
- [ ] History is append-only
- [ ] No state change without its timeline/audit entry
- [ ] Authorized user required

**Not in scope:** No broker/outbox yet; avoid dual-writing to an external service.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-13 — Transactional outbox and local publisher

**Outcome:** Persist event intent atomically with dispute status and timeline; publish via testable local dispatcher.

**Allowed implementation:** Outbox table with eventId, aggregateId, version, payload, occurredAt, publishedAt, attempts; background publisher interface; crash/retry tests before Kafka.

**Acceptance:**
- [ ] Case+timeline+outbox commit or roll back together
- [ ] Unpublished events survive process restart
- [ ] No event claimed published until acknowledgement
- [ ] Replay test

**Not in scope:** No dependency on production Kafka cluster.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-14 — Kafka transport and consumer inbox

**Outcome:** Connect outbox to Kafka transport with schema-versioned events, inbox dedupe and bounded consumer groups.

**Allowed implementation:** Add Kafka behind adapter; developer opt-in Compose broker; inbox `(consumerName, eventId)` unique key; explicit partitioning by case id and offset/transaction rules; async contract tests.

**Acceptance:**
- [ ] Duplicate event produces one side effect per consumer
- [ ] Restart-before-ack replay is safe
- [ ] Broker-down leaves outbox pending
- [ ] Event version documented

**Not in scope:** No unreviewed schema changes to established events; no exactly-once claim for external money.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-15 — Unknown-outcome reconciliation and recovery

**Outcome:** Recover from external timeouts without submitting duplicate financial operations.

**Allowed implementation:** Stable operationId, mock adapter query/status endpoint, `OutcomeUnknown`, bounded exponential retry+jitter, circuit breaker and manual recovery; reconcile confirmed result; failure simulation tests.

**Acceptance:**
- [ ] Timeout after remote success does not cause second reversal
- [ ] Status remains pending/unknown until confirmed
- [ ] Circuit breaker protects degraded dependency
- [ ] Recovery runbook and failure tests

**Not in scope:** No real financial movement or infinite retry loops.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-16 — Ownership and security hardening

**Outcome:** Prove customer isolation, agent authorization and safe security boundaries across all endpoints.

**Allowed implementation:** Test IDOR, role checks, CSRF, rate-limit, auth expiration and dev-only controls; secure headers/error handling; forbid PII in logs; security review checklist.

**Acceptance:**
- [ ] User A cannot read/change user B case/evidence
- [ ] Unauthorized ops actions rejected
- [ ] CSRF state change blocked
- [ ] Demo-only route absent outside Development

**Not in scope:** No full identity-provider rewrite or real production login integration.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-17 — Evidence upload behind private adapter

**Outcome:** Store and retrieve evidence securely with metadata and limited file types/sizes.

**Allowed implementation:** Add evidence port with local development implementation and private S3-ready adapter; object path scoped by case; authorization/anti-malware integration seam; quarantine/pending scan states.

**Acceptance:**
- [ ] Only owner/authorized agent can retrieve
- [ ] Unsupported type/oversize rejected
- [ ] No public presigned link beyond bounded expiry
- [ ] No contents in DB/logs/events

**Not in scope:** Do not claim live malware scan when only stubbed; no public buckets.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-18 — Customer UX and E2E quality

**Outcome:** Polish three end-to-end journeys and truthful status presentation without duplicating policy in Angular.

**Allowed implementation:** Split oversized UI component into focused features; typed forms, accessible error/timeline elements; E2E for three flows, submission retry and evidence state.

**Acceptance:**
- [ ] Customers can raise each supported dispute
- [ ] Submitting/processing/finance statuses are distinct
- [ ] Network errors clearly handled
- [ ] Critical E2E checks pass

**Not in scope:** No pixel-perfect redesign, new framework or new mobile app.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-19 — Observability and controlled load tests

**Outcome:** Expose actionable telemetry and produce measured test results.

**Allowed implementation:** W3C trace IDs across API/workers, OpenTelemetry metrics, logging filters, readiness/liveness, k6 scenario, dashboard queries and alert thresholds marked as proposed.

**Acceptance:**
- [ ] Can trace dispute from HTTP to event worker
- [ ] No PII in telemetry
- [ ] k6 commands/report checked in
- [ ] No unsupported million-RPS claims

**Not in scope:** Do not add expensive observability service or call load estimates measured facts.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-20 — Container and Kubernetes deployment blueprint

**Outcome:** Make reproducible containers and safe Kubernetes deployment configuration without provisioning production AWS.

**Allowed implementation:** Multi-stage container builds, runtime non-root, health probes, limits, graceful termination, staged secrets config, minimal Kustomize/Helm deploy; ADR for EKS vs simpler option.

**Acceptance:**
- [ ] Images build locally
- [ ] Manifest validation passes
- [ ] No embedded secret or public DB
- [ ] Rollback/feature flags documented

**Not in scope:** Do not create live EKS/MSK/RDS resources or promise 23M concurrent users.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.

### PR-21 — Release readiness and interview demo

**Outcome:** Document and prove the full assessment story with CI gates and reproducible smoke scenarios.

**Allowed implementation:** Finish GitHub Actions quality matrix, dependency/secret scans, migrations validation, smoke script, runbooks, README 10-minute demo, architecture ADRs and known limitations.

**Acceptance:**
- [ ] Clean-clone setup is reproducible
- [ ] All required CI jobs actually pass or named blockers documented
- [ ] Failure demo recoverable
- [ ] Public repo has no credentials/real client data

**Not in scope:** Do not assert production deployment, legal certification or benchmark results without evidence.

**Merge and rollback rule:** Keep prior behavior working. A new schema or public contract must be forward/backward compatible or introduce an explicit bridge/migration; include precise rollback instructions in the PR.
