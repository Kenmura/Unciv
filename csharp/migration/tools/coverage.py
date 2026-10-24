#!/usr/bin/env python3
"""Line/branch coverage per source file, with a ratchet baseline (T122). Run: python3 -I tools/coverage.py ...

Cost model: ONE instrumented `dotnet test` run (~45 s) per ticket, in the Review step (and by the implementer once
before Needs-Review). Never on every test run. The result is cached in csharp/migration/.coverage/report.json and
reused while no .cs/.csproj/.props file is newer than it, so --check, --report and --update-baseline after one run
cost nothing. Runs through distrobox `dotnet-dev` automatically when `dotnet` is not on PATH.

  --check            Gate. Exit 1 if, for any checked file, line coverage fell below its baseline, or a file with no
                     baseline entry has < 80% line coverage and is not excused. Prints the per-file table.
  --report           Same table for the checked files plus uncovered line ranges; never fails (for the Review step).
  --update-baseline  Raise/add baseline entries for the checked files (never lowers one; a lowered file must be
                     fixed with tests, or excused). Writes migration/coverage-baseline.json.
  --run              Force a fresh coverage run even if the cached report is current.
  --all              Check every file instead of only the files this branch changed (vs csharp-migration, plus
                     uncommitted/untracked). Use with --update-baseline once to (re)generate the whole baseline.
  FILE...            Explicit files (relative to csharp/) to check instead of the changed set.

--update-baseline --all grandfathers every existing file at its current coverage (ratchet start); afterwards only
files born later face the 80% rule.

Excusing a new file below 80% (declarations only, platform glue, ...): add it to "excused" in the baseline file with a
reason naming a ticket; files with no executable lines are skipped automatically.
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
MIGRATION = os.path.dirname(HERE)
CSHARP = os.path.dirname(MIGRATION)
CACHE = os.path.join(MIGRATION, ".coverage")
REPORT = os.path.join(CACHE, "report.json")
BASELINE = os.path.join(MIGRATION, "coverage-baseline.json")
NEW_FILE_MIN = 80.0
CONTAINER = "dotnet-dev"


def run_coverage():
    raw = os.path.join(CACHE, "raw")
    shutil.rmtree(raw, ignore_errors=True)
    os.makedirs(raw)
    cmd = ('dotnet test Unciv.Core.Tests --collect:"XPlat Code Coverage" --results-directory "%s" '
           '-- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura' % raw)
    if shutil.which("dotnet") is None:
        cmd = 'distrobox enter %s -- bash -c %s' % (CONTAINER, shlex_quote("cd %s && %s" % (shlex_quote(CSHARP), cmd)))
        full = cmd
    else:
        full = "cd %s && %s" % (shlex_quote(CSHARP), cmd)
    print("running instrumented tests (~1 min)...", flush=True)
    proc = subprocess.run(full, shell=True, capture_output=True, text=True)
    tail = "\n".join((proc.stdout + proc.stderr).splitlines()[-15:])
    files = glob.glob(os.path.join(raw, "**", "coverage.cobertura.xml"), recursive=True)
    if proc.returncode != 0 or not files:
        print(tail)
        sys.exit("coverage: the instrumented test run failed (fix the tests; see output above)")
    return files[0]


def shlex_quote(s):
    return "'" + s.replace("'", "'\\''") + "'"


def parse(path):
    """-> {file: {"lines": {n: hits}, "branches": {n: (covered, total)}}} merged across classes/methods."""
    out = {}
    for cls in ET.parse(path).getroot().iter("class"):
        fn = cls.get("filename").replace("\\", "/")
        if ".Tests/" in fn or fn.startswith("/"):
            continue
        f = out.setdefault(fn, {"lines": {}, "branches": {}})
        for line in cls.find("lines"):
            n = int(line.get("number"))
            f["lines"][n] = max(f["lines"].get(n, 0), int(line.get("hits")))
            m = re.search(r"\((\d+)/(\d+)\)", line.get("condition-coverage") or "")
            if m:
                c, t = int(m.group(1)), int(m.group(2))
                if n not in f["branches"] or c > f["branches"][n][0]:
                    f["branches"][n] = (c, t)
    result = {}
    for fn, f in sorted(out.items()):
        valid = len(f["lines"])
        covered = sum(1 for h in f["lines"].values() if h > 0)
        bt = sum(t for _, t in f["branches"].values())
        bc = sum(c for c, _ in f["branches"].values())
        result[fn] = {
            "valid": valid, "covered": covered,
            "line": round(100.0 * covered / valid, 2) if valid else 100.0,
            "branch": round(100.0 * bc / bt, 2) if bt else 100.0,
            "uncovered": ranges(sorted(n for n, h in f["lines"].items() if h == 0)),
        }
    return result


def ranges(nums):
    out, start, prev = [], None, None
    for n in nums:
        if start is None:
            start = prev = n
        elif n == prev + 1:
            prev = n
        else:
            out.append((start, prev))
            start = prev = n
    if start is not None:
        out.append((start, prev))
    return ["%d" % a if a == b else "%d-%d" % (a, b) for a, b in out]


def is_stale():
    if not os.path.exists(REPORT):
        return True
    t = os.path.getmtime(REPORT)
    for root, dirs, files in os.walk(CSHARP):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj", ".coverage", ".git", "node_modules")]
        for name in files:
            if name.endswith((".cs", ".csproj", ".props", ".editorconfig")) and os.path.getmtime(os.path.join(root, name)) > t:
                return True
    return False


def load_report(force):
    if force or is_stale():
        data = parse(run_coverage())
        os.makedirs(CACHE, exist_ok=True)
        with open(REPORT, "w") as fh:
            json.dump(data, fh)
        return data
    with open(REPORT) as fh:
        return json.load(fh)


def git(*args):
    return subprocess.run(["git", "-C", CSHARP] + list(args), capture_output=True, text=True).stdout.split("\n")


def changed_files():
    base = git("merge-base", "HEAD", "csharp-migration")[0].strip() or "csharp-migration"
    names = set(git("diff", "--name-only", "--relative", base, "--", "."))
    names |= set(git("ls-files", "--others", "--exclude-standard", "--", "."))
    return sorted(n for n in names if n.endswith(".cs"))


def load_baseline():
    if not os.path.exists(BASELINE):
        return {"files": {}, "excused": {}}
    with open(BASELINE) as fh:
        return json.load(fh)


def save_baseline(b):
    # one entry per line, sorted: parallel tickets touch different lines, so rebases merge cleanly
    def block(name, d, fmt):
        rows = ",\n".join('    %s: %s' % (json.dumps(k), fmt(v)) for k, v in sorted(d.items()))
        return '  "%s": {\n%s\n  }' % (name, rows) if rows else '  "%s": {}' % name
    text = "{\n%s,\n%s\n}\n" % (block("files", b["files"], lambda v: json.dumps(v, sort_keys=True)),
                                block("excused", b["excused"], json.dumps))
    with open(BASELINE, "w") as fh:
        fh.write(text)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--update-baseline", action="store_true")
    ap.add_argument("--run", action="store_true")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("files", nargs="*")
    a = ap.parse_args()
    if not (a.check or a.report or a.update_baseline or a.run):
        ap.error("choose --check, --report, --update-baseline or --run")

    report = load_report(a.run)
    baseline = load_baseline()
    wanted = sorted(report) if a.all else (a.files or changed_files())
    problems, rows = [], []
    for fn in wanted:
        cur = report.get(fn)
        if cur is None:
            if os.path.exists(os.path.join(CSHARP, fn)) and fn.startswith("Unciv.") and ".Tests/" not in fn:
                rows.append((fn, None, None, "no executable lines"))
            continue
        if cur["valid"] == 0:
            continue
        base = baseline["files"].get(fn)
        status = "ok"
        if base is not None:
            if cur["line"] + 1e-9 < base["line"]:
                status = "REGRESSED from %.2f%%" % base["line"]
                problems.append("%s: line coverage %.2f%% < baseline %.2f%%" % (fn, cur["line"], base["line"]))
        elif fn in baseline["excused"]:
            status = "excused: " + baseline["excused"][fn]
        elif cur["line"] < NEW_FILE_MIN:
            status = "NEW below %d%%" % NEW_FILE_MIN
            problems.append("%s: new file at %.2f%% line coverage (< %d%%; add tests or excuse it in %s with a reason)"
                            % (fn, cur["line"], NEW_FILE_MIN, os.path.basename(BASELINE)))
        else:
            status = "new"
        rows.append((fn, cur, base, status))

    if a.check or a.report:
        print("%-72s %7s %7s %7s  %s" % ("file", "line%", "branch%", "base%", "status"))
        for fn, cur, base, status in rows:
            if cur is None:
                print("%-72s %7s %7s %7s  %s" % (fn, "-", "-", "-", status))
                continue
            print("%-72s %7.2f %7.2f %7s  %s" % (fn, cur["line"], cur["branch"], "%.2f" % base["line"] if base else "-", status))
            if a.report and cur["uncovered"]:
                print("    uncovered lines: " + ", ".join(cur["uncovered"]))
        if not rows:
            print("(no changed source files with executable lines)")

    if a.update_baseline:
        # --all regenerates the whole baseline from reality: pre-existing files below 80% are grandfathered at their level
        problems = [p for p in problems if not (a.all and "new file at" in p)]
        if problems:
            print("\n".join(problems))
            sys.exit("coverage: refusing to update the baseline while checks fail")
        for fn, cur, base, _ in rows:
            if cur is not None and (base is None or cur["line"] > base["line"]):
                baseline["files"][fn] = {"line": cur["line"], "branch": cur["branch"]}
        for fn in [f for f in baseline["files"] if not os.path.exists(os.path.join(CSHARP, f))]:
            del baseline["files"][fn]
        save_baseline(baseline)
        print("baseline updated: %d files" % len(baseline["files"]))
        return 0
    if a.check and problems:
        print("\ncoverage check FAILED:\n  " + "\n  ".join(problems))
        return 1
    if a.check:
        print("\ncoverage check passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
