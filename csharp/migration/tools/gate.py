#!/usr/bin/env python3
"""The C# checks in one command, from anywhere: python3 -I tools/gate.py [--quick] [--since REV]

  (default)  the Green gate: dotnet build -warnaserror; dotnet format --verify-no-changes on the files this branch changed
             (the whole solution when a style/config/project file changed); the WHOLE test suite sharded across processes
             and instrumented for coverage in the same single pass (tools/ptest.py --coverage); then the coverage ratchet
             (tools/coverage.py --check), which is instant because the pass above just stored the full report.
  --quick    the dev loop: build, format the changed files, and only the test classes that mention the changed types
             (tools/ptest.py --affected). No coverage. An approximation of the gate, never a substitute for it.

"Changed" = differing from the merge-base with csharp-migration, plus uncommitted and untracked files (--since REV overrides).
Format of the unchanged files is skipped because the base tip already passed it; a changed .editorconfig, Directory.*.props,
*.csproj, BannedSymbols*.txt or a tool-version file makes it check everything.
"""
import argparse
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ptest  # noqa: E402  (explicit path: -I drops the script directory from sys.path)

CSHARP = ptest.CSHARP
GLOBAL_CONFIG = ("Directory.Build.props", "Directory.Packages.props", ".editorconfig", "global.json", "BannedSymbols.txt",
                 "BannedSymbols.Tests.txt")


def run(name, argv, cwd=None, show_tail=True):
    t = time.time()
    print("gate: %s ..." % name, flush=True)
    r = subprocess.run(argv, cwd=cwd, capture_output=True, text=True)
    out = r.stdout + r.stderr
    if r.returncode:
        print("\n".join(out.splitlines()[-60:]) if show_tail else out)
        print("gate: %s FAILED (exit %d, %.0fs)" % (name, r.returncode, time.time() - t), file=sys.stderr)
        return False, out
    print("gate: %s ok (%.0fs)" % (name, time.time() - t), flush=True)
    return True, out


def stream(name, argv):
    """Like run() but lets the child print (ptest/coverage have their own summaries)."""
    t = time.time()
    print("gate: %s ..." % name, flush=True)
    r = subprocess.run(argv)
    if r.returncode:
        print("gate: %s FAILED (exit %d, %.0fs)" % (name, r.returncode, time.time() - t), file=sys.stderr)
        return False
    print("gate: %s ok (%.0fs)" % (name, time.time() - t), flush=True)
    return True


def format_args(changed):
    """-> None to skip, [] to format everything, or the --include list."""
    if any(os.path.basename(c) in GLOBAL_CONFIG or c.endswith((".csproj", ".targets", ".sln")) for c in changed):
        return []
    cs = [c for c in changed if c.endswith(".cs") and os.path.exists(os.path.join(CSHARP, c)) and not c.startswith("migration/")]
    return cs or None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--quick", action="store_true")
    ap.add_argument("--since", metavar="REV")
    a = ap.parse_args()
    t0 = time.time()

    argv, cwd = ptest.dotnet_cmd(["build", "-warnaserror", "--nologo", "-v", "q"])
    if not run("build", argv, cwd)[0]:
        return 1

    fmt = format_args(ptest.changed_since(a.since))
    if fmt is None:
        print("gate: format skipped (no changed .cs files)")
    else:
        args = ["format", "--verify-no-changes", "--no-restore"] + (["--include"] + fmt if fmt else [])
        argv, cwd = ptest.dotnet_cmd(args)
        ok, _ = run("format (%s)" % ("%d changed file(s)" % len(fmt) if fmt else "everything"), argv, cwd)
        if not ok:
            print("gate: run `dotnet format` (no flag) and review the diff; it must touch only your files", file=sys.stderr)
            return 1

    ptest_py = os.path.join(HERE, "ptest.py")
    if a.quick:
        if not stream("tests (affected classes)", [sys.executable, "-I", ptest_py, "--no-build", "--affected"] + (["--since", a.since] if a.since else [])):
            return 1
    else:
        if not stream("tests + coverage (one pass)", [sys.executable, "-I", ptest_py, "--no-build", "--coverage"]):
            return 1
        cov = [sys.executable, "-I", os.path.join(HERE, "coverage.py"), "--check"] + (["--since", a.since] if a.since else [])
        if not stream("coverage ratchet", cov):
            return 1
    print("gate: %s passed in %.0fs" % ("quick check" if a.quick else "Green gate", time.time() - t0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
