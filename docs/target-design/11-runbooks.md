# 11 | Operational runbooks and incident response

## RB-01 — Kafka/broker unavailable

**Trigger:** Broker publishing failures, oldest unpublished outbox age rises.  
**Customer behavior:** If PostgreSQL healthy, accept requests and show `submitted`; never claim downstream processing complete.  
**Actions:** Check broker, network, producer auth and quotas; cap publisher retry rate; watch outbox disk usage; restore broker; let publisher drain at a bounded rate; verify stable IDs/inbox dedupe.  
**Closure:** backlog drains, notification and processing lag under agreed threshold, no duplicate actions, reconcile affected cases.  
**Don't:** delete outbox to hide backlog or reissue finance commands with new IDs.

## RB-02 — Unknown financial reversal outcome

**Trigger:** External bank call timed out or transport disconnected after command submission.  
**Actions:** Freeze reversal submission retries; keep operation `outcomeUnknown`; query the original immutable reference (local demo: `ReconcileReversal`; production: authoritative bank ledger); compare amount, customer, currency and transaction; update only on authoritative match. Escalate mismatches, customer impact and deadline.
**Closure:** `confirmed`, `rejected`, or documented manual exception with audited evidence.  
**Don't:** issue another reversal with a new reference, mark reversed on HTTP 200 or time elapsed alone.

## RB-03 — Database outage

**Trigger:** RDS unavailable or sustained connection failures, pending write errors.  
**Actions:** Mark readiness false on accepting write endpoints, fail with 503 and safe retry guidance; investigate RDS failover, connection exhaustion, locks, disk space; restore, test migrations/consistency, inspect pending idempotency and outbox before resuming write traffic.  
**Closure:** no partial committed cases, health restored, incidents and metrics normal.

## RB-04 — Poison event and DLQ growth

**Trigger:** Repeated schema/validation failure on same event with bounded retry exhaustion.  
**Actions:** Quarantine event with immutable ID/headers and reason (no raw PII in visible alerts); stop impact propagation; determine publisher/consumer version mismatch; fix and contract-test; replay original eventId in staging then production; reconcile case.  
**Closure:** DLQ count cleared by verified processed or explicitly quarantined outcomes, no duplicate side effects.

## RB-05 — Data restore / disaster recovery

**Trigger:** regional or data integrity incident.  
**Actions:** Invoke declared RTO/RPO, freeze risky finance actions, restore infra and PostgreSQL to known point, reestablish identities and KMS access, compare restored cases/outbox with external bank ledger, replay using original IDs and reconcile unknown operations. Validate customer isolation and security before enabling writes.  
**Closure:** documented integrity attestation, controlled restart and stakeholder communication.

## RB-06 — Suspected authorization/data exposure

**Trigger:** Cross-customer access, unexpected evidence download, suspicious agent overrides.  
**Actions:** Contain affected credentials/route, preserve evidence and immutable logs, revoke sessions, coordinate security and compliance response, assess impact/notification obligations, patch and add regression test, review all similar endpoints.  
**Closure:** security sign-off before re-enabling affected route.

## Runbook fields every team should keep

Incident ID, detection time, severity, affected cases, customer impact, rollback decision, data integrity status, alert links, trace/correlation IDs, latest retry positions, escalation owner, remediation record, proof of recovery, and after-action items. Do not put raw personal data or secrets into incident channels.
