# START HERE — Paste into the coding agent

You are implementing **ClearCase Dispute Platform**, an independent senior full-stack engineering assessment, in the repository `clearcase-dispute-platform`. The current repository may still be the earlier Angular/.NET/SQLite starter. Treat documentation under `docs/target-design/` as **target design**, not as implemented behavior.

Read, in order: `AGENTS.md`; `.github/instructions/`; `docs/implementation/EXECUTION_PROTOCOL.md`; `docs/implementation/PR_ROADMAP.md`; `docs/implementation/tickets/PR-01.md`; relevant `docs/target-design/` files; the existing `README.md` and current source.

**Assignment: Implement PR-01 ONLY — Establish reproducible baseline.**

Rules:
1. Do not generate the entire system, alter unrelated modules, or jump to Kafka, AWS or PostgreSQL.
2. First identify the actual current baseline (routes, project paths, builds, tests, configs, security). Describe a focused red/green plan in a few lines.
3. Make the smallest changes needed to get repeatable `.NET` and Angular builds/tests with CI. Maintain login, transactions, disputes, timeline, and local demo.
4. Avoid secrets, real customer data, real banking integration, false success reporting or untested claims.
5. Run the commands that actually exist; report exact results and environment blockers. Document baseline omissions honestly.
6. Branch `chore/pr-01-baseline-green`. Keep the diff narrow and reviewable. Provide a PR description including scope, verification, risks, rollback and demo steps. If GitHub access and push authorization are available, open a draft PR; otherwise provide commits/patch ready for review.
7. **STOP. Do not implement PR-02 or any later PR.**

When finished, report the branch/PR link (only if it really exists), files changed, before/after test results, and the next suggested PR without starting it.
