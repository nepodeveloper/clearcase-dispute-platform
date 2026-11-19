# Operations runbook

Applies to any deployment. Replace the placeholders with your environment details.

## Health and signals

| Signal | Where | Meaning |
| --- | --- | --- |
| `/health/live` | API | Process is responsive. Restart if it fails. |
| `/health/ready` | API | Database reachable and no pending migrations. Traffic is withheld while it fails. |
| `clearcase.outbox.parked` | OTel metric | Events failed repeatedly and need a Supervisor decision. Alert when above 0 for 15 minutes. |
| `clearcase.kafka.failed` / `.quarantined` | OTel metric | Consumer errors or malformed records. |
| `correlationId` | Problem Details, logs, traces | Quote it in every incident; it ties a request to its traces and audit rows. |

Telemetry is exported only when `Observability:OtlpEndpoint` (or `OTEL_EXPORTER_OTLP_ENDPOINT`) is set.

## Migrations

Run `dotnet ClearCase.Api.dll --migrate-only` before starting a new version (the Helm chart does this in an init
container). Migrations are forward-only: take a backup first, and roll back by restoring it.

## Financial operation with an unknown outcome

1. Do **not** retry the operation. A timeout or ambiguous send is recorded as `OutcomeUnknown`.
2. Open the case as an Agent or Supervisor and use **Reconcile reversal**. It asks the gateway for the status of the
   original operation reference and never sends a second request.
3. If the gateway still reports unknown, escalate to a Supervisor and reconcile with the provider's records.

## Parked outbox events

A Supervisor lists them via `GET /api/v1/supervisor/outbox/parked`, confirms the broker received (or did not receive)
the message, then requeues with a written reason via `POST /api/v1/supervisor/outbox/{id}/requeue`. Every requeue is audited.

## Backup and restore (SQLite)

- Back up `/data/clearcase.db` together with `-wal` and `-shm`, using `sqlite3 .backup` or a volume snapshot taken while
  the API is stopped or quiesced. Also back up `/data/keys` so sessions survive a restore.
- Rehearse restores on a schedule; record the achieved RTO/RPO. Targets are stakeholder-defined.
- After a restore, reconcile financial operations against provider records before reopening traffic.

## Rollback

Redeploy the previous image digest. If the new version applied a migration, restore the pre-deployment backup first.

## Incident checklist

Confirm readiness, check parked/unknown counts, capture a `correlationId`, check the audit timeline for the case, and
only then change data. Never edit audit or timeline tables: database triggers reject updates and deletes by design.
