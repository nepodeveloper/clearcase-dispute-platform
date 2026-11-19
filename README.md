# ClearCase

ClearCase is a local demo platform for raising and handling bank-transaction disputes.
Customers dispute card purchases, debit orders and bank fees; bank Agents investigate and
process the cases; Supervisors review escalations. Every step is recorded on a timeline the
customer can follow.

> [!IMPORTANT]
> **Demo only.** All data is synthetic and every financial step (chargebacks, reversals, fee
> refunds) is a local mock, so no real money moves. This is an engineering foundation, not a
> production system: there is no bank identity provider, MFA, live banking integration or cloud
> deployment. Don't load real customer data or expose the Development environment to untrusted users.

## Features

- **Customers:** search and filter transactions, check whether a transaction can be disputed,
  submit a dispute and follow its progress.
- **Agents:** work a bank-wide case queue, add private investigation notes, move cases through
  their workflow (decision and mock settlement, reversal or adjustment) and escalate cases.
- **Supervisors:** acknowledge and resolve escalations raised by Agents.
- **Under the hood:** idempotent submissions, optimistic concurrency, append-only timelines and
  audit records, and a transactional outbox with optional Kafka publishing.

## Tech stack

| Area | Technology |
| --- | --- |
| API | ASP.NET Core 10 modular monolith, EF Core with SQLite, ASP.NET Core Identity (cookie auth) |
| Web | Angular 21, Vitest, Playwright |
| CI/CD | GitHub Actions, with equivalent Azure DevOps pipelines |

## Repository layout

```text
apps/api/src/       ClearCase.Api, .Application, .Domain, .Infrastructure (migrations, seed data)
apps/api/tests/     API integration tests
apps/web/           Angular app (src/) and Playwright browser tests (e2e/)
.github/workflows/  CI and the manual release-simulation workflow
.azuredevops/       Equivalent Azure DevOps pipelines
scripts/ci/         Repository policy and release-simulation scripts
infra/azure/        Azure infrastructure blueprint (Bicep)
deploy/helm/        Helm chart (Ingress load balancer, mock mode for local clusters)
docs/               Production-readiness register, Kubernetes guide and operations runbook
```

## Prerequisites

- .NET 10 SDK
- Node.js 24.18.0 and npm 11.16.x
- PowerShell 7 (used by the commands below)

SQLite is bundled, so no database server, Docker or administrator rights are needed.

## Run locally

**1. Start the API** from the repository root. Choose a password for each demo account when
prompted (at least 12 characters, with uppercase, lowercase, a digit and a symbol):

```powershell
$env:Authentication__SeedPasswords__Customer = Read-Host 'Customer password' -MaskInput
$env:Authentication__SeedPasswords__OtherCustomer = Read-Host 'Second customer password' -MaskInput
$env:Authentication__SeedPasswords__Agent = Read-Host 'Agent password' -MaskInput
$env:Authentication__SeedPasswords__Supervisor = Read-Host 'Supervisor password' -MaskInput
dotnet run --project apps/api/src/ClearCase.Api --urls http://localhost:5100
```

On first start the API creates `apps/api/src/ClearCase.Api/App_Data/clearcase.db`, applies
migrations and seeds the demo accounts and synthetic transactions. The passwords are used only
when an account is first created; changing them later doesn't affect an existing database.

**2. Start the web app** in a second terminal:

```powershell
Set-Location apps/web
npm ci
npm start
```

**3. Open <http://localhost:4200>** and sign in. The dev server proxies `/api` to port 5100, so
keep the API on that port.

The API also serves `/health/live`, `/health/ready` and, in Development, an OpenAPI document at
`/openapi/v1.json`. Example requests are in [ClearCase.Api.http](apps/api/ClearCase.Api.http).

## Demo accounts

| Username | Password variable | Opens |
| --- | --- | --- |
| `customer.demo` | `Authentication__SeedPasswords__Customer` | Customer transactions (`/`) |
| `customer.other` | `Authentication__SeedPasswords__OtherCustomer` | A second customer, for checking data isolation |
| `agent.demo` | `Authentication__SeedPasswords__Agent` | Dispute work queue (`/agent/disputes`) |
| `supervisor.demo` | `Authentication__SeedPasswords__Supervisor` | Escalation queue (`/supervisor/escalations`) |

## Demo walkthrough

1. Sign in as `customer.demo`, open a transaction, pick a reason, describe the issue (at least
   15 characters) and select **Submit dispute**. The case timeline opens.
2. Sign out, sign in as `agent.demo`, search the queue for the case reference and open it.
   Add a private note, then process the case with the action buttons (each asks for confirmation).
3. Or select **Escalate case** instead, then sign in as `supervisor.demo` to acknowledge and
   resolve the escalation. A case can't be processed while its escalation is open.
4. Sign back in as the customer. The timeline shows progress and the outcome, never staff notes.

Seeded transactions for each category, with the Agent's happy-path actions:

| Transaction | Category | Agent actions |
| --- | --- | --- |
| `/transactions/00000000-0000-0000-0000-000000000001` | Card purchase | Initiate chargeback → Send to acquirer → Merchant accepts → Request settlement → Process mock settlement → Close case |
| `/transactions/00000000-0000-0000-0000-000000000002` | Debit order | Start review → Verify mandate → Approve & request reversal → Process mock reversal → Close case |
| `/transactions/00000000-0000-0000-0000-000000000003` | Bank charge | Start review → Approve dispute → Request fee adjustment → Process mock adjustment → Close case |

## Build and test

Backend, from the repository root:

```powershell
dotnet restore apps/api/ClearCase.slnx
dotnet build apps/api/ClearCase.slnx --configuration Release --no-restore
dotnet test apps/api/ClearCase.slnx --configuration Release --no-build
```

Frontend, from `apps/web`:

```powershell
npm ci
npm run test:ci                          # unit tests
npm run build
npx playwright install chromium          # first time only
npm run test:e2e                         # browser tests
```

`npm run test:e2e` starts its own API and web server with temporary databases and generated
passwords, so it never touches your local database. To run one project, use
`npm run test:e2e -- --project customer-desktop`.

## Reset the demo database

Startup never overwrites existing data. To start fresh, stop the API and delete the database
files. Only do this for disposable demo data.

```powershell
$db = 'apps/api/src/ClearCase.Api/App_Data/clearcase.db'
Remove-Item $db, "$db-wal", "$db-shm", "$db-journal" -ErrorAction SilentlyContinue
```

The next start recreates and reseeds it, so set the password variables again first.

## Optional: Kafka

Events go to a local logging publisher by default. To publish to Kafka instead (requires Docker),
start the broker and set these variables before starting the API:

```powershell
docker compose -f compose.kafka.yml up -d
$env:Messaging__Kafka__Enabled = 'true'
$env:Messaging__Kafka__BootstrapServers = 'localhost:9092'
```

## CI/CD

- [ci.yml](.github/workflows/ci.yml) runs on pull requests and pushes to `main`. It covers
  repository policy, the backend build (locked NuGet restore), tests, migration checks and coverage gates,
  dependency audits, the frontend tests and build, the Playwright browser tests, Helm lint and schema validation,
  container image builds and a Kubernetes smoke test on kind with mock data.
- [security.yml](.github/workflows/security.yml) runs CodeQL and dependency review;
  [package-images.yml](.github/workflows/package-images.yml) is a manual workflow that pushes attested images to GHCR.
- [release-simulation.yml](.github/workflows/release-simulation.yml) is a manual, demo-only
  promotion through `clearcase-development` → `clearcase-uat` → `clearcase-production`, with
  approval required before production. It deploys nothing; setup steps are in the workflow's
  header comment.

## Limitations

- Local Identity accounts only: no bank identity provider, MFA or case assignment. Agents and
  Supervisors can see every case.
- All financial processing is mocked. There are no real refunds, reversals, chargebacks,
  notifications or evidence uploads.
- SQLite and the bundled Kafka broker are for local use. A production database, broker hosting
  and cloud deployment are not included.
- Coverage is gated in CI (merged line coverage: Domain 85%, Application 80%, API 75%, overall 80%).
- Production readiness gaps (identity provider, shared database, maker-checker, real banking adapters) are tracked in
  [docs/PRODUCTION-READINESS.md](docs/PRODUCTION-READINESS.md).
