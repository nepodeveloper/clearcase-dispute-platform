# ClearCase

ClearCase is a starter monorepo for incrementally building a financial dispute management platform. This PR establishes a compilable ASP.NET Core 10 API, an Angular 21 application, basic health endpoints, automated tests, and CI.

**This is an engineering foundation, not a production dispute platform.** It does not yet include authentication, customer or transaction data, dispute submission, a timeline, persistence, banking integrations, Kafka, or AWS. No real customer information or money movement is involved.

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

Open `http://localhost:4200`. The API exposes `GET /health/live` and `GET /health/ready` at `http://localhost:5100`. In Development, the OpenAPI document is available at `http://localhost:5100/openapi/v1.json`. The readiness response verifies application startup only; it does not check a database or other dependency.

For a manual API smoke check, open `apps/api/ClearCase.Api.http` in VS Code with the REST Client extension, or request the endpoints directly with `curl`.

## Current limitations

- There is no authentication or authorization. The API must not be exposed as a customer-facing service.
- Health endpoints are the only API behavior. The Angular shell does not submit disputes or display customer data.
- No database, migrations, event broker, external banking adapter, cloud resources, or deployment manifests are included.
- Development CORS permits only the configured local Angular origin. CORS is not enabled outside Development.
- On 2026-11-12, `npm audit --omit=dev` reported zero vulnerabilities. The full audit reported 16 moderate findings in development tooling; npm's suggested forced fix upgrades Angular CLI to 22, so that breaking upgrade is deferred for a dedicated dependency review.

See `docs/adr/0001-bootstrap-foundation.md` for the initial scope decision. Target designs in `docs/target-design/` describe future work and are not claims about implemented behavior.
