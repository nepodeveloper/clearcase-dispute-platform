# Local assessment demo scenarios

All records are **synthetic**. They are insert-only and tied to deterministic fixture IDs
(`00000000-0000-0000-0000-00000000000N`). A **new** Development database receives
transactions with dates anchored at 08:30 UTC **yesterday**, spaced over the previous
29 days. This remains within the configured 60-day debit-order / 120-day card window
as the system clock advances. The test factory injects a frozen seed clock for
snapshot-based contract assertions; browser demos use the actual system clock.

## Repeatable customer-to-staff journeys

| Scenario | Demo customer transaction | Type | Role journey |
| --- | --- | --- | --- |
| Card merchant accepts | `...000001` | CardPurchase | Customer submit → Agent Initiate → ForwardToAcquirer → MerchantAccept → RequestSettlement → ExecuteSettlement (mock) → Close → customer sees all milestones |
| Card merchant contests | `...000007` | CardPurchase | Customer submit → Agent Initiate → ForwardToAcquirer → MerchantContest → ReceiveRepresentment (synthetic evidence) → ReviewRepresentment → Approve / Reject → settlement or Close |
| Debit order approved | `...000002` | DebitOrder | Customer submit → Agent StartReview → VerifyMandate or CompleteInvestigation → RequestReversal → ExecuteReversal (mock) → Close |
| Debit order needs review | `...000005` | DebitOrder | Above configured standard-review amount; verify policy and bank authorization before a decision |
| Debit order incomplete | `...000008` | DebitOrder | Missing mandate details; requires manual investigation rather than invented eligibility |
| Bank charge approved | `...000003` | BankCharge | Customer submit → Agent StartReview → Approve → RequestAdjustment → ExecuteAdjustment (mock) → Close |
| Bank charge rejected | `...000006` | BankCharge | Customer submit → Agent StartReview → Reject → Close |
| Pending authorization | `...000004` | CardPurchase | Pending transaction is not offered for dispute |

Use the normal customer/Agent/Supervisor login flows; do not send simulation keys
to the frontend. Private Agent notes and Supervisor resolution histories are never
part of customer-visible events. Backend authorization and transition checks remain
authoritative even if the UI suggests an action.

**Retained data:** Startup never silently edits a previously seeded transaction. Existing
demo databases from before this change retain their historical transaction dates;
for a fresh time-relative set, create a **new disposable** local demo database.
Never reset a database holding work you need to keep. A future migration could add
a versioned fixture catalog, but must not rewrite references on active disputes.
