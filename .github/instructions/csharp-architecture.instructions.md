---
applyTo: "**/*.cs"
---
# Proposed C# architecture and quality conventions

- Domain controls invariants and transitions; application orchestrates commands/queries; infrastructure adapts EF, messaging and external banking; API maps HTTP.
- Prefer SRP, explicit names, encapsulated state, DI, focused interfaces and ports/adapters; avoid helper/god services and needless abstractions.
- Ensure public asynchronous I/O accepts CancellationToken and propagates it.
- Catch exceptions only at a boundary where translating, recovering or adding meaningful context. Never swallow failures or log secrets.
- For each state change: atomic case + timeline + outbox + audit and optimistic version guard.
- UTC times, injected `TimeProvider` / clock; deterministic GUID/test data.
- Use readable methods with minimal parameters; use records/value objects instead of primitive parameter piles.
- Write failing invariant tests first; AAA; integration test real PostgreSQL behavior.
- Do not use a domain entity/EF model as public HTTP DTO.
