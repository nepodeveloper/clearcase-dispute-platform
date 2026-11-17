"""Zero-cloud release promotion simulator. No Azure SDK, identities, secrets or real deployments."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile

BASELINE = "sha256:" + hashlib.sha256(b"clearcase-known-good-DEMO").hexdigest()
POLICIES = json.loads((Path(__file__).resolve().parents[2] /
    ".azuredevops/new/templates/variables/mock-environments.json").read_text(encoding="utf-8"))
ALLOWED = {"success", "readiness-failure", "smoke-failure", "telemetry-failure"}


class ReleaseGateError(Exception):
    pass


def save(path, state):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix=".release-", suffix=".json", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(state, stream, indent=2, sort_keys=True)
            stream.write("\n")
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def load(path):
    if not path.is_file():
        raise ReleaseGateError("No release evidence. Run prepare first.")
    state = json.loads(path.read_text(encoding="utf-8"))
    if state.get("schemaVersion") != 1:
        raise ReleaseGateError("Unsupported release state version.")
    return state


def record(state, event, **fields):
    state["events"].append({"sequence": len(state["events"]) + 1, "event": event, **fields})


def prepare(path, commit):
    if path.exists():
        raise ReleaseGateError("Release already exists; cannot replace immutable candidate.")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ReleaseGateError("Expected a complete 40-character Git commit SHA.")
    digest = "sha256:" + hashlib.sha256(("SIMULATED:" + commit).encode()).hexdigest()
    state = {"schemaVersion": 1, "commit": commit.lower(), "artifactDigest": digest,
             "syntheticDigest": True, "stages": {}, "approval": None,
             "production": {"currentDigest": BASELINE, "previousDigest": None,
                            "trafficPercentNew": 0}, "alerts": [], "events": []}
    record(state, "candidate_prepared", digest=digest, note="No actual image was built")
    save(path, state)
    return state


def approve(path, approver, ticket):
    state = load(path)
    if state["stages"].get("uat") != "healthy":
        raise ReleaseGateError("Production approval requires successful UAT verification.")
    if state["stages"].get("prod") is not None:
        raise ReleaseGateError("Cannot approve a candidate after a production attempt.")
    if not approver.strip() or not ticket.strip():
        raise ReleaseGateError("Demo approver and change record are mandatory.")
    state["approval"] = {"claimedApprover": approver.strip(), "changeRecord": ticket.strip(),
                         "mode": "DEMO ONLY - identity is not authenticated"}
    record(state, "production_demo_approval_recorded", changeRecord=ticket.strip())
    save(path, state)
    return state


def deploy(path, environment, scenario="success"):
    if environment not in POLICIES or scenario not in ALLOWED:
        raise ReleaseGateError("Unknown environment or failure scenario.")
    state = load(path)
    if state["stages"].get(environment) is not None:
        raise ReleaseGateError("Environment already attempted for this candidate.")
    prerequisite = {"dev": None, "uat": "dev", "prod": "uat"}[environment]
    if prerequisite and state["stages"].get(prerequisite) != "healthy":
        raise ReleaseGateError(f"{environment} requires healthy {prerequisite} evidence.")
    if POLICIES[environment]["approval"] and not state["approval"]:
        raise ReleaseGateError("Production deployment blocked: approval missing.")
    record(state, "deployment_started", environment=environment, digest=state["artifactDigest"],
           identity=POLICIES[environment]["identity"], vaultReference=POLICIES[environment]["vault"])
    if environment == "prod":
        state["production"]["previousDigest"] = state["production"]["currentDigest"]
        state["production"]["trafficPercentNew"] = 10
        record(state, "canary_traffic_started", percent=10)
    checks = {"readiness": scenario != "readiness-failure",
              "smoke": scenario != "smoke-failure",
              "telemetry": scenario != "telemetry-failure"}
    state["checks"] = {environment: checks}
    record(state, "post_deploy_checks", environment=environment, **checks)
    if not all(checks.values()):
        state["stages"][environment] = "rolled_back" if environment == "prod" else "failed"
        state["alerts"].append({"environment": environment, "severity": "critical",
                                "reason": next(k for k, v in checks.items() if not v)})
        record(state, "automated_alert_raised", environment=environment)
        if environment == "prod":
            state["production"]["trafficPercentNew"] = 0
            state["production"]["currentDigest"] = state["production"]["previousDigest"]
            record(state, "traffic_rolled_back", digest=state["production"]["currentDigest"],
                   note="Application revision traffic only; schema/financial effects are NOT reverted")
    else:
        state["stages"][environment] = "healthy"
        if environment == "prod":
            state["production"]["currentDigest"] = state["artifactDigest"]
            state["production"]["trafficPercentNew"] = 100
            record(state, "canary_promoted", percent=100)
        record(state, "deployment_verified", environment=environment)
    save(path, state)
    return state


def main(argv=None):
    parser = argparse.ArgumentParser(description="ClearCase local-only CI/CD promotion simulator")
    parser.add_argument("command", choices=("prepare", "deploy", "approve", "status"))
    parser.add_argument("--state", type=Path, required=True)
    parser.add_argument("--commit")
    parser.add_argument("--environment", choices=tuple(POLICIES))
    parser.add_argument("--scenario", choices=tuple(sorted(ALLOWED)), default="success")
    parser.add_argument("--approver")
    parser.add_argument("--ticket")
    args = parser.parse_args(argv)
    try:
        if args.command == "prepare":
            if not args.commit:
                raise ReleaseGateError("--commit is required")
            result = prepare(args.state, args.commit)
        elif args.command == "deploy":
            if not args.environment:
                raise ReleaseGateError("--environment is required")
            result = deploy(args.state, args.environment, args.scenario)
        elif args.command == "approve":
            result = approve(args.state, args.approver or "", args.ticket or "")
        else:
            result = load(args.state)
        print(json.dumps({"stages": result["stages"], "approval": result["approval"],
                          "production": result["production"], "alerts": result["alerts"],
                          "lastEvent": result["events"][-1]}, indent=2))
        if args.command == "deploy" and result["stages"][args.environment] != "healthy":
            return 1
        return 0
    except (ReleaseGateError, ValueError, OSError) as error:
        print("RELEASE GATE BLOCKED:", str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
