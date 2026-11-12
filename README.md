# ClearCase

ClearCase is a starter monorepo for incrementally building a financial dispute management platform. It includes an ASP.NET Core 10 API, an Angular 21 transaction listing, health endpoints, automated tests, and CI.

**Security warning: the transaction listing is a synthetic demo, not real customer authentication.** In Development and Testing, every caller is automatically assigned the same server-configured synthetic customer identity. Do not expose these environments to untrusted users or connect real customer data. Production and other environments reject transaction requests until real authentication is implemented. No real customer information or money movement is involved.

**This is an engineering foundation, not a production dispute platform.** It does not include real authentication, dispute submission, a timeline, banking integrations, Kafka, or AWS. SQLite stores only local transaction data in this slice.

## Prerequisites

- .NET 10 SDK
- Node.js 24.18.0 and npm 11.16.x

The Angular project pins its public npm registry and enables a lockfile in `apps/web/.npmrc`, independent of machine-level registry settings.
SQLite is bundled with its .NET provider; no database server, Docker, administrator rights, or globally installed EF tool is required.

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

The launch profile uses Development. On Development startup the API creates the configured data directory, applies the checked-in SQLite migrations, and inserts missing synthetic records. Failures stop startup; existing databases are not deleted or reset. See [SQLite persistence](#sqlite-persistence) before using any non-disposable data.

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

Amounts are decimal **strings**, not JSON numbers, to preserve exact decimal values across clients; timestamps are UTC ISO 8601. `isDisputable` remains a provisional synthetic display indicator, not an authoritative eligibility decision. A follow-up contract change should derive it from the shared policy or retire it; the detailed eligibility endpoint is authoritative for this PR.

The Development seed inserts 30 fixed, dated transactions across all three types for the demo identity and one record for a second synthetic identity to exercise isolation. IDs and dates are fixed, not random or derived from the current clock. Repeated seeding inserts missing IDs without overwriting existing records. The configured `Demo:CustomerId` is trusted server configuration, present only in Development and Testing settings. The demo authentication handler is not registered in Production or other environments.

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

Amounts are stored as integer ZAR cents, converted exactly to decimals, and exposed as decimal strings, consistent with the listing contract. Merchant names and references are synthetic demo data. The Angular transaction list links to `/transactions/{id}`; the details page includes a back link and loading, not-found, and retryable-error states. It does not submit disputes.

## SQLite persistence

SQLite was chosen for a self-contained local demo without a server or administrator access; it is not a production scaling claim. `Persistence:DatabasePath` defaults to `App_Data/clearcase.db` relative to the API content root, not the shell's working directory. Override it with configuration, for example `Persistence__DatabasePath`; do not place it in the web root or expose it via static files. Database, WAL, SHM and journal files are Git-ignored. Protect the file and its backups with appropriate filesystem permissions; never put real customer records in the demo environments.

The Infrastructure adapter implements the existing application transaction-reader port. The SQLite schema has a transaction GUID primary key, customer GUID, category, display fields, status, reference, provisional `isDisputable`, and the facts required by the eligibility policies. Customer/date/ID and customer/type/date/ID indexes support scoped list queries; there is no separate customer table to reference yet. Queries apply ownership and filters in SQLite before materialization, order by UTC timestamp then ID, and paginate in the database. Transaction timestamps use UTC ticks and ZAR amounts use integer cents, avoiding binary floating point. A database check limits this slice to ZAR; supporting currencies with other minor-unit scales requires a separate migration and contract review.

The connection enables foreign keys and sets a configurable busy/locked command timeout (`Persistence:BusyTimeoutSeconds`, default 5); the provider handles busy/locked retries within that timeout. The EF SQLite provider enables write-ahead logging (WAL) for newly created databases. WAL permits concurrent readers but SQLite still allows only one writer at a time; it requires reliable local filesystem locking and is unsuitable for many network filesystems. Back up with a SQLite-consistent backup procedure, not a copy of the database file alone while it is active.

### Migrations and seed

Development startup applies migrations and runs the seed in a transaction. The seed contains synthetic data only and does not overwrite existing transaction IDs. Testing uses isolated databases explicitly migrated and seeded by tests. Production and other environments **do not** automatically migrate, create the database, or seed; the runtime opens an existing database in read/write mode. Development must never be pointed at a production database.

The repository-local `dotnet-ef` tool is pinned in the tool manifest. To apply migrations explicitly to the default local database, run from the repository root in a dedicated PowerShell session:

```powershell
dotnet tool restore
$apiRoot = (Resolve-Path .\apps\api\src\ClearCase.Api).Path
$dbPath = Join-Path $apiRoot 'App_Data\clearcase.db'
New-Item -ItemType Directory -Force -Path (Split-Path $dbPath) | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet tool run dotnet-ef database update --project .\apps\api\src\ClearCase.Infrastructure --startup-project .\apps\api\src\ClearCase.Api --connection "Data Source=$dbPath;Mode=ReadWriteCreate;Foreign Keys=True;Default Timeout=5"
```

For a differently configured database, replace `$dbPath` with its approved absolute path. The explicit `ReadWriteCreate` connection is for the migration command, not the production runtime. Review migrations and take a verified backup before applying them to retained data. To scaffold a future SQLite migration, use `dotnet tool run dotnet-ef migrations add <Name> --project .\apps\api\src\ClearCase.Infrastructure --startup-project .\apps\api\src\ClearCase.Api --output-dir Migrations` from the repository root; review the generated provider-specific migration.

To reset **only a disposable local demo database**, stop all API processes, confirm the resolved `$dbPath` is the intended demo file and that no data needs preserving, then remove the exact database and sidecars:

```powershell
$apiRoot = (Resolve-Path .\apps\api\src\ClearCase.Api).Path
$dbPath = Join-Path $apiRoot 'App_Data\clearcase.db'
foreach ($file in @($dbPath, "$dbPath-wal", "$dbPath-shm", "$dbPath-journal")) {
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
}
```

For a differently configured database, resolve and verify its path instead. The next Development startup recreates and reseeds it. Do not use this reset procedure on retained or production data.

Run relational persistence and startup tests with:

```powershell
dotnet test .\apps\api\tests\ClearCase.Api.IntegrationTests\ClearCase.Api.IntegrationTests.csproj --filter "FullyQualifiedName~SqliteTransactionReaderTests|FullyQualifiedName~SqliteStartupTests"
```

A future PostgreSQL provider requires separately tested provider-specific migrations, exact-money and UTC mapping, data transfer and rollback plans, and concurrency/operational review; SQLite migrations and WAL behavior are not portable as-is. See [ADR 0003](docs/adr/0003-sqlite-local-persistence.md).

## Dispute eligibility

The transaction details page requests a read-only decision from `GET /api/v1/transactions/{id}/dispute-eligibility`. The legacy alias `GET /v1/transactions/{id}/dispute-eligibility` is also available. Both routes require authentication, scope the transaction lookup to the trusted customer identity, return 404 for missing or inaccessible IDs, preserve correlation IDs, and use `Cache-Control: private, no-store`. A decision is a successful 200 response even when its outcome is `Ineligible`; this API never creates a dispute or initiates a reversal, refund, or other financial operation.

```powershell
curl.exe "http://localhost:5100/api/v1/transactions/00000000-0000-0000-0000-000000000001/dispute-eligibility"
```

```json
{
  "transactionId": "00000000-0000-0000-0000-000000000001",
  "outcome": "EligibleToSubmit",
  "reasonCodes": [],
  "explanation": "This transaction can be submitted for review.",
  "policyVersion": "demo-v1",
  "evaluatedAt": "2026-11-12T10:00:00+00:00"
}
```

`ClearCase.Domain.DisputeEligibility.IDisputeEligibilityPolicy` defines the pure policy boundary. Separate card-purchase, debit-order, and bank-charge implementations receive transaction facts and an evaluation context; the application handler supplies the trusted transaction, configuration, and injected `TimeProvider`. Policies do not access storage or call external systems.

| Category and condition | Outcome | Example reason code |
| --- | --- | --- |
| Posted/settled card debit with information inside the configured demo window | `EligibleToSubmit` | — |
| Pending card authorization, or outside the configured demo window | `Ineligible` | `card.pendingAuthorization`, `card.outsideDemoWindow` |
| Missing card information or no configured card window | `RequiresManualReview` | `transaction.informationMissing`, `card.windowNotConfigured` |
| Debit order with effective policy, complete authorized mandate facts, within window, and amount at or below threshold | `EligibleToSubmit` | — |
| Debit order outside its standard window, above the internal threshold, with unclear/unauthorized mandate facts, or valid authorized DebiCheck | `RequiresManualReview` | `debitOrder.outsideStandardWindow`, `debitOrder.amountAboveStandardReviewThreshold`, `debitOrder.mandateInformationMissing`, `debitOrder.authorisedDebiCheckRequiresReview` |
| Bank charge posted with required information and no configured review condition | `EligibleToSubmit` | — |
| Pending bank charge | `Ineligible` | `bankCharge.notPosted` |
| Bank charge missing information or meeting the configured review condition | `RequiresManualReview` | `transaction.informationMissing`, `bankCharge.configuredReviewCondition` |

Reason codes are stable machine-readable identifiers; the response explanation is customer-safe text. Every outcome includes `policyVersion` and an ISO-8601 `evaluatedAt` timestamp.

The current policy reason codes are:

- Card: `policy.notConfigured`, `card.unsupportedType`, `transaction.notDebit`, `card.pendingAuthorization`, `transaction.notPosted`, `card.invalidAmount`, `transaction.informationMissing`, `card.windowNotConfigured`, `card.transactionDateInFuture`, `card.outsideDemoWindow`.
- Debit order: `policy.notConfigured`, `debitOrder.unsupportedType`, `debitOrder.notDebit`, `debitOrder.notPosted`, `debitOrder.invalidAmount`, `debitOrder.collectionInformationMissing`, `debitOrder.policyUnavailable`, `debitOrder.mandateInformationMissing`, `debitOrder.unauthorisedCollection`, `debitOrder.mandateMismatch`, `debitOrder.authorisedDebiCheckRequiresReview`, `debitOrder.outsideStandardWindow`, `debitOrder.amountAboveStandardReviewThreshold`.
- Bank charge: `policy.notConfigured`, `bankCharge.unsupportedType`, `bankCharge.notPosted`, `transaction.informationMissing`, `bankCharge.invalidAmount`, `bankCharge.configuredReviewCondition`.

### Demonstration configuration and policy caveats

`appsettings.Development.json` and `appsettings.Testing.json` contain the local demonstration settings. The base `appsettings.json` leaves demo policies disabled; an unconfigured policy returns `RequiresManualReview` rather than applying a demo rule.

- Card purchases use a configurable 120-calendar-day **demo-only** window. `DemoWindowEnabled` controls applicability; disabling it routes cases to manual review. This is not a card-network deadline or statement of customer rights.
- The debit-order example rule has an effective date of 13 April 2026, a 60-calendar-day standard window (day 1 begins after collection), and a configurable R2,000 standard-review threshold. Rule selection uses the collection date. The rule/effective date reflect a proposed demonstration assumption and require confirmation against authoritative bank, scheme, product, legal, and operational guidance before production use.
- Amounts above R2,000 route to enhanced/manual review, not rejection. The threshold is an illustrative internal routing value, not a legal limit. Outside-window debit orders, unauthorised/unclear mandates, and valid authorised DebiCheck collections remain eligible for further review; no automatic reversal is implied.
- Bank charges use a separate posted-status, information, and configurable internal review condition (`ManualReviewAboveAmount`, set to R5,000 for this demo). Card-window and debit-order mandate rules are not applied to bank charges.
- Seeded examples include an eligible card purchase (ID ending `000001`), EFT debit order (`000002`), bank charge (`000003`), pending card authorization (`000004`), high-value debit order (`000005`), incomplete mandate (`000008`), and authorised DebiCheck (`000011`).

The policies use deterministic inputs and never read the system clock directly. The API injects `TimeProvider`; tests use a fixed clock for date boundaries. A future dispute-submission use case must separately revalidate ownership, policy and evidence and must use an authorized, idempotent banking adapter; eligibility is not approval or a promise of funds.

Focused checks:

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --filter "FullyQualifiedName~DisputeEligibilityPolicyTests|FullyQualifiedName~DisputeEligibilityEndpointsTests"
Set-Location apps/web
npm run test:ci -- --include=src/app/transactions/transaction-details-page.spec.ts
```

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
- SQLite persistence is local-only; production authentication, PostgreSQL, event broker, external banking adapter, cloud resources, and deployment manifests are not included.
- Development CORS permits only the configured local Angular origin. CORS is not enabled outside Development.
- On 2026-11-12, `npm audit --omit=dev` reported zero vulnerabilities. The full audit reported 16 moderate findings in development tooling; npm's suggested forced fix upgrades Angular CLI to 22, so that breaking upgrade is deferred for a dedicated dependency review.

See `docs/adr/0001-bootstrap-foundation.md` for the initial scope decision. Target designs in `docs/target-design/` describe future work and are not claims about implemented behavior.
