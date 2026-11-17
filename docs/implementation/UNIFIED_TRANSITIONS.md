# PR-39: unified dispute lifecycle and persistence

## Boundaries

- **Domain**: `DisputeLifecycle` validates cross-category invariants and computes the next
  case version and immutable public timeline event. DebitOrderState, BankChargeState
  and Chargeback remain category-specific pure state machines. The domain also
  issues canonical SpecialistReview and SpecialistReviewComplete transitions.
- **Application**: handlers perform eligibility, authorization and mock-adapter
  coordination, returning category-specific transition results; no EF dependencies.
- **Infrastructure**: `DisputeTransitionPersistence` owns one database transaction,
  version/active-escalation guard, the Domain lifecycle projection, case write,
  append-only timeline, outbox and employee audit. Each category store only maps
  category-specific changes/chargeback metadata and delegates to the shared writer.
  Supervisor escalation uses the same Domain lifecycle for referral/resolution but
  retains its separate idempotent Supervisor audit transaction.

## Invariants

- A closed case cannot be reopened by category workflows.
- A rejected case cannot be resumed except closure.
- Confirmed financial status cannot be downgraded.
- A new confirmed financial status requires an approved decision.
- An active Supervisor escalation suspends processing.
- Writes use the existing optimistic case version; a concurrency conflict produces
  the established 409 response.
- Timeline sequence, aggregate version, outbox version and employee audit are
  committed atomically. The public event never contains private Agent/Supervisor notes.
- External adapter invocations remain outside the database transaction via PR-35.

## Compatibility / rollout

No schema migration, routes, idempotency header, outbox envelope version or public
status vocabulary changes. Existing integration tests cover all three categories,
Supervisor review, competing writers and financial intent/recovery. The new Domain
tests cover canonical event naming/versioning and forbidden cross-category changes.

Rollback is code-only; retain existing records and outbox history. Do not attempt
a data rollback to a previous case version.
