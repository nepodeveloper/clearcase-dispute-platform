"""Fail-closed repository governance guard; no third-party Python packages."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REQUIRED = (
    ".github/workflows/ci.yml",
    ".github/instructions/ci-cd.instructions.md",
    ".github/agents/clearcase-reviewer.agent.md",
    ".ai/pr-review.prompt.md",
    ".azuredevops/new/pipelines/azure-pr-validation.yml",
    ".azuredevops/new/templates/stages/backend-validation.yml",
    ".azuredevops/new/templates/stages/frontend-validation.yml",
    ".azuredevops/new/templates/variables/toolchain.yml",
)

def validate(root: Path = ROOT) -> list[str]:
    errors = [f"Missing required CI standard: {p}" for p in REQUIRED if not (root / p).is_file()]
    settings_path = root / ".ai/ai-review.config.json"
    try:
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
    except (FileNotFoundError, ValueError) as exc:
        return errors + [f"Review settings unavailable or malformed: {exc}"]
    if settings.get("runAIReview") is not False or settings.get("executionMode") != "local-only":
        errors.append("Automated or remote AI review requires explicit security sign-off")
    if settings.get("requireChangedLineEvidence") is not True:
        errors.append("AI reviewer must require changed-line evidence")
    if settings.get("requireBatchTokenVerification") is not True:
        errors.append("AI reviewer must require batch verification")
    if settings.get("oversizeFallback") not in ("skip", "truncate"):
        errors.append("AI review oversized batches must use an explicit safe fallback")
    for key in ("maxFilesPerBatch", "maxChangedLinesPerBatch"):
        if type(settings.get(key)) is not int or not 1 <= settings[key] <= 10000:
            errors.append(f"Invalid review limit: {key}")
    if (root / ".github/workflows/ci.yml").is_file():
        workflow = (root / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        for requirement in ("NuGetAuditLevel=high", "npm audit --omit=dev --audit-level=high",
                            "test:e2e", "Migration", "coverage.cobertura.xml"):
            if requirement not in workflow:
                errors.append(f"Missing mandatory GitHub CI evidence gate: {requirement}")
    return errors

if __name__ == "__main__":
    failures = validate()
    if failures:
        for message in failures:
            print(f"ERROR: {message}")
        raise SystemExit(1)
    print("Repository CI governance: passed (AI pipeline review remains disabled)")
