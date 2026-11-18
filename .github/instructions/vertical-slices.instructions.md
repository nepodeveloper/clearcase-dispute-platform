---
applyTo: "apps/api/src/**/*.cs,apps/api/tests/**/*.cs"
---
# ClearCase vertical-slice conventions

- Organize API commands/queries and application handlers by a concrete business
  use case (`Features/Disputes/SubmitDispute`, `GetDispute`, `ListDisputes`, etc.).
  Use focused command/request, handler, validation, response and endpoint types.
- Preserve inward dependencies: API maps transport; Application orchestrates; Domain
  owns eligibility/invariants; Infrastructure owns EF and external adapters.
  Never use DbContext directly in API or Application financial command slices.
- Reuse one canonical case transition writer, immutable timeline, version checks,
  employee audit, durable financial intents and transactional outbox. Never
  implement financial settlement as an HTTP return value.
- Customer identity comes from trusted session context, not request body.
  Every customer endpoint has explicit auth; mutating cookie endpoints require CSRF.
- Validation belongs beside the slice; map errors consistently through ApiProblem
  and the existing RFC Problem Details envelope. No unreviewed new NuGet dependencies.
- Keep aliases, route names, OpenAPI, contract tests, pagination, cursor and
  idempotency behavior stable during organization-only PRs.
- Add tests for changed behavior, not only folder existence. Run .NET integration
  tests, migrations, Angular tests and E2E before merging.
- `scripts/ci/architecture_guard.py` is a fast safety net, not a proof of
  absence of coupling or a substitute for code review.
