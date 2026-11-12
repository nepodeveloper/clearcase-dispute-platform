# ClearCase — coding agent contract

**Project:** `clearcase-dispute-platform` (Capitec technical assessment, independent project).  
**Delivery model:** one small, independently mergeable PR per task; never big-bang redesign.  
**Source of truth:** existing working implementation + the target-design documents under `docs/target-design/` + `docs/implementation/PR_ROADMAP.md`. A target-design feature is **proposed until a test and executable code prove it works**.

## First actions — before modifying anything
1. Read `README.md`, `AGENTS.md`, `.github/instructions/*`, `docs/implementation/PR_ROADMAP.md`, the selected ticket, and relevant target-design sections.
2. Inspect repository structure, Git status, SDK/package versions, current tests, routes, and migration history. Do not assume examples from docs already exist.
3. Verify baseline with `dotnet --info`, `dotnet restore backend/CardDisputes.sln` (if solution remains at that location), `dotnet build ...`, `dotnet test ...`, `npm ci` (when lockfile exists), `npm run build`, `npm test`. Report unavailable tools or reproducible failures; do not claim unexecuted commands passed.
4. Before code, write a compact implementation plan with paths to change, tests to add first, expected compatibility, security/finance impact, and rollback.

## PR execution loop
1. Work only on **the assigned PR ID**. Branch `feat/pr-XX-short-name` or `chore/pr-XX-short-name` from up-to-date default branch.
2. Write or update a failing test for the behavior before implementation when practical. Run red → green → refactor; use AAA and deterministic clock/test data.
3. Modify the smallest coherent vertical slice. Keep API + frontend + tests together **only where required to keep the slice deployable**. Prefer <400 meaningful changed lines (exclude generated migration code and tests); split larger changes with safe seams.
4. Ensure current application and existing customer flow remain working. No undocumented breaking routes, schema, state, config or event changes. Use temporary backwards-compatible adapters as necessary, then retire separately.
5. Verify touched functionality and relevant regression tests. Update OpenAPI/AsyncAPI, ADR, README, screenshots/runbook only when affected.
6. Provide a PR summary containing **purpose, implementation, files, tests actually run and results, risks, migration/rollback, demo steps, deferred work**.
7. Stop after that PR. **Do not begin the next PR**. Open a draft PR only when repository permissions are available and the user authorized the coding agent to push; otherwise deliver branch/patch instructions. Never claim a PR was created without tool confirmation.

## Architecture and business invariants
- Start as **one deployable modular monolith**, with **source dependencies pointing inward**: API and Infrastructure depend on Application, Application depends on Domain, and Domain depends on neither. Keep API mappings thin. Do not extract microservices or provision AWS to impress reviewers. Document extraction criteria.
- `DisputeCase` is not a ledger. `caseStatus`, `decisionStatus`, and `financialStatus` are distinct; money moves only through an authorized adapter and is **confirmed** only after authoritative external result/reconciliation. Current assessment uses simulated banking integrations, never real settlement.
- Support `cardPurchase`, `debitOrder`, `bankCharge`. Card disputes may progress through chargeback, merchant acceptance/representment and issuer decision. Bank fees are internal. Debit orders require mandate type, effective-dated policy, verified collection details, and bank-core processing.
- April 2026 60-calendar-day debit-order window is a versioned **proposed policy rule pending operational/legal confirmation**, not a universal substitute for every scheme/mandate rule. R2,000 is an **illustrative configurable internal review threshold**, not a legal maximum; never block all larger complaints.
- A case may only be accessed by its authenticated customer or an explicitly authorized bank agent. Derive customer identity from trusted authentication, never request JSON.
- No duplicate active case for the same constrained dispute scope; repeated requests and Kafka events must not duplicate effects. Use database constraints plus idempotency rather than in-memory checks.
- A state change validates transitions, policy, required evidence and auth; atomically records new state/version, immutable timeline, audit and outbox intent where appropriate.
- An external reversal with a timeout has an **unknown outcome**, not an assumed failure. Reconcile using a stable external operation reference before retry; never blindly issue a second reversal.

## HTTP / contract rules
- Use `/api/v{majorVersion}/{plural-resource}` (e.g. `/api/v1/disputes`), plural nouns, kebab-case multiword paths, GUID IDs, legitimate parent-child routes, correct HTTP verbs. Commands that cannot be modeled as CRUD can use named POST subresources.
- camelCase JSON, explicit DTOs, optional fields omitted when absent, defined empty collections as `[]`, ISO-8601 UTC timestamps, money as minor units + ISO currency (or explicit decimal-string contract). No EF/domain entities exposed as HTTP DTOs.
- Pagination: stable ordering, bounded page size, opaque cursor and `pageInfo` for large collections.
- RFC 9457 / Problem Details with stable `code`, `correlationId` and per-field errors as `{pointer, code, message}` using JSON Pointer; map 400/401/403/404/409/422/429 deliberately. Do not leak sensitive internals.
- Use `Idempotency-Key` for side-effecting dispute creation. Same key + same payload returns prior result; same key + changed payload returns 409. Concurrent requests must be safe via unique constraints and transactions.
- Keep OpenAPI and versioned event contracts in sync with implementation; protect migration compatibility.

## Code quality conventions
- C#/.NET: SOLID, SRP, encapsulation, DI, narrow ports, named handlers, minimal parameters, async I/O with `CancellationToken`, `TimeProvider` for time-dependent logic, explicit errors, no swallowed exceptions.
- Angular/TypeScript: strict TS, typed forms/HTTP, single quotes, strict equality, explicit null checks, accessible semantic controls, reactive patterns appropriate to Angular, component-sized features; server owns authorization and dispute policy.
- Testing: fast unit tests for invariants; integration tests against real PostgreSQL (Testcontainers preferred); API ownership/CSRF/idempotency tests; frontend component/contract/E2E; failure and replay tests for Kafka/external effects.
- Security: httpOnly same-origin cookies + CSRF as implemented; least privilege, private evidence S3, allow-list file types/size, no raw PII or credentials in events/logs, audit actor and correlation ID. Development-only demo controls must not exist in production.
- Observability: W3C trace context, structured logs, correlation IDs, metrics and readiness/liveness probes. Failure alerts and runbooks; no sensitive values in telemetry.
- AWS target: PostgreSQL on RDS, container app (EKS only if justified), S3 private evidence, Kafka-compatible broker where required. Use IaC and least privilege. No real paid infrastructure provisioning without explicit instruction.

## Scope and honesty controls
- No new library, service, Kafka cluster, AWS infrastructure, auth provider or blanket rewrite unless the selected PR needs it; note dependency/license/operational trade-off.
- Never delete existing tests to obtain green CI, bypass validation, expose customer data, or set fake finance statuses to `Confirmed`.
- If pre-existing build is broken, first PR fixes **only** the smallest blocker; record other issues as follow-ups.
- Do not claim production-ready or millions of users supported without measured results; document assumptions and verification gaps.
- Every PR merges cleanly, is independently demoable, and can be rolled back safely; database and event compatibility require special care.
