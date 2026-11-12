# 12 | Sources, assumptions, policy caveats and verification register

## Authoritative reference links consulted (12 November 2026)

1. **Capitec Bank debit order FAQ**: https://www.capitecbank.co.za/were-here-to-help/faqs/ — 60-calendar-day window from 13 April 2026, day 1 after debit, valid authorised DebiCheck non-disputable via ordinary route, historic rules and escalation guidance.
2. **Nedbank 2026 debit order guidance**: https://group.nedbank.co.za/content/nedbank/za/en/personal/learn/help-centre/bank/dispute-debit-order.html — 13 April 2026 rules, EFT/RM changes and prior 40-day handling.
3. **Payment Association of South Africa debit payments**: https://pasa.org.za/about-payments/introduction-to-payments/debit-payments/ — distinctions among EFT, registered mandates and DebiCheck.
4. **AWS Transactional Outbox pattern**: https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/transactional-outbox.html — dual write, outbox and duplicate consumer considerations.
5. **OWASP API Security Top 10 (2023)**: https://api-security.owasp.org/editions/2023/en/0x11-t10/ — object authorization, auth, API resource consumption and other classes.
6. **AWS Well-Architected failure management**: https://docs.aws.amazon.com/wellarchitected/latest/framework/rel-failmgmt.html — isolation, failure testing, recovery objectives.
7. **Temenos dispute process**: https://developer.temenos.com/article/transaction-disputes — issuer, acquirer, merchant and internal bank fee flows described in the assessment brief.

## Standards grounding

These documents apply the user's recently established engineering preferences: versioned plural REST paths, GUIDs, proper method semantics, JSON camelCase and optional/null handling, error contract with Problem Details and JSON Pointer, Clean Architecture, SOLID, DI, SRP, encapsulation, explicit naming, TDD and multiple test levels, structured logging, resilience, event consistency, CI/CD, ADRs and modular infrastructure code. Some highly detailed wording from the eight screenshots was not directly accessible here; **the `.github/instructions` templates included with this package are newly authored proposed adaptations, not a verbatim copy of screenshots.** Repository-specific conventions and explicit assessments take precedence over mechanical rules when there is a sound documented trade-off.

## Regulatory and business rules are NOT validated production policy

- 60-day debit-order regime effective April 2026 must be selected by historical applicability and bank confirmation, not globally forced for all historical transactions.
- Valid, authorised DebiCheck collections are excluded from ordinary reversal; a customer claiming an invalid/unauthorised mandate needs a legitimate bank escalation/investigation process, not a fake final rejection.
- R2,000 is a user-suggested example amount used as a configurable **internal review tier**, not a PASA/Capitec statutory threshold.
- Card-network reason-code windows, evidence standards, chargeback liability, reversals, card fraud rights, finance postings and insurer obligations vary and require legal, scheme and bank-owner review.
- AWS services are proposed for role alignment, not claims of actual production deployment or cost/performance validation.
- Sample SLO values and traffic scenarios are illustrative only and not tested results.

## Scope / evidence register

| Item | Status as of v2 design | Evidence needed before claiming complete |
|---|---|---|
| Angular transaction UI | Present in baseline starter | Local run + browser and accessibility tests |
| ASP.NET Core authenticated API | Present in baseline starter | .NET build/test success and security audit |
| SQLite persistent disputes | Present in baseline starter | Migrate to real PostgreSQL and test migrations |
| Card vs fee routing | Present but simplified | Distinct chargeback, financial ledger statuses and tests |
| Debit orders | Not in baseline | Policy strategies + seed cases + automated tests |
| Versioned `/api/v1` REST | Not in baseline | OpenAPI changes, compatibility and HTTP tests |
| Outbox, inbox, Kafka | Not in baseline | durable schema, workers, duplicate/crash tests |
| Evidence object upload | Not in baseline | private S3/mocks, scan, auth and delete/retention tests |
| EKS/MSK/AWS deployment | Blueprint only | IaC plan/apply + deployed smoke, logs and rollback |
| Production readiness | Not asserted | business sign-off, verified tests, SLO evidence, security and compliance review |

## Open questions for Capitec engineers/product owners

1. Is the assessment intended to implement only card purchase disputes, or can a unified disputes platform be offered as an explicitly separate extension?
2. What authentication and customer identity boundaries should a real bank integration use?
3. Which transaction and mandate sources are authoritative? Which write operation APIs support idempotent references and status lookup?
4. What case management SLAs, authorization/approval thresholds and scheme deadlines apply?
5. What operational load, region, RTO/RPO, retention and compliance review are expected?

Answer these only after product/engineering confirmation. Do not block a mock assessment on unavailable internal bank data.
