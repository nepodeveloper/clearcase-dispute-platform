# PR-42: parked outbox events and audited operational recovery

## Why the dispatcher parks messages

The at-least-once outbox from PR-33 still retries broker failures safely,
but an unrecoverable message could otherwise retry forever. PR-42 parks an
event after **8 failed delivery attempts**. The dispatcher durably records
`ParkedAt` and sanitized `ParkReasonCode=delivery.attemptsExhausted`
and clears the lease/retry schedule. It **does not mark the message
published or delete its payload**.

This is intentional: a parked earlier version blocks later unpublished
events for the *same dispute*, preserving per-aggregate order. Other
disputes continue dispatching. New events on a blocked dispute may be
persisted but cannot overtake the parked version.

Metrics via `ClearCase.Outbox`:
- `clearcase.outbox.retried`: eligible retry scheduled
- `clearcase.outbox.parked`: durable parking after exhaustion
- `clearcase.outbox.requeued`: authorized recovery action

Configure a metrics exporter to scrape these counters. Alert on a parked
message, increasing retry counts, or growing oldest unpublished age.

## Supervisor-only API (synthetic local assessment)

`GET /api/v1/supervisor/outbox/parked?limit=50`

Returns at most 100 safe records: event ID, aggregate ID/version, event
type, attempts, time and sanitized reason. **No raw payload, correlation
token, customer information or private notes** are returned.

`POST /api/v1/supervisor/outbox/{eventId}/requeue`

Requires a current Supervisor cookie, CSRF header and JSON body
`{"reason":"Document the verified broker state and why replay is safe."}`
with 15–500 characters. The server obtains employee identity only from
the authenticated cookie; the request cannot supply or impersonate it.
A successful 200 resets the attempt budget and makes the same stable
event ID eligible for delivery. A second attempt returns 409. Each
successful manual requeue appends a durable employee+reason audit.

Do **not** requeue simply because an event has been parked. Before
authorization:
1. Verify whether the broker accepted the last attempt, including an
   ambiguous client timeout. Check consumer inbox/deduplication records.
2. Investigate and remediate source failures, schema changes, security
   credentials or downstream outages.
3. Confirm consumers deduplicate by stable event ID and the event remains
   correct for its aggregate version.
4. Document the investigation and recovery approval in the audit reason.
5. Requeue once, monitor the subsequent delivery and related case events.

No operator endpoint marks a message published, changes its event ID, or
deletes its row. Do not edit the database to bypass ordering.

## Migrations and recovery

Migration `20261117100000_AddOutboxParkingAndRecoveryAudit` is additive
and follows the 18 existing migrations; the resulting schema has 19.
Back up SQLite and retain event payloads, pending leases and audit rows
before upgrading. Existing rows start unparked. The migration can
technically be downgraded after stopping workers, but **do not drop
parking/audit metadata while any unresolved parked messages exist**.
Resolve or archive operational records in an approved recovery plan.

Tests cover eight failed sends, durable parked state, lease fencing,
successful audited requeue and replay, as well as authorization boundaries.
