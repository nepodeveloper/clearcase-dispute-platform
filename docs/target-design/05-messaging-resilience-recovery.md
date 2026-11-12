# 05 | Kafka, resilience, recovery and reconciliation

## Why asynchronous messaging exists

Accept dispute submissions quickly while decoupling notifications, integrations and reporting. Use Kafka only where it improves real behavior; for the smallest runnable assessment, a PostgreSQL outbox + background worker and mock adapters is sufficient. An AWS MSK deployment is an evolution step. Kafka brokers do **not** make external financial systems transactionally consistent.

## Event contract and conventions

Envelope fields: `eventId` (UUID), `eventType`, `eventVersion`, `aggregateId`, `aggregateVersion`, `occurredAt`, `correlationId`, `causationId`, `producer`, `payload`. Published events describe a completed fact, e.g. `dispute.submitted.v1`, `chargeback.initiated.v1`, `debit-order.reversal-requested.v1`, `dispute.resolved.v1`. Distinguish domain events from commands; do not publish unapproved financial instructions as if they are settled facts.

Topic examples: `disputes.case-events.v1`, `disputes.operations.v1`, `disputes.notifications.v1`, `disputes.dead-letter.v1`. Partition key: `disputeId` for in-order events *within a partition*; consumers still verify aggregate version. Retention, replication, schema registry, partition count and DLQ operation need workload-specific benchmarks and runbooks.

## The six engineering questions

### 1. What if a dependent service goes down?

Use explicit client timeouts, cancellation, bounded retry with exponential backoff and jitter for **transient safe** calls, circuit breaking, concurrency bulkheads, readiness/liveness, and durable `PendingExternalProcessing` state. Do not let an external outage block reading historic cases. Show a truthful pending status and ETA only if backed by agreed SLA.

### 2. How do we prevent duplicate messages?

Every producer event carries a stable `eventId`. Every consumer writes `(consumerName,eventId)` to an inbox/processed table in the same transaction as its effects. Unique external operation references prevent double bank reversals. Outbox may publish more than once; broker producer idempotency is supplementary, not a replacement.

### 3. How do we handle millions of requests?

Start with traffic estimation: registered users ≠ concurrent users. Bound and paginate APIs, horizontal scale stateless API replicas, cache safe reference data, index high-volume customer queries, pool DB connections, scale read capacity carefully, control Kafka lag, rate limit per principal/client, implement backpressure and measure p95/p99. Avoid uncontrolled parallelism that overwhelms RDS.

### 4. How do we ensure consistency?

ACID within PostgreSQL for case/timeline/idempotency/outbox, optimistic concurrency on transitions, clear aggregate ownership, idempotent consumers, eventual consistency across Kafka and bank core, durable financial operations with authoritative reconciliation. Use saga-style orchestration only if there are genuinely multiple independently committed external steps and a documented compensation strategy.

### 5. How do we recover from failures?

Exponential backoff for bounded transient retries; operational DLQ for poison messages; replay only after defect resolution and with stable IDs; scheduled reconciliation for unknown external outcomes; point-in-time restore and tested runbooks; SLO-driven alerts on backlog, failures and aged unknown states. Test DB and broker failures deliberately.

### 6. How do we keep it secure?

OIDC sessions with secure cookies, CSRF, authorization per object and operation, least privilege IAM, network segmentation, encryption and secret rotation, signed/verified inbound webhooks, PII-safe logs, evidence scanning, auditable operations, secure CI dependencies and image signing/pinning as feasible.

## Resilience matrix

| Component | Failure mode | Expected behavior | Verification |
|---|---|---|---|
| PostgreSQL writer | unavailable | Reject new writes with 503; do not claim submitted; read-only degraded path only if safe | Kill DB and check response + persistence |
| Kafka broker | unavailable | Case creation succeeds if DB healthy; outbox accumulates; alert on age; publisher retries | Pause broker, then restore, count exactly one business effect |
| Outbox publisher | crashes after ACK | Re-publishes identical event ID; consumer inbox deduplicates | Kill publisher after publish before mark |
| External mandate API | timeout after processing | Mark `outcomeUnknown`, query by stable reference; do not blindly reissue | Simulate remote success/local timeout |
| Merchant/processor API | 429/503 | Respect Retry-After where safe, backoff, circuit breaker | Latency and rate tests |
| Evidence scanner | down | Uploaded file quarantined and inaccessible; case processing may continue only if permitted | Simulate malicious/pending scan |
| Notification service | down | Outbox/event retained; customer can still see accurate timeline | Disable notification consumer |
| Zone outage | one AZ lost | Replicas spread across AZs; RDS Multi-AZ; verify failover | Controlled resiliency game day |
| Consumer poison event | invalid schema | Quarantine with reason and alert; do not block entire partition indefinitely | Malformed event test |

## Retry classification

- **Safe to retry automatically:** GET by stable ID; idempotent POST when remote contract guarantees same operation reference; transient broker publish with unchanged event ID.
- **Retry only after querying outcome:** bank reversal, chargeback submission, settlement instructions where remote response is unknown.
- **Do not retry automatically:** authentication/authorization failures, permanent validation errors, 4xx business rejections, invalid message schema without fix.
- **Operational escalation:** exhausted retries, deadline close, recon mismatch, evidence content classified as unsafe, unknown ledger outcomes beyond agreed SLO.

## Reconciliation algorithm

For each `financial_operations` entry in `outcome_unknown`/`pending_confirmation` older than the retry policy threshold: query bank core by **original immutable `external_operation_reference`**; compare amount, currency, customer/account scope and status; commit local transition and immutable audit/outbox only on verified matching data; alert/hold manual review on inconsistent or conflicting results. A webhook is evidence, not an unconditional truth—validate signature, replay age and authoritative reference.

## Recovery and DLQ playbook

1. Alert identifies consumer/topic/partition/offset/correlationId and affected case count, without exposing account details.
2. Classify transient vs poison vs external ambiguous financial outcome.
3. Contain: pause only affected consumer or switch relevant integration to pending state, preserving intake where safe.
4. Fix and deploy with feature flag/canary; verify schema compatibility.
5. Replay with original event IDs and stable operation references, apply inbox deduplication.
6. Reconcile source-of-truth case and financial ledger. Produce an auditable incident and follow-up test.

## Observability for messaging

Metrics: outbox oldest-unpublished age; `pending_outbox_count`; consumer lag by topic/partition; retry rate; DLQ depth; `financial_outcome_unknown_age`; status transition failures; processor dependency latency; end-to-end from submitted to externally acknowledged. Alerts require agreed thresholds and on-call response; don't publish a hard-coded aspirational SLO as achieved fact.
