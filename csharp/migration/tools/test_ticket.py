"""Tests for the Green-before-Done gate of ticket.py (T163). Run: python3 -I -m unittest discover -s tools -p 'test_*.py'."""
import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parent / "ticket.py"

T001 = """### T001 Code ticket
- State: In-Review [Bot]
- Depends: none
- Deliverables: Unciv.Core/Foo.cs
- Notes:
"""
T002 = """### T002 Docs ticket
- State: In-Review [Bot]
- Depends: none
- Deliverables: PROTOCOL.md
- Notes:
"""


def git(cwd, *a):
    return subprocess.run(["git", "-C", str(cwd), *a], capture_output=True, text=True, check=True).stdout


class GreenGate(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        git(root, "init", "-q", "-b", "csharp-migration")
        git(root, "config", "user.email", "t@t")
        git(root, "config", "user.name", "t")
        self.mig = root / "csharp" / "migration"
        self.mig.mkdir(parents=True)
        (self.mig / "backlog").mkdir()
        (self.mig / "backlog" / "README.md").write_text("# Backlog\n")
        (self.mig / "backlog" / "T001.md").write_text(T001)
        (self.mig / "backlog" / "T002.md").write_text(T002)
        (self.mig / "notes.txt").write_text("nothing\n")
        git(root, "add", "-A")
        git(root, "commit", "-q", "-m", "init")
        git(root, "checkout", "-q", "-b", "agent/x")
        self.root = root
        os.environ["TICKET_MIGRATION_DIR"] = str(self.mig)
        spec = importlib.util.spec_from_file_location("ticket_under_test", TOOL)
        self.t = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.t)
        self.ran = []
        self.fail_step = None

        def fake():
            return [("a", "true"), ("b", "false" if self.fail_step == "b" else "true")]

        self.t.green_commands = fake
        orig = subprocess.run

        def spy(cmd, *a, **k):
            if isinstance(cmd, str):
                self.ran.append(cmd)
            return orig(cmd, *a, **k)

        self.t.subprocess.run = spy
        self.orig_run = orig

    def tearDown(self):
        self.t.subprocess.run = self.orig_run
        os.environ.pop("TICKET_MIGRATION_DIR", None)
        self.tmp.cleanup()

    def state(self, *args):
        try:
            self.t.cmd_state(list(args))
            return 0
        except SystemExit as e:
            return e.code

    def test_done_runs_checks_and_succeeds(self):
        self.assertEqual(self.state("T001", "Done"), 0)
        self.assertEqual(len(self.ran), 2)
        self.assertIn("Done", self.t.current_state("T001"))

    def test_failing_check_refuses_and_leaves_state(self):
        self.fail_step = "b"
        self.assertEqual(self.state("T001", "Done"), 1)
        self.assertTrue(self.t.current_state("T001").startswith("In-Review"))
        self.assertNotIn(self.t.tree_id(), self.t.stamped_trees())

    def test_stamp_is_shared_across_worktrees_and_keeps_other_trees(self):
        self.t.cmd_green()
        first = self.t.tree_id()
        self.assertEqual(self.t.stamp_path().parent.resolve(), (self.root / ".git").resolve())  # the common dir, not a per-worktree one
        (self.mig / "notes.txt").write_text("second tree\n")
        git(self.root, "commit", "-qam", "second")
        self.t.cmd_green()
        self.assertEqual(self.t.stamped_trees(), [first, self.t.tree_id()])
        self.fail_step = "b"
        with self.assertRaises(SystemExit):
            self.t.run_green(force=True)
        self.assertEqual(self.t.stamped_trees(), [first])  # a failing tree loses its own stamp only

    def test_stamp_skips_rerun_for_same_tree_only(self):
        self.t.cmd_green()
        self.ran.clear()
        self.assertEqual(self.state("T001", "Done"), 0)
        self.assertEqual(self.ran, [])
        git(self.root, "commit", "-q", "--allow-empty", "-m", "x")  # same tree: stamp still valid
        self.assertEqual(self.state("T002", "Done", "--no-green", "docs"), 0)
        (self.mig / "notes.txt").write_text("changed\n")
        git(self.root, "commit", "-qam", "change")
        self.t.stamp_path().write_text("stale\n")
        self.ran.clear()
        self.t.run_green()
        self.assertEqual(len(self.ran), 2)

    def test_dirty_tree_refused(self):
        (self.mig / "notes.txt").write_text("dirty\n")
        self.assertEqual(self.state("T001", "Done"), 1)
        self.assertEqual(self.ran, [])

    def test_not_rebased_refused(self):
        git(self.root, "checkout", "-q", "csharp-migration")
        (self.mig / "notes.txt").write_text("tip moved\n")
        git(self.root, "commit", "-qam", "tip")
        git(self.root, "checkout", "-q", "agent/x")
        self.assertEqual(self.state("T001", "Done"), 1)
        self.assertEqual(self.ran, [])

    def test_stale_todo_marker_refused_but_backlog_mentions_ignored(self):
        (self.mig / "backlog" / "T001.md").write_text(T001 + "\nsee TODO(T001) here\n")
        (self.mig / "Seams.cs").write_text("// TODO(T001) later\n// TODO(T0011) other\n")
        git(self.root, "add", "-A")
        git(self.root, "commit", "-q", "-m", "marker")
        self.assertEqual(self.state("T001", "Done"), 1)
        self.assertEqual(len(self.t.stale_markers("T001")), 1)
        self.assertIn("Seams.cs", self.t.stale_markers("T001")[0])
        self.assertEqual(self.t.stale_markers("T002"), [])

    def test_no_green_allowed_for_docs_and_noted(self):
        self.assertEqual(self.state("T002", "Done", "--no-green", "docs only"), 0)
        self.assertEqual(self.ran, [])
        self.assertIn("docs only", (self.mig / "backlog" / "T002.md").read_text())

    def test_no_green_refused_for_code_ticket_and_empty_reason(self):
        self.assertEqual(self.state("T001", "Done", "--no-green", "lazy"), 1)
        self.assertEqual(self.state("T002", "Done", "--no-green", " "), 1)
        self.assertEqual(self.ran, [])

    def test_no_green_refused_for_empty_deliverables_flag_reason_and_code_diff(self):
        self.t.mutate("T002", lambda b: self.t.set_line(b, "Deliverables", ""))
        git(self.root, "commit", "-qam", "empty")
        self.assertEqual(self.state("T002", "Done", "--no-green", "docs"), 1)
        self.t.mutate("T002", lambda b: self.t.set_line(b, "Deliverables", "X.md"))
        git(self.root, "commit", "-qam", "md")
        self.assertEqual(self.state("T002", "Done", "--no-green", "--publish"), 1)
        (self.mig / "tool.py").write_text("x = 1\n")
        git(self.root, "add", "-A")
        git(self.root, "commit", "-qm", "code")
        self.assertEqual(self.state("T002", "Done", "--no-green", "docs"), 1)
        self.assertEqual(self.ran, [])

    def test_done_with_all_refused_and_other_states_unchecked(self):
        self.assertEqual(self.state("T001", "Done", "--all"), 1)
        self.assertEqual(self.state("T001", "Available"), 0)
        self.assertEqual(self.ran, [])

    def test_stale_markers_ignore_other_ids_prefix(self):
        (self.mig / "A.cs").write_text("// TODO(T10) x\n")
        git(self.root, "add", "-A")
        self.assertEqual(self.t.stale_markers("T1"), [])

    def test_claim_commits_only_that_ticket_and_map(self):
        self.t.mutate("T001", lambda b: self.t.set_line(b, "State", "Available"))
        git(self.root, "commit", "-qam", "avail")
        (self.mig / "notes.txt").write_text("unrelated\n")
        self.t.cmd_claim(["T001", "Z", "--no-publish"])
        files = git(self.root, "show", "--name-only", "--format=", "HEAD").split()
        self.assertEqual(sorted(files), ["csharp/migration/DEPENDENCY_MAP.md", "csharp/migration/backlog/T001.md"])
        self.assertIn("notes.txt", git(self.root, "status", "--porcelain"))

    def test_add_order_show_and_history(self):
        import io, contextlib
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            self.t.cmd_add(["--title", "New one", "--depends", "T001"])
        self.assertEqual(buf.getvalue().strip(), "T003")
        self.assertEqual([r[0] for r in self.t.tickets()], ["T001", "T002", "T003"])
        git(self.root, "add", "-A")
        git(self.root, "commit", "-qm", "add")
        pts = self.t.backlog_history()
        self.assertEqual(pts[-1][1:], (1, 0, 2, 0))
        self.assertEqual(self.t.cmd_archive(), None)

    def test_history_reads_old_single_file_layout(self):
        git(self.root, "checkout", "-q", "csharp-migration")
        git(self.root, "rm", "-rq", "csharp/migration/backlog")
        (self.mig / "backlog.md").write_text("### T001 A\n- State: Available\n\n### T002 B\n- State: Done\n")
        git(self.root, "add", "-A")
        git(self.root, "commit", "-qm", "legacy")
        for p in (self.mig / "backlog.md",):
            p.unlink()
        (self.mig / "backlog").mkdir()
        (self.mig / "backlog" / "T001.md").write_text("### T001 A\n- State: In-Progress [x]\n")
        git(self.root, "add", "-A")
        git(self.root, "commit", "-qm", "split")
        self.t._cache.clear()
        pts = self.t.backlog_history()
        self.assertEqual([p[1:] for p in pts][-2:], [(1, 0, 0, 1), (0, 0, 1, 0)])


if __name__ == "__main__":
    unittest.main()


class ScreenshotGallery(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.mig = Path(self.tmp.name) / "csharp" / "migration"
        (self.mig / "backlog").mkdir(parents=True)
        (self.mig / "backlog" / "README.md").write_text("# Backlog\n")
        for n, state in (("C001", "Done"), ("C002", "Done"), ("C003", "Available")):
            (self.mig / "backlog" / f"{n}.md").write_text(f"### {n} Thing {n}\n- State: {state}\n- Depends: none\n- Notes:\n")
        golden = Path(self.tmp.name) / "csharp" / "Unciv.Godot.Harness" / "golden" / "s"
        golden.mkdir(parents=True)
        (golden / "a.png").write_bytes(b"x")
        self.env = {**os.environ, "TICKET_MIGRATION_DIR": str(self.mig)}

    def tearDown(self):
        self.tmp.cleanup()

    def run_tool(self, tool, *args):
        return subprocess.run(["python3", "-I", str(Path(__file__).resolve().parent / tool), *args],
                              capture_output=True, text=True, env=self.env)

    def write_map(self, mapping):
        import json
        (self.mig / "screenshots.json").write_text(json.dumps(mapping))

    def test_generate_check_and_missing(self):
        self.write_map({"C001": {"scenario": "s", "about": "A", "shots": [{"golden": "s/a.png", "caption": "Cap"}]}})
        self.assertEqual(self.run_tool("screenshots.py", "--missing").stdout.split(), ["C002"])
        self.assertEqual(self.run_tool("screenshots.py", "--check").returncode, 1)  # no page yet + C002 unmapped
        self.write_map({"C001": {"scenario": "s", "about": "A", "shots": [{"golden": "s/a.png", "caption": "Cap"}]},
                        "C002": {"none": "no UI"}})
        self.assertEqual(self.run_tool("screenshots.py").returncode, 0)
        page = (self.mig / "SCREENSHOTS.md").read_text()
        self.assertIn("## C001 Thing C001", page)
        self.assertIn("golden/s/a.png)", page)
        self.assertIn("**C002 Thing C002**: no UI", page)
        self.assertEqual(self.run_tool("screenshots.py", "--check").returncode, 0)

    def test_missing_golden_fails_check(self):
        self.write_map({"C001": {"scenario": "s", "about": "A", "shots": [{"golden": "s/zzz.png", "caption": "C"}]},
                        "C002": {"none": "n"}})
        self.run_tool("screenshots.py")
        r = self.run_tool("screenshots.py", "--check")
        self.assertEqual(r.returncode, 1)
        self.assertIn("missing golden", r.stdout)

    def test_ticket_check_warns_but_passes(self):
        self.write_map({"C001": {"none": "n"}})
        r = self.run_tool("ticket.py", "check")
        self.assertEqual(r.returncode, 0)
        self.assertIn("WARN C002", r.stdout)
        self.assertNotIn("WARN C001", r.stdout)


class ReleaseFolder(unittest.TestCase):
    """tools/release.py: pruning keeps the newest versions and what `latest` points at; the folder is git-ignored."""

    def load(self):
        spec = importlib.util.spec_from_file_location("release", Path(__file__).resolve().parent / "release.py")
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        return mod

    def make(self, root, name, built):
        d = root / name
        d.mkdir()
        (d / "manifest.json").write_text('{"built": %d}' % built)

    def test_prune_keeps_newest_and_latest(self):
        rel = self.load()
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            for i in range(6):
                self.make(root, f"T{i}-aaaa", i + 1)
            rel.point_latest(root, "T0-aaaa")  # oldest, but `latest`: must survive
            gone = rel.prune(root, 3)
            self.assertEqual(sorted(gone), ["T1-aaaa", "T2-aaaa"])
            self.assertEqual(sorted(d.name for d in rel.versions(root)), ["T0-aaaa", "T3-aaaa", "T4-aaaa", "T5-aaaa"])

    def test_nothing_pruned_under_the_limit(self):
        rel = self.load()
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            self.make(root, "T1-a", 1)
            self.assertEqual(rel.prune(root, 5), [])

    def run_with(self, rel, codes, retries):
        from unittest import mock
        calls = []

        def fake(cmd, **kw):
            calls.append(cmd)
            return subprocess.CompletedProcess(cmd, codes[len(calls) - 1], "", "")
        with mock.patch.object(rel.subprocess, "run", fake), mock.patch.object(rel, "clear_import_cache") as clear:
            try:
                rel.run("x", "true", crash_retries=retries)
                ok = True
            except SystemExit:
                ok = False
        return ok, len(calls), clear.call_count

    def test_crashed_export_is_retried_with_a_cleared_cache(self):
        rel = self.load()
        self.assertEqual(self.run_with(rel, [139, 0], 2), (True, 2, 1))

    def test_persistent_crash_gives_up_after_the_bound(self):
        rel = self.load()
        self.assertEqual(self.run_with(rel, [139, 139, 139], 2), (False, 3, 2))

    def test_ordinary_failure_and_no_retry_budget_are_not_retried(self):
        rel = self.load()
        self.assertEqual(self.run_with(rel, [1], 2), (False, 1, 0))
        self.assertEqual(self.run_with(rel, [139], 0), (False, 1, 0))

    def test_releases_folder_is_ignored_by_git(self):
        repo = Path(__file__).resolve().parents[3]
        r = subprocess.run(["git", "-C", str(repo), "check-ignore", "csharp-releases/T1-abc/client-linux/Unciv.x86_64"], capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, "csharp-releases/ must be in .gitignore: binaries never go into git")


class PhaseFourRelease(unittest.TestCase):
    """Phase 4 (queued plans) is offered only once phases 1-3 hold no open, non-Blocked ticket; phase 5 only once 1-4 are clear."""

    def ready_ids(self, states):
        spec = importlib.util.spec_from_file_location("ticket_phase4", TOOL)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        mod.all_states = lambda: states
        mod.phase_of = lambda tid: {"T1": 1, "C1": 3, "C9": 4, "B1": 4, "C5": 5}[tid]
        mod.downstream_counts = lambda ts: {}
        ts = [(i, i, s, "none", "") for i, s in states.items()]
        return [r[1] for r in mod.ready_list(ts)]

    def test_held_while_earlier_phase_has_open_work(self):
        self.assertEqual(self.ready_ids({"T1": "Available", "C1": "In-Progress [Bot]", "C9": "Available"}), ["T1"])
        self.assertEqual(self.ready_ids({"T1": "Done", "C1": "In-Progress [Bot]", "C9": "Available"}), [])

    def test_released_when_earlier_phases_are_done_or_blocked(self):
        self.assertEqual(self.ready_ids({"T1": "Done", "C1": "Blocked", "C9": "Available", "B1": "Available"}), ["B1", "C9"])

    def test_phase_five_waits_for_phase_four(self):
        self.assertEqual(self.ready_ids({"T1": "Done", "C9": "Available", "C5": "Available"}), ["C9"])
        self.assertEqual(self.ready_ids({"T1": "Done", "C9": "In-Progress [Bot]", "C5": "Available"}), [])
        self.assertEqual(self.ready_ids({"T1": "Done", "C9": "Done", "B1": "Blocked", "C5": "Available"}), ["C5"])
        self.assertEqual(self.ready_ids({"T1": "Available", "C9": "Done", "C5": "Available"}), ["T1"])
