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
        # T255: the failed scenario renders; T277: the debug APKs and the emulator smoke output (throw-away keystore, never a store signature). Nothing else, all 7 days.
        self.assertEqual(self.text.count("upload-artifact"), 3)
        for name in ("scenario-renders", "android-apk", "android-smoke"):
            self.assertIn(f"name: {name}\n", self.text)
        self.assertEqual(self.text.count("retention-days: 7"), 3)
        self.assertNotRegex(self.text, r"(?i)jarsigner|keystore-password|\.jks|release-keystore")
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
        self.assertEqual(self.text.count("if: github.event_name != 'schedule'"), 3)  # client-export and android (T277), plus the engine line (prefix match)
        self.assertEqual(self.text.count("if: github.event_name != 'schedule' && needs.throttle.outputs.run == 'true'"), 1)  # engine
        self.assertEqual(
            self.text.count("if: ${{ !cancelled() && github.event_name != 'schedule' && needs.throttle.outputs.run == 'true' }}"), 1
        )  # client-scenarios (T246)



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
        packaged = re.findall(r"Unciv\.Godot/tools/package\.sh (?!android)([a-z]+)\n", text)  # desktop clients only; the Android APKs (T277) are their own job
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

RELEASE_WORKFLOW = REPO / ".github" / "workflows" / "csharp-release.yml"


class ReleaseWorkflowShape(unittest.TestCase):
    """T279: the release workflow runs on a version tag only, creates a draft with minimal permissions and uses nothing but github.token."""

    text = RELEASE_WORKFLOW.read_text(encoding="utf-8")
    code = re.sub(r"(?m)(^|\s)#.*$", "", text)

    def jobs(self):
        body = re.search(r"(?ms)^jobs:\n(.*)", self.text).group(1)
        parts = re.split(r"(?m)^  (?=[a-z][\w-]*:\n)", body)
        return {re.match(r"([\w-]+):", p).group(1): p for p in parts if p.strip() and re.match(r"[\w-]+:\n", p)}

    def test_triggered_by_version_tags_only(self):
        on = re.search(r"^on:\n(.*?)(?=^\S)", self.text, re.S | re.M).group(1)
        self.assertEqual(re.findall(r"^  (\w+):", on, re.M), ["push"])
        self.assertRegex(on, r"(?m)^    tags: \['csharp-v\*'\]$")
        self.assertNotIn("branches", on)
        self.assertNotIn("pull_request", self.code)
        self.assertNotIn("schedule", self.code)
        self.assertNotIn("workflow_dispatch", self.code)

    def test_write_permission_only_on_the_publish_job(self):
        self.assertRegex(self.text, r"(?m)^permissions:\n  contents: read\n(?=\n)")
        jobs = self.jobs()
        self.assertEqual(sorted(jobs), ["android", "cli", "client", "publish", "relay", "server", "verify"])
        for name, body in jobs.items():
            if name == "publish":
                self.assertRegex(body, r"(?m)^    permissions:\n      contents: write\n(?!      )")
            else:
                self.assertNotIn("permissions:", body, name)
        self.assertEqual(len(re.findall(r"(?m)^\s*contents: write", self.code)), 1)
        self.assertNotRegex(self.code, r"(?m)^\s*(?!contents)\w[\w-]*: write")

    def test_no_secrets_but_github_token_and_no_dangerous_triggers(self):
        self.assertNotIn("pull_request_target", self.text)
        self.assertNotRegex(self.code, r"(?i)\bsecrets\b")
        self.assertEqual(set(re.findall(r"\$\{\{\s*(github\.token)\s*\}\}", self.code)), {"github.token"})
        self.assertNotRegex(self.code, r"GITHUB_TOKEN")
        for env in re.findall(r"(?m)^\s+(GH_TOKEN|GITHUB_TOKEN): (.*)$", self.code):
            self.assertEqual(env, ("GH_TOKEN", "${{ github.token }}"))

    def test_release_is_a_draft_created_with_gh_and_never_edited_or_published(self):
        self.assertEqual(len(re.findall(r"gh release create", self.code)), 1)
        self.assertRegex(self.code, r"gh release create [^\n]*--draft")
        self.assertIn("--verify-tag", self.code)
        self.assertNotRegex(self.code, r"gh release (edit|upload|delete)|--draft=false|--latest|gh api -X|--clobber")
        self.assertNotRegex(self.code, r"git push")
        self.assertNotIn("softprops", self.code)
        self.assertNotIn("create-release", self.code)

    def test_every_action_is_pinned_by_full_commit_sha_and_publish_needs_all_builds(self):
        uses = re.findall(r"uses:\s*(\S+)", self.text)
        self.assertTrue(uses)
        for u in uses:
            self.assertRegex(u, r"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$", u)
        self.assertRegex(self.jobs()["publish"], r"needs: \[verify, cli, server, relay, client, android\]")
        for name in ("cli", "server", "relay", "client", "android"):
            self.assertRegex(self.jobs()[name], r"needs: verify")

    def test_checkouts_do_not_persist_credentials(self):
        self.assertEqual(self.text.count("actions/checkout@"), self.text.count("persist-credentials: false"))

    def test_the_workflow_builds_exactly_the_archives_the_release_expects(self):
        mod = ClientPlatforms().load("release_assets")
        packaged = re.findall(r"Unciv\.Godot/tools/package\.sh (?!android)(\w+)", self.text)  # desktop clients; the APK is the android job (T280)
        self.assertEqual(packaged, ClientPlatforms().load("release").CLIENT_PLATFORMS)
        self.assertEqual(re.findall(r"check_package\.py (\w+)", self.text), packaged)
        for name in mod.client_archive_names().values():
            self.assertIn(name, self.text)
        rids = re.search(r"for rid in ([\w\- ]+); do", self.text).group(1).split()
        self.assertEqual(rids, mod.CLI_RIDS)
        server_rids = re.findall(r"for rid in ([\w\- ]+); do\n\s+\(cd csharp && dotnet publish Unciv\.Server", self.text)  # T266
        self.assertEqual([r.split() for r in server_rids], [mod.SERVER_RIDS])
        self.assertIn("release_assets.py smoke-server", self.text)
        relay_rids = re.findall(r"for rid in ([\w\- ]+); do\n\s+\(cd csharp && dotnet publish Unciv\.Relay", self.text)  # T366
        self.assertEqual([r.split() for r in relay_rids], [mod.RELAY_RIDS])
        self.assertIn("release_assets.py pack-relay", self.text)
        self.assertIn("release_assets.py smoke-relay", self.text)
        self.assertIn("name: release-relay", self.text)
        self.assertIn("python3 -I csharp/migration/tools/release_assets.py verify", self.text)
        self.assertIn(mod.ANDROID_APK, self.text)  # T280
        self.assertIn("package.sh android\n", self.text)
        self.assertRegex(self.text, r"needs: \[verify, cli, server, relay, client, android\]")
        self.assertRegex(self.text, r"name: release-android")
        self.assertNotRegex(self.text, r"(?i)\$\{\{\s*secrets\.|keystore-password|\.jks")

    def test_csharp_yml_server_job_builds_and_smokes_only(self):  # T266
        text = WORKFLOW.read_text(encoding="utf-8")
        job = re.search(r"(?ms)^  server-publish:\n(.*?)(?=^  \S|\Z)", text).group(1)
        self.assertIn("needs: engine", job)
        self.assertEqual(re.findall(r"for rid in ([\w\- ]+); do", job)[0].split(), ClientPlatforms().load("release_assets").SERVER_RIDS)
        self.assertIn("dotnet publish Unciv.Server", job)
        self.assertIn("release_assets.py smoke-server", job)
        self.assertNotIn("upload-artifact", job)

    def test_csharp_yml_relay_job_builds_and_smokes_only(self):  # T366
        text = WORKFLOW.read_text(encoding="utf-8")
        job = re.search(r"(?ms)^  relay-publish:\n(.*?)(?=^  \S|\Z)", text).group(1)
        self.assertIn("needs: engine", job)
        self.assertEqual(re.findall(r"for rid in ([\w\- ]+); do", job)[0].split(), ClientPlatforms().load("release_assets").RELAY_RIDS)
        self.assertIn("dotnet publish Unciv.Relay", job)
        self.assertIn("release_assets.py pack-relay", job)
        self.assertIn("release_assets.py smoke-relay", job)
        self.assertNotIn("upload-artifact", job)

    def test_csharp_yml_android_job_builds_both_apks_and_smokes_without_secrets(self):  # T277
        text = WORKFLOW.read_text(encoding="utf-8")
        job = re.search(r"(?ms)^  android:\n(.*?)(?=^  \S|\Z)", text).group(1)
        self.assertIn("needs: engine", job)
        self.assertRegex(job, r"package\.sh android\n")
        self.assertRegex(job, r"package\.sh android-emulator\n")
        self.assertIn("android-smoke.sh", job)
        self.assertIn("steps.kvm.outputs.available == 'true'", job)  # no KVM: build and artifact only
        self.assertIn("continue-on-error: true", job)  # T277: smoke unverified on hosted runners; drop with its first green run
        self.assertNotRegex(job, r"(?i)secrets|\bgh \w+|git push")

    def test_csharp_yml_is_untouched_by_the_release(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertNotIn("csharp-v", text)
        self.assertNotRegex(re.search(r"^on:\n(.*?)(?=^\S)", text, re.S | re.M).group(1), r"(?m)^    tags:")

    def test_no_expression_is_interpolated_into_a_shell_script(self):
        # a tag name must only reach the shell through env vars ($GITHUB_REF_NAME), never through ${{ }} inside run: (script injection)
        for run in re.findall(r"(?ms)^\s+run: (.*?)(?=^\s+(?:- |\w[\w-]*:)|\Z)", self.code):
            for expr in re.findall(r"\$\{\{\s*([^}]*?)\s*\}\}", run):
                self.assertIn(expr, ("runner.temp", "github.token"), run)


class ReleaseAssets(unittest.TestCase):
    """T279: release_assets.py (tag check, CLI archives, checksums, completeness, notes)."""

    mod = ClientPlatforms().load("release_assets")

    def test_tag_names(self):
        for tag, version in (("csharp-v1.2.3", "1.2.3"), ("csharp-v0.0.1", "0.0.1"), ("csharp-v1.2.3-rc.1", "1.2.3-rc.1")):
            self.assertEqual(self.mod.check_tag(tag), version, tag)
        for tag in ("csharp-v1.2", "v1.2.3", "csharp-1.2.3", "csharp-v01.2.3", "csharp-v1.2.3-", "csharp-v1.2.3 x", "csharp-v1.2.3\n", "csharp-vfoo", "xcsharp-v1.2.3", "T279-abc"):
            self.assertIsNone(self.mod.check_tag(tag), tag)

    def test_expected_assets_are_the_stable_names(self):
        self.assertEqual(
            self.mod.expected_assets(),
            ["Unciv-client-linux.tar.gz", "Unciv-client-windows.zip", "Unciv-client-macos.zip",
             "Unciv-cli-linux-x64.tar.gz", "Unciv-cli-win-x64.zip", "Unciv-cli-osx-arm64.tar.gz",
             "Unciv-server-linux-x64.tar.gz", "Unciv-server-win-x64.zip",
             "Unciv-relay-linux-x64.tar.gz", "Unciv-relay-win-x64.zip", "Unciv-client-android-arm64.apk"],
        )

    def fake_publish(self, tmp, rid):
        d = Path(tmp) / f"pub-{rid}"
        d.mkdir(parents=True)
        exe = "Unciv.Cli.exe" if rid.startswith("win-") else "Unciv.Cli"
        (d / exe).write_bytes(b"x")
        (d / exe).chmod(0o755)
        (d / "Unciv.Cli.pdb").write_bytes(b"debug")
        (d / "lib.dll").write_bytes(b"y")
        jsons = Path(tmp) / "jsons"
        (jsons / "sub").mkdir(parents=True, exist_ok=True)
        (jsons / "ModCategories.json").write_text("[]")
        (jsons / "sub" / "a.json").write_text("{}")
        lic = Path(tmp) / "LICENSE"
        lic.write_text("MPL")
        return d, jsons, lic

    def test_pack_cli_layout_modes_and_refusals(self):
        import tarfile
        import zipfile

        with tempfile.TemporaryDirectory() as t:
            for rid in self.mod.CLI_RIDS:
                d, jsons, lic = self.fake_publish(t, rid)
                out = self.mod.pack_cli(rid, d, Path(t) / "out", jsons, lic)
                self.assertEqual(out.name, self.mod.cli_archive_name(rid))
                exe = "Unciv.Cli.exe" if rid.startswith("win-") else "Unciv.Cli"
                top = f"Unciv-cli-{rid}/"
                if out.suffix == ".zip":
                    names = zipfile.ZipFile(out).namelist()
                else:
                    with tarfile.open(out) as tf:
                        names = tf.getnames()
                        self.assertEqual(tf.getmember(top + exe).mode, 0o755)
                        self.assertEqual(tf.getmember(top + "LICENSE").mode, 0o644)
                self.assertEqual(sorted(names), sorted(top + n for n in (exe, "lib.dll", "LICENSE", "data/jsons/ModCategories.json", "data/jsons/sub/a.json")))
            with self.assertRaises(SystemExit):
                self.mod.pack_cli("linux-arm", d, Path(t) / "out", jsons, lic)
            with self.assertRaises(SystemExit):
                self.mod.pack_cli("win-x64", self.fake_publish(Path(t) / "other", "linux-x64")[0], Path(t) / "out", jsons, lic)  # a Linux publish folder is not a win-x64 one
            (jsons / "ModCategories.json").unlink()
            with self.assertRaises(SystemExit):
                self.mod.pack_cli("linux-x64", d, Path(t) / "out", jsons, lic)

    def test_pack_server_layout_and_release_py_rids(self):
        import tarfile
        import zipfile

        self.assertEqual(self.mod.SERVER_RIDS, ClientPlatforms().load("release").SERVER_RIDS)
        with tempfile.TemporaryDirectory() as t:
            for rid in self.mod.SERVER_RIDS:
                d, jsons, lic = self.fake_publish(t, rid)
                exe = "Unciv.Server.exe" if rid.startswith("win-") else "Unciv.Server"
                (d / ("Unciv.Cli.exe" if rid.startswith("win-") else "Unciv.Cli")).rename(d / exe)
                out = self.mod.pack_server(rid, d, Path(t) / "out", jsons, lic)
                self.assertEqual(out.name, f"Unciv-server-{rid}.{'zip' if rid.startswith('win-') else 'tar.gz'}")
                names = zipfile.ZipFile(out).namelist() if out.suffix == ".zip" else tarfile.open(out).getnames()
                top = f"Unciv-server-{rid}/"
                self.assertEqual(sorted(names), sorted(top + n for n in (exe, "lib.dll", "LICENSE", "README.md", "data/jsons/ModCategories.json", "data/jsons/sub/a.json")))
            with self.assertRaises(SystemExit):
                self.mod.pack_server("osx-arm64", d, Path(t) / "out", jsons, lic)  # no macOS server
            with self.assertRaises(SystemExit):
                self.mod.pack_server("linux-x64", self.fake_publish(Path(t) / "cli", "linux-x64")[0], Path(t) / "out", jsons, lic)  # a CLI publish folder is not a server one

    def test_pack_relay_layout_carries_guides_and_deploy_and_no_jsons(self):  # T366
        import tarfile
        import zipfile

        self.assertEqual(self.mod.RELAY_RIDS, ClientPlatforms().load("release").RELAY_RIDS)
        deploy = sorted(p.relative_to(self.mod.CSHARP / "Unciv.Relay").as_posix() for p in (self.mod.CSHARP / "Unciv.Relay" / "deploy").rglob("*")
                        if p.is_file() and p.name != ".env" and (p.parent.name != "certs" or p.name == ".gitkeep"))
        self.assertIn("deploy/Dockerfile", deploy)
        self.assertIn("deploy/unciv-relay.service", deploy)
        with tempfile.TemporaryDirectory() as t:
            for rid in self.mod.RELAY_RIDS:
                d, _, lic = self.fake_publish(t, rid)
                exe = "Unciv.Relay.exe" if rid.startswith("win-") else "Unciv.Relay"
                (d / ("Unciv.Cli.exe" if rid.startswith("win-") else "Unciv.Cli")).rename(d / exe)
                out = self.mod.pack_relay(rid, d, Path(t) / "out", lic)
                self.assertEqual(out.name, f"Unciv-relay-{rid}.{'zip' if rid.startswith('win-') else 'tar.gz'}")
                names = zipfile.ZipFile(out).namelist() if out.suffix == ".zip" else tarfile.open(out).getnames()
                top = f"Unciv-relay-{rid}/"
                self.assertEqual(sorted(names), sorted(top + n for n in [exe, "lib.dll", "LICENSE", "README.md", "SETUP.md"] + deploy))
            with self.assertRaises(SystemExit):
                self.mod.pack_relay("osx-arm64", d, Path(t) / "out", lic)
            with self.assertRaises(SystemExit):
                self.mod.pack_relay("linux-x64", self.fake_publish(Path(t) / "cli", "linux-x64")[0], Path(t) / "out", lic)  # a CLI publish folder is not a relay one

    def test_pack_relay_never_packs_a_local_env_or_certificate(self):  # T366
        deploy = self.mod.CSHARP / "Unciv.Relay" / "deploy"
        secrets = [deploy / ".env", deploy / "certs" / "fullchain.pem"]
        for s in secrets:
            self.assertFalse(s.exists(), f"{s} exists locally; remove it before running this test")
        try:
            for s in secrets:
                s.write_text("secret")
            names = [rel for rel, _ in self.mod._relay_extras()]
            self.assertNotIn("deploy/.env", names)
            self.assertNotIn("deploy/certs/fullchain.pem", names)
            self.assertIn("deploy/certs/.gitkeep", names)
        finally:
            for s in secrets:
                s.unlink(missing_ok=True)

    def test_smoke_relay_reports_a_relay_that_never_listens_and_accepts_a_clean_stop(self):  # T366
        with tempfile.TemporaryDirectory() as t:
            fake = Path(t) / "Unciv.Relay"
            fake.write_text("#!/bin/sh\necho '--tunnel-port'\n[ \"$1\" = --help ] && exit 0\nexit 7\n")
            fake.chmod(0o755)
            problems = self.mod.smoke_relay(t)
            self.assertTrue(problems and "LISTENING tunnel" in problems[0], problems)
            fake.write_text("#!/bin/sh\nexit 3\n")
            self.assertTrue("--help failed" in self.mod.smoke_relay(t)[0])
            fake.write_text("#!/bin/sh\n[ \"$1\" = --help ] && { echo '--tunnel-port'; exit 0; }\ntrap 'exit 0' TERM\nprintf 'info\\nLISTENING tunnel 5\\nLISTENING http 6\\n'\nwhile :; do sleep 0.1; done\n")
            self.assertEqual(self.mod.smoke_relay(t), [])
            fake.write_text(fake.read_text().replace("trap 'exit 0' TERM", "trap 'exit 4' TERM"))
            self.assertTrue("expected 0" in self.mod.smoke_relay(t)[0])

    def test_smoke_server_reports_a_server_that_never_listens(self):
        with tempfile.TemporaryDirectory() as t:
            fake = Path(t) / "Unciv.Server"
            fake.write_text("#!/bin/sh\necho '--game-id'\n[ \"$1\" = --help ] && exit 0\nexit 7\n")
            fake.chmod(0o755)
            problems = self.mod.smoke_server(t)
            self.assertTrue(problems and "LISTENING" in problems[0], problems)
            fake.write_text("#!/bin/sh\nexit 3\n")
            self.assertTrue("--help failed" in self.mod.smoke_server(t)[0])

    def test_smoke_server_accepts_a_coalesced_listening_line_and_a_clean_stop(self):
        with tempfile.TemporaryDirectory() as t:
            fake = Path(t) / "Unciv.Server"
            fake.write_text("#!/bin/sh\n[ \"$1\" = --help ] && { echo '--game-id'; exit 0; }\ntrap 'exit 0' TERM\nprintf 'info line\\nLISTENING 5\\n'\nwhile :; do sleep 0.1; done\n")
            fake.chmod(0o755)
            self.assertEqual(self.mod.smoke_server(t), [])
            fake.write_text(fake.read_text().replace("trap 'exit 0' TERM", "trap 'exit 4' TERM"))
            self.assertTrue("expected 0" in self.mod.smoke_server(t)[0])

    def complete_dir(self, tmp):
        import zipfile

        out = Path(tmp) / "assets"
        out.mkdir()
        check = self.mod._load("check_package")
        for platform, (name, exe, minimum, required) in check.PLATFORMS.items():
            if name.endswith(".zip"):
                with zipfile.ZipFile(out / name, "w") as z:
                    for p, size in {exe: minimum, **{r.replace("\\", ""): 1 for r in required}}.items():
                        z.writestr(f"Unciv-{platform}/{p}", b"\0" * size)
        return out, check

    def test_sums_and_verify(self):
        import tarfile

        with tempfile.TemporaryDirectory() as t:
            out, check = self.complete_dir(t)
            self.assertTrue(self.mod.verify(out))  # incomplete: the linux client and the CLIs are missing
            linux = check.PLATFORMS["linux"]
            name, exe, minimum, required = linux
            src = Path(t) / "src"
            for p, size in {exe: minimum, **{r.replace("\\", ""): 1 for r in required}}.items():
                p = p + "f" if p.endswith("/") else p
                (src / "Unciv-linux" / p).parent.mkdir(parents=True, exist_ok=True)
                (src / "Unciv-linux" / p).write_bytes(b"\0" * size)
            with tarfile.open(out / name, "w:gz") as tf:
                tf.add(src / "Unciv-linux", "Unciv-linux")
            import zipfile

            for n in self.mod.expected_assets():
                if not (out / n).exists():
                    (out / n).write_bytes(n.encode())
            with zipfile.ZipFile(out / self.mod.ANDROID_APK, "w") as apk:  # T280: must be a zip with the arm64 library
                apk.writestr("lib/x86_64/libgodot_android.so", b"x")
            self.mod.write_sums(out)
            self.assertTrue(any("arm64" in p for p in self.mod.verify(out)))
            with zipfile.ZipFile(out / self.mod.ANDROID_APK, "w") as apk:
                apk.writestr("lib/arm64-v8a/libgodot_android.so", b"x")
            lines = self.mod.write_sums(out)
            self.assertEqual(len(lines), 11)
            self.assertEqual(lines, sorted(lines, key=lambda l: l.split("  ")[1]))
            self.assertEqual(self.mod.verify(out), [])
            self.assertEqual(self.mod.write_sums(out), lines)  # SHA256SUMS does not hash itself
            subprocess.run(["sha256sum", "-c", "SHA256SUMS"], cwd=out, check=True, capture_output=True)
            (out / "Unciv-cli-win-x64.zip").write_bytes(b"tampered")
            self.assertTrue(any("checksum" in p for p in self.mod.verify(out)))
            (out / "extra.txt").write_text("x")
            self.assertTrue(any("unexpected" in p for p in self.mod.verify(out)))

    def test_notes_say_the_apk_is_not_device_tested(self):  # T280
        text = self.mod.notes("csharp-v1.2.3", "a" * 40, "2026-01-02T03:04:05")
        self.assertIn(self.mod.ANDROID_APK, text)
        self.assertIn("NOT device-tested", text)

    def test_notes_mark_windows_and_macos_unverified_and_name_the_commit(self):
        text = self.mod.notes("csharp-v1.2.3", "a" * 40, "2026-01-02T03:04:05")
        self.assertIn("a" * 40, text)
        self.assertIn("1.2.3", text)
        rows = {r.split("|")[1].strip(" `"): r for r in text.splitlines() if r.startswith("| `")}
        for name in ("Unciv-client-windows.zip", "Unciv-client-macos.zip", "Unciv-cli-win-x64.zip", "Unciv-cli-osx-arm64.tar.gz", "Unciv-server-win-x64.zip", "Unciv-relay-win-x64.zip"):
            self.assertIn("**UNVERIFIED.**", rows[name], name)
        for name in ("Unciv-client-linux.tar.gz", "Unciv-cli-linux-x64.tar.gz", "Unciv-relay-linux-x64.tar.gz"):
            self.assertNotIn("UNVERIFIED", rows[name], name)
        self.assertIn("not notarised", rows["Unciv-client-macos.zip"])
        with self.assertRaises(SystemExit):
            self.mod.notes("v1.2.3", "a", "d")

    def test_cli_entry_points(self):
        self.assertEqual(self.mod.main(["check-tag", "csharp-v1.0.0"]), 0)
        self.assertEqual(self.mod.main(["check-tag", "main"]), 1)
        self.assertEqual(self.mod.main(["bogus"]), 2)


class ProjectSettings(unittest.TestCase):
    def test_etc2_astc_import_stays_on(self):
        # macOS universal and Android exports both refuse to export without it (T258, T260 regression)
        text = (REPO / "csharp" / "Unciv.Godot" / "project.godot").read_text(encoding="utf-8")
        self.assertIn("textures/vram_compression/import_etc2_astc=true", text)

    def test_executable_projects_stay_rid_agnostic_for_the_godot_export(self):
        # T265 made Unciv.Server an Exe; Godot's `dotnet publish -r linux-x64` of the client then built it twice (with and without the RID) and failed with
        # NETSDK1152, so every client export ("export produced no .NET assemblies folder") broke. Any Exe the client references must set IsRidAgnostic.
        root = REPO / "csharp"
        texts = {c.parent.name: c.read_text(encoding="utf-8") for c in root.glob("*/*.csproj")}
        refs = lambda n: re.findall(r"\.\.\\([\w.]+)\\[\w.]+\.csproj", texts.get(n, ""))
        referenced, todo = set(), ["Unciv.Godot"]  # the transitive closure of what the client publishes
        while todo:
            for m in refs(todo.pop()):
                if m not in referenced:
                    referenced.add(m)
                    todo.append(m)
        for name in sorted(referenced):
            if "<OutputType>Exe</OutputType>" in texts.get(name, ""):
                self.assertIn("<IsRidAgnostic>true</IsRidAgnostic>", texts[name], name)


if __name__ == "__main__":
    unittest.main()
