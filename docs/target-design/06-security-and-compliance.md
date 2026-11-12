# 06 | Security, privacy, authorization and threat model

## Security goals

Prevent customer-to-customer data access, unauthorised reversals, evidence disclosure, identity compromise and forged processor callbacks. Detect misuse and preserve verifiable financial and operational audit history. Use defense in depth: authorization at route, application and data-access layers; infrastructure boundaries protect even if one control fails.

## Actors and permissions

- **Customer**: read own transactions/cases, open own disputes, attach requested evidence, view own public timeline. Cannot alter decision or set `customerId`.
- **Bank agent**: access assigned cases through bank-controlled scopes; can request evidence and record investigation decisions only with justified reason. Restricted amount-based approvals via four-eyes where required.
- **Supervisor**: approve exceptions/escalations with independent identity and audit record.
- **Worker**: narrow machine identity for its specific event/adapter and DB operations; no admin endpoints.
- **Auditor**: immutable read-only audit access through approved channel.

## Frontend and identity

Use a bank-approved OIDC identity provider (authorization code + PKCE or same-origin BFF), Secure+HttpOnly+SameSite session cookie, anti-CSRF header and token for mutations, idle and absolute session expiry, session revocation and step-up/MFA for high-risk bank agent commands. A client-side role guard is UX only; the API must enforce all permissions. Do not store tokens in localStorage. CORS and Content-Security-Policy must be narrow and tested. Never expose internal staff endpoints in customer navigation.

## Authorization rules

All customer-owned access must include `principal.customerId == resource.customerId` in the query/authorization evaluation; missing or other customer's resource returns 404 to avoid enumeration when appropriate. Check object ownership independently of a GUID's randomness. For staff, use attribute-based scopes: role, branch/team, assigned case, action, case risk tier and approval delegation. Domain never trusts a user-provided `customerId`.

## Evidence handling

1. Request an upload grant for an owned dispute and allowed MIME types (whitelist, size limits, extension not trusted).
2. Generate short-lived presigned S3 upload URL constrained to a per-case key and maximum size. Keep bucket private; enforce SSE-KMS and block public access.
3. Store metadata in `pendingScan`, scan asynchronously using an approved malware detection pipeline, verify content type/content magic, quarantine on failure.
4. Only scanned `clean` objects become available to entitled staff; presigned download URLs short-lived and logged; no PII in object key.
5. Legal retention, retention locks, delete requests and POPIA purpose limitation require bank/legal governance sign-off.

## Threat model

| Threat / abuse case | Primary control | Test |
|---|---|---|
| BOLA: guess another case GUID | Scoped ownership + authorization + 404 | User B fetches A's case and event/evidence IDs |
| Mass assignment | Request DTO whitelist, immutable domain fields | Attempt to set `customerId`, `caseStatus`, `financialStatus` |
| CSRF | SameSite+anti-CSRF for cookie POST/PATCH | Submit without/invalid token |
| Credential stuffing | OIDC provider protections, bot detection, distributed limiter | Attack simulation with false/missing credentials |
| Duplicate reversal | Unique operation ref, inbox/outbox + remote idempotency | Concurrent workers deliver same command |
| Webhook spoofing | Signed payload, timestamp window, replay store, allowlist if available | Forged signature / old request |
| Evidence malware | Private quarantine and scanner before fetch | Infected sample or scanner outage |
| SSRF | No arbitrary URL fetch; egress allowlist, DNS pinning as needed | Malicious callback/evidence URL |
| Data leakage in logs | Structured redaction, no PAN/token/evidence body | Inspect traces, logs, crash reports |
| Insider unauthorized approval | RBAC/ABAC, maker-checker, reason codes, immutable audit | Same user tries submit+approve higher-risk reversal |
| Supply chain | SCA, SAST, container scanning, secrets detection, pinned CI actions | CI deliberate vulnerable dependency |
| Denial of service | Throttling, payload and file limits, backpressure, WAF | API load/rate and upload abuse |

## Privacy and cryptography

Minimise stored card data: transaction token or masked PAN only, never CVV/CVC, no full card number in logs, analytics or event payload. Encrypt in transit TLS 1.2+ and at rest (RDS and S3 KMS). Secrets in AWS Secrets Manager / Parameter Store with least-privilege IAM roles for service accounts. Use distinct keys and identities per environment. Log audit actor identity and purpose without exposing content; secure retention and deletion schedules must follow POPIA and applicable card and financial regulations. PCI-DSS applicability must be formally assessed rather than presumed absent solely because a demo uses masked data.

## Security verification gate

A release is blocked if cross-customer isolation fails, unauthorized staff changes succeed, mutation CSRF is bypassed, high-severity dependency findings are unreviewed, S3 evidence is public, or secrets are stored in source. Require a documented threat model, periodic pen test of any production adaptation, and evidence of access reviews. Never equate security checklists with audit certification.
