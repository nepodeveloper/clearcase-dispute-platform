---
applyTo: ".github/workflows/**/*.yml,.github/workflows/**/*.yaml,.azuredevops/**/*.yml,.azuredevops/**/*.yaml,scripts/ci/**"
---
# ClearCase CI/CD engineering standards

- Organize CI as independent governance, backend, frontend and release-evidence gates.
  Keep templates reusable, environment variable values distinct and naming consistent.
- CI on PR and main must run real Release .NET restore/build/tests, NuGet high/critical
  advisory checks, EF migration/model checks, Angular install/audit/unit/build,
  and authenticated browser E2E journeys. Never silently skip failing gates.
- Use immutable dependency lockfiles and known SDK/npm versions. Publish test results,
  coverage and failure artifacts; do not claim a coverage threshold unless enforced.
- Fail closed for missing evidence, nonzero exit codes, disabled security controls or
  incompatible schema. Use least-privilege workflow permissions and no PR secrets.
- Separate release packaging from deployment. Do not deploy mocks to public banking
  endpoints. Require approved environment/service connection, private secrets, IaC
  validation, database backup/restore rehearsal, migration compatibility, post-deploy
  health checks and documented rollback before enabling deployment.
- Keep Development, NonProd and Prod environment approvals/variables external to source;
  no credentials in YAML. Use immutable image digests, provenance and manual production approval.
- AI review is **local-only** and never a pipeline gate while `runAIReview` is false.
  Require diff-bounded findings, changed-line grounding, batch integrity and disclosed
  incomplete coverage before any future rollout is considered.
- CI green is evidence of assessment code quality, not production banking readiness.
