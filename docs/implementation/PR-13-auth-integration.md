## PR ID / business outcome
- PR: PR-13 - Authentication UI & Backend Integration.
- Title: `feat(auth): connect ClearCase login UI and role-based navigation`
- Customer/operational outcome: Customer, Agent and Supervisor sign in through the existing design using ASP.NET Core Identity cookies, restore sessions, reach their assigned workspace and sign out. Customer dispute submission remains CSRF-protected.

## Scope / non-goals
- Changed:
  - Typed signal-based session service, reactive login, functional guards, role-aware shell, accessible staff placeholders, safe return destinations and explicit loading/error states.
  - Shared same-origin API CSRF/correlation interceptor; dispute submission payload and business logic remain unchanged.
  - Development fallback removed; Testing fallback defaults off and is explicitly enabled only in legacy fixtures.
  - Seed passwords supplied by configuration; existing password hashes, security stamps and lockout state are not reset.
  - Real-API browser tests with disposable SQLite and runtime-generated passwords; shared login styles extracted from the isolated preview.
  - Fixed preview mobile overflow from absolutely positioned table labels, drawer visibility/focus timing, decorative button naming and dialog tab containment. Existing accessibility assertions retained.
  - CI frontend job explicitly installs .NET for real-API browser tests. Concurrent Angular test servers disable dependency prebundling to avoid shared optimized-cache/duplicate-Angular injection failures.
- Not changed: Agent queues, assignment, note persistence, escalation APIs, financial workflows, Kafka, MFA, external identity providers, schema, event contracts or dispute idempotency. PR-14 has not started.

## Design / contracts
- API, events, database migrations, ADRs affected:
  - Existing `POST /api/v1/auth/login`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me` reused; generated OpenAPI metadata and [HTTP examples](../../apps/api/ClearCase.Api.http) updated.
  - `/auth/me` returns non-cacheable identity, backend roles and CSRF token. Staff `customerId` remains null for compatibility.
  - No migrations, event changes, new libraries or architecture-layer changes.
- Compatibility and versioning:
  - Existing customer routes, owned-data queries and dispute contracts remain intact.
  - Anonymous Development requests intentionally change to 401. Agent/Supervisor access to Customer APIs remains forbidden. Guards are UX, not the authorization boundary.
  - CSRF is attached only to same-origin versioned API mutations, including `/v1` aliases, excluding initial login. Authentication failures clear in-memory identity/token; external 401s and late failures from an older token do not invalidate a newer session.
  - Login returns Customer to `/`, Agent to `/agent/disputes`, Supervisor to `/agent/escalations`, unless a known, role-appropriate application-relative return URL is present.

## Verification — actual executed commands
- [x] Relevant failing test written first (where practical)
  - `dotnet test .\apps\api\tests\ClearCase.Api.IntegrationTests\ClearCase.Api.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~AuthEndpointsTests"`: initial red, 3 Development fallback failures, 11 passing.
  - From `apps/web`: `npm run test:ci -- --include "src/app/auth/*.spec.ts" --include "src/app/csrf-session.spec.ts"`: initial red, missing integration modules.
- [x] Backend build + tests — command/result:
  - `dotnet --info`: .NET SDK 10.0.403 available.
  - `dotnet build .\apps\api\ClearCase.slnx -c Release --no-restore`: passed, zero warnings/errors.
  - `dotnet test .\apps\api\ClearCase.slnx -c Release --no-restore`: final build/test passed, **238/238**.
- [x] Angular build + tests — command/result:
  - From `apps/web`: `npm run test:ci`: **77/77** tests, 12 files passed.
  - `npm run build`: passed.
  - `npm run build:ui`: passed.
  - Dependencies were already available; no reinstall/restore was necessary. `npm ci` was not rerun locally; CI performs clean installs.
- [x] Integration/E2E/negative tests — command/result:
  - From `apps/web`: `npm run test:e2e`: final **11/11** passed.
  - Real cookie login for every role, protected return navigation, refresh, denied routes, logout, empty/invalid/expired cookies, CSRF, ownership, supervisor policy and seed preservation covered across browser/backend/unit tests.
  - Browser tests submit an actual bank-charge dispute with CSRF and open its timeline; no mocks replace the authentication API.
  - Initial preview browser failures were reproduced and fixed. Broad regression failures from legacy auto-login assumptions were fixed by explicit session setup, without deleting tests.
  - `git diff --check`: passed.
- [ ] Lint/security scans — command/result:
  - No separate lint/security scanner run; no new dependencies. Narrow source/bundle checks found no former hardcoded seed password prefix or real-application role-preview selector.
- [x] Customer ownership and sensitive-data handling reviewed
  - No browser storage of passwords/tokens/cookies. Cookie remains HTTP-only and server-authoritative; screenshots exclude credentials and auth tracing is disabled.
- [x] Financial state/duplicate/unknown-outcome implications reviewed
  - No financial status, policy, duplicate handling, gateway or settlement behavior changed.

## Deployment, feature flag and rollback
- Deployment steps:
  1. Follow [local setup](../../README.md#run-locally). Provide four strong seed passwords through process environment variables for a new disposable Development database. Never commit values.
  2. Treat PR-12's published passwords as compromised. Recreate only a verified disposable demo database, or rotate credentials/security stamps through controlled Identity administration for retained data. Updating seed variables alone does not rotate existing users.
  3. Deploy API and Angular together. Development always requires sign-in; use same-origin proxy/hosting and HTTPS outside local Development/Testing.
- Data migration / rollback:
  - No schema migration or downgrade. Preserve the Identity/dispute database and audit history.
  - For operational backout, withdraw the new UI or redeploy a reviewed previous application build while retaining disabled fallback and the configured/non-resetting seeder. Do not restore the old hardcoded credentials or enable anonymous customer access.
  - A plain revert to PR-12 also restores unsafe demo defaults; it is not a security-preserving rollback.
- Feature flag or compatibility alias:
  - `Authentication:EnableDevelopmentCompatibilityPath` is honored only in Testing, with explicit true. It defaults false in checked-in configuration.
  - Removal follow-up: replace remaining legacy fixture auto-login with explicit cookies, then delete the Testing-only fallback and identity override. No runtime customer-flow fallback remains in Development.

## Demo / evidence
- Steps to reproduce:
  1. Configure seed passwords and start API on 5100 plus Angular on 4200.
  2. Open `/disputes` anonymously: sign-in appears without loading protected records.
  3. Sign in as `customer.demo`, inspect transactions, submit a dispute and open its timeline. Refresh, then sign out.
  4. Sign in as `agent.demo` or `supervisor.demo`: only the corresponding authenticated placeholder and operations navigation are available. `/disputes` leads to access denied.
  5. Use `npm run start:ui` only for the separately labelled, API-free design preview.
- Screenshots/logs/results with secrets and PII removed:

  | Role | Login desktop | Login mobile | Workspace desktop | Workspace mobile |
  | --- | --- | --- | --- | --- |
  | Customer | [Screenshot](pr-13-screenshots/customer-login-desktop.png) | [Screenshot](pr-13-screenshots/customer-login-mobile.png) | [Screenshot](pr-13-screenshots/customer-workspace-desktop.png) | [Screenshot](pr-13-screenshots/customer-workspace-mobile.png) |
  | Agent | [Screenshot](pr-13-screenshots/agent-login-desktop.png) | [Screenshot](pr-13-screenshots/agent-login-mobile.png) | [Screenshot](pr-13-screenshots/agent-workspace-desktop.png) | [Screenshot](pr-13-screenshots/agent-workspace-mobile.png) |
  | Supervisor | [Screenshot](pr-13-screenshots/supervisor-login-desktop.png) | [Screenshot](pr-13-screenshots/supervisor-login-mobile.png) | [Screenshot](pr-13-screenshots/supervisor-workspace-desktop.png) | [Screenshot](pr-13-screenshots/supervisor-workspace-mobile.png) |

  Browser evidence was captured using isolated synthetic data. Owned test servers were stopped and the temporary E2E database removed.

  Main changed files:
  - [Auth service](../../apps/web/src/app/auth/auth-service.ts), [guards](../../apps/web/src/app/auth/auth-guards.ts), [interceptor](../../apps/web/src/app/auth/session-interceptor.ts), [login](../../apps/web/src/app/auth/login-page.ts), [placeholder](../../apps/web/src/app/auth/workspace-page.ts).
  - [Routes](../../apps/web/src/app/app.routes.ts), [providers](../../apps/web/src/app/app.config.ts), [shell](../../apps/web/src/app/foundation.html), [CSRF state](../../apps/web/src/app/csrf-session.ts), [dispute client](../../apps/web/src/app/disputes/dispute-api.ts), shared styles and focused preview fixes.
  - [Seed](../../apps/api/src/ClearCase.Infrastructure/Authentication/SyntheticIdentitySeeder.cs), [authentication handler](../../apps/api/src/ClearCase.Api/Authentication/DemoAuthenticationHandler.cs), [startup](../../apps/api/src/ClearCase.Api/Program.cs), Development/Testing settings and [auth endpoints](../../apps/api/src/ClearCase.Api/Endpoints/AuthEndpoints.cs).
  - Auth/route/component/seed/ownership regression tests, legacy fixture session setup, [browser tests](../../apps/web/e2e/auth.spec.ts), [runner](../../apps/web/e2e/run-tests.mjs), [Playwright config](../../apps/web/playwright.config.ts), [CI](../../.github/workflows/ci.yml), README and assignment-scope notes.

## Risks, follow-ups, known blockers
- Agent and Supervisor APIs remain placeholders, not simulated authority over real cases.
- Local Identity/SQLite is not a bank-approved production identity integration; MFA, production operations, global session revocation and distributed login rate limiting are not delivered.
- Existing seeded accounts require explicit credential rotation; this change intentionally does not reset them on startup.
- No new idempotency/reconciliation guarantees are claimed.
- Local npm emits a machine-level `always-auth` configuration warning; builds/tests pass. No unrelated machine configuration was changed.
- Remote CI results must be checked on the actual PR; the evidence above is local execution.

**Agent acknowledgement:** This PR is the only assigned scope. Next PR has not started.
