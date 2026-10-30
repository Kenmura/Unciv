#!/usr/bin/env python3
"""Show what dotnet processes use memory, per worktree, and (on request) kill the dead ones (T360).

    python3 -I tools/reap.py                 list mode (default): totals, one row per worktree and process kind, the verdict per process
    python3 -I tools/reap.py --kill          kill every process the list marks KILL (SIGTERM, then SIGKILL after 5 s)
    python3 -I tools/reap.py --min-age 15    idle threshold in minutes (default 30)

Needs no container: the dotnet-dev processes are visible in the host's /proc. A process belongs to a worktree when its command
line names `.worktrees/<name>`, else when its environment (PWD / OLDPWD, inherited from the `podman exec` that started it) or its
working directory does; MSBuild nodes and VBCSCompiler are parented to conmon, so the environment is the only way to attribute them.

Safety rule. Only these kinds are ever killed: MSBuild worker nodes, VBCSCompiler, test hosts (`dotnet exec ... .runtimeconfig.json`),
datacollector, vstest.console, and `dotnet build|test|format|publish` drivers. gate.py / ptest.py / podman exec are only listed. A
process is killed when
  * its worktree directory no longer exists (reason `worktree-gone`), or
  * it is older than --min-age AND used no CPU during the sampling window (--sample seconds, default 3) AND its worktree is not
    busy. A worktree is busy when any process attributed to it is younger than --min-age or used CPU in the window, i.e. a gate,
    ptest or build is running. VBCSCompiler is shared by all worktrees and is killed only when no worktree at all is busy.
A live gate/ptest/build is therefore never touched: it keeps at least one young or running process in its worktree.
Exit status 0; with --kill the number of processes that survived is printed.
"""
import argparse
import os
import re
import signal
import sys
import time

KILLABLE = {"msbuild-node", "vbcscompiler", "testhost", "datacollector", "vstest", "dotnet-cli", "msbuild-main"}
WT_RE = re.compile(r"(/\.worktrees/[^/\s\0]+|/Unciv-wt-[^/\s\0]+)")
TICKS = os.sysconf("SC_CLK_TCK")
PAGE = os.sysconf("SC_PAGE_SIZE")


def classify(cmd):
    """Kind of a process from its command line (a list of argv strings)."""
    line = " ".join(cmd)
    if "MSBuild.dll" in line:
        return "msbuild-node" if "/nodemode" in line else "msbuild-main"
    if "VBCSCompiler" in line:
        return "vbcscompiler"
    if "datacollector.dll" in line:
        return "datacollector"
    if "vstest.console.dll" in line:
        return "vstest"
    if "testhost" in line or (len(cmd) > 2 and cmd[1] == "exec" and "--runtimeconfig" in cmd and "datacollector" not in line):
        return "testhost"
    exe = os.path.basename(cmd[0]) if cmd else ""
    if exe == "dotnet" and len(cmd) > 1 and cmd[1] in ("build", "test", "format", "publish", "restore", "pack", "build-server"):
        return "dotnet-cli"
    if exe in ("podman", "distrobox", "distrobox-enter") or (exe.startswith("python") and any(a.endswith(("gate.py", "ptest.py", "coverage.py", "release.py")) for a in cmd)):
        return "driver"
    return "other"


def worktree_of(*texts):
    """First `.worktrees/<name>` (or Unciv-wt-<name>) directory named by any of `texts`, else None."""
    for t in texts:
        m = WT_RE.search(t or "")
        if m:
            i = t.find(m.group(1))
            return t[:i] + m.group(1) if t[:i].startswith("/") else m.group(1)
    return None


def read(path):
    try:
        with open(path, "rb") as fh:
            return fh.read()
    except OSError:
        return b""


def scan():
    """One snapshot: pid -> dict(pid, ppid, kind, rss_mb, age_s, cpu_ticks, worktree, cmd) for every dotnet-related process."""
    out = {}
    try:
        with open("/proc/uptime") as fh:
            uptime = float(fh.read().split()[0])
    except OSError:
        uptime = 0.0
    for name in os.listdir("/proc"):
        if not name.isdigit():
            continue
        pid = int(name)
        raw = read("/proc/%d/cmdline" % pid)
        if not raw:
            continue
        cmd = raw.decode(errors="replace").split("\0")[:-1] or [""]
        kind = classify(cmd)
        if kind == "other":
            continue
        stat = read("/proc/%d/stat" % pid).decode(errors="replace")
        rest = stat[stat.rfind(")") + 2:].split()  # fields from `state` on
        if len(rest) < 22:
            continue
        env = {}
        for item in read("/proc/%d/environ" % pid).decode(errors="replace").split("\0"):
            k, _, v = item.partition("=")
            if k in ("PWD", "OLDPWD"):
                env[k] = v
        try:
            cwd = os.readlink("/proc/%d/cwd" % pid)
        except OSError:
            cwd = ""
        wt = worktree_of(" ".join(cmd), env.get("PWD"), env.get("OLDPWD"), cwd)
        rss = read("/proc/%d/statm" % pid).split()
        out[pid] = {"pid": pid, "ppid": int(rest[1]), "kind": kind, "cmd": " ".join(cmd),
                    "rss_mb": int(rss[1]) * PAGE / 1048576 if len(rss) > 1 else 0.0,
                    "age_s": max(0.0, uptime - int(rest[19]) / TICKS),
                    "cpu": int(rest[11]) + int(rest[12]), "worktree": wt}
    return out


def decide(before, after, min_age_s, exists=os.path.isdir):
    """pid -> (verdict, reason) for the processes seen in both snapshots. Pure: tests drive it with fabricated snapshots."""
    active = {pid: after[pid]["cpu"] != before[pid]["cpu"] for pid in after if pid in before}
    busy = set()  # worktrees with a young or running process
    any_busy = False
    for pid, p in after.items():
        if p["age_s"] < min_age_s or active.get(pid, True):
            busy.add(p["worktree"])
            any_busy = True
    verdicts = {}
    for pid, p in after.items():
        wt = p["worktree"]
        if p["kind"] not in KILLABLE:
            verdicts[pid] = ("keep", "driver/other, never killed")
        elif wt and not exists(wt):
            verdicts[pid] = ("KILL", "worktree-gone")
        elif p["age_s"] < min_age_s:
            verdicts[pid] = ("keep", "younger than threshold")
        elif active.get(pid, True):
            verdicts[pid] = ("keep", "using CPU")
        elif p["kind"] == "vbcscompiler":
            verdicts[pid] = ("keep", "other worktrees busy") if any_busy else ("KILL", "idle, nothing building")
        elif wt in busy:
            verdicts[pid] = ("keep", "worktree busy")
        else:
            verdicts[pid] = ("KILL", "idle and worktree not busy")
    return verdicts


def mem_summary():
    info = {}
    for line in read("/proc/meminfo").decode().splitlines():
        k, _, v = line.partition(":")
        info[k] = int(v.split()[0]) // 1024 if v.split() else 0
    return info


def fmt_age(s):
    return "%dd%02dh" % (s // 86400, s % 86400 // 3600) if s >= 86400 else "%dh%02dm" % (s // 3600, s % 3600 // 60)


def report(procs, verdicts, verbose):
    mem = mem_summary()
    total = sum(p["rss_mb"] for p in procs.values())
    print("memory: %d MB total, %d MB available, swap used %d MB; dotnet-related RSS %.0f MB in %d processes" % (
        mem.get("MemTotal", 0), mem.get("MemAvailable", 0), mem.get("SwapTotal", 0) - mem.get("SwapFree", 0), total, len(procs)))
    groups = {}
    for pid, p in procs.items():
        g = groups.setdefault(p["worktree"] or "(unattributed)", {})
        k = g.setdefault(p["kind"], [0, 0.0, 0.0, 0])
        k[0] += 1
        k[1] += p["rss_mb"]
        k[2] = max(k[2], p["age_s"])
        k[3] += verdicts.get(pid, ("keep", ""))[0] == "KILL"
    print("\n%-34s %-13s %5s %8s %8s %5s" % ("worktree", "kind", "procs", "RSS MB", "oldest", "KILL"))
    for wt in sorted(groups, key=lambda w: -sum(k[1] for k in groups[w].values())):
        for kind, (n, rss, age, kill) in sorted(groups[wt].items(), key=lambda kv: -kv[1][1]):
            print("%-34s %-13s %5d %8.0f %8s %5d" % (os.path.basename(wt), kind, n, rss, fmt_age(age), kill))
    if verbose:
        print()
        for pid, p in sorted(procs.items(), key=lambda kv: -kv[1]["rss_mb"]):
            v, why = verdicts.get(pid, ("keep", ""))
            print("%-4s %7d %-13s %6.0f MB %7s %-24s %s | %s" % (v, pid, p["kind"], p["rss_mb"], fmt_age(p["age_s"]),
                  os.path.basename(p["worktree"] or "-"), why, p["cmd"][:90]))
    kills = [pid for pid, v in verdicts.items() if v[0] == "KILL"]
    print("\n%d process(es) marked KILL, %.0f MB" % (len(kills), sum(procs[p]["rss_mb"] for p in kills)))
    return kills


def kill(pids):
    for sig, wait in ((signal.SIGTERM, 5), (signal.SIGKILL, 2)):
        for pid in pids:
            try:
                os.kill(pid, sig)
            except OSError:
                pass
        end = time.time() + wait
        while time.time() < end and any(os.path.exists("/proc/%d" % p) for p in pids):
            time.sleep(0.2)
        pids = [p for p in pids if os.path.exists("/proc/%d" % p)]
        if not pids:
            break
    return pids


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--kill", action="store_true", help="kill the processes marked KILL (default: only list)")
    ap.add_argument("--min-age", type=float, default=30.0, metavar="MIN", help="idle threshold in minutes (default 30)")
    ap.add_argument("--sample", type=float, default=3.0, metavar="SEC", help="CPU sampling window in seconds (default 3)")
    ap.add_argument("-q", "--quiet", action="store_true", help="per-worktree table only, no per-process lines")
    a = ap.parse_args()
    before = scan()
    time.sleep(a.sample)
    after = scan()
    verdicts = decide(before, after, a.min_age * 60)
    kills = report(after, verdicts, not a.quiet)
    if a.kill and kills:
        left = kill(kills)
        print("killed %d, %d survived" % (len(kills) - len(left), len(left)))
        freed = mem_summary()
        print("memory now: %d MB available" % freed.get("MemAvailable", 0))
    elif kills:
        print("(list mode: nothing killed; add --kill to reap them)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
