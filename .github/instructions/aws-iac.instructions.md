---
applyTo: "deploy/**/*,**/*.tf,**/*.yaml,**/*.yml"
---
# Proposed IaC and delivery standards

- AWS-native but preserve modular IaC, environment parameters, consistent naming, least privilege, diagnostics, tagging and secret hygiene from the user's broader IaC standards.
- Use distinct identities/secrets per service/environment; no literal secrets; no public RDS, Kafka or evidence S3.
- Kubernetes probes, resources, graceful shutdown, non-root execution, readiness and HPA justified by load profile.
- CI includes format/lint/tests, vulnerability scans, migration validation, immutable image, staging smoke and rollback checks.
- Document RTO/RPO as stakeholder-defined targets; regularly test restores and financial reconciliation.
