# 08 | TDD, automated testing, resilience exercises and evidence gates

## Testing principles

Use TDD for each domain invariant (red → green → refactor); Arrange–Act–Assert; deterministic clock and ID generators; dependency inversion for adapters; test doubles only at meaningful external boundaries. Favor real PostgreSQL in integration tests over EF InMemory for SQL/transaction behavior. Test the APIs as clients consume them, including ownership and Problem Details. Never claim test coverage equals correctness. Use coverage as a supporting signal and make domain risk coverage explicit.

## Test matrix

| Layer | Core tests | Tooling / method |
|---|---|---|
| Domain unit | allowed transitions; deadlines; 60th/61st calendar-day boundary; DebiCheck valid/invalid mandate; threshold routing; money/currency rules | xUnit + parameterized theories |
| Application unit | request orchestration, port invocation, cancellation, authorization context | xUnit, focused mocks/fakes |
| Infrastructure integration | PostgreSQL unique index races, optimistic version, outbox atomicity, publisher leasing | Testcontainers PostgreSQL |
| API integration | 201/200 replay, 409 mismatch, 422, 401, 404 ownership, 412 version, CSRF, 429 | WebApplicationFactory + test identity |
| Event/contract | schema versions, unknown optional fields, duplicate consumers, reorder events | schema/consumer tests + Kafka test container |
| UI component | loading/empty/error states, form validation, accessible timeline, keyboard use | Angular test runner + testing library |
| Browser E2E | customer transaction→dispute→timeline; evidence upload; auth expiry | Playwright |
| Security | BOLA, mass assignment, invalid CSRF, agent segregation, forged callbacks, malicious file | security and integration suite |
| Resilience | DB down, Kafka down, worker crash before commit, partner timeout after remote success, DLQ replay | fault injection harness |
| Performance | ramp, soak, spike, high cardinality customer pages, constrained DB pool | k6 + AWS staging dashboards |
| Disaster recovery | restore from backup, reconcile unknown external ops, outbox replay | recovery game day |

## Acceptance scenarios: detailed, executable in principle

**T01 / Same idempotency key concurrently:** launch 20 `POST /api/v1/disputes` for same principal/key/payload. Expect one case, one initial event, one outbox event, identical `id` in all accepted replays. No race-based 500.

**T02 / Same key different body:** expect deterministic 409 with `code=idempotency.payloadConflict`, no case mutation.

**T03 / Different keys same active transaction:** only one eligible active dispute created, rest 409, unless explicit reopening policy applies.

**T04 / Ownership:** user B cannot GET user A's transaction, case, timeline or evidence, or create a dispute for A's transaction. All relevant endpoints conceal resource consistently.

**T05 / Atomicity:** inject DB failure after inserting case but before timeline/outbox. Expect no committed case, no event, no stored response.

**T06 / Outbox crash after send:** Kafka receives same `eventId` twice. Consumer applies business effect once, inbox one row, one financial operation reference.

**T07 / Partner processed but timed out:** fake bank processes reversal then cuts response. The next retry must query operation status and must not send another new financial reference.

**T08 / Debit policy boundary:** post-effective date standard EFT debit exactly day 60 → process by applicable rules; day 61 → out-of-window route. Check the next-day-is-day-1 calculation and leap/daylight boundaries.

**T09 / Valid DebiCheck:** authentic confirmed mandate matching transaction → normal reversal blocked; mismatched/unauthorized → referral/investigation path, with user-friendly explanation.

**T10 / Merchant representment:** merchant rejects and submits evidence, issuer decides; customer-visible final case decision is distinct from funds posting.

**T11 / Concurrent transitions:** two operators try incompatible decisions from the same version; only one commits, the other receives 409/412 and refreshes.

**T12 / Malicious evidence:** file remains quarantined and inaccessible; case status accurate.

**T13 / Kafka unavailable:** create dispute returns 201 if PostgreSQL healthy; outbox backlog grows; on restore publisher drains while no duplicate effect occurs.

**T14 / Database unavailable:** submission fails with 503 and does not show a case ID; customer sees retriable status and can safely retry using same key.

**T15 / Load profile:** 10-minute warmup, ramp, burst and soak under specified request mix and dataset. Capture p50/p95/p99, throughput, CPU/memory, DB connection pool, lock waits, consumer lag, 429 rates and error budget. Record actual results or leave pending.

## Quality gates

**PR gate:** compile, format, lint, unit tests, API schema lint, positive+negative new behavior, no secret leakage, at least one risk-related test for every changed invariant; reviewer confirms ADR if architecture changed.

**Main/release gate:** integration tests against PostgreSQL, contract tests, E2E happy+negative, security scans, migration safety, recovery smoke, deployment smoke. Security BOLA or financial double-action test failing = no release.

**Assessment delivery evidence:** `dotnet test`, `npm ci && npm test && npm run build`, `docker compose up`, automated API integration summary, screenshots of UI, a short recorded failure demo and a truthful README note explaining unimplemented targets.

## Definition of done

Business behavior and acceptance examples agreed; policy is versioned; API contract published; unit and negative-path tests added; logging/metrics added; docs/ADR updated; rollback/recovery steps known; deployment and security review complete where relevant.
