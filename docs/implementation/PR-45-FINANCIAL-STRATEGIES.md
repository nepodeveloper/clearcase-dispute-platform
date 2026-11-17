# PR-45 — Financial operation strategy extraction

This PR extracts gateway dispatch from `SqliteFinancialOperationCoordinator` into three
provider-specific, injectable strategies. The coordinator retains durable intent
persistence, claim fencing, result recording, state mutation and unknown-outcome
recovery exactly as before. No schema, public HTTP, financial contract or mock
behaviour changes. Production unavailable adapters continue failing closed.

Acceptance: strategy unit tests validate card commands, stable reference forwarding,
debit execution and missing-provider availability; existing durable-intent
integration tests must remain green. This is a seam for further refactoring,
not permission to retry uncertain external payments.

Rollback: revert this PR; no database migrations or operational change.
