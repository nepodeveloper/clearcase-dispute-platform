# ClearCase Coding Agent Pack — 21 small PRs

This ZIP contains **instructions and target documentation only**. It does NOT implement the 21 PRs or modify your existing source. It was tailored against the previously supplied starter archive (`card-dispute-portal-starter.zip`) and the v2 engineering design source.

## Install into the GitHub repository
1. Create or clone the public `clearcase-dispute-platform` repository.
2. Make sure the existing starter source is committed (preferably as the baseline commit) and the working tree is clean.
3. Copy this pack's `AGENTS.md`, `.github/` and `docs/` into the repository root. Existing `.github/workflows` should remain unchanged until PR-01; do not overwrite app source.
4. Commit this agent guidance as a documentation-only setup commit, for example `docs: add incremental coding-agent plan`.
5. Open `docs/implementation/START_AGENT_HERE.md`, copy its entire content into the coding agent and give the agent access to this repository.
6. Review/merge PR-01. Then send only `docs/implementation/tickets/PR-02.md` or the reusable template with `XX=02`. Repeat one PR at a time.

## Folder map
- `AGENTS.md`: non-negotiable global instructions and financial invariants.
- `.github/instructions/*`: language/API/AWS conventions from the v2 design.
- `.github/pull_request_template.md`: PR quality gate and human review form.
- `docs/implementation/EXECUTION_PROTOCOL.md`: strict workflow, ready/done and stop conditions.
- `docs/implementation/PR_ROADMAP.md`: 21-item sequence with release cut-lines.
- `docs/implementation/tickets/PR-01.md` ... `PR-21.md`: exactly one agent task per PR.
- `docs/implementation/START_AGENT_HERE.md`: first copy/paste prompt.
- `docs/implementation/NEXT_PR_TEMPLATE.md`: reusable prompt.
- `docs/target-design`: v2 **proposed** engineering docs and editable Mermaid diagrams, contracts.

## Important honesty note
The earlier starter was reported with unverified .NET and Angular production builds. PR-01 must inspect actual files and verify before any architectural work. The target APIs and event contracts are not automatically the current implementation.
