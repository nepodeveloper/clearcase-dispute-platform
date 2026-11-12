# 09 | Delivery roadmap, definition of complete and Capitec walkthrough

## Goal

Present an honestly built **end-to-end customer experience** while showing senior-level decisions about interfaces, invariants, bank integration, reliability, observability, AWS and developer enablement. The assessment request is for a runnable GitHub repository, so prioritize demonstrable software over aspirational diagrams.

## Plan by increment

### Increment 1 — Foundation and contract alignment (P0)

- Separate projects: `ClearCase.Domain`, `ClearCase.Application`, `ClearCase.Infrastructure`, `ClearCase.Api`, `ClearCase.Worker`, `ClearCase.Tests.*`.
- Enforce architecture dependency direction with project references and a test.
- Rework API to `/api/v1` and OpenAPI, named DTOs, Problem Details pointer errors, idempotency, request validation.
- Migrate SQLite to PostgreSQL, EF Core migrations with seed scripts for mock data.
- Keep Angular frontend working end-to-end and update typed API client, accessibility and error presentation.
- Deliver: customer signs in, sees only own transactions, submits dispute, sees durable timeline.

### Increment 2 — Correct financial domain (P0)

- Add transaction categories `cardPurchase`, `debitOrder`, `bankCharge`.
- Versioned policies including EFT, RM and DebiCheck; transition rules and eligibility reasons.
- Model explicit chargeback initiation, merchant acceptance or representment, decision and financial reconciliation.
- Add separate case vs financial status, evidence metadata, operations agent actions and audit events.
- Deliver: 3 seeded scenarios, each driven through real domain handlers and mock external gateways.

### Increment 3 — Reliability and integration (P1)

- Atomic idempotency record, immutable timeline and PostgreSQL outbox.
- Publisher, consumer inbox, schema-versioned event contracts and deterministic mock adapters.
- Durable financial operation references, reconciliation loop, bounded retry, circuit breaker and unknown-outcome handling.
- Deliver: crash/retry/duplicate-message tests; demo of paused processor and successful resume.

### Increment 4 — Operational quality and AWS (P2)

- Docker Compose (API, Postgres, optional Kafka, local S3 mock where required).
- GitHub Actions for full build/test; SAST/dependency/secret scans and optional E2E.
- Kubernetes manifests / Helm chart or Kustomize with probes, limits, non-root, HPA and network policy design.
- IaC design for VPC, RDS, S3, EKS/ECS choice, broker and telemetry; cost estimate and deployment ADR.
- k6 load profile, alerts/dashboard examples and incident runbook.
- Deliver: measurable reliability evidence and explicit pending work, not fictitious production results.

## Suggested repository structure

```text
ClearCase/
  apps/
    web/                           # Angular, strict TS, feature components
    api/ClearCase.Api/             # thin HTTP mapping, auth, validation
    workers/ClearCase.Worker/      # outbox publishing, notifications, adapters
  src/
    ClearCase.Domain/              # aggregates, policies, invariants, value objects
    ClearCase.Application/         # command/query handlers, DTOs, ports, transactions
    ClearCase.Infrastructure/      # EF Core, Kafka, S3, external adapters
  tests/
    ClearCase.Domain.Tests/
    ClearCase.Application.Tests/
    ClearCase.Api.IntegrationTests/
    ClearCase.Infrastructure.IntegrationTests/
    ClearCase.ContractTests/
    web-e2e/
  docs/
    architecture/
    adr/
    api/
    runbooks/
  deploy/
    docker-compose.yml
    kubernetes/
    aws/
  .github/
    instructions/
    workflows/
  README.md
```

## Risk register

| Risk | Severity | Mitigation |
|---|---|---|
| Fake money movement represented as real refunds | Critical | Separate financialStatus and mock adapter; README disclosure |
| Incorrect dispute eligibility for SA mandate types | High | Effective-dated policy, compliance review and unit boundary tests |
| Duplicate external reversal | Critical | Immutable operation reference, idempotent remote call and reconciliation |
| Unverified startup/build | High | Actual CI and local `dotnet test`, `npm build` proof |
| Feature sprawl / unfinished assessment | High | Deliver core end-to-end flow first; defer nonessential Kafka/MSK/EKS if time constrained |
| Insecure public demo credentials | High | Dev-only identity, no live personal data, secrets scanning and no public real endpoints |
| Over-architected microservices | Medium | Single deployable modular monolith with extraction criteria in ADR |

## Interview walkthrough (10–12 minutes)

**0:00–1:00 — Problem and business outcome.** Explain customer journey, operational banking obligations and why the interface must represent financial status accurately.

**1:00–3:00 — UI and API.** Demonstrate owned transaction list, reason selection, idempotent submission, RFC Problem Details, accessible timeline.

**3:00–5:00 — Domain boundaries.** Walk through card chargeback versus debit order versus bank fee. Demonstrate 60-day and DebiCheck boundaries, R2,000 as illustrative review tier.

**5:00–7:00 — Transaction safety.** Show PostgreSQL transaction and outbox, stable event ID, consumer inbox, optimistic concurrency and external operation reference.

**7:00–9:00 — Failure scenario.** Disable mock external service, show durable pending state, restore service, show reconciliation without duplicate reversal and audit trail.

**9:00–10:30 — AWS and scaling.** Discuss stateless API replicas, RDS connection control, broker partition limits, targeted EKS use, security and monitoring with actual test results if available.

**10:30–12:00 — Leadership and trade-offs.** Explain why a modular monolith is better initially; how ADRs, quality gates, TDD, reusable interfaces, review conventions and mentoring enable engineers around you.

## Questions to anticipate

- Why PostgreSQL and not SQLite or DynamoDB? What consistency level matters?
- What guarantee does Kafka actually provide for external money operations?
- How does a 201 Created differ from a confirmed reversal?
- How do you handle a worker failure after the bank processes an instruction but before the callback arrives?
- Where would you split a microservice and why? What metrics would trigger the split?
- Why is R2,000 configurable, and what if the mandate rules change?
- How do you know a customer cannot read someone else's evidence?
- What tests tell you the platform is reliable, not only functional?

## Interview-ready positioning

> I approached this as a financial-service capability, not merely a CRUD portal. The customer journey, policy decisions, consistency model, integration failure modes, and operational recovery are all designed together. I chose a modular monolith because it preserves strong local transaction boundaries and is easier to understand and test; the external adapters and event contracts allow services to be extracted when workload evidence demands it. The key safeguard is that a dispute decision is not a financial posting. We only report confirmed funds movement after reconciliation, and we can recover from crashes without creating duplicate reversals.
