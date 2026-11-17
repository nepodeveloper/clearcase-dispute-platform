"""Guards the GitHub release simulation: manual-only, main-only, read-only token, gated environments."""
import json
import re
import unittest
from pathlib import Path

from release_simulator import ALLOWED

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-simulation.yml"
POLICIES = json.loads((ROOT / ".azuredevops/new/templates/variables/mock-environments.json")
                      .read_text(encoding="utf-8"))


def block(text, key):
    """Indented body of a top-level YAML key; stdlib only, so no YAML parser is needed."""
    match = re.search(rf"^{re.escape(key)}:[^\n]*\n((?:[ \t]+[^\n]*\n|\n)*)", text, re.M)
    return match.group(1) if match else ""


class ReleaseWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = WORKFLOW.read_text(encoding="utf-8")

    def test_only_a_manual_dispatch_can_start_a_release(self):
        triggers = re.findall(r"^  (\w+):", block(self.text, "on"), re.M)
        self.assertEqual(["workflow_dispatch"], triggers)

    def test_scenario_choices_match_the_simulator(self):
        choices = re.findall(r"^[ \t]+-[ \t]+(\S+)[ \t]*$", block(self.text, "on"), re.M)
        self.assertEqual(sorted(ALLOWED), sorted(choices))

    def test_stages_use_policy_environments_in_promotion_order(self):
        used = re.findall(r"^[ \t]+environment:[ \t]*(\S+)[ \t]*$", self.text, re.M)
        self.assertEqual([POLICIES[name]["environment"] for name in ("dev", "uat", "prod")], used)

    def test_release_is_main_only_with_read_only_token_and_no_credentials(self):
        self.assertIn("refs/heads/main", self.text)
        self.assertEqual("contents: read", block(self.text, "permissions").strip())
        self.assertNotRegex(self.text, r":\s*write\b")
        for forbidden in ("secrets.", "id-token"):
            self.assertNotIn(forbidden, self.text)


if __name__ == "__main__":
    unittest.main()
