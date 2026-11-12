# 03 | REST API standards and design contracts

## Standards interpreted for this repository

**Path:** `{baseUrl}/v{majorVersion}/{resource}` with base URL `/api` and major `v1`; plural nouns and kebab-case for multiword segments. Use GUIDs for primary public IDs. Nested paths only for genuine parent-child relationships, e.g. `/api/v1/disputes/{disputeId}/timeline-events`; do not nest disputes under everything. For actions outside CRUD use POST commands on explicit subresources, e.g. `POST /api/v1/disputes/{id}/evidence-requests` (bank agent) or `POST /api/v1/disputes/{id}/cancellation-requests`.

**Semantics:** GET safe/read-only; POST creates resource or executes an explicit business command; PUT complete replacement where permitted; PATCH partial update with controlled property whitelist and concurrency; DELETE only where actual resource removal is legal. No destructive DELETE on immutable audit records. Query strings for filtering/sorting/paging, never write payloads.

**Naming/contracts:** camelCase JSON; stable DTOs not EF entities; optional fields omitted if absent (configure serializer consciously); defined collections returned as `[]`. All timestamps ISO 8601 UTC; monetary values as `amountMinorUnits` + `currency` or decimal string, never IEEE 754 `float`; send IDs as strings. Return minimal, explicit records—don't leak customer IDs, processor secrets or mandate data not necessary for the view.

## Public endpoint inventory

| Method | Route | Description | Success |
|---|---|---|---|
| GET | `/api/v1/transactions` | Owned posted transactions, keyset paging and filters | 200 |
| GET | `/api/v1/transactions/{id}` | Owned transaction details and dispute eligibility projection | 200 / 404 |
| GET | `/api/v1/disputes` | Owned cases, status/type filters, cursor paging | 200 |
| POST | `/api/v1/disputes` | Create dispute using `Idempotency-Key` | 201 new / 200 same replay |
| GET | `/api/v1/disputes/{id}` | Case view with financial status | 200 / 404 |
| GET | `/api/v1/disputes/{id}/timeline-events` | Immutable customer-facing timeline | 200 |
| POST | `/api/v1/disputes/{id}/evidence` | Create evidence upload authorization / metadata | 201 / 202 |
| GET | `/api/v1/disputes/{id}/evidence` | List evidence metadata for current customer | 200 |
| POST | `/api/v1/disputes/{id}/evidence-submissions` | Confirm scanned/uploaded evidence association | 202 |
| GET | `/api/v1/dispute-reasons` | Permitted reason codes by transaction type | 200 |
| GET | `/health/live` | No external dependencies; Kubernetes liveness | 200 |
| GET | `/health/ready` | Dependencies necessary for safe acceptance | 200 / 503 |

**Ops routes:** `/api/v1/operations/disputes/...` require bank role and elevated authentication. Prefer separate authorization scopes and internal gateway path. Demo-only progression routes must never be deployed to the production environment.

## Create dispute contract

```http
POST /api/v1/disputes HTTP/1.1
Content-Type: application/json
Idempotency-Key: 1a45a4a4-7acb-48ae-bb52-4a1d8db9163c
X-Correlation-ID: req_abc123
X-XSRF-TOKEN: <session CSRF token>

{
  "transactionId": "550e8400-e29b-41d4-a716-446655440000",
  "reasonCode": "duplicateCharge",
  "description": "I was charged twice for one purchase.",
  "evidenceIds": []
}
```

```http
HTTP/1.1 201 Created
Location: /api/v1/disputes/123e4567-e89b-12d3-a456-426614174000
X-Correlation-ID: req_abc123
Content-Type: application/json

{
  "id": "123e4567-e89b-12d3-a456-426614174000",
  "transactionId": "550e8400-e29b-41d4-a716-446655440000",
  "type": "cardPurchase",
  "caseStatus": "submitted",
  "financialStatus": "notStarted",
  "createdAt": "2026-11-12T08:00:00Z",
  "links": {
    "self": "/api/v1/disputes/123e4567-e89b-12d3-a456-426614174000",
    "timeline": "/api/v1/disputes/123e4567-e89b-12d3-a456-426614174000/timeline-events"
  }
}
```

**Replay:** identical `Idempotency-Key`, principal, route, and canonical payload hash → return stored resource with 200 (or original response code if chosen as a documented policy). Same key, different canonical payload → 409. Concurrent keys for the same active transaction → business conflict or configured controlled reopening. TTL/retention must outlive plausible financial retry windows. Never rely on UI button disabling as the guarantee.

**Semantics note:** When dispute creation is fully durable immediately, 201 Created is appropriate even if downstream processing is asynchronous. For an async command that does not create an immediately addressable resource, 202 Accepted is appropriate. Both must clearly communicate pending financial state.

## Pagination, searching and filtering

`GET /api/v1/transactions?pageSize=25&cursor=<opaque>&from=2026-10-01&to=2026-11-12&kind=debitOrder`

```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "description": "Monthly service debit",
      "amountMinorUnits": 150000,
      "currency": "ZAR",
      "kind": "debitOrder",
      "postedAt": "2026-11-01T09:22:00Z",
      "canDispute": true
    }
  ],
  "pageInfo": { "pageSize": 25, "hasNextPage": false }
}
```

If there is a next page, include `nextCursor` in `pageInfo`. Omit it when absent. Cursor is opaque, authenticated, versioned/signed where appropriate; stable ordering `(postedAt DESC, id DESC)` and matching composite index. Cap `pageSize` to a reasonable ceiling (e.g. 100), reject unsupported sorts. Avoid expensive unbounded `totalCount` for every request; provide count only when cheap/needed. Prevent tampering and cross-customer cursor reuse.

## Errors: RFC Problem Details + JSON Pointer

```http
HTTP/1.1 422 Unprocessable Content
Content-Type: application/problem+json
X-Correlation-ID: req_abc123

{
  "type": "https://example.invalid/problems/dispute-ineligible",
  "title": "Dispute is not eligible for automated processing",
  "status": 422,
  "detail": "This debit order requires a different review path.",
  "instance": "/api/v1/disputes",
  "code": "dispute.ineligible",
  "correlationId": "req_abc123",
  "errors": [
    { "pointer": "/transactionId", "code": "transaction.outsideWindow", "message": "The debit order is outside the standard dispute period." }
  ]
}
```

Use a real governed problem-type URI when published; `example.invalid` is illustrative only. Never expose stack traces, SQL, bank account details or usernames. `errors` is a stable array and JSON Pointer uses RFC 6901 escaping rules. Standard HTTP outcomes: 400 invalid format, 401 unauthenticated, 403 authenticated but forbidden when disclosure safe, 404 hidden/nonexistent customer-owned resource, 409 duplicate/conflicting state or key, 412 failed `If-Match`, 422 valid syntax but business ineligibility, 429 rate limited (`Retry-After`), 503 dependency unavailable.

## Caching, conditional requests and telemetry

- `Cache-Control: private, no-store` for case details, balances, evidence and PII. Reference code lists may be cached where safe.
- Correlation ID propagated through inbound HTTP, handler, outbox and broker message headers; W3C `traceparent` for distributed tracing. Don't trust unsanitized incoming correlation fields as log format strings.
- Public status transitions are operations with expected version; expose `ETag: "<version>"` or explicit expected version. Client retries after 409/412 by refetching.
- Any non-idempotent business command must require an idempotency key; log a hashed reference rather than raw sensitive payload.
- Antiforgery for cookie-authenticated mutation endpoints. Authenticate bank users via a real IdP/OIDC in target production.

## OpenAPI and contract checks

See `docs/openapi.yaml` for a concise draft. CI should lint OpenAPI, detect accidental breaking changes, generate typed frontend clients or at least validate contracts, and run consumer/provider compatibility tests. Do not auto-generate unreviewed contract drift into production.
