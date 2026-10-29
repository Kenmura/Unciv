#!/usr/bin/env python3
"""The only way agents read or change the migration backlog (backlog/ holds one file per ticket, backlog/T001.md ...;
hand edits have clobbered neighbouring tickets). Run from anywhere: python3 -I tools/ticket.py <command>.

READ
  list [STATE]              one line per open ticket: id, state, deps, title
  show T027 [Field]         whole ticket (backlog/T027.md) or one field
  ready                     everything workable now: Needs-Review first, then Available with deps all Done
                            ordered by delivery phase first (phases.txt: 1 CLI, 2 single player, 3 multiplayer),
                            most downstream-unblocking first (direct + transitive incomplete dependents)
  next [AGENT]              the single pick of the Work Loop (review, else the ready ticket that unblocks most)
  blockers T033             for each dependency not Done: its id, state and title
  stale                     In-Progress/In-Review claims older than 3 hours
  check                     validate the backlog (states, deps exist, no cycles, unique ids); exit 1 on errors

WRITE (each touches only the named ticket's block, verified before the file is written)
  claim T033 AGENT [--no-publish]
                            Available -> In-Progress [AGENT], stamps Claimed, commits ONLY backlog/T033.md
                            (+ DEPENDENCY_MAP.md),
                            rebases onto csharp-migration, fast-forwards it. Exit 2 = lost the race.
  state T033 STATE [-m MSG] [--all] [--publish] [--origin] [--no-release]
                            STATE: Needs-Review AGENT | In-Review AGENT | Done | Available | Blocked | "Won't port".
                            Legal transitions only. Commits ONLY backlog/T033.md + DEPENDENCY_MAP.md (--all: every change in the worktree, e.g. your
                            deliverables, in the same commit); --publish also rebases and
                            fast-forwards csharp-migration; --origin also pushes it to origin (use on Done).
                            Done is refused unless the rebased tip is green (T163, PROTOCOL 'Green before Done'): the tree must
                            be clean and contain csharp-migration, `dotnet build -warnaserror && dotnet format
                            --verify-no-changes && dotnet test` and `coverage.py --check` must pass (re-run unless `green`
                            already stamped this exact tree), and no TODO(T033) marker may remain outside the backlog files.
                            --no-green REASON skips the build/test/coverage run for backlog-only/docs tickets (Deliverables
                            with no .cs/.csproj/.props/.editorconfig/.json/.py/.txt files); REASON is recorded in Notes.
                            After a Done, tools/release.py builds the CLI and Linux client into <repo>/csharp-releases/ (git-ignored;
                            failure only warns; --no-release skips it).
  green                     run (and stamp) the Green-before-Done checks on the current tree without changing any ticket
  reset T033 [-m REASON]    abandoned/stale claim -> Available, clears Claimed, notes the reason
  note T033 TEXT|-          append an indented bullet to Notes (- reads stdin, one bullet per call)
  set T033 FIELD VALUE|-    FIELD: Depends Source Fixtures Deliverables Gate Scope Title (not State, not Notes)
  add --title T [--depends "T1, T2"] [--source S] [--fixtures F] [--deliverables D] [--gate G]
      [--notes N] [--prefix T|C|A]
                            new Available ticket with the next free id, appended to its track; prints the id
  section [TEXT]            print non-ticket prose: every heading-delimited chunk (no ticket header) whose heading
                            contains TEXT (case-insens.), e.g. section Planned / section Non-goals; no TEXT lists headings
  planned-clear TEXT        delete the heading-delimited non-ticket chunk whose heading line contains TEXT
  drop T###                 delete a mistakenly added ticket: only if it is Available, unclaimed, depended on by nobody
                            and the newest ticket of its prefix (ids stay dense)
  graph                     regenerate DEPENDENCY_MAP.md, with its remaining-tickets-over-time chart (also done automatically by every write command)
  renumber OLD NEW TITLE_SUBSTR [--refs T1,T2]
                            fix a duplicate id (parallel `add` calls): rename that block, update the id in --refs tickets
  publish [--origin]        git rebase csharp-migration && git push . HEAD:csharp-migration (clean tree only)
  archive                   retired no-op (Done tickets stay in their own backlog/T###.md file)
"""
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

HERE = Path(os.environ.get("TICKET_MIGRATION_DIR") or Path(__file__).resolve().parent.parent)
BACKLOG_DIR = HERE / "backlog"
README = BACKLOG_DIR / "README.md"
LEGACY = HERE / "backlog.md"  # the pre-split single file; must not exist
TICKET_FILE = re.compile(r"^([A-Z])(\d+)\.md$")
DEPMAP = HERE / "DEPENDENCY_MAP.md"
HEADER = re.compile(r"^### ([A-Z]\d+)\b(.*)$")
STATE_RE = re.compile(r"^(Available|Blocked|Done|Won't port|(In-Progress|Needs-Review|In-Review) \[[^\]]+\])( \(.*\))?$")
SETTABLE = {"Depends", "Source", "Fixtures", "Deliverables", "Gate", "Scope"}
STALE = timedelta(hours=3)
BRANCH = "csharp-migration"
LEGAL = {  # kind of current state -> kinds it may move to
    "Available": {"In-Progress", "Blocked", "Won't"},
    "In-Progress": {"Needs-Review", "Available", "Blocked"},
    "Needs-Review": {"In-Review", "Done", "Available", "Blocked"},
    "In-Review": {"Done", "Available", "Blocked", "Needs-Review"},
    "Blocked": {"Available", "Won't"},
    "Done": set(),
    "Won't": set(),
}


def die(msg, code=1):
    print(msg, file=sys.stderr)
    sys.exit(code)


def kind(state):
    return state.split(" ")[0].split("[")[0] if state else ""


def blocks(path):
    """Yield (id, title, lines) for every ticket block; a block ends at the next '#'/'##'/'###' heading."""
    if not path.exists():
        return
    cur = None
    for line in path.read_text(encoding="utf-8").splitlines(keepends=True):
        m = HEADER.match(line)
        if m or line.startswith("#"):
            if cur:
                yield cur
            cur = (m.group(1), m.group(2).strip(), [line]) if m else None
        elif cur:
            cur[2].append(line)
    if cur:
        yield cur


def field(lines, name):
    for line in lines:
        m = re.match(rf"- {name}:\s*(.*)$", line)
        if m:
            return m.group(1).strip()
    return ""


_cache = []


def ticket_path(tid):
    return BACKLOG_DIR / f"{tid}.md"


def ticket_files():
    """Ticket files in backlog order: track (T, C, A, others), then number."""
    files = [p for p in BACKLOG_DIR.glob("*.md") if TICKET_FILE.match(p.name)] if BACKLOG_DIR.is_dir() else []
    rank = {k: n for n, k in enumerate(TRACKS)}
    return sorted(files, key=lambda p: (rank.get(p.name[0], 99), p.name[0], int(p.name[1:-3])))


def tickets():
    if not _cache:
        rows = []
        for p in ticket_files():
            for i, t, b in blocks(p):
                rows.append((i, t, field(b, "State"), field(b, "Depends"), b))
        _cache.append(rows)
    return _cache[0]


def deps_of(text):
    return re.findall(r"\b[A-Z]\d{3}\b", text)


def all_states():
    """id -> state for every ticket."""
    return {i: s for i, _, s, _, _ in tickets()}


def downstream_counts(ts):
    """id -> number of incomplete tickets that depend on it, directly or through other tickets."""
    open_deps = {i: set(deps_of(d)) for i, _, s, d, _ in ts if not s.startswith(("Done", "Won't"))}
    children = {}
    for i, ds in open_deps.items():
        for d in ds:
            children.setdefault(d, set()).add(i)
    counts = {}
    for i in open_deps:
        seen, stack = set(), [i]
        while stack:
            for c in children.get(stack.pop(), ()):
                if c not in seen:
                    seen.add(c)
                    stack.append(c)
        counts[i] = len(seen)
    return counts


def phase_of(tid):
    """Delivery phase from phases.txt (1 CLI/engine, 2 single player, 3 multiplayer); default by track."""
    explicit = {}
    path = HERE / "phases.txt"
    for line in path.read_text(encoding="utf-8").splitlines() if path.exists() else []:
        line = line.split("#")[0].strip()
        if ":" not in line:
            continue
        ph, ids = line.split(":", 1)
        for tok in ids.split():
            lo, _, hi = tok.partition("-")
            hi = hi or lo
            if lo[0] == hi[0] == tid[0] and int(lo[1:]) <= int(tid[1:]) <= int(hi[1:]):
                explicit[tid] = int(ph)
    return explicit.get(tid, 2 if tid[0] == "C" else 1)


def ready_list(ts):
    """Reviews first, then workable tickets ordered by delivery phase (phases.txt: earlier phases first, later phases
    only fill the gaps), then by how many incomplete tickets they unblock (direct and downstream), ties by id.
    Rows: (kind, id, title, state, downstream_count)."""
    done = {i for i, s in all_states().items() if s.startswith("Done")}
    counts = downstream_counts(ts)
    out = [("REVIEW", i, t, s, counts.get(i, 0)) for i, t, s, _, _ in ts if s.startswith("Needs-Review")]
    impl = []
    for i, t, s, d, _ in ts:
        deps = deps_of(d)
        if s == "Available" and set(deps) <= done and (deps or d.lower().startswith("none")):
            impl.append(("IMPLEMENT", i, t, s, counts.get(i, 0)))
    return out + sorted(impl, key=lambda r: (phase_of(r[1]), -r[4], r[1]))


# ---------------------------------------------------------------- mutation core

def locate(lines, tid):
    start = next((n for n, l in enumerate(lines) if l.startswith(f"### {tid} ") or l.rstrip() == f"### {tid}"), None)
    if start is None:
        die(f"{tid} not found in its backlog file")
    end = next((n for n in range(start + 1, len(lines)) if lines[n].startswith("#")), len(lines))
    return start, end


def write_file(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(".md.tmp")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, path)
    _cache.clear()


def write_backlog(path, lines):
    write_file(path, "\n".join(lines))
    refresh_map()


TRACKS = {"T": "Engine track", "C": "Client track (Godot)", "A": "Game AI track"}
STYLES = {  # kind -> (class name, mermaid style)
    "Available": ("waiting", "fill:#f3faf3,stroke:#9bc49b,color:#555,stroke-dasharray:2 2"),
    "OnDeck": ("ondeck", "fill:#ffe8cc,stroke:#e8590c,stroke-width:3px,color:#000"),
    "Ready": ("ready", "fill:#7ee787,stroke:#1a7f37,stroke-width:3px,color:#000"),
    "In-Progress": ("prog", "fill:#fff3b0,stroke:#b58900,color:#000"),
    "Needs-Review": ("review", "fill:#cfe4ff,stroke:#1f6feb,color:#000"),
    "In-Review": ("review", "fill:#cfe4ff,stroke:#1f6feb,color:#000"),
    "Blocked": ("blocked", "fill:#ffd6d6,stroke:#c62828,color:#000"),
    "Done": ("done", "fill:#eeeeee,stroke:#999999,color:#666,stroke-dasharray:4 3"),
    "Won't": ("done", "fill:#eeeeee,stroke:#999999,color:#666,stroke-dasharray:4 3"),
}


STATE_LINE = re.compile(r"^(?:### ([A-Z]\d+)\b|- State:[ \t]*(\S*))", re.M)
CHART_POINTS = 16
NICE_STEPS = [3600, 7200, 10800, 21600, 43200, 86400, 172800, 604800]
NEW_COLOUR, BLOCKED_COLOUR, ACTIVE_COLOUR = "#0969da", "#cf222e", "#d29922"
DONE_COLOUR, TOTAL_COLOUR = "#1a7f37", "#8c959f"


def count_remaining(text):
    """(new, blocked, active, done) ticket counts in one version of a backlog file (or of the old single backlog.md).

    new = Available (not started); blocked = Blocked; active = In-Progress, Needs-Review or In-Review; done = Done or Won't port.
    """
    new = blocked = active = done = 0
    seen = None
    for m in STATE_LINE.finditer(text):
        if m.group(1):
            seen = m.group(1)
        elif seen:
            seen = None
            k = kind(m.group(2))
            if k == "Available":
                new += 1
            elif k == "Blocked":
                blocked += 1
            elif k in ("In-Progress", "Needs-Review", "In-Review"):
                active += 1
            elif k in ("Done", "Won't"):
                done += 1
    return new, blocked, active, done


def backlog_history():
    """[(unix time, new, blocked, active, done)] oldest first: every commit that touched the backlog (the old single backlog.md,
    then the backlog/ folder), plus the live state if it differs."""
    log = git("log", "--reverse", "--no-renames", "--no-abbrev", "--relative", "--raw", "--format=%x01%H %at",
              "--", "backlog.md", "backlog", check=False)
    if log.returncode:
        return []
    commits, shas = [], set()
    for line in log.stdout.splitlines():
        if line.startswith("\x01"):
            commits.append((int(line.split()[1]), []))
        elif line.startswith(":") and commits:
            meta, _, path = line.partition("\t")
            m = meta.split()
            sha = None if m[4].startswith("D") else m[3]
            if path == "backlog.md" or (path.startswith("backlog/") and path.endswith(".md")):
                commits[-1][1].append((path, sha))
                if sha:
                    shas.add(sha)
    if not commits:
        return []
    r = subprocess.run(["git", "-C", str(HERE), "cat-file", "--batch"], capture_output=True,
                       input="".join(f"{h}\n" for h in sorted(shas)).encode())
    if r.returncode:
        return []
    out, pos, counts = r.stdout, 0, {}
    for h in sorted(shas):
        nl = out.index(b"\n", pos)
        head = out[pos:nl].split()
        if len(head) != 3:
            pos = nl + 1
            continue
        size = int(head[2])
        counts[h] = count_remaining(out[nl + 1:nl + 1 + size].decode("utf-8", "replace"))
        pos = nl + 1 + size + 1
    legacy, folder, points = None, {}, []
    for at, changes in commits:
        for path, sha in changes:
            c = counts.get(sha) if sha else None
            if path == "backlog.md":
                legacy = c
            else:
                folder[path] = c
        cur = legacy or tuple(sum(c[n] for c in folder.values() if c) for n in range(4))
        points.append((max(at, points[-1][0] if points else 0), *cur))
    live = tuple(sum(1 for _, _, s, _, _ in tickets() if kind(s) in ks) for ks in
                 (("Available",), ("Blocked",), ("In-Progress", "Needs-Review", "In-Review"), ("Done", "Won't")))
    if points and live != points[-1][1:]:
        points.append((max(int(datetime.now(timezone.utc).timestamp()), points[-1][0]), *live))
    return points


def render_chart():
    """Two mermaid xycharts built from the git history of the backlog: remaining tickets by state, and cumulative Done vs total scope."""
    pts = backlog_history()
    if len(pts) < 2:
        return []
    t0, t1 = pts[0][0], pts[-1][0]
    # Bin edges sit on round clock times (a "nice" step giving at most CHART_POINTS bins), starting at or before the first commit.
    step = next((x for x in NICE_STEPS if (t1 - t0) / x <= CHART_POINTS), NICE_STEPS[-1])
    edges = list(range(t0 // step * step, (t1 // step + 1) * step + 1, step))
    last = lambda e: ([p for p in pts if p[0] <= e] or pts[:1])[-1][1:]  # last known counts at each edge
    bins = [(e, *last(e), sum(last(e))) for e in edges]  # (edge, new, blocked, active, done, total)
    labels = ", ".join('"' + datetime.fromtimestamp(t, timezone.utc).strftime("%b %d %H:%M") + '"' for t, *_ in bins)
    series = lambda n: ", ".join(str(b[n]) for b in bins)

    def chart(title, colours, lines):
        top = max(max(b[n] for b in bins) for n in lines)
        return ["```mermaid",
                '%%{init: {"themeVariables": {"xyChart": {"plotColorPalette": "' + ", ".join(colours) + '"}}}}%%',
                "xychart-beta", f'    title "{title}"', f"    x-axis [{labels}]",
                f'    y-axis "Tickets" 0 --> {(top // 5 + 1) * 5}',
                *[f"    line [{series(n)}]" for n in lines], "```", ""]

    return ["## Remaining tickets over time", "",
            "Generated from the git history of `backlog/` (times are UTC; each point is the backlog as of that time, the last one the current backlog). "
            "Completed tickets (Done, Won't port) are not counted in this chart.", "",
            "| Line | Meaning |", "|---|---|",
            f"| blue ({NEW_COLOUR}) | New: Available, not started yet |",
            f"| red ({BLOCKED_COLOUR}) | Blocked |",
            f"| amber ({ACTIVE_COLOUR}) | Active: In-Progress, Needs-Review or In-Review |", "",
            *chart("Remaining tickets", (NEW_COLOUR, BLOCKED_COLOUR, ACTIVE_COLOUR), (1, 2, 3)),
            "## Progress and scope over time", "",
            "| Line | Meaning |", "|---|---|",
            f"| green ({DONE_COLOUR}) | Done: Done or Won't port, cumulative |",
            f"| grey ({TOTAL_COLOUR}) | Total scope: every ticket that existed at that time; a rise here is new tickets, not lost progress |", "",
            *chart("Done vs total scope", (DONE_COLOUR, TOTAL_COLOUR), (4, 5))]


def render_map():
    """GitHub-renderable mermaid map: every incomplete ticket and the edges between them; Done tickets are never drawn."""
    ts = tickets()
    info = {i: (t, s, d) for i, t, s, d, _ in ts}
    open_ids = [i for i, _, s, _, _ in ts if not s.startswith(("Done", "Won't"))]

    def is_done(i):
        return info[i][1].startswith(("Done", "Won't"))

    # Only incomplete dependencies are drawn; Done tickets never appear, as nodes or as edges.
    drawn = {i: [d for d in deps_of(info[i][2]) if d in info and not is_done(d)] for i in open_ids}
    shown = list(open_ids)
    for i, ds in drawn.items():
        for d in ds:
            if d not in shown:
                shown.append(d)
    shown.sort()
    ready_ids = {i for k, i, _, _, _ in ready_list(ts) if k == "IMPLEMENT"}
    counts = downstream_counts(ts)
    inflight = {i for i in open_ids if kind(info[i][1]) in ("In-Progress", "Needs-Review", "In-Review")}
    # On deck: Available, not ready yet, and every unfinished dependency is already being worked on.
    ondeck = {}
    for i in open_ids:
        if info[i][1] == "Available" and i not in ready_ids:
            pending = [d for d in deps_of(info[i][2]) if d in info and not is_done(d)]
            if pending and all(d in inflight for d in pending):
                ondeck[i] = pending

    def state_kind(i):
        return "Ready" if i in ready_ids else "OnDeck" if i in ondeck else kind(info[i][1])

    def label(i):
        t = info[i][0].replace('"', "'").replace("<", "(").replace(">", ")")
        return f"{i}<br/>{t[:60] + '...' if len(t) > 60 else t}"

    phase_rows = phase_overview(info, open_ids, ready_ids, inflight)
    out = ["# Migration dependency map", "",
           *phase_rows, "",
           "Generated by `tools/ticket.py` on every backlog change; do not edit by hand. "
           "Shows every incomplete ticket and the dependencies between them; completed tickets are hidden. "
           "An arrow `A --> B` means B depends on A.", "",
           "| Colour | Meaning |", "|---|---|",
           "| bright green, thick border | Ready to start: Available and every dependency is Done |",
           "| orange, thick border | On deck: Available, and every unfinished dependency is already in progress or in review, so it becomes ready when those finish |",
           "| pale green, dotted border | Available but waiting on dependencies that are not started yet |", "| yellow | In-Progress |",
           "| blue | Needs-Review / In-Review |", "| red | Blocked |", "",
           "```mermaid", "flowchart TD"]
    for prefix, name in TRACKS.items():
        ids = [i for i in shown if i.startswith(prefix)]
        if not ids:
            continue
        out.append(f'  subgraph {prefix}TRACK["{name}"]')
        out += [f'    {i}["{label(i)}"]' for i in ids]
        out.append("  end")
    edges = [(d, i) for i in open_ids for d in drawn.get(i, [])]
    out += [f"  {d} --> {i}" for d, i in edges]
    for cls, style in dict(STYLES.values()).items():
        out.append(f"  classDef {cls} {style}")
    for i in shown:
        out.append(f"  class {i} {STYLES.get(state_kind(i), STYLES['Blocked'])[0]}")
    out += ["```", ""] + render_chart()
    out += ["## Incomplete tickets", "", "| Ticket | State | Depends on | Unblocks | Title |", "|---|---|---|---|---|"]
    order = sorted(open_ids, key=lambda i: (i not in ready_ids, i not in ondeck, -counts.get(i, 0), i))
    for i in order:
        t, st, d = info[i]
        if i in ready_ids:
            st = "**Ready to start**"
        elif i in ondeck:
            st = "**On deck** (after " + ", ".join(ondeck[i]) + ")"
        elif st == "Available":
            waiting = [x for x in deps_of(d) if not info.get(x, ("", "Done"))[1].startswith("Done")]
            st = "Available (waiting on " + ", ".join(waiting) + ")"
        out.append(f"| {i} | {st} | {', '.join(deps_of(d)) or 'none'} | {counts.get(i, 0)} | {t.replace('|', '/')} |")
    return "\n".join(out) + "\n"


PHASE_NAMES = {1: "Phase 1: CLI (headless engine)", 2: "Phase 2: single player (Godot client)", 3: "Phase 3: multiplayer"}


def backlog_link(tid, title):
    """Markdown link to the ticket's file."""
    return f"[{tid}](backlog/{tid}.md)"


def phase_overview(info, open_ids, ready_ids, inflight):
    """Markdown block: per delivery phase (phases.txt) done/total plus the remaining tickets, so the map opens
    with what is left before each milestone. Work proceeds phase by phase (see ready_list)."""
    states = all_states()
    out = ["## Delivery phases", "",
           "Work is completed phase by phase (`phases.txt`; `ticket.py ready`/`next` list earlier phases first; later phases fill idle capacity).", "",
           "| Phase | Done | Remaining | Ready | In flight | Remaining tickets |", "|---|---|---|---|---|---|"]
    for ph, name in PHASE_NAMES.items():
        mine = [i for i, s in states.items() if phase_of(i) == ph and not s.startswith("Won't")]
        done = [i for i in mine if states[i].startswith("Done")]
        rem = sorted(i for i in open_ids if phase_of(i) == ph)
        ready = [i for i in rem if i in ready_ids]
        fly = [i for i in rem if i in inflight]
        out.append(f"| {name} | {len(done)}/{len(mine)} | {len(rem)} | {len(ready)} | {len(fly)} | "
                   f"{' '.join(backlog_link(i, info[i][0]) for i in rem) if rem else 'complete'} |")
    return out


def refresh_map():
    text = render_map()
    if not DEPMAP.exists() or DEPMAP.read_text(encoding="utf-8") != text:
        DEPMAP.write_text(text, encoding="utf-8")


def mutate(tid, fn):
    """Apply fn(block_lines) -> new block_lines to one ticket and write atomically.
    Everything outside the block is compared before writing."""
    path = ticket_path(tid)
    if not path.exists():
        die(f"{tid} not found in backlog/")
    lines = path.read_text(encoding="utf-8").split("\n")
    start, end = locate(lines, tid)
    new_block = fn(lines[start:end])
    if not new_block or not new_block[0].startswith(f"### {tid}"):
        die(f"internal error: edit of {tid} lost its header")
    out = lines[:start] + new_block + lines[end:]
    if out[:start] != lines[:start] or out[start + len(new_block):] != lines[end:]:
        die("internal error: edit leaked outside the ticket block")
    write_backlog(path, out)


def set_line(block, name, value):
    block = list(block)
    for n, l in enumerate(block):
        if l.startswith(f"- {name}:"):
            block[n] = f"- {name}: {value}" if value else f"- {name}:"
            return block
    if name == "Claimed":  # older tickets lack the line: insert it before Notes
        at = next((n for n, l in enumerate(block) if l.startswith("- Notes:")), len(block))
        block.insert(at, f"- Claimed: {value}" if value else "- Claimed:")
        return block
    die(f"ticket has no '- {name}:' line")


def add_note(block, text):
    block = list(block)
    last = max(n for n, l in enumerate(block) if l.strip())
    for k, line in enumerate(text.strip("\n").split("\n")):
        block.insert(last + 1 + k, ("  - " + line) if k == 0 else ("    " + line))
    return block


def current_state(tid):
    for i, _, s, _, _ in tickets():
        if i == tid:
            return s
    die(f"{tid} not found in backlog/")


def git(*args, check=True):
    r = subprocess.run(["git", "-C", str(HERE), *args], capture_output=True, text=True)
    if check and r.returncode:
        die(f"git {' '.join(args)} failed:\n{r.stdout}{r.stderr}")
    return r


def tree_clean():
    return not git("status", "--porcelain").stdout.strip()


def commit_backlog(msg, tid=None, everything=False):
    """Commit only the ticket's file + DEPENDENCY_MAP.md, or (everything) all of the worktree's changes in one commit."""
    if everything:
        git("add", "-A")
        git("commit", "-q", "-m", msg)
    else:
        paths = [str(ticket_path(tid)), str(DEPMAP)]
        git("add", "--", *paths)
        git("commit", "-q", "-m", msg, "--", *paths)


def publish(origin=False):
    if not tree_clean():
        die("publish needs a clean worktree: commit your other changes first")
    for _ in range(5):
        r = git("rebase", BRANCH, check=False)
        while r.returncode:
            # DEPENDENCY_MAP.md is generated: when it is the only conflict, rebuild it from the merged backlog.
            conflicted = git("diff", "--name-only", "--diff-filter=U", check=False).stdout.split()
            if conflicted and all(Path(c).name == DEPMAP.name for c in conflicted):
                refresh_map()
                git("add", "--", str(DEPMAP))
                r = subprocess.run(["git", "-C", str(HERE), "-c", "core.editor=true", "rebase", "--continue"],
                                   capture_output=True, text=True, env={**os.environ, "GIT_EDITOR": "true"})
                continue
            break
        if r.returncode:
            git("rebase", "--abort", check=False)
            die(f"rebase onto {BRANCH} conflicted; resolve by hand (each conflicted backlog/T###.md: take {BRANCH}'s file, "
                f"re-apply only your ticket's lines):\n{r.stdout}{r.stderr}", 3)
        if git("push", ".", f"HEAD:{BRANCH}", check=False).returncode == 0:
            if origin:
                git("push", "origin", BRANCH)
            return
    die("publish kept losing the race; retry", 3)


def would_cycle(tid, new_deps):
    deps = {i: set(deps_of(d)) for i, _, s, d, _ in tickets() if not s.startswith("Done")}
    deps[tid] = set(new_deps)
    seen, stack = set(), [tid]
    while stack:
        for d in deps.get(stack.pop(), ()):
            if d == tid:
                return True
            if d not in seen:
                seen.add(d)
                stack.append(d)
    return False


# ---------------------------------------------------------------- commands

def cmd_claim(argv):
    publish_it = "--no-publish" not in argv
    argv = [a for a in argv if a != "--no-publish"]
    tid, agent = argv[0], argv[1]
    if publish_it:
        if not tree_clean():
            die("claim needs a clean worktree (commit or discard your changes first)")
        git("rebase", BRANCH)
    s = current_state(tid)
    if s != "Available":
        die(f"{tid} is '{s}', not Available: pick another ticket", 2)
    st = all_states()
    deps = next(d for i, _, _, d, _ in tickets() if i == tid)
    unmet = [d for d in deps_of(deps) if not st.get(d, "").startswith("Done")]
    if unmet:
        die(f"{tid} has unmet dependencies: {', '.join(unmet)}", 2)
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%MZ")
    state = f"In-Progress [{agent}]"
    mutate(tid, lambda b: set_line(set_line(b, "State", state), "Claimed", stamp))
    commit_backlog(f"migration: claim {tid} [{agent}] {stamp}", tid)
    if publish_it:
        publish()
        s = current_state(tid)
        if s != state:
            die(f"lost the race for {tid} (now '{s}'): drop it and pick another", 2)
    print(f"{tid}: {state} {stamp}")


CODE_SUFFIXES = (".cs", ".csproj", ".props", ".editorconfig", ".json", ".py", ".txt", ".sln", ".targets")


def is_backlog_file(path):
    """Ticket files, the backlog README and the generated map are ledger text, not code (path relative to HERE)."""
    p = (HERE / path).resolve()
    return p == DEPMAP.resolve() or BACKLOG_DIR.resolve() in p.parents
CONTAINER = "dotnet-dev"


def green_commands():
    """Shell commands of the Green-before-Done rule, run from csharp/migration. tools/gate.py is the whole gate in one pass:
    build -warnaserror, dotnet format --verify-no-changes (changed files; everything when a style/project file changed), the
    full suite sharded across processes AND instrumented for coverage in the same run, then the coverage ratchet. It enters
    dotnet-dev itself when dotnet is not on PATH."""
    return [("build/format/test+coverage", f"cd {shlex.quote(str(HERE))} && python3 -I tools/gate.py")]


def stale_markers(tid):
    """TODO(tid) markers still in the tracked tree (the backlog files may mention them)."""
    pat = r"TODO\(" + re.escape(tid) + r"\b"
    out = git("grep", "-n", "-I", "-E", pat, "--", ":/", check=False).stdout.splitlines()
    return [l for l in out if not is_backlog_file(l.split(":", 1)[0])]


STAMP_KEEP = 50


def stamp_path():
    """One file in the git COMMON dir, shared by every worktree: a line per tree that passed the gate. A reviewer whose tree is
    byte-identical to the implementer's (the usual case after a fast-forward) therefore needs no second run."""
    return Path(git("rev-parse", "--path-format=absolute", "--git-common-dir").stdout.strip()) / "ticket-green"


def stamped_trees():
    try:
        return stamp_path().read_text().split()
    except OSError:
        return []


def write_stamps(trees):
    stamp_path().write_text("".join(t + "\n" for t in trees[-STAMP_KEEP:]))


def tree_id():
    return git("rev-parse", "HEAD^{tree}").stdout.strip()


def rebased():
    return git("merge-base", "--is-ancestor", BRANCH, "HEAD", check=False).returncode == 0


def run_green(force=False):
    """Build, format, test and coverage-check the current (clean, rebased) tree, or die. A stamp for this exact tree
    (written by a previous passing run) makes it instant."""
    if not tree_clean():
        die("green: the worktree has uncommitted changes; commit them first (the checks must cover what is published)")
    if not rebased():
        die(f"green: HEAD does not contain {BRANCH}; `git rebase {BRANCH}` and re-run the checks on the rebased tree")
    if not force and tree_id() in stamped_trees():
        print("green: this tree already passed (stamp)")
        return
    write_stamps([t for t in stamped_trees() if t != tree_id()])
    for name, cmd in green_commands():
        print(f"green: {name} ...", flush=True)
        r = subprocess.run(cmd, shell=True, capture_output=True, text=True)
        if r.returncode:
            print("\n".join((r.stdout + r.stderr).splitlines()[-40:]), file=sys.stderr)
            die(f"green: {name} failed (exit {r.returncode}); fix it before marking Done")
    write_stamps(stamped_trees() + [tree_id()])


def require_green(tid, no_green):
    """Gate for `state T### Done` (PROTOCOL 'Green before Done')."""
    if no_green:
        deliverables = field(next(b for i, _, _, _, b in tickets() if i == tid), "Deliverables") or ""
        if not deliverables.strip():
            die(f"--no-green needs {tid} to list its (non-code) Deliverables, e.g. PROTOCOL.md")
        touched = git("diff", "--name-only", f"{BRANCH}...HEAD", check=False).stdout.split()
        touched = [f for f in touched if not f.lower().endswith(".md")]
        if touched:
            die(f"--no-green is only for backlog-only/docs tickets; the branch changes: {', '.join(touched)}")
        code = [d for d in re.findall(r"[\w./+-]+", deliverables) if d.lower().endswith(CODE_SUFFIXES)]
        if code:
            die(f"--no-green is only for backlog-only/docs tickets; {tid} lists code deliverables: {', '.join(code)}")
    else:
        run_green()
    left = stale_markers(tid)
    if left:
        die(f"{tid} cannot be Done: markers naming it remain (retarget them to open tickets):\n" + "\n".join(left))


def cmd_green():
    run_green(force=True)
    print("green: ok")


def release(tid):
    """Build the CLI and the client into <repo>/csharp-releases (tools/release.py). The ticket is already Done and published, so a failure
    only warns: fix the cause and re-run `python3 -I tools/release.py T###`."""
    r = subprocess.run([sys.executable, "-I", str(HERE / "tools" / "release.py"), tid])
    if r.returncode:
        print(f"WARNING: release build for {tid} failed (exit {r.returncode}); {tid} stays Done. "
              f"Re-run: python3 -I tools/release.py {tid}", file=sys.stderr)


def cmd_state(argv):
    msg = None
    do_publish = do_origin = do_all = no_release = False
    no_green = None
    rest = []
    it = iter(argv)
    for a in it:
        if a == "-m":
            msg = next(it)
        elif a == "--publish":
            do_publish = True
        elif a == "--origin":
            do_origin = True
        elif a == "--all":
            do_all = True
        elif a == "--no-release":
            no_release = True
        elif a == "--no-green":
            no_green = next(it, "").strip()
            if not no_green or no_green.startswith("-"):
                die("--no-green needs a REASON (a sentence, not another flag)")
        else:
            rest.append(a)
    tid, state = rest[0], " ".join(rest[1:]).strip()
    m = re.match(r"^(Needs-Review|In-Review|In-Progress)\s+\[?([^\]]+)\]?$", state)
    if m:
        state = f"{m.group(1)} [{m.group(2)}]"
    if not STATE_RE.match(state):
        die(f"bad state '{state}'")
    cur = current_state(tid)
    if kind(state) not in LEGAL.get(kind(cur), set()):
        die(f"illegal transition {tid}: '{cur}' -> '{state}' (use `reset` for abandoned claims)")
    if kind(state) == "Done":
        if do_all:
            die("Done with --all would commit unchecked changes: commit the work, run the checks, then mark Done")
        require_green(tid, no_green)
    mutate(tid, lambda b: set_line(b, "State", state))
    if no_green and kind(state) == "Done":
        mutate(tid, lambda b: add_note(b, f"Done without the build/test run (--no-green): {no_green}"))
    commit_backlog(msg or f"migration: {tid} -> {state}", tid, everything=do_all)
    if do_publish or do_origin:
        publish(origin=do_origin)
    print(f"{tid}: {state}")
    if kind(state) == "Done" and not no_release:
        release(tid)


def cmd_reset(argv):
    tid = argv[0]
    reason = argv[argv.index("-m") + 1] if "-m" in argv else "reset"
    cur = current_state(tid)
    if kind(cur) not in ("In-Progress", "In-Review", "Needs-Review", "Blocked"):
        die(f"{tid} is '{cur}': nothing to reset")
    mutate(tid, lambda b: add_note(set_line(set_line(b, "State", "Available"), "Claimed", ""),
                                   f"Reset ({reason}); was '{cur}'."))
    commit_backlog(f"migration: reset {tid} ({reason})", tid)
    print(f"{tid}: Available")


def cmd_note(argv):
    tid, text = argv[0], argv[1]
    if text == "-":
        text = sys.stdin.read()
    if not text.strip():
        die("empty note")
    mutate(tid, lambda b: add_note(b, text))
    print(f"{tid}: note added")


def cmd_set(argv):
    tid, name, value = argv[0], argv[1], argv[2]
    if value == "-":
        value = sys.stdin.read().strip()
    if name == "Title":
        mutate(tid, lambda b: [f"### {tid} {value}"] + b[1:])
        print(f"{tid}: Title set")
        return
    if name not in SETTABLE:
        die(f"cannot set '{name}' (settable: {', '.join(sorted(SETTABLE))}, Title; use claim/state/note/reset)")
    if name == "Depends":
        known = all_states()
        bad = [d for d in deps_of(value) if d not in known]
        if bad:
            die(f"unknown dependency ids: {', '.join(bad)}")
        if would_cycle(tid, deps_of(value)):
            die(f"Depends '{value}' would create a dependency cycle")
    mutate(tid, lambda b: set_line(b, name, value))
    print(f"{tid}: {name} set")


def cmd_add(argv):
    opts = {"--prefix": "T"}
    it = iter(argv)
    for a in it:
        if not a.startswith("--"):
            die(f"unexpected argument {a}")
        opts[a] = next(it)
    if "--title" not in opts:
        die("add needs --title")
    prefix = opts["--prefix"]
    known = all_states()
    # Also look at the published branch so two worktrees adding at once do not pick the same id.
    ids = {i for i in known if i.startswith(prefix)}
    shown = git("ls-tree", "--name-only", BRANCH, "--", git("rev-parse", "--show-prefix").stdout.strip() + "backlog/", check=False)
    ids |= set(re.findall(rf"/({prefix}\d+)\.md$", shown.stdout, re.M))
    tid = f"{prefix}{max([int(i[1:]) for i in ids] or [0]) + 1:03d}"
    deps = opts.get("--depends", "none")
    bad = [d for d in deps_of(deps) if d not in known]
    if bad:
        die(f"unknown dependency ids: {', '.join(bad)}")
    block = [f"### {tid} {opts['--title']}", "- State: Available", f"- Depends: {deps}",
             f"- Source: {opts.get('--source', '')}", f"- Fixtures: {opts.get('--fixtures', 'none')}",
             f"- Deliverables: {opts.get('--deliverables', '')}", f"- Gate: {opts.get('--gate', '')}",
             "- Claimed:", f"- Notes: {opts.get('--notes', '')}"]
    block = [l.rstrip() for l in block]
    if prefix not in TRACKS:
        die(f"unknown prefix {prefix} (tracks: {', '.join(TRACKS)})")
    write_file(ticket_path(tid), "\n".join(block) + "\n")
    refresh_map()
    print(tid)


def prose_chunks():
    """(start, end, heading) of each '#'-heading chunk that is not a ticket block."""
    lines = README.read_text(encoding="utf-8").split("\n")
    heads = [n for n, l in enumerate(lines) if l.startswith("#") or re.match(r"^\*\*.*Planned", l)]
    out = []
    for k, n in enumerate(heads):
        if HEADER.match(lines[n]):
            continue
        out.append((n, heads[k + 1] if k + 1 < len(heads) else len(lines), lines[n]))
    return lines, out


def cmd_section(args):
    lines, chunks = prose_chunks()
    if not args:
        for n, e, h in chunks:
            print(f"{n + 1}\t{h}")
        return
    hit = [(n, e) for n, e, h in chunks if args[0].lower() in h.lower()]
    if not hit:
        die("no such section")
    for n, e in hit:
        print("\n".join(lines[n:e]))


def cmd_planned_clear(args):
    lines, chunks = prose_chunks()
    hit = [(n, e) for n, e, h in chunks if args[0].lower() in h.lower()]
    if len(hit) != 1:
        die(f"{len(hit)} sections match '{args[0]}' (need exactly 1)")
    n, e = hit[0]
    del lines[n:e]
    write_backlog(README, lines)
    print("section removed")


def cmd_drop(args):
    tid = args[0]
    ts = tickets()
    row = next((t for t in ts if t[0] == tid), None)
    if row is None:
        die(f"{tid} not found in backlog/")
    if row[2] != "Available" or field(row[4], "Claimed"):
        die(f"{tid} is not an unclaimed Available ticket")
    if any(tid in deps_of(d) for i, _, _, d, _ in ts if i != tid):
        die(f"{tid} is a dependency of another ticket")
    if max(all_states(), key=lambda i: (i[0] == tid[0], int(i[1:]))) != tid:
        die(f"{tid} is not the newest {tid[0]}-ticket")
    ticket_path(tid).unlink()
    _cache.clear()
    refresh_map()
    print(f"{tid}: dropped")


def cmd_renumber(argv):
    """renumber OLD NEW TITLE_SUBSTRING [--refs T1,T2]: fix a duplicate id created by parallel `add` calls.
    Renames the OLD block whose title contains TITLE_SUBSTRING, and replaces the id token in the --refs tickets."""
    old, new, sub = argv[0], argv[1], argv[2]
    refs = argv[argv.index("--refs") + 1].split(",") if "--refs" in argv else []
    if new in all_states():
        die(f"{new} already exists")
    path = ticket_path(old)
    if not path.exists():
        die(f"{old} not found")
    lines = path.read_text(encoding="utf-8").split("\n")
    if not (lines[0].startswith(f"### {old} ") and sub in lines[0]):
        die(f"{old}'s heading does not contain '{sub}'")
    lines[0] = lines[0].replace(f"### {old} ", f"### {new} ", 1)
    token = re.compile(rf"\b{old}\b")
    for tid in refs:
        rp = ticket_path(tid)
        rl = rp.read_text(encoding="utf-8").split("\n")
        write_file(rp, "\n".join(token.sub(new, l) for l in rl))
    write_file(ticket_path(new), "\n".join(lines))
    path.unlink()
    refresh_map()
    print(f"{old} ({sub}) -> {new}; refs updated in: {', '.join(refs) or 'none'}")


def cmd_check():
    errs = []
    seen = set()
    if LEGACY.exists():
        errs.append("backlog.md still exists: the backlog is the backlog/ folder (tools/split_backlog.py converts it)")
    for p in sorted(BACKLOG_DIR.glob("*")) if BACKLOG_DIR.is_dir() else []:
        if p.name != "README.md" and not TICKET_FILE.match(p.name):
            errs.append(f"backlog/{p.name}: not a ticket file (expected T###.md, C###.md or A###.md)")
    for p in ticket_files():
        found = [i for i, _, _ in blocks(p)]
        if found != [p.name[:-3]]:
            errs.append(f"backlog/{p.name}: must contain exactly the one ticket {p.name[:-3]}, found {found}")
    known = all_states()
    ts = tickets()
    for i, t, s, d, b in ts:
        if i in seen:
            errs.append(f"{i}: duplicate id")
        seen.add(i)
        if not STATE_RE.match(s):
            errs.append(f"{i}: bad state '{s}'")
        for dep in deps_of(d):
            if dep not in known:
                errs.append(f"{i}: unknown dependency {dep}")
        if not s.startswith("Done") and would_cycle(i, deps_of(d)):
            errs.append(f"{i}: dependency cycle")
    for e in errs:
        print("ERROR", e)
    shots = HERE / "screenshots.json"
    if shots.exists():  # warning only: Done UI tickets need a gallery entry (PROTOCOL 'UI tickets')
        mapped = json.loads(shots.read_text(encoding="utf-8"))
        for i, _, s, _, _ in ts:
            if i.startswith("C") and s == "Done" and i not in mapped:
                print(f"WARN {i}: Done but no entry in screenshots.json (add a screenshot or {{\"none\": reason}})")
    print(f"{len(ts)} tickets, {len(errs)} errors")
    sys.exit(1 if errs else 0)


def cmd_stale():
    now = datetime.now(timezone.utc)
    for i, t, s, _, b in tickets():
        if kind(s) in ("In-Progress", "In-Review"):
            c = field(b, "Claimed")
            try:
                age = now - datetime.strptime(c, "%Y-%m-%dT%H:%MZ").replace(tzinfo=timezone.utc)
            except ValueError:
                print(f"{i}\t{s}\tclaimed '{c or '(none)'}'\tSTALE (no valid timestamp)\t{t}")
                continue
            if age > STALE:
                print(f"{i}\t{s}\tclaimed {c}\tSTALE ({age})\t{t}")


def cmd_archive():
    print("archive is retired: Done tickets stay in their own backlog/T###.md file (nothing to do)")


def main():
    argv = sys.argv[1:]
    cmd = argv[0] if argv else "list"
    args = argv[1:]
    ts = tickets()
    if cmd == "list":
        want = args[0].lower() if args else None
        for i, t, s, d, _ in ts:
            if want is None or s.lower().startswith(want):
                print(f"{i}\t{s}\t[{d}]\t{t}")
    elif cmd == "show":
        for i, _, _, _, b in ts:
            if i == args[0]:
                print(field(b, args[1]) if len(args) > 1 else "".join(b).rstrip())
                return
        die(f"{args[0]} not found")
    elif cmd == "ready":
        for k, i, t, s, n in ready_list(ts):
            print(f"{k} {i} [P{phase_of(i)}, unblocks {n}] {t}" + (f" ({s})" if k == "REVIEW" else ""))
    elif cmd == "next":
        me = args[0].lower() if args else ""
        for i, t, s, _, _ in ts:
            if s.startswith("Needs-Review") and me not in s.lower():
                print(f"REVIEW {i} {t} ({s})")
                return
        mine = [(i, t) for i, t, s, _, _ in ts if s.startswith("Needs-Review") and me in s.lower()]
        if mine and me:
            print(f"SELF-REVIEW {mine[0][0]} {mine[0][1]}: sole agent, review via isolated adversarial subagent")
            return
        for k, i, t, _, n in ready_list(ts):
            if k == "IMPLEMENT":
                print(f"IMPLEMENT {i} [unblocks {n}] {t}")
                return
        print("NONE: nothing reviewable and nothing Available with deps Done")
    elif cmd == "blockers":
        st = all_states()
        names = {i: t for i, t, _, _, _ in ts}
        for dep in deps_of(next((d for i, _, _, d, _ in ts if i == args[0]), "")):
            if not st.get(dep, "").startswith("Done"):
                print(f"{dep}\t{st.get(dep, 'missing')}\t{names.get(dep, '')}")
    elif cmd == "stale":
        cmd_stale()
    elif cmd == "check":
        cmd_check()
    elif cmd == "renumber":
        cmd_renumber(args)
    elif cmd == "claim":
        cmd_claim(args)
    elif cmd == "state":
        cmd_state(args)
    elif cmd == "green":
        cmd_green()
    elif cmd == "reset":
        cmd_reset(args)
    elif cmd == "note":
        cmd_note(args)
    elif cmd == "set":
        cmd_set(args)
    elif cmd == "add":
        cmd_add(args)
    elif cmd == "section":
        cmd_section(args)
    elif cmd == "planned-clear":
        cmd_planned_clear(args)
    elif cmd == "drop":
        cmd_drop(args)
    elif cmd == "publish":
        publish(origin="--origin" in args)
        print("published")
    elif cmd == "graph":
        refresh_map()
        print(f"wrote {DEPMAP.name}")
    elif cmd == "archive":
        cmd_archive()
    else:
        die(__doc__)


if __name__ == "__main__":
    main()
