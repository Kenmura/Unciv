#!/usr/bin/env python3
"""Line/branch coverage per source file, with a ratchet baseline (T122). Run: python3 -I tools/coverage.py ...

Cost model (faster-tests): the instrumented run is tools/ptest.py --coverage (the suite split across processes). A FULL
instrumented run, by this script or by the Done gate's `ptest.py --coverage`, is cached under the git common dir keyed by a
hash of the file CONTENTS that can change a result (so every worktree shares it and a rebase that does not change those
files costs nothing; a plain mtime bump does not invalidate it). --check first tries the cheap path: it instruments only the
test classes that mention the changed types (ptest.py --affected); a subset can only under-report coverage, so if every
changed file already meets its baseline there the full run is skipped, otherwise it escalates to the full run. --report and
--update-baseline always use the full report (a partial one would show false uncovered ranges). Runs through distrobox
`dotnet-dev` automatically when `dotnet` is not on PATH.

  --check            Gate. Exit 1 if, for any checked file, line coverage fell below its baseline, or a file with no
                     baseline entry has < 80% line coverage and is not excused. Prints the per-file table.
  --report           Same table for the checked files plus uncovered line ranges; never fails (for the Review step).
  --update-baseline  Raise/add baseline entries for the checked files (never lowers one; a lowered file must be
                     fixed with tests, or excused). With --all: regenerate EVERY entry from the current tree, lowering
                     included (only for fixing a stale baseline, with a ticket for the lost coverage).
                     Writes migration/coverage-baseline.json.
  --run              Force a fresh FULL coverage run even if the cached report is current.
  --ingest DIR       Store the cobertura reports under DIR (from a full `ptest.py --coverage` run) as the cached report for
                     the current tree, then continue with the other options (or just exit).
  --full             With --check: skip the affected-tests shortcut and use the full report.
  --all              Check every file instead of only the files this branch changed (vs csharp-migration, plus
                     uncommitted/untracked). Use with --update-baseline once to (re)generate the whole baseline.
  --since REV        Changed set relative to REV instead of csharp-migration (reviewers use the implementation's parent).
  FILE...            Explicit files (relative to csharp/) to check instead of the changed set.

--update-baseline --all grandfathers every existing file at its current coverage; afterwards only files born later
face the 80% rule.

Excusing a new file below 80% (declarations only, platform glue, ...): add it to "excused" in the baseline file with a
reason naming a ticket; files with no executable lines are skipped automatically.
"""
import argparse
import glob
import hashlib
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
PTEST = os.path.join(HERE, "ptest.py")
KEY_SUFFIXES = (".cs", ".csproj", ".props", ".targets", ".editorconfig", ".sln", ".json", ".tsv", ".txt", ".map")
BASELINE = os.path.join(MIGRATION, "coverage-baseline.json")
NEW_FILE_MIN = 80.0
CONTAINER = "dotnet-dev"


def run_ptest(extra, results):
    """Run tools/ptest.py --coverage with EXTRA args; returns the cobertura files it left under RESULTS or exits on test failure."""
    proc = subprocess.run([sys.executable, "-I", PTEST, "--coverage", "--results", results] + extra, capture_output=True, text=True)
    sys.stdout.write("\n".join(l for l in proc.stdout.splitlines() if l.startswith("ptest:")) + "\n")
    files = glob.glob(os.path.join(results, "**", "coverage.cobertura.xml"), recursive=True)
    if proc.returncode != 0 or not files:
        print("\n".join((proc.stdout + proc.stderr).splitlines()[-40:]))
        sys.exit("coverage: the instrumented test run failed (fix the tests; see output above)")
    return files


def shlex_quote(s):
    return "'" + s.replace("'", "'\\''") + "'"


def is_generated(fn):
    """Compiler/source-generator output (RegexGenerator.g.cs under obj/, *.g.cs): not our source, so it has no
    baseline and no 80% floor. The hand-written code that declares a [GeneratedRegex] is still measured."""
    return "/obj/" in fn or fn.endswith(".g.cs")


def parse(paths):
    """-> {file: {"lines": {n: hits}, "branches": {n: (covered, total)}}} merged across classes/methods and across the
    per-shard reports (a line counts as hit when any shard hit it)."""
    out = {}
    for path in ([paths] if isinstance(paths, str) else paths):
        for cls in ET.parse(path).getroot().iter("class"):
            fn = cls.get("filename").replace("\\", "/")
            if ".Tests/" in fn or fn.startswith("/") or is_generated(fn):
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


def git(*args):
    return subprocess.run(["git", "-C", CSHARP] + list(args), capture_output=True, text=True).stdout.split("\n")


def cache_dir():
    """Shared by every worktree of the repo (the git common dir); falls back to .coverage/ outside a git checkout."""
    common = git("rev-parse", "--path-format=absolute", "--git-common-dir")[0].strip()
    d = os.path.join(common, "unciv-cache") if common else CACHE
    os.makedirs(d, exist_ok=True)
    return d


def source_key():
    """Hash of the CONTENT of every file that can change a coverage result: engine and test sources, project files, test data
    under csharp/ (not migration/, which holds tickets and tooling) and the rulesets the tests read."""
    h = hashlib.sha256()
    names = [n for n in git("ls-files", "-co", "--exclude-standard", "--", ".", "../android/assets/jsons") if n]
    for n in sorted(set(names)):
        if n.startswith("migration/") or not n.endswith(KEY_SUFFIXES):
            continue
        try:
            with open(os.path.join(CSHARP, n), "rb") as fh:
                data = fh.read()
        except OSError:
            continue  # deleted in the worktree but still in the index
        h.update(n.encode() + b"\0" + hashlib.sha256(data).digest())
    return h.hexdigest()[:24]


def cached_path(key):
    return os.path.join(cache_dir(), "coverage-%s.json" % key)


def load_cached():
    path = cached_path(source_key())
    if os.path.exists(path):
        with open(path) as fh:
            return json.load(fh)
    return None


def store(data):
    path = cached_path(source_key())
    tmp = path + ".%d.tmp" % os.getpid()
    with open(tmp, "w") as fh:
        json.dump(data, fh)
    os.replace(tmp, path)  # atomic: two worktrees may store the same key at once
    olds = sorted(glob.glob(os.path.join(os.path.dirname(path), "coverage-*.json")), key=os.path.getmtime, reverse=True)
    for old in olds[20:]:
        try:
            os.remove(old)
        except OSError:
            pass


def load_report(force):
    if not force:
        data = load_cached()
        if data is not None:
            return data
    files = run_ptest([], os.path.join(CACHE, "raw"))  # a full run ingests itself (ptest -> --ingest)
    data = load_cached()
    if data is None:
        data = parse(files)
        store(data)
    return data


def try_affected(since):
    """The cheap path of --check: instrument only the affected test classes. Returns a PARTIAL report, or None when ptest decided
    to run everything (it then ingested the full report; call load_cached())."""
    raw = os.path.join(CACHE, "raw-affected")
    args = ["--affected"] + (["--since", since] if since else [])
    files = run_ptest(args, raw)
    return parse(files)


def changed_files(since=None):
    base = since or git("merge-base", "HEAD", "csharp-migration")[0].strip() or "csharp-migration"
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
    ap.add_argument("--ingest", metavar="DIR")
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--since", metavar="REV", help="changed set = files differing from REV (reviewers: the commit before the "
                    "implementation, e.g. 'abc123^', since the rebased branch already contains it)")
    ap.add_argument("files", nargs="*")
    a = ap.parse_args()
    if a.ingest:
        files = glob.glob(os.path.join(a.ingest, "**", "coverage.cobertura.xml"), recursive=True)
        if not files:
            sys.exit("coverage: no coverage.cobertura.xml under " + a.ingest)
        store(parse(files))
        if not (a.check or a.report or a.update_baseline):
            print("coverage: stored the full report for this tree")
            return 0
    elif not (a.check or a.report or a.update_baseline or a.run):
        ap.error("choose --check, --report, --update-baseline, --run or --ingest")

    for f in a.files:
        if not os.path.exists(os.path.join(CSHARP, f)):
            sys.exit("coverage: no such file (path relative to csharp/): " + f)
    wanted = None if a.all else (a.files or changed_files(a.since))
    if wanted is not None and not wanted and not a.run:
        print("(no changed .cs files: skipping the coverage run; use --all or --since REV)")
        return 0
    baseline = load_baseline()
    report = None if a.run else load_cached()
    if report is None and a.check and not (a.report or a.update_baseline or a.run or a.full or a.all or a.files):
        partial = try_affected(a.since)
        if not evaluate(partial, wanted, baseline)[1]:
            print("(coverage from the affected test classes only already meets every baseline; the full instrumented run is skipped)")
            report = partial
        else:
            print("(the affected test classes alone fall short of a baseline: running the full suite instrumented)")
            report = load_cached()  # ptest may already have run everything
    if report is None:
        report = load_report(a.run)
    if wanted is None:
        wanted = sorted(report)
    rows, problems = evaluate(report, wanted, baseline)

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
    return finish(a, rows, problems, baseline)


def evaluate(report, wanted, baseline):
    """-> (rows, problems) of the ratchet for WANTED files (None = every file of the report)."""
    if wanted is None:
        wanted = sorted(report)
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

    return rows, problems


def finish(a, rows, problems, baseline):
    if a.update_baseline:
        # --all regenerates the whole baseline from reality: pre-existing files below 80% are grandfathered at their level
        if a.all:
            problems = []
        if problems:
            print("\n".join(problems))
            sys.exit("coverage: refusing to update the baseline while checks fail")
        for fn, cur, base, _ in rows:
            if cur is not None and (a.all or base is None or cur["line"] > base["line"]):
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
