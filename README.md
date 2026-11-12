# ClearCase

ClearCase is a starter monorepo for incrementally building a financial dispute management platform. It includes an ASP.NET Core 10 API, an Angular 21 transaction listing, health endpoints, automated tests, and CI.

**Security warning: the transaction listing is a synthetic demo, not real customer authentication.** In Development and Testing, every caller is automatically assigned the same server-configured synthetic customer identity. Do not expose these environments to untrusted users or connect real customer data. Production and other environments reject transaction requests until real authentication is implemented. No real customer information or money movement is involved.

**This is an engineering foundation, not a production dispute platform.** It does not include real authentication, dispute submission, a timeline, persistence, banking integrations, Kafka, or AWS.

## Prerequisites

- .NET 10 SDK
- Node.js 24.18.0 and npm 11.16.x

The Angular project pins its public npm registry and enables a lockfile in `apps/web/.npmrc`, independent of machine-level registry settings.

## Build and test

From the repository root:

```powershell
dotnet restore apps/api/ClearCase.slnx
dotnet build apps/api/ClearCase.slnx --configuration Release --no-restore
dotnet test apps/api/ClearCase.slnx --configuration Release --no-build
```

Run the frontend commands from `apps/web` so its project-local npm configuration is applied:

```powershell
Set-Location apps/web
npm ci
npm run test:ci
npm run build
```

GitHub Actions runs the same backend restore/build/test and frontend clean-install/test/build gates on pull requests and pushes to `main`.

## Run locally

Start the API from the repository root:

```powershell
dotnet run --project apps/api/src/ClearCase.Api --urls http://localhost:5100
```

In a second terminal, start Angular:

```powershell
Set-Location apps/web
npm start
```

Open `http://localhost:4200`. The Angular development server proxies `/api` to `http://localhost:5100`; start the API with the URL above. The API exposes `GET /health/live` and `GET /health/ready` at `http://localhost:5100`. In Development, the generated OpenAPI document is available at `http://localhost:5100/openapi/v1.json`. The readiness response verifies application startup only; it does not check a database or other dependency.

For a manual API smoke check, open `apps/api/ClearCase.Api.http` in VS Code with the REST Client extension, or request the endpoints directly with `curl`.

## Transaction listing API

`GET /api/v1/transactions` lists only transactions belonging to the authenticated customer. `GET /v1/transactions` is a compatibility alias with identical behavior. Both require authorization and return `401` outside Development and Testing. No customer ID from a request header or query parameter is trusted.

| Parameter | Meaning |
| --- | --- |
| `pageNumber` | Positive integer, default `1`. |
| `pageSize` | Integer from `1` to `100`, default `20`. |
| `transactionType` | Optional, case-sensitive `CardPurchase`, `DebitOrder`, or `BankCharge`. |
| `fromDate`, `toDate` | Optional inclusive UTC calendar dates in `YYYY-MM-DD` format; `fromDate` must not be later than `toDate`. |

Filtering occurs before offset pagination; ordering is transaction date descending, then ID ascending. `totalCount` counts filtered records; `totalPages` is zero for no matches and `hasNextPage` is false on the last page. A page beyond the end has an empty `items` array. Invalid parameters return HTTP 400 `application/problem+json` with a stable code, correlation ID and per-field errors. Responses use `Cache-Control: private, no-store`.

The Angular filters reject an inverted date range before making a request and display validation messages returned by the API. After a page change, focus moves to the transactions heading.

Example, with the API running in Development:

```powershell
curl.exe "http://localhost:5100/api/v1/transactions?transactionType=CardPurchase&fromDate=2026-11-10&pageSize=1"
```

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "transactionType": "CardPurchase",
      "description": "Woolworths",
      "amount": "750.00",
      "currency": "ZAR",
      "transactionDate": "2026-11-10T08:30:00+00:00",
      "status": "Posted",
      "isDisputable": true
    }
  ],
  "pageNumber": 1,
  "pageSize": 1,
  "totalCount": 1,
  "totalPages": 1,
  "hasNextPage": false
}
```

Amounts are decimal **strings**, not JSON numbers, to preserve exact decimal values across clients; timestamps are UTC ISO 8601. `isDisputable` is synthetic display data, not a real eligibility decision.

The in-memory reader deterministically generates 30 fixed, dated transactions across all three types for the demo identity and one record for a second synthetic identity to exercise isolation. IDs and dates are fixed, not random or derived from the current clock. The configured `Demo:CustomerId` is trusted server configuration, present only in Development and Testing settings. The demo authentication handler is not registered in Production or other environments. Data resets on restart and is never persisted.

## Transaction details

`GET /api/v1/transactions/{id}` returns an authenticated customer's transaction; `GET /v1/transactions/{id}` is an equivalent alias. A malformed GUID returns 400 Problem Details. A missing transaction or one owned by another customer returns the same 404 response; callers without authentication receive 401. Both successful and error responses include a correlation ID and use `Cache-Control: private, no-store` for authenticated detail requests. Optional merchant and reference fields are omitted when unavailable.

With the API running in Development:

```powershell
curl.exe "http://localhost:5100/api/v1/transactions/00000000-0000-0000-0000-000000000001"
```

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "transactionType": "CardPurchase",
  "description": "Woolworths",
  "merchantName": "Woolworths",
  "amount": "750.00",
  "currency": "ZAR",
  "transactionDate": "2026-11-10T08:30:00+00:00",
  "status": "Posted",
  "reference": "TXN-2026-000001",
  "isDisputable": true
}
```

Amounts are stored as decimals and exposed as decimal strings, consistent with the listing contract. Merchant names and references are synthetic demo data. The Angular transaction list links to `/transactions/{id}`; the details page includes a back link and loading, not-found, and retryable-error states. It does not submit disputes.

For focused tests:

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --filter FullyQualifiedName~TransactionEndpointsTests
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --filter "FullyQualifiedName~TransactionDetailsEndpointsTests|FullyQualifiedName~TransactionApplicationTests"
Set-Location apps/web
npm run test:ci -- --include=src/app/transactions/transactions-page.spec.ts --include=src/app/transactions/transaction-details-page.spec.ts
```

The checked-in target-design OpenAPI document describes a future cursor-based contract; the generated Development OpenAPI document describes this implemented, page-number-based slice. Moving to a different contract requires a separately planned compatibility change.

## Current limitations

- The temporary demo identity is not authentication for real customers. Production transaction requests are unauthorized.
- The Angular pages display synthetic data only and cannot submit disputes.
- No database, migrations, event broker, external banking adapter, cloud resources, or deployment manifests are included.
- Development CORS permits only the configured local Angular origin. CORS is not enabled outside Development.
- On 2026-11-12, `npm audit --omit=dev` reported zero vulnerabilities. The full audit reported 16 moderate findings in development tooling; npm's suggested forced fix upgrades Angular CLI to 22, so that breaking upgrade is deferred for a dedicated dependency review.

See `docs/adr/0001-bootstrap-foundation.md` for the initial scope decision. Target designs in `docs/target-design/` describe future work and are not claims about implemented behavior.
