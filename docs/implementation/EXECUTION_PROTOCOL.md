# How the coding agent should work

## One PR at a time
The user explicitly rejected a big-bang implementation. The agent receives exactly one `PR-XX` at a time. Never batch several PRs into a single mega-branch. Keep `main` protected, prefer squash merge, short-lived branches, and green checks before merge. If a PR requires >400 meaningful lines, >one day of focused work, a live infrastructure dependency, or touches unrelated boundaries: split it and update this roadmap before coding.

## Startup command
Copy/paste `docs/implementation/START_AGENT_HERE.md` into the coding agent. For each subsequent task use the PR-specific file under `docs/implementation/tickets/` and the reusable prompt `docs/implementation/NEXT_PR_TEMPLATE.md`.

## Definition of Ready (DoR)
- A named business or engineering outcome and one PR ID.
- Concrete files/modules, compatibility assumptions, 3–7 acceptance checks, and explicit non-goals.
- Runnable tests or evidence of a legitimate blocked dependency.
- Migration/backout strategy if a schema, route or event changes.

## Definition of Done (DoD)
- Appropriate failing test first and passing tests afterward, with actual output recorded.
- Baseline builds, lint/format checks, API and frontend if touched; no newly introduced failures.
- Security/customer ownership and sensitive fields tested where relevant.
- Contracts/migrations/docs/ADR updated if changed; exact rollout/backout stated.
- Branch is narrow and reviews cleanly, no dead code or unused dependencies.
- No 'TODO: production' for mandatory business invariants. Deferred items recorded separately and explicitly.
- Draft PR includes description, tests, manual demo and screenshots where UI changed.

## Suggested branch/commit/PR conventions
- Branch: `feat/pr-08-debit-order-workflow`, `chore/pr-01-baseline-green`
- Commit: `feat(disputes): add policy decision for EFT debit orders`
- Title: `[PR-08] Debit-order reversal workflow behind mock gateway`
- Link related ADR/docs and reference previous PR as dependency.
- Prefer squash merge; do not rewrite or force-push unrelated branches.

## Stop / escalation triggers
Stop and report with best available work when a change would expose real customer data, invoke a real financial operation, require cloud spend/secrets, or break public contracts without a migration path. Ask for the missing authorization only in the coding environment if it is required for execution; otherwise implement a safe mock/feature flag, document the boundary, and finish the narrow PR.

## Required agent response after each PR
1. PR ID and goal
2. What changed / what intentionally did not change
3. Tests run (exact commands, pass/fail, environment blockers)
4. Security/finance risks and controls
5. Migration, feature flag and rollback
6. Demonstration steps
7. Suggested next PR (do **not** start it)
