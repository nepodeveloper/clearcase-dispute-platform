# Vertical Slice Architecture adoption checklist (PR-45 to PR-49)

**Scope:** Organize by business use cases without changing APIs, data model, routes,
service lifetime, authentication, transactional outbox, financial status or version guards.
Reference: Milan Jovanović, [Structuring Vertical Slices](https://milanjovanovic.tech/blog/vertical-slice-architecture-structuring-vertical-slices).
Build the slices *around* protected Application/Domain/Infrastructure boundaries,
not by moving EF persistence into HTTP endpoints.

- [x] **PR-45:** Isolate `SubmitDispute` HTTP endpoint, command and handler; preserve
  aliases, Idempotency-Key, customer scope, CSRF, RFC Problem Details and outbox.
- [x] **PR-46:** Extract dispute read/query slices and eligibility query without
  changing cursor semantics, read ownership or status projection.
- [x] **PR-47:** Isolate card, debit-order and bank-fee command entrypoints while
  retaining canonical domain transitions, version checks and financial intent fencing.
- [x] **PR-48:** Introduce feature-local validation and shared endpoint registration
  conventions without breaking response/error codes.
- [x] **PR-49:** Add architecture/feature structure tests and CI rules, and document
  safe expansion rules for future slices.

## PR-45 verification
- [x] Explicit feature folders for API and Application without namespace changes.
- [x] Existing endpoint registration delegates to the same routes and metadata.
- [x] Structural regression check is included in CI Python unittest discovery.
- [ ] GitHub CI backend/unit/integration/migration/coverage checks confirmed green.
- [ ] GitHub CI Angular and E2E checks confirmed green.
- [ ] Human review and merge (not automatic).

## Non-goals
No new libraries, mediator, service-bus hosting, EF refactors, database migrations,
Azure deployment, or mocks suggesting financial settlement.

## PR-46 verification
- [x] Move ListDisputes, GetDispute, timeline and eligibility endpoint and handler slices.
- [x] Preserve existing customer ownership, cursor and public API names and aliases.
- [x] Include Python feature directory checks.
- [ ] GitHub CI green and human review.

## PR-47 verification
- [x] Move card, debit-order, bank-fee command handlers and endpoint files into feature scopes.
- [x] Keep namespaces and DI untouched; shared transition persistence, outbox, and durable
  financial operation intents remain separate from HTTP endpoints.
- [x] Add structural coverage for protected financial command paths.
- [ ] GitHub regression suite green and human review.

## PR-48 verification
- [x] Submission input validation now lives beside its use case with identical error codes.
- [x] Customer query/command registration is composed through a dedicated feature registrar.
- [x] Existing ApiProblem/Create remains the sole common HTTP error format.
- [x] Structural check covers the feature validator and composition root.
- [ ] GitHub CI and peer review passed.

## PR-49 verification
- [x] Require inward dependency direction for Domain and Application with a fail-closed Python guard.
- [x] Require the customer feature folders, authorization, CSRF and feature-local submission validation.
- [x] Detect removal of common financial transition case/event/outbox writes.
- [x] Run architecture gate in both GitHub Actions and optional Azure DevOps PR validation.
- [x] Test the architecture guard detects injected dependency violations.
- [ ] PR-49 CI checks green and reviewed.

## Merge order / release checklist
- [ ] Merge PR-45 (GitHub PR #44) after successful CI and human review.
- [ ] Rebase PR-46, merge after PR-45 (GitHub PR #45).
- [ ] Rebase PR-47, merge after PR-46 (GitHub PR #46).
- [ ] Rebase PR-48, merge after PR-47 (GitHub PR #47).
- [ ] Rebase PR-49, merge after PR-48 (GitHub PR #48).
- [ ] Confirm full backend integration tests, migrations, coverage evidence, Angular and Playwright E2E on merged main.
- [ ] Confirm dispute chargeback, debit-order and bank-fee simulations including unknown financial outcomes.

No deployment, schema or financial-adapter change is part of these PRs.
