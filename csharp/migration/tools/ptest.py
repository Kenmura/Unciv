#!/usr/bin/env python3
"""Sharded parallel test runner for Unciv.Core.Tests. Run from anywhere: python3 -I tools/ptest.py ...

Why processes, not xunit parallelism: ~190 test classes share the static RulesetCache (plus Log/Translations) and clear and
reload it in their constructors, so xunit has to run them as one serial collection. Instead of untangling the statics, the
unchanged suite is split by test class into N shards and each shard runs in its own `dotnet test` process: serial inside
(statics stay safe), parallel across processes. Shards are balanced from per-class timings (LPT), a final "remainder" shard
runs every class that has no timing yet, so a new test class can never be skipped.

  (no args)            build Unciv.Core.Tests, run every test in N shards
  --no-build           skip the build (the gate has just built)
  --shards N           number of shards (default: min(6, cpu/3); env UNCIV_TEST_SHARDS)
  --coverage           collect cobertura per shard under --results (default migration/.coverage/raw); a FULL run also stores
                       the merged report in the coverage cache so a following `coverage.py --check` is free
  --affected [--since REV]
                       dev loop: only test classes that mention a type declared in a changed .cs file (changed vs REV, default
                       the merge-base with csharp-migration, plus uncommitted/untracked). An approximation, NOT the gate:
                       it follows one level of references. Falls back to the full run when a non-.cs file changed.
  --class SUBSTR ...   only test classes whose full name contains SUBSTR (any of them)
  --list-affected      print the selected classes and exit

Exit 0 only if every selected test passed and at least one test ran.
"""
import argparse
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
MIGRATION = os.path.dirname(HERE)
CSHARP = os.path.dirname(MIGRATION)
TESTS = "Unciv.Core.Tests"
SEED = os.path.join(MIGRATION, "test-timings.json")
CONTAINER = "dotnet-dev"
DEFAULT_RAW = os.path.join(MIGRATION, ".coverage", "raw")
TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
UNKNOWN_WEIGHT = 1.0
MIN_WEIGHT = 0.05


def git(*args):
    return subprocess.run(["git", "-C", CSHARP] + list(args), capture_output=True, text=True).stdout


def local_timings_path():
    common = git("rev-parse", "--path-format=absolute", "--git-common-dir").strip()
    return os.path.join(common, "unciv-ptest-timings.json") if common else None


def load_timings():
    t = {}
    for path in (SEED, local_timings_path()):
        try:
            with open(path) as fh:
                t.update(json.load(fh))
        except (OSError, TypeError, ValueError):
            pass
    return t


def dotnet_cmd(args):
    """argv (or shell string through distrobox) that runs `dotnet <args>` from csharp/."""
    if shutil.which("dotnet"):
        return ["dotnet"] + args, CSHARP
    inner = "cd %s && dotnet %s" % (shlex.quote(CSHARP), " ".join(shlex.quote(a) for a in args))
    return ["distrobox", "enter", CONTAINER, "--", "bash", "-c", inner], None


def default_shards():
    env = os.environ.get("UNCIV_TEST_SHARDS")
    if env and env.isdigit() and int(env) > 0:
        return int(env)
    return max(2, min(6, (os.cpu_count() or 6) // 3))


def partition(classes, weights, n):
    """Longest-processing-time-first. Returns n lists of class names (some may be empty)."""
    shards = [[] for _ in range(n)]
    load = [0.0] * n
    for c in sorted(classes, key=lambda c: (-weights.get(c, UNKNOWN_WEIGHT), c)):
        i = min(range(n), key=lambda k: (load[k], k))
        shards[i].append(c)
        load[i] += max(weights.get(c, UNKNOWN_WEIGHT), MIN_WEIGHT)
    return shards


def class_filter(classes):
    return "|".join("FullyQualifiedName~%s." % c for c in classes)


def remainder_filter(known):
    return "&".join("FullyQualifiedName!~%s." % c for c in known)


# ---- affected-test selection -------------------------------------------------------------------------------------

DECL = re.compile(r"\b(?:class|record|struct|interface|enum)\s+(?:struct\s+|class\s+)?([A-Za-z_]\w*)")
NS = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
TOKEN = re.compile(r"[A-Za-z_]\w*")
# Names so common that a hit says nothing; they would drag in every test.
GENERIC = {"Program", "Tests", "Test", "Helper", "Helpers", "Utils", "Extensions", "Result", "Options", "Config", "State",
           "Type", "Item", "Data", "Context", "Info", "Id", "Name", "Value", "Key", "Node", "Entry", "Kind", "Mode"}


def read(path):
    try:
        with open(path, encoding="utf-8", errors="replace") as fh:
            return fh.read()
    except OSError:
        return ""


def changed_since(since=None):
    base = since or git("merge-base", "HEAD", "csharp-migration").strip() or "csharp-migration"
    names = set(git("diff", "--name-only", "--relative", base, "--", ".").split("\n"))
    names |= set(git("ls-files", "--others", "--exclude-standard", "--", ".").split("\n"))
    return sorted(n for n in names if n)


def test_sources():
    """-> {relative path: (declared types, all identifiers, namespace)} for every .cs under Unciv.Core.Tests."""
    out = {}
    for root, dirs, files in os.walk(os.path.join(CSHARP, TESTS)):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        for f in files:
            if f.endswith(".cs"):
                p = os.path.join(root, f)
                text = read(p)
                ns = NS.search(text)
                out[os.path.relpath(p, CSHARP)] = (set(DECL.findall(text)), set(TOKEN.findall(text)), ns.group(1) if ns else "")
    return out


def test_classes_in(rel):
    """Fully qualified names of the classes declared in a test file that carry tests."""
    text = read(os.path.join(CSHARP, rel))
    ns = NS.search(text)
    if not ns or not re.search(r"\[(Fact|Theory)\b", text):
        return set()
    return {"%s.%s" % (ns.group(1), n) for n in DECL.findall(text)}


def affected_classes(changed):
    """-> (classes or None, reason). None means "everything": a non-.cs file changed or nothing could be mapped."""
    cs = [c for c in changed if c.endswith(".cs")]
    other = [c for c in changed if not c.endswith(".cs") and not c.endswith(".md") and not c.startswith("migration/")]
    if other:
        return None, "non-.cs file changed (%s)" % other[0]
    if not cs:
        return set(), "no changed .cs files"
    types, stems = set(), set()
    for c in cs:
        stems.add(os.path.splitext(os.path.basename(c))[0])
        types |= set(DECL.findall(read(os.path.join(CSHARP, c))))
    types = {t for t in types if t not in GENERIC}
    sources = test_sources()
    selected = set()
    for rel, (_declared, tokens, _ns) in sources.items():
        base = os.path.splitext(os.path.basename(rel))[0]
        hit = rel in cs or tokens & types or any(base == s + "Tests" or base == s + "Test" for s in stems)
        if hit:
            selected |= test_classes_in(rel)
    return selected, "%d changed .cs file(s), %d type name(s)" % (len(cs), len(types))


# ---- running -----------------------------------------------------------------------------------------------------

def parse_trx(path):
    """-> (counts, per-class seconds, failures [(name, message)])."""
    counts = {"passed": 0, "failed": 0, "skipped": 0}
    seconds, failures = {}, []
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError):
        return counts, seconds, failures
    for r in root.iter(TRX_NS + "UnitTestResult"):
        outcome, name = r.get("outcome"), r.get("testName") or "?"
        if outcome == "Passed":
            counts["passed"] += 1
        elif outcome == "Failed":
            counts["failed"] += 1
            msg = r.find(".//" + TRX_NS + "Message")
            failures.append((name, (msg.text or "").strip() if msg is not None else ""))
        else:
            counts["skipped"] += 1
        try:
            h, m, s = (r.get("duration") or "0:0:0").split(":")
            sec = int(h) * 3600 + int(m) * 60 + float(s)
        except ValueError:
            continue
        cls = ".".join(name.split("(")[0].split(".")[:-1])
        if cls:
            seconds[cls] = seconds.get(cls, 0.0) + sec
    return counts, seconds, failures


def output_dir():
    """The newest built bin/<config>/<tfm> folder of the test project."""
    cands = []
    for root, _dirs, files in os.walk(os.path.join(CSHARP, TESTS, "bin")):
        if TESTS + ".dll" in files:
            cands.append(root)
    if not cands:
        sys.exit("ptest: %s has not been built (run without --no-build)" % TESTS)
    return max(cands, key=lambda d: os.path.getmtime(os.path.join(d, TESTS + ".dll")))


def private_copy(index):
    """Coverlet rewrites the assemblies it instruments IN PLACE (and restores them afterwards), so concurrent coverage shards
    must not share the output folder. The copy stays under csharp/ because tests find android/assets/jsons by walking up
    from AppContext.BaseDirectory."""
    dest = os.path.join(MIGRATION, ".coverage", "bin", "shard%d" % index)
    shutil.rmtree(dest, ignore_errors=True)
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    subprocess.run(["cp", "-r", "--reflink=auto", output_dir(), dest], check=True)
    return dest


def run_shard(index, flt, results, coverage):
    d = os.path.join(results, "shard%d" % index)
    shutil.rmtree(d, ignore_errors=True)
    os.makedirs(d)
    args = ["test", TESTS, "--no-build", "--filter", flt, "--results-directory", d,
            "--logger", "trx;LogFileName=shard%d.trx" % index, "--verbosity", "quiet"]
    if coverage:
        # a project run (not a bare dll) keeps the coverlet adapter wiring; OutDir points it at this shard's private copy
        args += ["-p:OutDir=" + private_copy(index) + os.sep]
        args += ["--collect:XPlat Code Coverage", "--",
                 "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura",
                 "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.SingleHit=true"]
    argv, cwd = dotnet_cmd(args)
    log = open(os.path.join(d, "output.log"), "w")
    proc = subprocess.Popen(argv, cwd=cwd, stdout=log, stderr=subprocess.STDOUT)
    return proc, d, log


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--shards", type=int)
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--coverage", action="store_true")
    ap.add_argument("--results", default=DEFAULT_RAW)
    ap.add_argument("--affected", action="store_true")
    ap.add_argument("--since")
    ap.add_argument("--class", dest="classes", nargs="+")
    ap.add_argument("--list-affected", action="store_true")
    a = ap.parse_args()

    timings = load_timings()
    known = sorted(timings)
    selected = None  # None = everything
    if a.affected:
        selected, why = affected_classes(changed_since(a.since))
        print("ptest: affected selection: %s" % why, flush=True)
        if selected is not None and not selected and not a.classes:
            print("ptest: nothing to run")
            return 0
        if selected is None:
            print("ptest: running the full suite", flush=True)
    raw_terms = []
    if a.classes:
        pool = set(known) if selected is None else set(selected)
        selected = {c for c in pool if any(t in c for t in a.classes)}
        # a term that matches no known class may be a brand new class (no timing yet): filter by it directly
        raw_terms = [t for t in a.classes if not any(t in c for c in pool)]
    if a.list_affected:
        print("\n".join(sorted(selected)) if selected is not None else "(all)")
        return 0

    t0 = time.time()
    if not a.no_build:
        argv, cwd = dotnet_cmd(["build", TESTS, "-warnaserror", "-v", "q", "--nologo"])
        r = subprocess.run(argv, cwd=cwd, capture_output=True, text=True)
        if r.returncode:
            print("\n".join((r.stdout + r.stderr).splitlines()[-40:]))
            print("ptest: build failed", file=sys.stderr)
            return 1
        print("ptest: built in %.0fs" % (time.time() - t0), flush=True)

    n = a.shards or default_shards()
    jobs = []  # (label, filter)
    if selected is None:
        parts = [p for p in partition(known, timings, n) if p]
        jobs = [("shard%d" % i, class_filter(p)) for i, p in enumerate(parts)]
        if known:
            jobs.append(("remainder", remainder_filter(known)))
        else:
            jobs = [("all", "FullyQualifiedName~.")]
    else:
        parts = [p for p in partition(sorted(selected), timings, n) if p]
        jobs = [("shard%d" % i, class_filter(p)) for i, p in enumerate(parts)]
        jobs += [("term:" + t, "FullyQualifiedName~" + t) for t in raw_terms]

    results = os.path.abspath(a.results)
    shutil.rmtree(results, ignore_errors=True)
    os.makedirs(results)
    t1 = time.time()
    running = [(label, *run_shard(i, flt, results, a.coverage)) for i, (label, flt) in enumerate(jobs)]
    print("ptest: %d process(es) started" % len(running), flush=True)

    totals = {"passed": 0, "failed": 0, "skipped": 0}
    seconds, failures, bad = {}, [], False
    for i, (label, proc, d, log) in enumerate(running):
        rc = proc.wait()
        log.close()
        counts, secs, fails = parse_trx(os.path.join(d, "shard%d.trx" % i))
        for k in totals:
            totals[k] += counts[k]
        for c, s in secs.items():
            seconds[c] = seconds.get(c, 0.0) + s
        failures += fails
        ran = sum(counts.values())
        print("ptest: %-9s %5d passed %3d failed %3d skipped (exit %d)" % (label, counts["passed"], counts["failed"], counts["skipped"], rc))
        if rc != 0 or (ran == 0 and label != "remainder"):
            if rc != 0 and not fails:
                print("\n".join(open(os.path.join(d, "output.log")).read().splitlines()[-25:]))
            bad = bad or rc != 0 or ran == 0

    for name, msg in failures[:30]:
        print("FAILED %s\n    %s" % (name, "\n    ".join(msg.splitlines()[:6])))
    if len(failures) > 30:
        print("... and %d more failures" % (len(failures) - 30))

    # remember what the classes cost, for the next balance (only from runs that cover them for real)
    path = local_timings_path()
    if path and seconds and not failures:
        merged = dict(timings)
        merged.update({c: round(s, 2) for c, s in seconds.items()})
        try:
            with open(path, "w") as fh:
                json.dump(merged, fh, sort_keys=True)
        except OSError:
            pass

    ran_total = totals["passed"] + totals["failed"]
    print("ptest: %s - %d passed, %d failed, %d skipped in %.0fs (%d process(es))" % (
        "FAILED" if bad or totals["failed"] or not ran_total else "ok",
        totals["passed"], totals["failed"], totals["skipped"], time.time() - t1, len(running)))
    if bad or totals["failed"] or not ran_total:
        return 1
    if a.coverage and selected is None and not a.classes:
        # a full instrumented run is the authoritative coverage report: hand it to the cache
        r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "coverage.py"), "--ingest", results])
        if r.returncode:
            return r.returncode
    return 0


if __name__ == "__main__":
    sys.exit(main())
