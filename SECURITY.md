# Security policy

ClearCase is a demo platform: all data is synthetic and every financial step is mocked. It still follows
production-style controls, so please report vulnerabilities privately.

## Reporting

Use GitHub's **Report a vulnerability** (Security tab > Advisories) for this repository. Do not open a public
issue for a suspected vulnerability, and never include real customer or credential data in a report.

## Scope

In scope: authentication and session handling, CSRF, authorization between customer, agent and supervisor roles,
idempotency and concurrency guards, the audit trail, container images and the Helm chart.
Out of scope: findings that need the Development/mock configuration to be exposed to untrusted users
(the README states it must not be).

## Supply chain controls

- NuGet restores run in locked mode against committed `packages.lock.json` files; high/critical advisories fail the build.
- `npm audit` gates production dependencies; Dependabot proposes weekly updates.
- CodeQL and dependency review run in CI; container images are built with SBOM and provenance attestations.
