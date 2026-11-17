# ClearCase CI/CD governance and release controls (PR-43)

## Current executable CI
`.github/workflows/ci.yml` is the existing **active** GitHub Actions PR/main pipeline:
repository-policy verification -> backend .NET Release restore/advisory gate/build/
tests/migrations/Cobertura -> frontend Angular clean install/audit/unit/build/E2E.
Required checks must be configured in GitHub branch rules by a repository administrator.
Upload artifacts contain test evidence but no live customer data.

An **optional** Azure DevOps equivalent is defined at
`.azuredevops/new/pipelines/azure-pr-validation.yml`. Connect the public GitHub
repository to Azure Pipelines, select that YAML path and set PR branch validation in
the repository. It uses shared stage templates and pinned toolchain variables;
it is *not* automatically active just because the YAML is committed.
It is an alternative CI runner, not a second deployment platform.

## Deployment cut line
CI does **not** deploy. There is no verified Docker/image packaging pipeline,
cloud IaC or real payment adapter in this repository. Do not claim continuous
deployment from these files. Release-enablement work is a **separate PR**.

Before shipping a CD job, require: validated and reviewed IaC; isolated
Development/NonProd/Prod configuration and least-privilege OIDC identities;
immutable signed image digest; private secrets; explicit non-production smoke
test; approved production environment gate; migration backup and tested
rollback; readiness/health check; abort criteria; verified ledger/financial
reconciliation. Never auto-retry an unknown financial operation.
No mock endpoint is allowed to imply actual customer refunds.

## AI reviewer rollout
The attached engineering screenshots describe a local-only review agent
which only reviews changed PR files/hunks, uses bounded batches, verifies
returned findings against changed lines and reports skipped/oversized input.
`.github/agents/clearcase-reviewer.agent.md` and `.ai/pr-review.prompt.md`
capture the human-operated review policy; `.ai/ai-review.config.json`
sets `runAIReview=false`. The Python governance guard rejects accidental
activation but does **not** implement the screenshot's batching, token
challenge, response authentication or hallucination metrics. Those require
a dedicated design/security review and separate implementation PR before any
automation. Never add AI credentials to PR-triggered workflows.

## Validation, operations and rollback
Locally: `python3 scripts/ci/repository_policy.py` and
`python3 -m unittest discover -s scripts/ci -p 'test_*.py'`.
Full quality gates: `dotnet restore/build/test apps/api/ClearCase.slnx`
and `cd apps/web && npm ci && npm run test:ci && npm run build && npm run test:e2e`.
Read `docs/implementation/RELEASE_EVIDENCE.md` for migration safety and
existing release artifacts. On a policy-only rollback, revert this PR;
there is no database migration, service exposure or cloud resource to unwind.
If CI fails, preserve logs and artifacts and block the merge rather than
bypassing required checks.
