# Paste template — any subsequent PR

Implement **PR-XX only** in `clearcase-dispute-platform`. Open and follow `docs/implementation/tickets/PR-XX.md`, `AGENTS.md`, `.github/instructions/*`, `docs/implementation/EXECUTION_PROTOCOL.md`, and relevant `docs/target-design` references. The current default branch contains all merged prior PRs.

1. Inspect code and tests first. Write a short scoped plan (≤8 bullets), proposed file changes, dependencies, compatibility/rollback and tests that will first fail.
2. Implement the smallest vertical or architectural slice satisfying only this ticket. Preserve the working customer demo and avoid introducing unrelated infrastructure.
3. Follow Clean Architecture/SOLID, versioned REST contracts, Problem Details + correlation ID, secure customer isolation, PostgreSQL consistency rules, and typed accessible Angular standards **when relevant to this PR**.
4. Include negative paths, concurrency/resilience tests or event-contract tests if this change touches financial or asynchronous operations. No real transfers. Keep money movement pending until reconciled.
5. Run all feasible relevant commands, document exact results, known gaps and risk. Do not invent a green check or claim a PR exists without confirmation.
6. Create a narrow branch and draft PR only if authorized/connected. Include testing, rollout/rollback, review checklist and demo steps.
7. **STOP after this PR. Do not work on PR-(XX+1).**
