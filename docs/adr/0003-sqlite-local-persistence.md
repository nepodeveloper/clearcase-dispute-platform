# ADR 0003: SQLite for local transaction persistence

- Status: Accepted
- Date: 2026-11-12

## Context

The transaction listing, details and eligibility decisions used fixed in-memory data. The local demo needs durable relational persistence without Docker, administrative privileges or an external server. PostgreSQL remains a possible future production choice, not a requirement for this slice.

## Decision

Use EF Core 10 with its SQLite provider in Infrastructure behind the existing application transaction-reader port. Store a configurable database file under the API content root by default, outside the web root. Use checked-in SQLite-specific migrations, UTC ticks, integer ZAR cents, customer-scoped SQL queries, a GUID primary key and indexes for the existing access patterns. The schema limits amounts to ZAR until currency-specific scales are designed. No customer table or speculative dispute tables are introduced.

Development applies migrations and idempotently inserts missing synthetic transactions; it does not overwrite existing records. Production does not automatically create, migrate or seed a database. Explicit migration commands and a disposable-demo reset procedure are documented in the README. A repository-local EF tool avoids global tooling.

## Consequences

- Existing HTTP contracts and eligibility rules remain unchanged; SQLite data survives restarts.
- Foreign keys are enabled and busy/locked operations have a bounded timeout. WAL improves reader/writer coexistence but SQLite has one writer and needs reliable local filesystem locking.
- SQLite files and sidecars require filesystem protection and consistent backups. The demo identity is not suitable for real customer data.
- The provider, migrations and WAL are not interchangeable with PostgreSQL. Any production transition requires separately tested schema/data migration, concurrency, backup, rollback and operational plans.
- No dispute submission, financial posting or external banking integration is introduced.
