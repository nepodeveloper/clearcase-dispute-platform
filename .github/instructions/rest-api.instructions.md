---
applyTo: "**/*Endpoint*.cs,**/*Controller*.cs,**/api*.ts,**/contracts/**"
---
# Proposed repository REST conventions (adapted from established preferences)

- Use `/api/v{majorVersion}/{plural-resource}`; kebab-case multiword routes, GUID IDs.
- Use noun resources, real hierarchy only, safe GET; POST creates/commands; PUT complete replace; PATCH partial replace; DELETE only where valid.
- JSON camelCase, optional missing properties omitted, defined collections as `[]`.
- Query parameters for searching and paging, not write data.
- Document cursor pagination, stable ordering, `pageInfo` and `nextCursor` as optional.
- Standard `application/problem+json` response with `code`, `correlationId`, `errors[{pointer,code,message}]`.
- Thin endpoints, no direct EF/domain mutation, explicit auth on every operation; never accept client-supplied customer identity.
- OpenAPI/consumer-contract changes required for changed routes, statuses or error codes.
- Reused `Idempotency-Key` has deterministic semantics and must be tested under races.
