# PR-40 — Secure API hosting and operational readiness

## Production HTTP/TLS

The API requires HTTPS in Production: HTTP requests receive a 307 redirect to
HTTPS port 443 (override `Security:HttpsPort`). HSTS max-age is 365 days on
HTTPS responses; preload and includeSubDomains are deliberately disabled until
the deployment hostname and domain ownership are reviewed.

Every API response uses `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a restrictive
`Content-Security-Policy`, and a restricted `Permissions-Policy`.
This policy is intended for the JSON backend, **not** an Angular SPA served on
the same origin; if hosting is consolidated, review CSP independently.

## Proxy forwarding and rate limiting

Forwarded headers are ignored unless an operator configures an explicit allowlist
of literal reverse-proxy IP addresses:

```json
{
  "Security": {
    "HttpsPort": 443,
    "TrustedProxyIps": ["10.0.0.5"]
  }
}
```

Only after configuring those addresses will the API honor one forwarded hop
(`X-Forwarded-For` and `X-Forwarded-Proto`). The trusted proxy must overwrite
incoming forwarded headers rather than append untrusted client values. The
login limiter is keyed from the resolved remote IP, and forwarding middleware
runs before HTTPS redirection, rate limits and authentication.

Never set a wildcard or trust the public internet as a proxy. In particular,
an untrusted `X-Forwarded-Proto: https` cannot bypass HTTPS redirection.
Ensure the ingress terminates TLS, supplies the true protocol and real client
address, and uses a network ACL between the proxy and Kestrel.

## Readiness

`GET /health/live` reports only process liveness. `GET /health/ready`
requires completed startup, an accessible SQLite database and **no pending EF
migrations**. On failure it returns 503 with minimal component statuses;
connection paths and exception details are never placed in health JSON.

The endpoint verifies the actual SQLite schema, not Kafka broker health or
financial-provider availability. Include these in deployment-specific operational
checks before using real financial integrations. Readiness must be checked
before routing traffic to a newly deployed instance.

## Validation and rollback

Tests cover HTTPS redirect, untrusted forwarded-header injection, HSTS/security
headers, missing-database 503, and a successful migration-ready database.
Roll back code only; no schema changes. Review any edge/CDN security policy
for duplication or conflicts with these backend headers.
