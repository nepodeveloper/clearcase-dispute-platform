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
- [x] GitHub CI backend/unit/integration/migration/coverage checks confirmed green on merged main ([CI run 44299208734](https://github.com/nepodeveloper/clearcase-dispute-platform/actions/runs/44299208734)).
- [x] GitHub CI Angular and E2E checks confirmed green on merged main ([CI run 44299208734](https://github.com/nepodeveloper/clearcase-dispute-platform/actions/runs/44299208734)).
- [x] Merged as [GitHub PR #44](https://github.com/nepodeveloper/clearcase-dispute-platform/pull/44).
- [ ] Independent human approval not verified from PR review records (Copilot bot commented).

## Non-goals
No new libraries, mediator, service-bus hosting, EF refactors, database migrations,
Azure deployment, or mocks suggesting financial settlement.

## PR-46 verification
- [x] Move ListDisputes, GetDispute, timeline and eligibility endpoint and handler slices.
- [x] Preserve existing customer ownership, cursor and public API names and aliases.
- [x] Include Python feature directory checks.
- [x] Merged as [GitHub PR #45](https://github.com/nepodeveloper/clearcase-dispute-platform/pull/45); backend/frontend quality gates passed on merged main.
- [ ] Independent human approval not verified.

## PR-47 verification
- [x] Move card, debit-order, bank-fee command handlers and endpoint files into feature scopes.
- [x] Keep namespaces and DI untouched; shared transition persistence, outbox, and durable
  financial operation intents remain separate from HTTP endpoints.
- [x] Add structural coverage for protected financial command paths.
- [x] Merged as [GitHub PR #46](https://github.com/nepodeveloper/clearcase-dispute-platform/pull/46); regression CI passed on merged main.
- [ ] Independent human approval not verified.

## PR-48 verification
- [x] Submission input validation now lives beside its use case with identical error codes.
- [x] Customer query/command registration is composed through a dedicated feature registrar.
- [x] Existing ApiProblem/Create remains the sole common HTTP error format.
- [x] Structural check covers the feature validator and composition root.
- [x] Merged as [GitHub PR #47](https://github.com/nepodeveloper/clearcase-dispute-platform/pull/47); CI passed on merged main.
- [ ] Independent human approval not verified.

## PR-49 verification
- [x] Require inward dependency direction for Domain and Application with a fail-closed Python guard.
- [x] Require the customer feature folders, authorization, CSRF and feature-local submission validation.
- [x] Detect removal of common financial transition case/event/outbox writes.
- [x] Run architecture gate in both GitHub Actions and optional Azure DevOps PR validation.
- [x] Test the architecture guard detects injected dependency violations.
- [x] Merged as [GitHub PR #48](https://github.com/nepodeveloper/clearcase-dispute-platform/pull/48); CI architecture gate and regression suites passed on merged main.
- [ ] Independent human approval not verified.

## Merge order / release checklist
- [x] PR-45 merged (GitHub PR #44); CI is green on merged main. Human approval remains unverified.
- [x] PR-46 merged after PR-45 (GitHub PR #45).
- [x] PR-47 merged after PR-46 (GitHub PR #46).
- [x] PR-48 merged after PR-47 (GitHub PR #47).
- [x] PR-49 merged after PR-48 (GitHub PR #48).
- [x] Confirm backend integration tests, migrations, coverage evidence, Angular and Playwright E2E on merged main ([CI run 44299208734](https://github.com/nepodeveloper/clearcase-dispute-platform/actions/runs/44299208734)).
- [ ] Confirm dispute chargeback, debit-order and bank-fee simulations including unknown financial outcomes.

## Evidence reconciliation — 18 November 2026

The vertical-slice implementation PRs #44 through #48 were merged in order on
18 November 2026. GitHub main CI run [44299208734](https://github.com/nepodeveloper/clearcase-dispute-platform/actions/runs/44299208734)
finished successfully, including governance, backend build/security/migrations/coverage,
and frontend build/security/browser E2E jobs. This checks **implementation and CI**;
it does not prove an independent human approved each PR. GitHub review records show
Copilot review comments and no verified human APPROVED review for those PRs.

The existing financial domain and state-transition tests remain required for each
future change. Keep human-review boxes open until an accountable reviewer confirms
approval evidence; do not retrospectively infer review from merged status.

No deployment, schema or financial-adapter change is part of these PRs.
