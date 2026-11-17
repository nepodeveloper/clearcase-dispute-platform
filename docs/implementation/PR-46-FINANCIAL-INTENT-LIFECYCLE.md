# PR-46 — Extract durable intent persistence and recovery

This PR is stacked on PR-45. The compatibility coordinator now orchestrates
`IFinancialIntentStore` (durable scheduling, claim fencing, recovery search,
record/complete) and `IRecordedFinancialOutcomeApplier` (case/timeline/outbox
transitions), instead of mixing EF code with provider execution.

The persisted schema, table names, transaction boundaries, two-minute lease,
stable operation reference, version checks and the unknown-outcome rule are
unchanged. An expired Dispatched lease becomes Recorded/Unknown and is **never**
sent again; a ResultRecorded claim replays the result without reissuing the call.
No automatic retries of ambiguous financial operations.

Verification: run existing `FinancialOperationRecoveryTests`, dispute case/route
tests and all CI gates before merging. PR-47 adds isolated orchestration tests
and consumer-facing DI/architecture checks.

Rollback: revert this PR after reverting dependent PR-47; no migration required.
