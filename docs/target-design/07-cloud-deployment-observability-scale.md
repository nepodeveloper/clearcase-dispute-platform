# 07 | AWS deployment, scaling, observability and delivery

## Assessment environment versus target AWS

**Local runnable assessment:** Angular dev/build, ASP.NET Core API, PostgreSQL, mock bank integrations, Docker Compose, outbox publisher, optional Kafka and notification worker; fake OIDC/test identity in development only. The assessor must be able to run tests without AWS credentials or a paid cloud account.

**Target AWS design:** CloudFront+S3 or same-origin HTTPS frontend; ALB+AWS WAF; EKS for container orchestration if justified by organization capability, otherwise ECS/Fargate is a valid lower-operations alternative; RDS for PostgreSQL Multi-AZ; private S3 evidence bucket + KMS; MSK for Kafka if event volume/operational requirements warrant; CloudWatch/OpenTelemetry; Secrets Manager; IAM roles for service accounts; Route 53 and VPC multi-AZ subnets. No public DB or broker endpoints. Explicitly note that cloud resources are **design proposals**, not already deployed.

## Topology

- **Edge:** HTTPS, HSTS, TLS termination, WAF managed rules, ALB, request IDs and safe rate limits.
- **Compute:** stateless ASP.NET API replicas across at least two AZs; horizontal autoscaler from CPU + latency/request metrics with guarded max replicas. Worker replicas use Kafka partition limits or queued-work metrics, not blindly CPU.
- **Data:** private RDS PostgreSQL, appropriately pooled connections, reviewed indexes and backups; Multi-AZ for availability. Use read replica only if query patterns justify it and stale reads are acceptable.
- **Messaging:** MSK with appropriate broker count and partitioning when deployed; outbox buffered in PostgreSQL during broker outages; bounded retention and monitoring.
- **Storage:** encrypted private S3 + malware scan/quarantine; lifecycle policy aligned with document retention.
- **Security:** IAM least privilege, KMS keys, VPC endpoints, egress restrictions, Secrets Manager, audit logs.
- **Delivery:** build once and promote immutable container digests; database migrations as a controlled release step; canary or rolling deployment and tested rollback.

## Infrastructure as Code conventions

Choose one approved AWS IaC mechanism (Terraform, AWS CDK or CloudFormation). Prefer modules/stacks for networking, runtime, database, messaging, storage and observability. Parameterise environment, region, name prefix, cost limits, retention and replica counts. Use consistent resource naming and tags (`service`, `environment`, `owner`, `dataClassification`, `costCentre`). Never hard-code credentials, subnet IDs or URLs. Apply static validation, drift detection and least privilege. This carries forward the user's previously saved IaC standards at the **principle** level while staying AWS-native instead of duplicating Bicep.

## Performance engineering

First derive credible traffic: e.g., for illustration **1 million dispute page views/day** = about 11.6 average requests/s across 24 hours; 100x peaks would imply ~1,160 requests/s. These are scenarios, **not Capitec's published traffic measurements**. High request volume and high dispute creation volume require separate models.

- Keyset paginate customer transactions/disputes; lazy-load timeline and evidence metadata.
- Index `(customerId, createdAt DESC, id DESC)` and all ownership joins.
- Use ASP.NET async all the way with cancellation; control DB pool sizes and avoid N+1 EF queries via projection and `AsNoTracking` for reads.
- Cache public/reference data, never personal dispute content in shared caches.
- Avoid calling merchant processors synchronously on the UI request path.
- Introduce rate limits per customer and per operation; backpressure on integration workers; cap Kafka consumer concurrency by external throughput.
- Use load tests to find the first saturation point (CPU, RDS connections, broker lag, lock contention) and scale that bottleneck, not every component.

## SLO proposals (to agree with product and operations)

| Indicator | Illustrative target | Measurements |
|---|---|---|
| API availability | 99.9% monthly (proposal, not achieved) | Successful eligible requests excluding planned exclusions |
| Read latency | p95 < 300 ms in selected load profile | `/transactions` and `/disputes` |
| Case creation latency | p95 < 500 ms without processor call | Commit and 201 response |
| Outbox publication age | p95 < 30 s in healthy topology | Committed-at to broker-ack |
| Unknown financial state | 100% accounted for, alert after policy threshold | Age, count and reconciliation progress |
| Security isolation | Zero cross-customer access in tests | API and penetration suites |

Targets above are hypotheses. The README must distinguish planned SLOs from load-tested evidence.

## Telemetry design

**Structured logs:** `timestamp`, `level`, `service`, `environment`, `traceId`, `correlationId`, `eventId`, `disputeId`, `operationRef`, `outcome`, `durationMs`, safe error code. Hash/redact identifying customer data. No dump of evidence or request bodies.

**Traces:** Angular user interaction → API request → DB transaction → outbox publication → Kafka consumption → adapter call → reconciliation; W3C context through Kafka headers.

**Metrics:** endpoint requests/latencies, 4xx vs 5xx, rejected owner attempts, DB pool and lock wait, outbox backlog/age, consumer lag, DLQ count, external timeouts, unknown financial operations, reconciliation mismatches, agent decision throughput.

**Dashboards & alerts:** separate customer-experience and financial-integrity dashboards. Page on irreversible integrity risk, alert on sustained queue age, warn on degraded dependency. Correlate incidents across trace and case without exposing customer PII.

## CI/CD quality gates

1. Restore using lockfiles → format and static analysis for .NET and TypeScript.
2. Unit tests + architecture rules + API/OpenAPI compatibility checks.
3. PostgreSQL-backed integration tests and mock adapter contract tests in containers.
4. Frontend tests and browser E2E; accessibility audits.
5. Dependency/secret/container/IaC security scanning.
6. Build immutable container image and SBOM; deploy staging, run smoke and migration tests.
7. Manual approval or policy gate for production; canary rollout, health/error and rollback thresholds.

Avoid nondeterministic CI dependent on public bank endpoints or unstubbed live financial systems. Deploy database expand steps before application rollout and contract steps only after all consumers upgraded.

## Disaster recovery and operational availability

Set RTO and RPO with the business; don't assume zero loss. Enable RDS PITR/backups, backups for infrastructure and configuration, S3 versioning where applicable, regular restore drills and multi-AZ placement. Recovery requires **both** data restore and financial reconciliation with external ledgers before reopening write paths. A restored copy of the dispute DB may have older state than external banking operations. That difference must be treated as a financial integrity incident, not blindly replayed.
