# Production readiness register

What the repository now provides, and what must still be decided or built before real customer data or money is involved.
CI green is evidence of engineering quality, not banking readiness.

## In place

| Area | Control |
| --- | --- |
| Security | Cookie auth with CSRF, role policies, global per-user/IP rate limit plus login limit, security headers, fail-closed startup if the test-only auth fallback is enabled outside Testing. |
| Proxy awareness | `Security:TrustedProxyIps` and `Security:TrustedProxyNetworks` (CIDR) so client addresses survive a load balancer. |
| Resilience | Idempotent submissions, optimistic concurrency, transactional outbox, durable financial intents that are never auto-resent. |
| Observability | OpenTelemetry traces and metrics (opt-in OTLP export), correlation ID on spans, health probes. |
| Delivery | Locked NuGet restores, merged coverage gates, container images, Helm chart with kind smoke test, SBOM/provenance packaging, CodeQL, Dependabot. |
| Operations | Migration job mode (`--migrate-only`), persisted data-protection keys, runbook. |
| Architecture | Per-category command processors, central domain-exception mapping, server-driven staff actions, compiled dependency-rule tests. |

## Still required (needs a decision, infrastructure or product work)

| Gap | Why it blocks production | Suggested next step |
| --- | --- | --- |
| Identity provider and MFA | Local Identity accounts only. | OIDC (for example Entra ID) with role mapping from claims. |
| Shared database | SQLite allows one API writer; no HA. | PostgreSQL provider, then scale the API and split workers. |
| Maker-checker | One agent can approve and execute money movement. | Second-approver rule above a configurable amount, audited. |
| Real banking adapters | Adapters are mocks or "unavailable". | Implement gateways behind the existing ports; keep calls outside dispute transactions. |
| Data protection and retention | Dispute text is stored in plaintext; no erasure or retention policy. | Encryption at rest, retention rules, access-read auditing (POPIA/GDPR). |
| Case assignment and SLAs | Agents see every case. | Assignment, ownership and SLA timers. |
| Customer notifications and evidence | No email/SMS/push, no uploads. | Notification outbox consumer; evidence store with malware scanning. |
| Deployment automation | Chart is validated on kind only; release workflow is a simulation. | Environment-specific IaC, approvals, backup/restore rehearsal, canary and rollback. |
| Domain model | `Dispute` is a factory; transitions live in lifecycle/workflow records and EF rows are mutated directly. | Rehydrate an aggregate root, use typed commands and constraint-name conflict detection. |
