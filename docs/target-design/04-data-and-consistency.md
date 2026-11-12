# 04 | PostgreSQL data model, invariants, idempotency and consistency

## Decision: ACID core, eventual integration

Inside PostgreSQL, a single transaction commits a dispute, its initial immutable timeline event, idempotency response record and outbox event. This is the **atomic consistency boundary**. Kafka emission and external bank actions are *outside* that DB transaction and are reconciled asynchronously. This avoids distributed 2-phase commit and avoids pretending an external financial system is an ACID participant.

## Illustrative schema (naming is PostgreSQL snake_case)

```sql
CREATE TABLE dispute_cases (
  id uuid PRIMARY KEY,
  customer_id uuid NOT NULL,
  transaction_id uuid NOT NULL,
  dispute_type text NOT NULL,
  reason_code text NOT NULL,
  case_status text NOT NULL,
  financial_status text NOT NULL DEFAULT 'not_started',
  policy_version text NOT NULL,
  version bigint NOT NULL DEFAULT 1,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL,
  resolved_at timestamptz,
  CONSTRAINT dispute_type_check CHECK (dispute_type IN ('card_purchase','debit_order','bank_charge'))
);

CREATE INDEX ix_disputes_customer_created
  ON dispute_cases(customer_id, created_at DESC, id DESC);

CREATE TABLE case_timeline_events (
  id uuid PRIMARY KEY,
  dispute_id uuid NOT NULL REFERENCES dispute_cases(id),
  sequence_no bigint NOT NULL,
  public_status text NOT NULL,
  reason_code text NOT NULL,
  actor_type text NOT NULL,
  occurred_at timestamptz NOT NULL,
  recorded_at timestamptz NOT NULL,
  correlation_id text NOT NULL,
  CONSTRAINT uq_case_sequence UNIQUE (dispute_id, sequence_no)
);

CREATE TABLE idempotency_records (
  id uuid PRIMARY KEY,
  customer_id uuid NOT NULL,
  route_key text NOT NULL,
  idempotency_key uuid NOT NULL,
  request_hash text NOT NULL,
  resource_id uuid,
  response_status integer,
  response_body jsonb,
  created_at timestamptz NOT NULL,
  expires_at timestamptz NOT NULL,
  CONSTRAINT uq_idempotency UNIQUE (customer_id, route_key, idempotency_key)
);

CREATE TABLE outbox_messages (
  event_id uuid PRIMARY KEY,
  aggregate_id uuid NOT NULL,
  aggregate_sequence bigint NOT NULL,
  event_type text NOT NULL,
  schema_version integer NOT NULL,
  payload jsonb NOT NULL,
  occurred_at timestamptz NOT NULL,
  published_at timestamptz,
  attempts integer NOT NULL DEFAULT 0,
  CONSTRAINT uq_outbox_aggregate_sequence UNIQUE (aggregate_id, aggregate_sequence)
);
CREATE INDEX ix_outbox_pending ON outbox_messages(occurred_at)
  WHERE published_at IS NULL;

CREATE TABLE processed_messages (
  consumer_name text NOT NULL,
  event_id uuid NOT NULL,
  processed_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (consumer_name, event_id)
);

CREATE TABLE financial_operations (
  id uuid PRIMARY KEY,
  dispute_id uuid NOT NULL REFERENCES dispute_cases(id),
  operation_type text NOT NULL,
  external_operation_reference text NOT NULL UNIQUE,
  amount_minor_units bigint NOT NULL CHECK (amount_minor_units >= 0),
  currency char(3) NOT NULL,
  operation_status text NOT NULL,
  attempts integer NOT NULL DEFAULT 0,
  last_reconciled_at timestamptz
);
```

Additional tables needed for a real implementation: owned transactions/reference snapshots, chargebacks, mandates, evidence metadata, bank decisions, operational audit events, integration callbacks, policy releases and agent approvals. Use EF Core migrations; migration review and safe expand/contract steps are mandatory.

## Ownership and duplicate scope

Query cases through customer ownership predicate on every access. Do not use `customerId` from request body. PostgreSQL row-level security can be an additional defense-in-depth layer, but does not replace application authorization. `UNIQUE(transaction_id)` may be *too strict* for legitimate lifecycle reopening, split disputes or re-disputes. Instead define permitted active-dispute uniqueness via persisted `active_scope_key`/partial unique index or a separate locked active-case table, agreed with the product owner.

## Write algorithm

1. Authenticate principal and CSRF; normalize/validate payload; calculate canonical hash.
2. Begin PostgreSQL transaction. Acquire/insert idempotency record under `(customer, route, key)` unique constraint; resolve racing requests deterministically (wait for winner or retry select).
3. Verify owned, posted transaction; select effective-dated policy; evaluate eligibility; validate duplicate-active-case rule.
4. Insert `dispute_cases` with version 1 and appropriate status; append timeline `sequence_no=1`; persist idempotency response; append `DisputeSubmitted` outbox event with same business transaction.
5. Commit. Return 201 + `Location`. If same request replays, return stored case; mismatched payload returns 409.
6. An independent publisher reads outbox entries safely (claim rows with `FOR UPDATE SKIP LOCKED` or leasing), publishes and marks `published_at` only upon broker acknowledgment. Publishing may happen twice after a crash; consumers must deduplicate.

## Optimistic concurrency and transitions

Use a command `TransitionCase(caseId, expectedVersion, targetStatus, reason, actor)` and SQL/EF compare-and-swap: `UPDATE ... WHERE id = @id AND version = @expectedVersion`. Zero affected rows → 409/412, not last-write-wins. The aggregate validates allowed transition and actor policy first. Append timeline + outbox in the same transaction. Store `occurred_at` and `recorded_at` separately; sequence number is canonical ordering under clock skew.

## Consuming exactly once in the domain, not in transport

Consumer transaction: `INSERT INTO processed_messages(consumer_name,event_id)` → if unique conflict, no business work; otherwise perform idempotent business write and commit. Broker offset commits **after** DB commit. This gives *effectively-once local business effects*, not globally exactly-once delivery. If remote financial system supports idempotent operation references, always reuse the same reference. Otherwise reconcile status before retry.

## Read consistency, storage and retention

- `GET /disputes/{id}` should read from the authoritative DB or a consistent projection with staleness policy.
- Index `(customer_id, created_at DESC, id DESC)` for queries, `(dispute_id, sequence_no)` for timeline and pending-outbox index.
- Separate public case timeline from security and integration audit logs; enforce append-only permissions where practical.
- Use backups/PITR; actual RPO/RTO need business sign-off; retention and deletion follow financial-services obligations and POPIA controls.
- Financial amounts in integer minor units for ZAR and explicit ISO currency. Different currencies have different minor unit conventions; never assume all currencies have two digits.
- Backfill and expand/contract migration for new statuses; old consumers remain compatible while rollout is in progress.

## Worked race cases

**Two HTTP submissions with the same key:** unique idempotency record serialises outcome → one case returned twice.

**Two different keys on same transaction:** unique active-scope index or aggregate lock returns an explicit `dispute.alreadyExists` conflict to the loser, unless domain permits reopening.

**Worker crashes after financial partner action:** operation remains `outcome_unknown` until status poll/callback and reconciliation; never send a new reversal reference.

**Outbox publisher crashes after broker ACK:** eventual resend of same event ID; consumer inbox suppresses duplicate business effect.
