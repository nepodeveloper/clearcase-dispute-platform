# ClearCase

ClearCase is a starter monorepo for incrementally building a financial dispute management platform. It includes an ASP.NET Core 10 API, an Angular 21 transaction listing, health endpoints, automated tests, and CI.

**Local assessment only:** sign in with an explicitly configured synthetic ASP.NET Core Identity account. Development no longer signs callers in automatically. Do not expose Development/Testing to untrusted users or connect real customer data. No real customer information or money movement is involved.

**This is an engineering foundation, not a production dispute platform.** Local cookie authentication is implemented, but bank identity-provider integration, MFA, banking integrations, real financial reversals, Kafka and AWS are not. SQLite stores local transactions, dispute cases and their immutable timelines. Debit-order, card-chargeback and internal bank-charge processing are restricted local simulations only.

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
# PowerShell 7: enter strong local-only passwords without putting values in shell history.
$env:Authentication__SeedPasswords__Customer = Read-Host 'Customer seed password' -MaskInput
$env:Authentication__SeedPasswords__OtherCustomer = Read-Host 'Second customer seed password' -MaskInput
$env:Authentication__SeedPasswords__Agent = Read-Host 'Agent seed password' -MaskInput
$env:Authentication__SeedPasswords__Supervisor = Read-Host 'Supervisor seed password' -MaskInput
dotnet run --project apps/api/src/ClearCase.Api --urls http://localhost:5100
```

Passwords require at least 12 characters with uppercase, lowercase, a digit and a symbol. They are required only when creating missing accounts, are hashed in SQLite, and must never be committed or logged. Stop the API and clear these process environment variables when finished. Do not set these variables in the frontend environment.

The launch profile uses Development. On Development startup the API creates the configured data directory, applies the checked-in SQLite migrations, and inserts missing synthetic records. Failures stop startup; existing databases are not deleted or reset. See [SQLite persistence](#sqlite-persistence) before using any non-disposable data.

In a second terminal, start Angular:

```powershell
Set-Location apps/web
npm start
```

Open `http://localhost:4200`. The Angular development server proxies `/api` to `http://localhost:5100`; start the API with the URL above. The API exposes `GET /health/live` and `GET /health/ready` at `http://localhost:5100`. In Development, the generated OpenAPI document is available at `http://localhost:5100/openapi/v1.json`. The readiness response verifies application startup only; it does not check a database or other dependency.

For a manual API smoke check, open `apps/api/ClearCase.Api.http` in VS Code with the REST Client extension, or request the endpoints directly with `curl`.

## Local authentication and UI integration (PR-12 / PR-13)

The backend now uses ASP.NET Core Identity with SQLite-backed tables in the same local database as disputes and transactions. Cookie authentication is the primary path:

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`
- `GET /api/v1/auth/me`

Development seeds deterministic synthetic identities; integration tests explicitly seed isolated SQLite databases with generated, process-local passwords:

| Username | Password configuration key under `Authentication:SeedPasswords` | Role | Workspace |
| --- | --- | --- | --- |
| `customer.demo` | `Customer` | `Customer` | `/` |
| `customer.other` | `OtherCustomer` | `Customer` | `/` (isolated second customer) |
| `agent.demo` | `Agent` | `Agent` | `/agent/disputes` |
| `supervisor.demo` | `Supervisor` | `Supervisor` | `/agent/escalations` |

Use `/login` in the normal Angular application. The reactive form validates username/password, supports keyboard use and password visibility, and explains invalid credentials and rate limiting. Roles come only from the backend session. Agent and Supervisor workspaces are authenticated placeholders: queue, assignment, investigation and escalation APIs are not implemented.

Login and `/auth/me` return `{ username, actorType, roles, customerId, csrfToken }`; staff `customerId` is currently null. Angular restores `/auth/me` before route decisions, keeps identity/CSRF only in memory, and uses the HTTP-only `clearcase.auth` cookie as authority. It does not use localStorage/sessionStorage. Customer routes include `/`, `/transactions/:id`, `/disputes` and `/disputes/:id`; role mismatch goes to `/unauthorized`. Only known, role-appropriate application-relative return URLs are preserved.

The shared interceptor sends `X-CSRF-TOKEN` on same-origin `/api/v1/` and legacy `/v1/` POST/PUT/PATCH/DELETE operations, except initial login. It preserves/provides `X-Correlation-ID`, updates tokens on login/restoration, and clears state on logout or 401. It never adds CSRF to an external origin. Failed logout remains visible and retryable; it is not shown as successful. The server independently validates authorization, ownership and CSRF (`400`, `code: auth.csrfInvalid`). `/auth/me` is non-cacheable. Cookies use SameSite=Strict, HttpOnly authentication and Secure outside local Development/Testing; use HTTPS in deployed environments.

Development compatibility is disabled even if its old flag is set to true. Testing also defaults to false. Only the legacy integration-test factory explicitly opts in with `Authentication:EnableDevelopmentCompatibilityPath=true`; auth/browser tests exercise real cookies. Invalid, empty or expired identity cookies cannot fall back. **Removal plan:** migrate legacy workflow fixtures to explicit test login, then remove the Testing-only compatibility handler and `Demo:CustomerId` override in a separate hardening change.

Seeding does not reset existing password hashes, security stamps or lockout state. **Upgrade from PR-12:** previously published seed passwords must be considered compromised. Changing seed environment variables does not rotate existing accounts. Before running PR-13, recreate only an explicitly disposable demo database using the reset procedure below and seed new passwords; retained data requires an administrator-controlled Identity credential/security-stamp rotation instead. Do not downgrade to the old hardcoded seeder.

### Browser verification

From `apps/web`, run `npm run test:e2e`. The wrapper generates ephemeral credentials, starts owned API/UI servers on 5101/4301/4302, migrates a unique temporary SQLite database, and deletes that database and sidecars after servers exit. It never uses the developer's database. Browser tests cover all three roles, mobile/desktop screenshots, restoration, logout, denied navigation, invalid cookies, rate limiting, and customer submission with CSRF. Authentication traces are disabled to avoid recording credentials; screenshots are taken before password entry or after sign-in. Reports/screenshots are in `apps/web/playwright-report` and `apps/web/test-results` and uploaded by existing CI. Run filters through the wrapper, e.g. `npm run test:e2e -- --project authentication`.

The separate `npm run start:ui` build remains a clearly labelled, API-free design preview; its role picker is not included in the real application. PR-13 also fixes the preview's mobile table overflow, drawer focus timing, decorative button name and escalation-dialog focus containment without weakening the browser assertions.

## Connected customer journey (PR-14)

Use the normal `npm start` application, not `npm run start:ui`:

1. Sign in at `/login` using a configured customer assessment account. Login opens **Your transactions**.
2. Filter by transaction type and inclusive dates, then use **Next** / **Previous**. Filtering and pagination run on the server; unsupported demo search/account totals are not fabricated.
3. Select a transaction to view its persisted GUID-linked details and authoritative eligibility. The shared ClearCase theme is retained; amounts are formatted directly from decimal strings without floating-point conversion.
4. Select a backend-provided reason, enter a 15-2000 character description, and **Submit dispute**. Pending submission is locked against repeated clicks. Field errors remain associated with the form; forbidden, expired-session, duplicate and ineligible responses are explained.
5. Successful submission automatically opens `/disputes/{id}` using the backend-issued case ID/reference. **My disputes** uses the API's opaque continuation cursor. Case, decision and financial statuses remain distinct; no approval implies confirmed funds.
6. Refresh or revisit a case to read its SQLite-backed timeline, sorted by `sequenceNumber`, including tied or reversed timestamps. Only the customer-safe projection is rendered. A transient refresh failure retains the last loaded case with an explicit error and retry.
7. Sign out to end the cookie session. Expired sessions return to login; another customer cannot access the transaction, eligibility or case.

There is one active customer route implementation. The isolated `ui-demo` entrypoint is only a design reference; its in-memory workspace, fake account details and role selector are not imported into the connected journey. Database seed records remain explicitly synthetic local assessment data.

### PR-14 verification and backout

Focused frontend checks (from `apps/web`):

```powershell
npm run test:ci -- --include "src/app/transactions/*.spec.ts" --include "src/app/disputes/*.spec.ts"
npm run build
```

The additional `customer-desktop` and `customer-mobile` Playwright projects use the existing real Angular/API runner and isolated SQLite database, not `DemoWorkspace`. Run with `npm run test:e2e -- --project customer-desktop --project customer-mobile`. Browser setup disables only the configurable synthetic card date window, routing posted cards to manual review so fixed seed dates do not expire the journey; deterministic backend tests retain window-boundary coverage. Offline retry uses the browser network setting, not mocked API responses. Screenshots are taken only after authentication; traces remain disabled.

No API, database, policy implementation, auth or event contract changed. Roll back by redeploying the PR-13 frontend; keep the API, SQLite records and secure PR-13 authentication configuration. Full idempotency-key replay, staff workflows and real financial processing remain deferred, not implied by the UI.

## Transaction listing API

`GET /api/v1/transactions` lists only transactions belonging to the authenticated customer. `GET /v1/transactions` is a compatibility alias with identical behavior. Both return `401` without authentication and `403` for staff identities. No customer ID from a request header or query parameter is trusted. The curl examples below require a prior login and its cookie jar; use the `.http` examples or signed-in Angular UI for the complete flow.

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

The Development seed inserts 30 fixed, dated transactions across all three types for the demo identity and one record for a second synthetic identity to exercise isolation. IDs and dates are fixed, not random or derived from the current clock. Repeated seeding inserts missing IDs without overwriting existing records. Identity customer mappings match these fixed IDs. `Demo:CustomerId` is used only by explicitly enabled Testing compatibility; other environments cannot use it to authenticate.

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

Amounts are stored as integer ZAR cents, converted exactly to decimals, and exposed as decimal strings, consistent with the listing contract. Merchant names and references are synthetic demo data. The Angular transaction list links to `/transactions/{id}`; the details page includes a back link and loading, not-found, and retryable-error states, plus the eligibility-driven submission form.

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
| Debit order outside its standard window or above the internal threshold | `RequiresManualReview` | `debitOrder.outsideStandardWindow`, `debitOrder.amountAboveStandardReviewThreshold` |
| Missing, unauthorized, inactive or mismatched mandate | `RequiresManualReview`, investigation route | `debitOrder.mandateInformationMissing`, `debitOrder.unauthorisedCollection`, `debitOrder.mandateNotActive`, `debitOrder.mandateMismatch` |
| Valid authorized DebiCheck | Ordinary reversal `Ineligible`; authorization complaint remains `RequiresManualReview`, investigation route | `debitOrder.authorisedDebiCheckRequiresReview` |
| Bank charge posted with required information and no configured review condition | `EligibleToSubmit` | — |
| Pending bank charge | `Ineligible` | `bankCharge.notPosted` |
| Bank charge missing information or meeting the configured review condition | `RequiresManualReview` | `transaction.informationMissing`, `bankCharge.configuredReviewCondition` |

Reason codes are stable machine-readable identifiers; the response explanation is customer-safe text. Every outcome includes `policyVersion` and an ISO-8601 `evaluatedAt` timestamp.

The current policy reason codes are:

- Card: `policy.notConfigured`, `card.unsupportedType`, `transaction.notDebit`, `card.pendingAuthorization`, `transaction.notPosted`, `card.invalidAmount`, `transaction.informationMissing`, `card.windowNotConfigured`, `card.transactionDateInFuture`, `card.outsideDemoWindow`.
- Debit order: `policy.notConfigured`, `debitOrder.unsupportedType`, `debitOrder.notDebit`, `debitOrder.notPosted`, `debitOrder.invalidAmount`, `debitOrder.collectionInformationMissing`, `debitOrder.policyUnavailable`, `debitOrder.mandateInformationMissing`, `debitOrder.unauthorisedCollection`, `debitOrder.mandateNotActive`, `debitOrder.mandateMismatch`, `debitOrder.authorisedDebiCheckRequiresReview`, `debitOrder.outsideStandardWindow`, `debitOrder.amountAboveStandardReviewThreshold`.
- Bank charge: `policy.notConfigured`, `bankCharge.unsupportedType`, `bankCharge.notPosted`, `transaction.informationMissing`, `bankCharge.invalidAmount`, `bankCharge.configuredReviewCondition`.

### Demonstration configuration and policy caveats

`appsettings.Development.json` and `appsettings.Testing.json` contain the local demonstration settings. The base `appsettings.json` leaves demo policies disabled; an unconfigured policy returns `RequiresManualReview` rather than applying a demo rule.

- Card purchases use a configurable 120-calendar-day **demo-only** window. `DemoWindowEnabled` controls applicability; disabling it routes cases to manual review. This is not a card-network deadline or statement of customer rights.
- The debit-order example rule has an effective date of 13 April 2026, a 60-calendar-day standard window (day 1 begins after collection), and a configurable R2,000 standard-review threshold. Rule selection uses the collection date. The rule/effective date reflect a proposed demonstration assumption and require confirmation against authoritative bank, scheme, product, legal, and operational guidance before production use.
- Amounts above R2,000 route to enhanced/manual review, not rejection. The threshold is an illustrative internal routing value, not a legal limit or an actual bank self-service limit. Outside-window debit orders, unauthorised/unclear mandates, and authorization complaints about valid authorised DebiCheck collections remain eligible for further review; no automatic reversal is implied.
- Bank charges use a separate posted-status, information, and configurable internal review condition (`ManualReviewAboveAmount`, set to R5,000 for this demo). Card-window and debit-order mandate rules are not applied to bank charges.
- Seeded examples include an eligible card purchase (ID ending `000001`), EFT debit order (`000002`), bank charge (`000003`), pending card authorization (`000004`), high-value debit order (`000005`), incomplete mandate (`000008`), authorised DebiCheck (`000011`), Registered Mandate (`000014`), unauthorized EFT (`000017`), cancelled mandate (`000020`), DebiCheck amount mismatch (`000023`), and suspended Registered Mandate (`000026`).

The policies use deterministic inputs and never read the system clock directly. The API injects `TimeProvider`; tests use a fixed clock for date boundaries. A future dispute-submission use case must separately revalidate ownership, policy and evidence and must use an authorized, idempotent banking adapter; eligibility is not approval or a promise of funds.

### PR-07 mandate verification and investigation

The existing eligibility evaluator now reads `IDebitOrderMandateReader` after the customer-scoped transaction lookup, for debit orders only. Both the GET eligibility query and POST dispute submission use this evaluator; submission reads the mandate again rather than trusting a previous GET. The domain policy stays pure. The reader accepts the trusted customer ID, transaction ID and cancellation token.

Development and Testing use fixed, customer-scoped synthetic snapshots: EFT, Registered Mandate and DebiCheck, status **at collection** (`Active`, `Cancelled`, `Suspended`), authorization, authorized amount/currency and collection date. Unknown records or unavailable fields require investigation; legacy mandate flags stored on transactions are retained for schema compatibility but are **not** a verification fallback. Outside demo environments no banking adapter is configured: the reader returns missing information, never synthetic verification. Production never seeds accounts or enables synthetic auto-login.

This demo assumes exact amount, currency and collection-date matching, not real mandate tolerances or variable-amount rules. A later cancellation is not evidence that a mandate was cancelled at collection. A real source, lifecycle mapping and scheme-specific matching rules require separate confirmation and implementation. Raw mandate records are not exposed in HTTP responses or logged.

Debit-order configuration is under `DisputeEligibility:DebitOrder`:

| Setting | Meaning |
| --- | --- |
| `PolicyVersion` | Optional per-rule identifier; current demo `proposed-debit-order-2026-04-13-v2`. Falls back to the existing top-level version when omitted. |
| `EffectiveFrom`, `EffectiveTo` | Inclusive collection-date bounds; absent/null end means no expiry. Latest matching `EffectiveFrom` wins, independent of list order. Use unique start dates. Gaps or invalid window/threshold values require policy confirmation, never a guessed historical rule. |
| `WindowCalendarDays` | Proposed EFT/Registered Mandate standard window: `60`. |
| `StandardReviewAmountLimit` | Existing compatible name for the internal review tier: `2000.00` ZAR. At/below is standard, strictly above is enhanced/manual review, not refusal. No bank self-service limit is configured or inferred. |

Collection dates are South African calendar dates. Evaluation converts the injected UTC instant to South African time (UTC+02:00, no daylight saving). The collection day is day 0; the next calendar day is day 1; day 60 is included and day 61 is referred for review. Day 0/future collections also need review. This refines the earlier UTC-date calculation at the South African midnight boundary. Dates, the proposed 13 April 2026 applicability and the 60-day window still require operational/legal confirmation; no historical production rule is invented.

Valid, active, authorized, matching DebiCheck collections are excluded from ordinary reversal, independently of the EFT/RM window and review threshold. To preserve the existing complaint flow, the overall outcome remains `RequiresManualReview`; the response adds `ordinaryReversalOutcome: "Ineligible"` and `reviewRoute: "Investigation"`. Missing/unauthorized/inactive/mismatched mandate information also sets the investigation route, without claiming a definitive ordinary-reversal outcome. Optional fields are omitted elsewhere, including card/bank-charge responses. Existing outcomes, reason codes and `availableReasons` remain compatible.

For example, GET the eligibility route for transaction `00000000-0000-0000-0000-000000000011`, then submit `unauthorisedDebitOrder` with a description via the existing POST disputes endpoint. It creates `ManualReview` / `Pending` / `NotInitiated` and one initial event, not a reversal. Angular displays the server explanation and keeps the existing complaint form available; it implements no mandate policy.

PR-07 provides assessment and manual-review intake, **not** an investigator queue or bank-core workflow. Case storage retains the selected eligibility policy version, overall outcome and complaint reason, not a mandate snapshot. PR-08 adds the restricted simulation described below; it does not introduce a production investigation service.

Focused checks:

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --filter "FullyQualifiedName~DisputeEligibilityPolicyTests|FullyQualifiedName~DisputeEligibilityEndpointsTests|FullyQualifiedName~DebitOrderMandateReaderTests|FullyQualifiedName~DisputeEndpointsTests"
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

## Dispute submission

The eligibility response includes `availableReasons` (`[{ "code", "label" }]`, empty when `Ineligible`). The transaction details page shows a form for eligible and manual-review outcomes, then navigates to the persisted case timeline. A confirmation link remains available if navigation does not complete.

| Route | Purpose |
| --- | --- |
| `POST /api/v1/disputes` (alias `POST /v1/disputes`) | Submit a dispute for one of the caller's transactions |
| `GET /api/v1/disputes/{id}` (alias `GET /v1/disputes/{id}`) | Read one of the caller's disputes, including its timeline |

```powershell
curl.exe -X POST "http://localhost:5100/api/v1/disputes" -H "Content-Type: application/json" `
  -d '{"transactionId":"00000000-0000-0000-0000-000000000001","reasonCode":"unrecognisedTransaction","description":"I did not make this purchase at the store."}'
```

A successful submission returns `201 Created` with a `Location` header:

```json
{
  "id": "…",
  "reference": "DSP-0A1B2C3D4E5F",
  "transactionId": "00000000-0000-0000-0000-000000000001",
  "category": "CardPurchase",
  "reasonCode": "unrecognisedTransaction",
  "description": "I did not make this purchase at the store.",
  "caseStatus": "Submitted",
  "decisionStatus": "Pending",
  "financialStatus": "NotInitiated",
  "createdAt": "2026-11-12T10:00:00+00:00",
  "timeline": [{ "sequenceNumber": 1, "eventType": "DisputeSubmitted", "caseStatus": "Submitted", "…": "…" }]
}
```

Rules:

- The customer is derived from the trusted identity; any `customerId` in the body is ignored. The transaction lookup is customer-scoped, so another customer's transaction is indistinguishable from an unknown one.
- Eligibility is re-evaluated with the same PR-04 policies at submission time. `Ineligible` is rejected; `RequiresManualReview` creates the case with `caseStatus: ManualReview`.
- `reasonCode` must belong to the transaction's category (card: `unrecognisedTransaction`, `duplicateCharge`, `goodsOrServicesNotReceived`, `incorrectAmount`; debit order: `unauthorisedDebitOrder`, `amountDiffersFromMandate`, `mandateCancelled`, `duplicateCollection`; bank charge: `incorrectFee`, `duplicateFee`, `feeNotAgreed`). `description` is trimmed and must be 15–2000 characters.
- `caseStatus`, `decisionStatus`, and `financialStatus` are independent. This slice only produces `Submitted`/`ManualReview`, `Pending`, and `NotInitiated`; no money moves.
- The dispute row and its first `DisputeEvent` are saved in a single transaction. `DisputeEvents` is append-only: SQLite triggers reject `UPDATE` and `DELETE`.
- A filtered unique index (`IX_Disputes_CustomerId_TransactionId_Open`, where `CaseStatus <> 'Closed'`) prevents more than one open case per customer transaction, including under concurrent requests.

| Status | Code | When |
| --- | --- | --- |
| 400 | `dispute.invalidRequest` | Malformed JSON or invalid fields (`errors[]` with `/transactionId`, `/reasonCode`, `/description` pointers) |
| 400 | `dispute.invalidId` | Non-GUID dispute ID on `GET` |
| 401 | — | Unauthenticated, including Development |
| 403 | — | Authenticated identity lacks Customer access |
| 404 | `dispute.notFound` | Unknown or another customer's dispute |
| 409 | `dispute.activeCaseExists` | An open dispute already exists for the transaction |
| 422 | `dispute.transactionNotFound`, `dispute.invalidReason`, `dispute.transactionIneligible` | Unknown/inaccessible transaction, reason not valid for the category, or ineligible transaction (`detail` holds the customer-safe explanation) |

The `AddDisputes` migration is additive (`Disputes`, `DisputeEvents`, indexes, and triggers); its `Down` removes them without touching transactions. Full request idempotency (`Idempotency-Key` replay) remains deferred under the revised roadmap; a retried submission returns 409 once the first succeeds. A network timeout is not proof that submission failed: check **My disputes** before retrying.

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --filter "FullyQualifiedName~DisputeEndpointsTests|FullyQualifiedName~SqliteDisputeStoreTests"
Set-Location apps/web
npm run test:ci -- --include=src/app/disputes/dispute-form.spec.ts
```

## Unified dispute tracking (PR-10)

Use **Your disputes** in the navigation, or open Angular `/disputes`, to see the authenticated
customer's submitted cases across all three categories. Each item links to `/disputes/{id}`;
successful submission opens the timeline automatically. The dashboard has loading,
empty, error/retry and load-more states. Case details reuse the existing case/status presentation,
with a keyboard-focusable ordered timeline and explicit UTC timestamps. The details heading receives
focus after loading; switching cases cancels the previous request. Refresh status reads the latest
case and timeline together, without initiating processing or inferring confirmation from approval.

| API route (both prefixes supported) | Result |
| --- | --- |
| `GET /api/v1/disputes`, `GET /v1/disputes` | Owned summaries, newest `createdAt` first and reference ascending for timestamp ties |
| `GET /api/v1/disputes/{id}`, `GET /v1/disputes/{id}` | Owned case, separate statuses, customer-safe `statusDescription`, and complete timeline |
| `GET /api/v1/disputes/{id}/events`, `GET /v1/disputes/{id}/events` | Array of customer-safe events ordered by ascending `sequenceNumber` |

All queries require authentication and use `Cache-Control: private, no-store`. Another customer's
case/events are indistinguishable from a missing case (404). Lists never expose complaint text,
evidence, audit identities or internal operation references. Timelines retain their existing small,
unpaged simulation-history contract; sequence is the canonical chronological order even when timestamps
tie or the clock changes. The dashboard uses GET case's state/timeline snapshot; the events subresource
is available for timeline-only consumers, not an extra request that could race the case status.

List parameters: `pageSize` defaults to 20 and is bounded to 1-100; optional `cursor` must be the opaque
`pageInfo.nextCursor` from the previous response. Invalid values return 400 `dispute.invalidQuery`
with `/pageSize` or `/cursor` errors. The next cursor is omitted when there is no next page.
The cursor is not an authorization credential; customer scoping always applies. It uses the immutable
creation time/reference boundary, so newly submitted cases appear on refresh rather than shifting
already traversed pages. This new collection uses cursor pagination; existing transaction-list
page-number pagination is unchanged.

```json
{ "items": [], "pageInfo": { "hasNextPage": false } }
```

### Bank-charge investigation and adjustment

`POST /api/v1/disputes/{id}/bank-charge-simulations` is Development/Testing-only and requires the
authenticated owner plus `X-Simulation-Key`, checked against `Simulation:BankChargeKey`. With no
configured key, customers cannot process cases. Other environments have no route or mock registration.
Card/debit-order cases cannot enter this workflow, and bank-charge cases cannot enter either of theirs.

| Command | Required state | Result |
| --- | --- | --- |
| `StartReview` | Submitted or legacy ManualReview bank-charge case | UnderReview, decision Pending, financial NotInitiated |
| `Approve` | UnderReview/Pending and current valid fee facts/policy | Decision Approved; financial still NotInitiated |
| `Reject` | UnderReview/Pending | Case and decision Rejected, no adjustment |
| `RequestAdjustment` | Approved, financial NotInitiated, current valid fee facts/policy | Financial Requested, stable `MOCK-FEE-{caseId}` reference |
| `ExecuteAdjustment` | Approved, financial Requested, current valid fee facts/policy | Explicit mock result: Confirmed, Failed or OutcomeUnknown |
| `Close` | Approved/Confirmed or Rejected/NotInitiated | Closed; decision and financial outcome preserved |

Investigation does not automatically approve a fee. Missing transaction information or an unavailable
policy blocks approval/adjustment; an amount routed for internal review can be explicitly approved
after investigation. Invalid/repeated commands return 409 `bankCharge.invalidTransition`. The case,
adjustment reference and append-only `DisputeEvent` commit in one SQLite transaction. Concurrent
execution and restart replays cannot repeat a committed adjustment. Failed/unknown results remain
unconfirmed and cannot be retried or closed; recovery remains future work.
The mock is pure, local and never posts funds. Never replace it with an external call inside the
SQLite writer transaction.

### Three end-to-end demo journeys

Use a disposable local demo database with the normal API/Angular startup commands. To enable
privileged simulation, generate private keys in the API shell before startup:

```powershell
$env:Simulation__DebitOrderKey = [guid]::NewGuid().ToString('N')
$env:Simulation__CardNetworkKey = [guid]::NewGuid().ToString('N')
$env:Simulation__BankChargeKey = [guid]::NewGuid().ToString('N')
```

Keep these values private to the simulator (REST Client examples are in `apps/api/ClearCase.Api.http`);
they are never given to the customer UI. In the transaction list, open and submit each case below,
using a description of at least 15 characters. Submission opens the timeline; then return to
**Your disputes** to verify all categories are present. A fresh submission adds one initial event.

| Journey | Seeded transaction / reason | Privileged simulation commands, then refresh the customer details |
| --- | --- | --- |
| Card purchase | `00000000-0000-0000-0000-000000000001` / `duplicateCharge` | Initiate, ForwardToAcquirer, MerchantAccept, RequestSettlement, ExecuteSettlement, Close |
| Debit order | `00000000-0000-0000-0000-000000000002` / `duplicateCollection` | StartReview, VerifyMandate, RequestReversal, ExecuteReversal, Close |
| Bank charge | `00000000-0000-0000-0000-000000000003` / `incorrectFee` | StartReview, Approve, RequestAdjustment, ExecuteAdjustment, Close |

Before each execution command, refresh to verify requested is **not confirmed**. Only an explicit
successful mock result shows simulated confirmation; no real money moves. For the bank rejection
path, use another posted synthetic fee (for example transaction ending `000006`), then StartReview,
Reject, Close. The detailed card representment and debit investigation examples below remain available.
Seed dates are fixed, so eligibility changes with time; automated tests inject a deterministic clock.

**Migration / rollback:** `AddBankChargeTracking` adds one nullable adjustment-reference column,
a unique reference index and a customer/creation/reference listing index. Existing rows/history and
routes remain compatible. Development auto-migrates; other environments use the documented explicit
migration procedure. Clear the bank simulation key to disable processing immediately. Before fee
processing, downgrade to `20261113063056_AddChargebacks` and revert code. Once operations exist,
retain the column/current reader or restore an approved pre-PR disposable-demo backup; do not drop
pending/unknown operation references. No new library, external integration or global state store is used.

Focused checks:

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~DisputeTrackingTests
Set-Location apps/web
npm run test:ci -- --include=src/app/disputes/dispute-pages.spec.ts --include=src/app/disputes/dispute-form.spec.ts
```

## Current limitations

### PR-08 debit-order processing simulation

`POST /api/v1/disputes/{id}/debit-order-simulations` accepts `{ "command": "StartReview" }`.
It is mapped **only** in Development/Testing, requires the authenticated case owner **and**
`X-Simulation-Key` matching server configuration `Simulation:DebitOrderKey`. No configured key,
a missing key, or an incorrect key returns 403; Production/Staging have no route (404), even
when a key is configured. The key is never sent to Angular, logged, stored in a case, or checked in.
Generate a fresh local value in the API shell before starting:

```powershell
$env:Simulation__DebitOrderKey = [guid]::NewGuid().ToString('N')
dotnet run --project apps/api/src/ClearCase.Api --urls http://localhost:5100
```

Keep the key private to the local simulator. Do not put real customer data in demo environments.
Use the REST Client examples in `apps/api/ClearCase.Api.http` with that environment variable.
Ordinary customer requests cannot process cases. GET details remains customer-owned, without the key.
The existing submission confirmation can now refresh case, decision, financial status and the full
timeline; it has no processing controls.

| Command | Required state / checks | Result |
| --- | --- | --- |
| `StartReview` | `Submitted` or legacy `ManualReview`, debit order only | `UnderReview` |
| `VerifyMandate` | `UnderReview`; fresh PR-07 mandate and eligibility evaluation | `MandateVerified` for ordinary eligible collections; otherwise remains `UnderReview` with an investigation event |
| `CompleteInvestigation` | `UnderReview`; explicit simulated investigation, fresh eligible mandate/policy | `MandateVerified`; never overrides missing/mismatched data, valid DebiCheck exclusion or enhanced-review policy |
| `RequestReversal` | `MandateVerified`; recheck current eligibility | `ReversalRequested` / decision `Approved` / financial `Requested`, durable `MOCK-DO-{caseId}` operation reference |
| `ExecuteReversal` | `ReversalRequested` / financial `Requested`; recheck current eligibility | Mock `Confirmed`: `ReversalConfirmed` / financial `Confirmed`; mock `Failed` or `Unknown`: remain `ReversalRequested` / financial `Failed` or `OutcomeUnknown` |
| `Reject` | `UnderReview`, `MandateVerified`, or a definitively failed simulated reversal | `Rejected` / decision `Rejected`; no financial confirmation |
| `Close` | `Rejected` or `ReversalConfirmed` only | `Closed`, preserving decision and financial outcome |

Authorization, amount and cancellation complaints require explicit investigation, even when the
reader reports a matching mandate. Missing information cannot be waived by a command. A specialist
queue, evidence capture and enhanced-review approval are deferred; affected cases remain under review
or can be explicitly rejected in this simulation. Submission behavior and legacy aliases are unchanged.

Invalid transitions and repeated processing return 409 `debitOrder.invalidTransition`; bad commands
return 400 `debitOrder.invalidCommand` with `/command` field errors. Missing/inaccessible cases return
404 `dispute.notFound`. The generated Development OpenAPI describes the actual route and response.

`IDebitOrderProcessingGateway` is implemented by a deterministic, side-effect-free mock (default:
confirmed). Tests inject its failed/unknown outcomes. Approval never confirms funds. SQLite acquires
the writer lock **before** reading processing state, and commits case updates, the unique operation
reference and each `DisputeEvent` together. Racing requests serialize; a committed gateway outcome
cannot be executed again, including after restart. Unknown outcomes cannot be retried, rejected or
closed by this interface; reconciliation is deferred to PR-15.

This transaction boundary is safe **only because the mock has no external side effects**. A database
rollback can recompute the pure mock using the same reference; it cannot move money twice. Never
replace it with an external banking call inside this transaction. Outbox delivery, audit actors,
general optimistic concurrency and recovery remain later roadmap work.

**Migration / rollback:** `AddDebitOrderOperationReference` adds one nullable column and unique index.
Existing cases/events are preserved. Development applies it on startup; other environments require
the documented explicit migration process. Disable simulation immediately by clearing the key.
Before any new states are persisted, the migration can be downgraded to `20261112185347_AddDisputes`
and code reverted (tested with legacy rows). After processing, old binaries cannot read the new enum
values: retain this reader and disable commands, or restore a verified pre-PR backup of the disposable
demo database. Do not downgrade retained processing history or discard unknown operation references.

**Demo:** submit `duplicateCollection` for synthetic EFT transaction ending `000002`, then run
`StartReview`, `VerifyMandate`, `RequestReversal`, `ExecuteReversal`, `Close`, refreshing the existing
confirmation after each command. Approval/requested is visibly not confirmation. Transaction `000008`
demonstrates missing mandate; reason `unauthorisedDebitOrder` on `000002` demonstrates explicit
investigation. Seed date-based eligibility changes as time passes; tests use a fixed clock.

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~DebitOrderProcessingTests|FullyQualifiedName~SqliteDisputeStoreTests"
Set-Location apps/web
npm run test:ci -- --include=src/app/disputes/dispute-form.spec.ts
```

### PR-09 card chargeback and representment simulation

`POST /api/v1/disputes/{id}/chargeback-simulations` reuses the owned dispute resource and accepts
`{ "command": "Initiate" }`. Like debit-order processing, it exists only in Development/Testing
and requires both authentication and `X-Simulation-Key`. This workflow uses its own server setting,
`Simulation:CardNetworkKey`; the debit-order key does not grant card privileges.
Generate a private local key before starting the API:

```powershell
$env:Simulation__CardNetworkKey = [guid]::NewGuid().ToString('N')
dotnet run --project apps/api/src/ClearCase.Api --urls http://localhost:5100
```

Missing/unconfigured/wrong keys return 403. Production/Staging have no simulation endpoint or mock
gateway registration, even if a key is configured. No processing controls or keys are added to Angular.

| Command | Required chargeback state | Result |
| --- | --- | --- |
| `Initiate` | No chargeback; owned submitted/manual-review card dispute with freshly eligible transaction | `Initiated` on mock acknowledgement; case `UnderReview`, decision `Pending`, financial `NotInitiated` |
| `ForwardToAcquirer` | `Initiated` | Mock network forwards to acquirer; `MerchantReview` |
| `MerchantAccept` | `MerchantReview` | `MerchantAccepted`, decision `Approved`; financial remains `NotInitiated` |
| `MerchantContest` | `MerchantReview` | `MerchantContested`, decision still `Pending`; contest is not a customer loss |
| `ReceiveRepresentment` | `MerchantContested` with valid metadata | `RepresentmentReceived` |
| `ReviewRepresentment` | `RepresentmentReceived` with stored evidence | `IssuerReview` |
| `Approve` / `Reject` | `IssuerReview` | `DecisionIssued`; final dispute decision `Approved` (customer favoured) or `Rejected`, no financial confirmation |
| `RequestSettlement` | Merchant acceptance or issuer approval; financial `NotInitiated` | Financial `Requested` only |
| `ExecuteSettlement` | Approved decision and financial `Requested` | Explicit mock result sets financial `Confirmed`, `Failed` or `OutcomeUnknown` |
| `Close` | Approved and financially confirmed, or rejected without settlement | Chargeback and case `Closed`, preserving decision and financial status |

`Chargeback` is linked one-to-one to the dispute. Its stable `MOCK-CB-{caseId}` reference is reserved
locally, **not proof of network acknowledgement**. The reason code comes from the existing card complaint
catalog, not real Visa/Mastercard scheme codes. GET dispute details adds an optional `chargeback` object:
`reference`, `reasonCode`, `status`, `lastOperationOutcome`, and optional `evidence`. Other cases omit it.
The existing confirmation/refresh display shows chargeback progress separately from the case decision
and financial status; a card settlement is not labelled a debit-order reversal.

Representment accepts only synthetic metadata, for example:

```json
{
  "command": "ReceiveRepresentment",
  "evidence": {
    "id": "aaaaaaaa-1111-4222-8333-444455556666",
    "kind": "Receipt"
  }
}
```

The ID must be a nonempty GUID. Kinds are `Receipt`, `DeliveryConfirmation`, or `AuthorizationRecord`;
`receivedAt` comes from the server clock. Metadata is required for representment and forbidden on other
commands. No file is uploaded or verified, and no filename, content or storage URL is accepted/exposed.
Actual evidence storage/authorization remains future work.

The deterministic `ICardNetworkGateway` mock handles initiation, forwarding and explicit settlement;
tests inject failed and unknown results (unknown models a timeout). A failed/unknown operation retains
the prior chargeback stage (`InitiationPending` if initiation never acknowledged) and exposes its
`lastOperationOutcome`. It blocks all further processing, including repeated attempts and closure.
An initiation/forwarding timeout never changes financial status to confirmed. Recovery is deferred;
there is no retry loop, automatic refund, arbitration, real network call or Kafka.

Each command atomically updates the case and chargeback, appends a customer-safe `DisputeEvent`, and
appends a separate `ChargebackAuditEvent` with authenticated actor ID, correlation ID, command,
resulting statuses, evidence metadata and timestamp. A unique `MOCK-CARD-{caseId}-{command}` operation
reference, one chargeback per dispute, and SQLite's writer lock prevent duplicate committed operations,
including across races/restarts. Audit UPDATE/DELETE is rejected by SQLite triggers.
The gateway is pure and runs inside that local transaction; rollback may recompute it without external
effects. Do not substitute a real network call here. External intent/outbox/reconciliation need a
separate design before integration.

Invalid fields return 400 `chargeback.invalidRequest` with JSON Pointer errors; invalid transitions
or repeated commands return 409 `chargeback.invalidTransition`; inaccessible cases return 404
`dispute.notFound`. A 200 processing response means the simulated outcome was recorded, **not** that
funds succeeded. The generated Development OpenAPI includes the route, commands and explicit response
DTOs; the architecture document distinguishes this executable slice from future network integration.

**Migration / rollback:** `AddChargebacks` adds `Chargebacks`, `ChargebackAuditEvents`, constraints,
indexes and audit triggers; existing case states, tables, timelines and routes remain compatible.
Apply via Development startup or the existing explicit migration procedure. Clear the card simulation
key for immediate operational backout, preserving readers and history. Before chargeback processing,
the schema can be downgraded to `20261113060653_AddDebitOrderOperationReference`. After processing,
retain the tables/reader or restore an approved pre-PR disposable-demo backup; downgrade would delete
chargeback/audit records and must not be used to erase retained financial or unknown-outcome history.

**Demo:** submit `duplicateCharge` for synthetic card transaction ending `000001` in a disposable
demo database, then `Initiate`, `ForwardToAcquirer`, `MerchantAccept`, `RequestSettlement`,
`ExecuteSettlement`, `Close`. For representment, instead use `MerchantContest`, `ReceiveRepresentment`
with the metadata above, `ReviewRepresentment`, then `Approve` or `Reject`. Approval still requires
the two explicit settlement steps before closure. Refresh the existing confirmation after each action.
REST Client examples are in `apps/api/ClearCase.Api.http`. Policy windows remain demo-only.

```powershell
dotnet test apps/api/tests/ClearCase.Api.IntegrationTests/ClearCase.Api.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~ChargebackProcessingTests|FullyQualifiedName~DebitOrderProcessingTests|FullyQualifiedName~SqliteDisputeStoreTests"
Set-Location apps/web
npm run test:ci -- --include=src/app/disputes/dispute-form.spec.ts
```

### Other limitations

- Local Identity accounts are not a bank identity-provider integration. Production needs controlled account provisioning, HTTPS and operational hardening; no automatic accounts or demo fallback are available.
- The Angular pages display synthetic data only. Dispute submission creates a case record but no reversal, refund, chargeback, notification, or evidence upload.
- Dispute submission has CSRF protection but no `Idempotency-Key` replay contract yet; submission/idempotency behavior is unchanged by PR-13.
- SQLite persistence is local-only; production identity integration, PostgreSQL, event broker, external banking adapter, cloud resources, and deployment manifests are not included.
- Development CORS permits only the configured local Angular origin. CORS is not enabled outside Development.
- On 2026-11-12, `npm audit --omit=dev` reported zero vulnerabilities. The full audit reported 16 moderate findings in development tooling; npm's suggested forced fix upgrades Angular CLI to 22, so that breaking upgrade is deferred for a dedicated dependency review.

See `docs/adr/0001-bootstrap-foundation.md` for the initial scope decision. Target designs in `docs/target-design/` describe future work and are not claims about implemented behavior.
