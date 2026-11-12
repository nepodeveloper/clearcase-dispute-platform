---
applyTo: "**/*.ts,**/*.html,**/*.css"
---
# Proposed TypeScript/Angular standards

- TypeScript strict mode; no unreviewed `any`, strict equality, explicit null/undefined handling, single quotes and consistent formatting.
- Prefer async/await where it clarifies asynchronous application flow; use RxJS appropriately for Angular HTTP streams and cancellation.
- Cohesive feature-level components, typed forms and API clients; avoid business-rule duplication from backend.
- Route guards are UX only: all authorization enforced on server.
- Display caseStatus separately from financialStatus and show pending states truthfully.
- Accessible keyboard interactions, labels/errors, semantic timeline and responsive layouts; no direct unsafe HTML injection.
- Never persist sensitive bearer tokens in localStorage; same-origin session and XSRF protection.
- Add component/contract/E2E tests for user-visible state including error/retry paths.
