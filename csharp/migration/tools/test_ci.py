"""Tests for the CI pieces of T169: ci_base.py, retry-on-crash.sh and the shape of .github/workflows/csharp.yml.
Run: python3 -I -m unittest discover -s tools -p 'test_*.py' (from csharp/migration)."""
import importlib.util
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
WORKFLOW = REPO / "csharp" / "ci" / "csharp-workflow.yml"
RETRY = REPO / "csharp" / "Unciv.Godot" / "tools" / "retry-on-crash.sh"

spec = importlib.util.spec_from_file_location("ci_base", HERE / "ci_base.py")
ci_base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci_base)


def git(cwd, *a):
    return subprocess.run(["git", "-C", str(cwd), *a], capture_output=True, text=True, check=True).stdout.strip()


class CiBase(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        origin = Path(self.tmp.name) / "origin"
        origin.mkdir()
        git(origin, "init", "-q", "-b", "main")
        git(origin, "config", "user.email", "t@t")
        git(origin, "config", "user.name", "t")
        (origin / "a").write_text("1")
        git(origin, "add", "a")
        git(origin, "commit", "-q", "-m", "one")
        self.base = git(origin, "rev-parse", "HEAD")
        self.clone = Path(self.tmp.name) / "clone"
        subprocess.run(["git", "clone", "-q", str(origin), str(self.clone)], check=True)
        git(self.clone, "config", "user.email", "t@t")
        git(self.clone, "config", "user.name", "t")
        git(self.clone, "checkout", "-q", "-b", "feature")
        (self.clone / "b").write_text("2")
        git(self.clone, "add", "b")
        git(self.clone, "commit", "-q", "-m", "two")
        self.cwd = Path.cwd()
        import os
        os.chdir(self.clone)
        self.addCleanup(os.chdir, self.cwd)

    def test_pull_request_uses_merge_base_with_the_base_branch(self):
        self.assertEqual(ci_base.pick({"GITHUB_EVENT_NAME": "pull_request", "GITHUB_BASE_REF": "main"}), ["--since", self.base])

    def test_push_uses_the_commit_before(self):
        self.assertEqual(ci_base.pick({"GITHUB_EVENT_NAME": "push", "EVENT_BEFORE": self.base}), ["--since", self.base])

    def test_first_push_unknown_before_and_other_events_check_everything(self):
        for env in (
            {"GITHUB_EVENT_NAME": "push", "EVENT_BEFORE": "0" * 40},
            {"GITHUB_EVENT_NAME": "push", "EVENT_BEFORE": "f" * 40},  # force-pushed away: not in the clone
            {"GITHUB_EVENT_NAME": "push", "EVENT_BEFORE": "--output=x"},
            {"GITHUB_EVENT_NAME": "pull_request", "GITHUB_BASE_REF": "missing"},
            {"GITHUB_EVENT_NAME": "pull_request", "GITHUB_BASE_REF": "--upload-pack=x"},
            {"GITHUB_EVENT_NAME": "schedule"},
            {"GITHUB_EVENT_NAME": "workflow_dispatch"},
            {},
        ):
            self.assertEqual(ci_base.pick(env), ["--all"], env)


class RetryOnCrash(unittest.TestCase):
    def run_retry(self, retries, script):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            (d / ".godot" / "imported").mkdir(parents=True)
            (d / ".godot" / "imported" / "x").write_text("half written")
            counter = d / "count"
            body = f'n=$(cat "{counter}" 2>/dev/null || echo 0); echo $((n+1)) > "{counter}"; {script}'
            r = subprocess.run(["bash", str(RETRY), str(retries), "bash", "-c", body], capture_output=True, text=True,
                               env={"PATH": "/usr/bin:/bin", "RETRY_GODOT_PROJECT": str(d)})
            return r.returncode, int(counter.read_text()), (d / ".godot" / "imported").exists()

    def test_success_runs_once(self):
        self.assertEqual(self.run_retry(2, "exit 0"), (0, 1, True))

    def test_real_failure_is_not_retried(self):
        self.assertEqual(self.run_retry(2, "exit 1"), (1, 1, True))

    def test_crash_is_retried_with_the_import_cache_cleared(self):
        # crashes (SIGILL) on the first attempt, then succeeds
        self.assertEqual(self.run_retry(2, '[ "$n" -ge 1 ] && exit 0; exit 132'), (0, 2, False))

    def test_persistent_crash_gives_up_after_all_attempts(self):
        self.assertEqual(self.run_retry(2, "exit 139")[:2], (139, 3))

    def test_bad_usage(self):
        r = subprocess.run(["bash", str(RETRY), "x"], capture_output=True, text=True)
        self.assertEqual(r.returncode, 64)


class WorkflowShape(unittest.TestCase):
    text = WORKFLOW.read_text(encoding="utf-8")

    def test_only_csharp_paths_trigger_it(self):
        for trigger in ("push", "pull_request"):
            block = re.search(rf"^  {trigger}:\n(.*?)(?=^  \S|^\S)", self.text, re.S | re.M).group(1)
            paths = re.findall(r"^\s+- '([^']+)'", block, re.M)
            self.assertEqual(paths, ["csharp/**", "android/assets/jsons/**", ".github/workflows/csharp.yml"], trigger)

    def test_triggers_are_exactly_push_to_the_integration_branch_pull_request_schedule_dispatch(self):
        on = re.search(r"^on:\n(.*?)(?=^\S)", self.text, re.S | re.M).group(1)
        self.assertEqual(re.findall(r"^  (\w+):", on, re.M), ["push", "pull_request", "schedule", "workflow_dispatch"])
        self.assertRegex(on, r"(?m)^    branches: \[csharp-migration\]$")

    def test_read_only_without_secrets_or_publishing(self):
        self.assertRegex(self.text, r"(?m)^permissions:\n  contents: read\n(?=\n)")  # nothing else may follow in the block
        self.assertEqual(len(re.findall(r"(?m)^\s*permissions:", self.text)), 1, "no job-level permissions")
        code = re.sub(r"(?m)(^|\s)#.*$", "", self.text)  # comments may say "no secrets"
        self.assertNotRegex(code, r"(?i)\bsecrets\b")  # secrets.X, secrets['X'], toJSON(secrets), secrets: inherit
        self.assertNotRegex(code, r"(?i)github\.token|GITHUB_TOKEN")
        self.assertNotIn("pull_request_target", self.text)
        for word in ("upload-artifact", "gh release", "git push", "nuget push", "docker push"):
            self.assertNotIn(word, self.text)

    def test_every_action_is_pinned_by_full_commit_sha(self):
        uses = re.findall(r"uses:\s*(\S+)", self.text)
        self.assertTrue(uses)
        for u in uses:
            self.assertRegex(u, r"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$", u)

    def test_mirrors_the_green_gate(self):
        for cmd in ("dotnet build -warnaserror", "dotnet format --verify-no-changes", "dotnet test", "dotnet build Unciv.Godot -warnaserror",
                    "dotnet build Unciv.Godot.Harness -warnaserror", "tools/coverage.py --check", "tools/screenshots.py --check",
                    "tools/ticket.py check", "unittest discover"):
            self.assertIn(cmd, self.text)

    def test_soak_only_on_schedule_or_dispatch_and_nothing_else_on_schedule(self):
        self.assertRegex(self.text, r"if: github\.event_name == 'schedule' \|\| github\.event_name == 'workflow_dispatch'")
        self.assertEqual(self.text.count("if: github.event_name != 'schedule'"), 3)


if __name__ == "__main__":
    unittest.main()
