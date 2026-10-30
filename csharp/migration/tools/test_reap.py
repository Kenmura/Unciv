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


if __name__ == "__main__":
    unittest.main()
