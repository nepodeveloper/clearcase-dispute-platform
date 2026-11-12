# ADR 0002: Separate dispute eligibility from processing

- Status: Accepted
- Date: 2026-11-12

## Context

Transaction detail needs to explain the applicable review path before any dispute is submitted. The current application is read-only and uses synthetic transactions; it has no dispute persistence, case workflow, or banking adapter. Treating a boolean transaction-list field as an approval or coupling a policy check to a financial operation would misrepresent the current capability.

## Decision

Implement pure, category-specific eligibility policies in `ClearCase.Domain`. The application handler retrieves a transaction using the authenticated customer's scope, supplies configured policy facts and an injected clock, then returns a structured outcome, reason codes, safe explanation, policy version, and evaluation timestamp. A read-only API exposes that decision and the Angular detail page displays it. The existing `isDisputable` field remains a documented provisional indicator until a separate compatible contract change can derive it from this policy or retire it.

Eligibility returns `EligibleToSubmit`, `RequiresManualReview`, or `Ineligible`; it does not persist a dispute or invoke chargeback, mandate, refund, or reversal processing. The configured card window, debit-order rule/effective date, R2,000 review threshold, and bank-charge review condition are demonstration assumptions only, not verified production policy.

## Consequences

- Business rules can be tested independently with fixed timestamps and explicit configuration.
- Category-specific policies prevent bank charges from inheriting card-network or debit-mandate rules.
- Manual review remains available for out-of-window debit orders, uncertain mandates, valid authorised DebiCheck collections, and amounts above the illustrative threshold.
- A later submission flow must independently enforce authorization and idempotency and use an authorized financial adapter; a positive eligibility outcome is not approval or settlement.
- The additional domain project is intentional because the existing solution had no domain assembly and this PR introduces the first domain policy boundary.
