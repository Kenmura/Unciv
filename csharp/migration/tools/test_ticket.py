"""Tests for the Green-before-Done gate of ticket.py (T163). Run: python3 -I -m unittest discover -s tools -p 'test_*.py'."""
import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parent / "ticket.py"

BACKLOG = """# Backlog

### T001 Code ticket
- State: In-Review [Bot]
- Depends: none
- Deliverables: Unciv.Core/Foo.cs
- Notes:

### T002 Docs ticket
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
        (self.mig / "backlog.md").write_text(BACKLOG)
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
        self.assertFalse(self.t.stamp_path().exists())

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
        (self.mig / "backlog.md").write_text(BACKLOG + "\nsee TODO(T001) here\n")
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
        self.assertIn("docs only", (self.mig / "backlog.md").read_text())

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


if __name__ == "__main__":
    unittest.main()
