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
WORKFLOW = REPO / ".github" / "workflows" / "csharp.yml"
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
        # nothing else may follow in the block; actions: read only lets the throttle job list this workflow's runs
        self.assertRegex(self.text, r"(?m)^permissions:\n  contents: read\n  actions: read[^\n]*\n(?=\n)")
        self.assertEqual(len(re.findall(r"(?m)^\s*permissions:", self.text)), 1, "no job-level permissions")
        code = re.sub(r"(?m)(^|\s)#.*$", "", self.text)  # comments may say "no secrets"
        self.assertNotRegex(code, r"(?i)\bsecrets\b")  # secrets.X, secrets['X'], toJSON(secrets), secrets: inherit
        # the throttle job passes the read-only token to `gh api` and nothing else may use it
        self.assertEqual(len(re.findall(r"(?i)github\.token|GITHUB_TOKEN", code)), 1)
        self.assertRegex(code, r"GH_TOKEN: \$\{\{ github\.token \}\}")
        self.assertNotIn("pull_request_target", self.text)
        self.assertEqual(self.text.count("upload-artifact"), 1)  # T255: only the failed scenario renders, 7 days
        self.assertIn("name: scenario-renders", self.text)
        for word in ("gh release", "git push", "nuget push", "docker push"):
            self.assertNotIn(word, self.text)

    def test_every_action_is_pinned_by_full_commit_sha(self):
        uses = re.findall(r"uses:\s*(\S+)", self.text)
        self.assertTrue(uses)
        for u in uses:
            self.assertRegex(u, r"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$", u)

    def test_mirrors_the_green_gate(self):
        for cmd in ("dotnet build -warnaserror", "dotnet format --verify-no-changes", "tools/ptest.py --no-build --coverage", "dotnet build Unciv.Godot -warnaserror",
                    "dotnet build Unciv.Godot.Harness -warnaserror", "tools/coverage.py --check", "tools/screenshots.py --check",
                    "tools/ticket.py check", "unittest discover"):
            self.assertIn(cmd, self.text)

    def test_soak_only_on_schedule_or_dispatch_and_nothing_else_on_schedule(self):
        self.assertRegex(self.text, r"if: github\.event_name == 'schedule' \|\| github\.event_name == 'workflow_dispatch'")
        # engine and client-scenarios also wait for the throttle verdict; the throttle job itself is harmless on a schedule (it exits at once)
        self.assertEqual(self.text.count("if: github.event_name != 'schedule'"), 2)  # client-export, plus the engine line (prefix match)
        self.assertEqual(self.text.count("if: github.event_name != 'schedule' && needs.throttle.outputs.run == 'true'"), 1)  # engine
        self.assertEqual(
            self.text.count("if: ${{ !cancelled() && github.event_name != 'schedule' && needs.throttle.outputs.run == 'true' }}"), 1
        )  # client-scenarios (T246)


if __name__ == "__main__":
    unittest.main()


class ClientPlatforms(unittest.TestCase):
    """T258: release.py, CI, package.sh, the export presets and the archive checker agree on the platform list (T279 builds the same archives)."""

    def load(self, name):
        spec = importlib.util.spec_from_file_location(name, HERE / f"{name}.py")
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        return mod

    def test_release_py_builds_linux_windows_and_macos_by_default(self):
        self.assertEqual(self.load("release").CLIENT_PLATFORMS, ["linux", "windows", "macos"])

    def test_ci_exports_exactly_the_release_platforms(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        packaged = re.findall(r"Unciv\.Godot/tools/package\.sh (\w+)", text)
        checked = re.findall(r"check_package\.py (\w+)", text)
        self.assertEqual(packaged, self.load("release").CLIENT_PLATFORMS)
        self.assertEqual(checked, packaged)

    def test_package_script_presets_and_checker_cover_every_platform(self):
        package = (REPO / "csharp" / "Unciv.Godot" / "tools" / "package.sh").read_text(encoding="utf-8")
        presets = (REPO / "csharp" / "Unciv.Godot" / "export_presets.cfg").read_text(encoding="utf-8")
        checker = self.load("check_package")
        self.assertEqual(sorted(checker.PLATFORMS), sorted(self.load("release").CLIENT_PLATFORMS))
        for platform, preset in (("linux", "Linux"), ("windows", "Windows"), ("macos", "macOS")):
            self.assertIn(f'    {platform}) preset="{preset}"', package)
            self.assertIn(f'name="{preset}"', presets)
        self.assertRegex(presets, r'(?s)name="macOS".*codesign/codesign=0.*notarization/notarization=0')  # unsigned, un-notarised

    def test_archive_names_are_the_documented_ones(self):
        names = {p: self.load("check_package").PLATFORMS[p][0] for p in ("linux", "windows", "macos")}
        self.assertEqual(names, {"linux": "Unciv-client-linux.tar.gz", "windows": "Unciv-client-windows.zip", "macos": "Unciv-client-macos.zip"})
        package = (REPO / "csharp" / "Unciv.Godot" / "tools" / "package.sh").read_text(encoding="utf-8")
        self.assertIn("Unciv-client-linux.tar.gz", package)
        self.assertIn("Unciv-client-$platform.zip", package)


class CheckPackage(unittest.TestCase):
    """T258: check_package.py accepts a complete archive and names what is missing or too small."""

    def build(self, tmp, platform, drop=None, exe_size=None):
        import zipfile

        mod = ClientPlatforms().load("check_package")
        name, exe, minimum, required = mod.PLATFORMS[platform]
        top = f"Unciv-{platform}/"
        paths = {exe: exe_size if exe_size is not None else minimum}
        for pattern in required:
            paths.setdefault(pattern.replace("\\", ""), 1)  # a literal path that matches the regex
        archive = Path(tmp) / "a.zip"
        with zipfile.ZipFile(archive, "w") as z:
            for p, size in paths.items():
                if p != drop:
                    z.writestr(top + p, b"\0" * size)
        return mod, archive

    def test_complete_archive_passes_and_gaps_are_reported(self):
        with tempfile.TemporaryDirectory() as t:
            mod, archive = self.build(t, "windows")
            self.assertEqual(mod.check("windows", archive), [])
            mod, archive = self.build(t, "windows", drop="LICENSE")
            self.assertTrue(any("LICENSE" in p for p in mod.check("windows", archive)))
            mod, archive = self.build(t, "windows", exe_size=10)
            self.assertTrue(any("expected at least" in p for p in mod.check("windows", archive)))
            self.assertTrue(any("does not exist" in p for p in mod.check("windows", Path(t) / "none.zip")))
