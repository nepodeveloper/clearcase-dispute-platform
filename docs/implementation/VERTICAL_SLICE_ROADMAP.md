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
- [ ] **PR-49:** Add architecture/feature structure tests and CI rules, and document
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
