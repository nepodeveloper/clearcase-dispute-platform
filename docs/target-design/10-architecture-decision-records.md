# 10 | Architecture Decision Records (ADRs)

Each ADR is a proposed decision requiring project review. Status is intentionally `Proposed` unless implemented and approved. When accepted, record owner, timestamp, alternatives, measurable consequences and superseding ADR.

## ADR-001: Modular monolith first — Proposed

**Context:** Small assessment with a single case-management domain and no proven independent scaling need.  
**Decision:** One deployable ASP.NET Core API with distinct Domain, Application, Infrastructure and API layers. Separate worker when asynchronous processing is real.  
**Alternatives:** independent microservices; single unstructured API.  
**Consequences:** simpler ACID, fewer network hops, stronger testability; eventual service split requires disciplined boundaries.

## ADR-002: Angular + strict TypeScript — Proposed

**Decision:** Keep Angular to leverage demonstrated experience; standalone feature components, typed forms/HTTP contracts, strict TS and accessible UX.  
**Alternatives:** React (role accepts either), Vue.  
**Consequences:** strong conventions and speed; keep portability at API boundary, not by making frontend generic.

## ADR-003: ASP.NET Core 10 and Clean Architecture — Proposed

**Decision:** API thin; application use cases and focused interfaces; domain invariants encapsulated; infrastructure adapters implement ports.  
**Alternatives:** direct EF usage in endpoint handlers; heavy enterprise framework.  
**Consequences:** explicit test seams and boundaries, modest boilerplate investment.

## ADR-004: PostgreSQL as consistency anchor — Proposed

**Decision:** PostgreSQL transaction for dispute, timeline, idempotency and outbox. EF migrations and version guards.  
**Alternatives:** SQLite demo; NoSQL primary.  
**Consequences:** realistic bank-grade concurrency, backup and indexing; requires DB setup in local Compose.

## ADR-005: Transactional outbox and consumer inbox — Proposed

**Decision:** Durable write then async publish; consumers dedupe by eventId; operation references are stable and unique.  
**Alternatives:** synchronous publish in request; direct Kafka transaction spanning DB.  
**Consequences:** publish is eventual, operational monitor needed; no lost event caused by the dual write gap.

## ADR-006: Versioned REST contracts and Problem Details — Proposed

**Decision:** `/api/v1` with plural nouns, GUID identifiers, cursor pagination, stable DTOs and JSON Pointer validation details.  
**Alternatives:** unversioned ad-hoc routes; framework entity serialization.  
**Consequences:** contract clarity, documented migration responsibilities.

## ADR-007: Strategy policies and effective dates — Proposed

**Decision:** Separate card, debit order and fee eligibility strategies; durable `policyVersion` attached to every case.  
**Alternatives:** one monolithic `switch` without policy version.  
**Consequences:** independent rules and auditability; requires policy regression test matrix and bank approval.

## ADR-008: Separate case and money statuses — Proposed

**Decision:** A case can be decided while financial operation remains pending/unknown; settlement updates only from authoritative confirmation.  
**Alternatives:** map `ResolvedApproved` immediately to `Refunded`.  
**Consequences:** avoids misleading clients and supports reconciliation; more states to communicate clearly.

## ADR-009: AWS-target infrastructure with honest local baseline — Proposed

**Decision:** Local Docker Compose and mocked adapters; AWS RDS/S3/ALB/EKS or ECS/MSK blueprint, with actual deployment as separate scope.  
**Alternatives:** forced cloud deployment for the first increment; Azure services despite AWS role.  
**Consequences:** repeatable assessment and role alignment; environment parity needs documented differences.

## ADR-010: Secure same-origin session and bank IdP — Proposed

**Decision:** Production identity delegated to bank-approved OIDC; Secure+HttpOnly cookie, CSRF and scoped auth per case. Development-only test principal distinct.  
**Alternatives:** local password auth shipped to production; browser-stored long-lived tokens.  
**Consequences:** strong default protection; requires IdP integration and session lifecycle management.

## ADR-011: Durable reconciliation for external financial commands — Proposed

**Decision:** Reuse `externalOperationReference`, query unknown outcomes before issuing retry, compare authoritative bank data, audit discrepancy.  
**Alternatives:** blind POST retry after timeout.  
**Consequences:** prevents double financial instructions; eventual pending states and manual reconciliation workloads.

## ADR-012: Evidence kept private and scanned — Proposed

**Decision:** S3 private object with metadata in PostgreSQL; short-lived scoped URLs; malware scan and quarantine before availability.  
**Alternatives:** raw files in public web root, direct DB BLOB without controls.  
**Consequences:** scalable evidence handling with independent lifecycle/retention requirements.

## ADR-013: Observability and SLOs defined before scaling — Proposed

**Decision:** Instrument request→outbox→consumer→adapter traces; measure p95/p99, outbox lag and unknown financial outcomes. Establish and validate SLOs with stakeholders.  
**Alternatives:** choose replicas/partitions based on assumptions or user count alone.  
**Consequences:** justified capacity and cost decisions; tests and dashboards need maintenance.
