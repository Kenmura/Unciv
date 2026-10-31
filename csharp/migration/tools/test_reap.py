"""Tests for tools/reap.py and the memory settings of tools/ptest.py (T360).
Run: python3 -I -m unittest discover -s tools -p 'test_*.py' (from csharp/migration)."""
import importlib.util
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load(name):
    spec = importlib.util.spec_from_file_location(name, HERE / (name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


reap = load("reap")
ptest = load("ptest")

NODE = ["/usr/lib64/dotnet/dotnet", "/usr/lib64/dotnet/sdk/10.0.109/MSBuild.dll", "/nodemode:1", "/nodeReuse:true"]
HOST = ["/usr/lib64/dotnet/dotnet", "exec", "--runtimeconfig", "/r/.worktrees/w/csharp/X.runtimeconfig.json"]


def proc(kind, wt, age_min, cpu=0):
    return {"kind": kind, "worktree": wt, "age_s": age_min * 60.0, "cpu": cpu, "rss_mb": 1.0, "cmd": "", "pid": 0, "ppid": 0}


class Classify(unittest.TestCase):
    def test_kinds(self):
        self.assertEqual(reap.classify(NODE), "msbuild-node")
        self.assertEqual(reap.classify(HOST), "testhost")
        self.assertEqual(reap.classify(["x", "exec", "--runtimeconfig", "a", "/sdk/datacollector.dll"]), "datacollector")
        self.assertEqual(reap.classify(["/usr/bin/dotnet", "test", "P"]), "dotnet-cli")
        self.assertEqual(reap.classify(["/usr/bin/vim"]), "other")

    def test_worktree_path(self):
        self.assertEqual(reap.worktree_of("dotnet exec --runtimeconfig /v/r/.worktrees/claude-T1/csharp/a.json"), "/v/r/.worktrees/claude-T1")
        self.assertEqual(reap.worktree_of("", "PWD=x", "/v/r/.worktrees/w/csharp"), "/v/r/.worktrees/w")
        self.assertIsNone(reap.worktree_of("/usr/lib64/dotnet"))


class Decide(unittest.TestCase):
    def verdicts(self, procs, cpu_after=None, gone=()):
        before = {pid: dict(p) for pid, p in procs.items()}
        after = {pid: dict(p, cpu=(cpu_after or {}).get(pid, p["cpu"])) for pid, p in procs.items()}
        return {k: v[0] for k, v in reap.decide(before, after, 30 * 60, exists=lambda w: w not in gone).items()}

    def test_idle_old_in_idle_worktree_is_killed(self):
        v = self.verdicts({1: proc("msbuild-node", "/w/a", 90), 2: proc("testhost", "/w/a", 90)})
        self.assertEqual(v, {1: "KILL", 2: "KILL"})

    def test_live_build_protects_its_worktree(self):
        v = self.verdicts({1: proc("msbuild-node", "/w/a", 90), 2: proc("dotnet-cli", "/w/a", 2), 3: proc("msbuild-node", "/w/b", 90)})
        self.assertEqual(v, {1: "keep", 2: "keep", 3: "KILL"})

    def test_cpu_use_protects(self):
        v = self.verdicts({1: proc("testhost", "/w/a", 90)}, cpu_after={1: 5})
        self.assertEqual(v, {1: "keep"})

    def test_missing_worktree_is_killed_even_when_young(self):
        v = self.verdicts({1: proc("msbuild-node", "/w/gone", 1)}, gone={"/w/gone"})
        self.assertEqual(v, {1: "KILL"})

    def test_runaway_is_killed_even_when_busy(self):
        v = self.verdicts({1: proc("testhost", "/w/a", 24 * 60)}, cpu_after={1: 99})
        self.assertEqual(v, {1: "KILL"})

    def test_drivers_are_never_killed(self):
        v = self.verdicts({1: proc("driver", "/w/gone", 24 * 60)}, gone={"/w/gone"})
        self.assertEqual(v, {1: "keep"})

    def test_shared_compiler_waits_for_everyone(self):
        v = self.verdicts({1: proc("vbcscompiler", None, 90), 2: proc("dotnet-cli", "/w/a", 2)})
        self.assertEqual(v[1], "keep")
        self.assertEqual(self.verdicts({1: proc("vbcscompiler", None, 90)}), {1: "KILL"})


class MemorySettings(unittest.TestCase):
    def test_every_dotnet_command_disables_node_reuse_and_caps_nodes(self):
        argv, _ = ptest.dotnet_cmd(["build", "P", "-warnaserror"])
        line = " ".join(argv)
        for needle in ("MSBUILDDISABLENODEREUSE=1", "MSBUILDNODECOUNT=", "DOTNET_gcServer=0", "UseSharedCompilation=false", "-nodeReuse:false", "-m:"):
            self.assertIn(needle, line)

    def test_node_count_is_configurable_and_sanitised(self):
        import os
        old = os.environ.get("UNCIV_MSBUILD_NODES")
        try:
            os.environ["UNCIV_MSBUILD_NODES"] = "3"
            self.assertIn("MSBUILDNODECOUNT=3", ptest.dotnet_env())
            os.environ["UNCIV_MSBUILD_NODES"] = "x"
            self.assertIn("MSBUILDNODECOUNT=1", ptest.dotnet_env())
        finally:
            if old is None:
                os.environ.pop("UNCIV_MSBUILD_NODES", None)
            else:
                os.environ["UNCIV_MSBUILD_NODES"] = old

    def test_switches_go_before_the_runsettings_separator(self):
        args = ptest.dotnet_args(["test", "P", "--", "A=B"])
        self.assertLess(args.index("-nodeReuse:false"), args.index("--"))
        self.assertEqual(ptest.dotnet_args(["format", "--verify-no-changes"]), ["format", "--verify-no-changes"])


class Scratch(unittest.TestCase):
    """T390: stale scratch folder detection and removal, against a fabricated temp root."""
    NOW = __import__("time").time()
    HOUR = 3600.0

    def setUp(self):
        import os
        import tempfile
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.uid = os.getuid()
        self.addCleanup(self.tmp.cleanup)

    def mk(self, rel, age_h, files=("f",)):
        import os
        d = os.path.join(self.root, rel)
        os.makedirs(d)
        for f in files:
            p = os.path.join(d, f)
            with open(p, "w") as fh:
                fh.write("x")
            os.utime(p, (self.NOW - age_h * self.HOUR,) * 2)
        for root, dirs, _files in os.walk(d, topdown=False):
            for sub in dirs + [""]:
                os.utime(os.path.join(root, sub), (self.NOW - age_h * self.HOUR,) * 2)
        up = os.path.dirname(d)
        while up != self.root:
            os.utime(up, (self.NOW - age_h * self.HOUR,) * 2)
            up = os.path.dirname(up)
        return d

    def rows(self, usage="", alive=(), age=6, claude_age=24):
        r = reap.scratch_candidates(self.root, self.NOW, self.uid, usage, lambda pid: pid in alive, age * self.HOUR, claude_age * self.HOUR)
        return {__import__("os").path.relpath(x["path"], self.root): x["verdict"] for x in r}

    def test_run_folder_follows_its_pid(self):
        self.mk("unciv-run-111-abc", 0)
        self.mk("unciv-run-222-abc", 0)
        self.assertEqual(self.rows(alive=(111,)), {"unciv-run-111-abc": "keep", "unciv-run-222-abc": "STALE"})

    def test_named_temp_needs_age_and_no_user(self):
        a = self.mk("unciv-relay-new", 1)
        b = self.mk("unciv-relay-old", 10)
        c = self.mk("unciv-relay-busy", 10)
        self.assertEqual(self.rows(usage="cwd " + c), {"unciv-relay-new": "keep", "unciv-relay-old": "STALE", "unciv-relay-busy": "keep"})
        self.assertTrue(a and b)

    def test_empty_msbuild_dirs_only_when_empty_and_old(self):
        self.mk("MSBuildTempAbc123", 10, files=())
        self.mk("abc123", 10, files=())
        self.mk("abc124", 10)              # six characters but not empty: left alone, not even listed
        self.mk("MSBuildTempOld000", 1, files=())
        self.assertEqual(self.rows(), {"MSBuildTempAbc123": "STALE", "abc123": "STALE"})

    def test_agent_scratchpads(self):
        import os
        uid = "0d6d2614-99b0-48ef-bcf3-ca6fdf3bad2b"
        old = self.mk("claude-%d/-home-x/%s/scratchpad" % (self.uid, uid), 48)
        new = self.mk("claude-%d/-home-x/11111111-2222-3333-4444-555555555555/scratchpad" % self.uid, 1)
        busy = self.mk("claude-%d/-home-x/66666666-2222-3333-4444-555555555555/scratchpad" % self.uid, 48)
        self.mk("claude-%d/dn/shared" % self.uid, 99)   # not a project/session folder
        got = self.rows(usage=os.path.dirname(busy))
        self.assertEqual(got, {os.path.relpath(os.path.dirname(old), self.root): "STALE",
                               os.path.relpath(os.path.dirname(new), self.root): "keep",
                               os.path.relpath(os.path.dirname(busy), self.root): "keep"})

    def test_remove_deletes_only_stale_rows_inside_root(self):
        import os
        self.mk("unciv-run-222-abc", 0)
        self.mk("unciv-relay-new", 1)
        keep_outside = self.mk("elsewhere", 99)
        rows = reap.scratch_candidates(self.root, self.NOW, self.uid, "", lambda pid: False, 6 * self.HOUR, 24 * self.HOUR)
        rows.append({"path": keep_outside, "verdict": "STALE", "bytes": 0})   # fabricated: inside root, so removable
        rows.append({"path": "/usr", "verdict": "STALE", "bytes": 0})        # outside root: must be refused
        n, _ = reap.remove_scratch(rows, self.root)
        self.assertEqual(n, 2)
        self.assertFalse(os.path.exists(os.path.join(self.root, "unciv-run-222-abc")))
        self.assertTrue(os.path.exists(os.path.join(self.root, "unciv-relay-new")))
        self.assertTrue(os.path.isdir("/usr"))

    def test_read_only_folders_are_removed_too(self):
        import os
        d = self.mk("unciv-run-333-abc/inner/ro", 10)
        os.chmod(d, 0o500)
        n, _ = reap.remove_scratch(reap.scratch_candidates(self.root, self.NOW, self.uid, "", lambda pid: False, 6 * self.HOUR, 24 * self.HOUR), self.root)
        self.assertEqual(n, 1)
        self.assertFalse(os.path.exists(os.path.join(self.root, "unciv-run-333-abc")))

    def test_symlink_is_never_followed(self):
        import os
        target = self.mk("real", 99)
        os.symlink(target, os.path.join(self.root, "unciv-link"))
        self.assertNotIn("unciv-link", self.rows())
        self.assertEqual(reap.remove_scratch([{"path": os.path.join(self.root, "unciv-link"), "verdict": "STALE", "bytes": 0}], self.root), (0, 0))
        self.assertTrue(os.path.isdir(target))


class RunTmp(unittest.TestCase):
    def test_acquire_exports_tmpdir_and_release_removes_it(self):
        import os
        import tempfile
        rt = load("runtmp")
        saved = {k: os.environ.get(k) for k in (rt.ENV_DIR, rt.ENV_KEEP, "TMPDIR")}
        saved_tempdir = tempfile.tempdir
        try:
            os.environ.pop(rt.ENV_DIR, None)
            os.environ.pop(rt.ENV_KEEP, None)
            path = rt.acquire()
            self.assertTrue(os.path.basename(path).startswith("%s%d-" % (rt.PREFIX, os.getpid())))
            self.assertEqual(os.environ["TMPDIR"], path)
            self.assertIn("TMPDIR=" + path, ptest.dotnet_env())
            self.assertEqual(rt.acquire(), path)           # a second call reuses it
            rt.release()
            self.assertFalse(os.path.exists(path))
            path = rt.acquire()
            os.environ[rt.ENV_KEEP] = "1"
            rt.release()
            self.assertTrue(os.path.isdir(path))           # --keep / UNCIV_KEEP_TMP keeps it
            os.rmdir(path)
        finally:
            tempfile.tempdir = saved_tempdir
            for k, v in saved.items():
                if v is None:
                    os.environ.pop(k, None)
                else:
                    os.environ[k] = v


if __name__ == "__main__":
    unittest.main()
