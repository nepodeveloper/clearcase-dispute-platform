# ADR 0004: Database-backed idempotency for dispute submission

- Status: Accepted
- Date: 2026-11-14

## Context

Customers retry submissions after timeouts, double clicks or flaky mobile networks. The filtered open-case unique index already prevents a second active case, but a retry after a lost `201` surfaced as `409 dispute.activeCaseExists` instead of the original case, and a retry after the case closed could open a new case.

## Decision

`POST /api/v1/disputes` accepts an optional `Idempotency-Key` header (1-128 visible ASCII characters). A `DisputeSubmissions` row with primary key `(CustomerId, IdempotencyKey)` stores a SHA-256 fingerprint of the normalized payload (GUID-formatted transaction ID, trimmed reason and description) and the created dispute ID. It is inserted in the same `SaveChanges` transaction as the case and timeline.

- The key is looked up before eligibility is re-evaluated. A matching fingerprint returns `201` with the original case's current representation and `Idempotent-Replayed: true`. A different fingerprint returns `409 dispute.idempotencyKeyReused`.
- Concurrent requests race on database constraints, not in-memory checks. A losing request re-reads the key and replays the winner's case. Without a matching key, it reports `activeCaseExists`.
- Only successful creations are stored. Validation and policy rejections are recomputed on retry because they have no side effect.
- Keys are scoped to the authenticated customer and compared case-sensitively. The Angular form sends one UUID per draft, reuses it for an unchanged retry and rotates it when the draft changes.

## Consequences

- Requests without a key keep the previous behavior. Requiring the header is a separate, breaking follow-up.
- Keys never expire yet. Retention and purge rules are deferred.
- SQLite serializes writers. PostgreSQL migration requires re-verifying the constraint-violation mapping and concurrency tests.
- Rollback: revert the code and run the down-migration to `AddIdentityAuthenticationFoundation`. That drops `DisputeSubmissions` without touching cases.
