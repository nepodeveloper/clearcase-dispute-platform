# PR-47 — Consumer ports, DI lifetime checks and isolated financial orchestration tests

Depends on PR-46 (and PR-45). This PR keeps the old
`IFinancialOperationCoordinator` as an inherited compatibility interface
but provides narrowly scoped `IFinancialOperationExecutor`,
`IFinancialOperationScheduler` and `IFinancialOperationRecovery` ports.
HTTP application handlers depend on only execution; the background worker
resolves recovery in a fresh DI scope. All ports resolve to **one scoped
coordinator instance per request**, never a singleton DbContext.

`FinancialOperationSeamTests` uses handwritten fakes only; it checks a
recorded result is replayed without provider calls, an ambiguous provider
timeout records Unknown, a lost lease does not mutate a case, missing
providers fail closed before scheduling and scoped DI correctly reuses
the same coordinator within one scope.

Run the full backend and frontend CI, the existing real EF migration and
financial operation recovery integration tests, and examine failed/
repeated commands before merge. PRs must be merged in order (45→46→47).

No added NuGet packages, API changes, schema migrations, cloud resources
or financial side effects. Rollback from PR-47 before PR-46 and PR-45.
