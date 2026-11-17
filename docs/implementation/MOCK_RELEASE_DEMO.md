# PR-44 — mock release promotion and failure recovery

## What is running?
The **existing** GitHub CI checks remain the real build, test, NuGet/npm
advisory, migration and browser gates. This PR adds a Python release
*simulator*, tested by the existing `scripts/ci/test_*.py` discovery.

The optional Azure DevOps pipeline at
`.azuredevops/new/pipelines/azure-release-simulation.yml` is NOT connected
and will not run until imported. It does not package containers, access Azure,
move money, deploy code or read any Key Vault secret. The displayed
`sha256:` release identifier is *synthetic*, not an actual OCI image digest.

## Locally demonstrate a successful promotion
Use any Python 3.10+ from repository root, with a **new** temporary JSON state path:

```bash
STATE=/tmp/clearcase-release-demo.json
python3 scripts/ci/release_simulator.py prepare --state "$STATE" --commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
python3 scripts/ci/release_simulator.py deploy --state "$STATE" --environment dev
python3 scripts/ci/release_simulator.py deploy --state "$STATE" --environment uat
python3 scripts/ci/release_simulator.py approve --state "$STATE" --approver "Demo Release Manager" --ticket CAB-DEMO-001
python3 scripts/ci/release_simulator.py deploy --state "$STATE" --environment prod
python3 scripts/ci/release_simulator.py status --state "$STATE"
```

## Demonstrate a failed production rollout and recovery
Repeat using a *fresh* state path and same dev/UAT/approval steps, then:

```bash
python3 scripts/ci/release_simulator.py deploy --state "$STATE" --environment prod --scenario telemetry-failure
```

The command intentionally exits 1, records a **critical** alert and restores
production traffic to a pre-seeded, synthetic known-good baseline. It does
**not** undo database changes, outbox events or financial operations.
Run `python3 -m unittest discover -s scripts/ci -p 'test_*.py'` for positive
and negative tests. State files are intended as local demo artifacts, not as
secure evidence of real approver identities.

## Azure DevOps environment setup (not expressed as YAML)
1. Import the optional YAML as a separate manual pipeline.
2. Create environments `clearcase-development`, `clearcase-uat`,
   `clearcase-production` under **Pipelines > Environments**.
3. On `clearcase-production`, open **Approvals and checks**; configure
   authorised release approvers, exclusive lock and protected branches.
   Configure the same controls on the production service connection.
4. The YAML also includes a ManualValidation task for a visual pause, but
   its local `claimedApprover` field does **not** authenticate an identity.
   Never treat it as a substitute for an environment-owned approval check.
5. Run the YAML with `failureScenario=success` then with
   `failureScenario=telemetry-failure` on two separate run IDs.
6. Compare artifacts `release-candidate`, `release-dev`, `release-uat`,
   and `release-production-evidence` for SHA, event sequence and alert.

## Real implementation mapping, not deployed
- **IaC:** versioned Bicep module defines Azure resources, role grants,
  private network and alerts in a future standalone infrastructure PR.
  Perform `az bicep build`, `az deployment group what-if` and review first.
- **Identity:** separate least-privilege Azure Resource Manager service
  connections (workload identity federation/OIDC) per environment. Runtime
  uses a user-assigned managed identity with Key Vault Secrets User
  permission at minimal scope. Vault names in mock JSON end in
  `.example.invalid`; no values or tokens are stored.
- **Packaging:** produce SBOM, image vulnerability scan, attestation,
  and immutable OCI image digest from one successful main CI run.
- **Release:** same digest dev → UAT → production with a new healthy
  Container Apps revision in *multiple revision mode*. No prod traffic
  until readiness succeeds; start at 10%, observe, then advance.
- **Health:** `/health/live` is process-only; `/health/ready`
  validates database connectivity and pending EF migrations. Add an
  authenticated read-only transaction/dispute smoke check in future.
- **Monitoring:** Azure Monitor / Application Insights alerts for 5xx,
  p95 latency, readiness failure, queue age, outbox parking, failed
  financial reconciliation and unknown outcomes. Thresholds are
  illustrative and must be load-tested; monitor sustained windows.
- **Recovery:** halt rollout, shift revision traffic back to last-known-good,
  preserve failed revision/logs, open incident, verify financial intents
  before retry, restore DB only under an approved recovery plan.
  Forward-only database schema changes must remain backward compatible.

## Security and limits
No secrets, service principal passwords, paid infrastructure, real
provisioning, authenticated cloud approval, or actual telemetry are involved.
The local demo is intentionally **not** a production authorisation boundary.
Do not enable live deployment until infrastructure, identity, audits,
change management, compliance and tested rollback are implemented.
