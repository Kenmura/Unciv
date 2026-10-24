#!/usr/bin/env python3
"""The only way agents read or change the migration backlog (backlog.md is large and hand edits have
clobbered neighbouring tickets). Run from anywhere: python3 -I tools/ticket.py <command>.

READ
  list [STATE]              one line per open ticket: id, state, deps, title
  show T027 [Field]         whole ticket (backlog.md, then backlog-archive.md) or one field
  ready                     everything workable now: Needs-Review first, then Available with deps all Done,
                            most downstream-unblocking first (direct + transitive incomplete dependents)
  next [AGENT]              the single pick of the Work Loop (review, else the ready ticket that unblocks most)
  blockers T033             for each dependency not Done: its id, state and title
  stale                     In-Progress/In-Review claims older than 3 hours
  check                     validate the backlog (states, deps exist, no cycles, unique ids); exit 1 on errors

WRITE (each touches only the named ticket's block, verified before the file is written)
  claim T033 AGENT [--no-publish]
                            Available -> In-Progress [AGENT], stamps Claimed, commits ONLY backlog.md,
                            rebases onto csharp-migration, fast-forwards it. Exit 2 = lost the race.
  state T033 STATE [-m MSG] [--all] [--publish] [--origin]
                            STATE: Needs-Review AGENT | In-Review AGENT | Done | Available | Blocked | "Won't port".
                            Legal transitions only. Commits ONLY backlog.md (--all: every change in the worktree, e.g. your
                            deliverables, in the same commit); --publish also rebases and
                            fast-forwards csharp-migration; --origin also pushes it to origin (use on Done).
  reset T033 [-m REASON]    abandoned/stale claim -> Available, clears Claimed, notes the reason
  note T033 TEXT|-          append an indented bullet to Notes (- reads stdin, one bullet per call)
  set T033 FIELD VALUE|-    FIELD: Depends Source Fixtures Deliverables Gate Scope Title (not State, not Notes)
  add --title T [--depends "T1, T2"] [--source S] [--fixtures F] [--deliverables D] [--gate G]
      [--notes N] [--prefix T|C|A]
                            new Available ticket with the next free id, appended to its track; prints the id
  graph                     regenerate DEPENDENCY_MAP.md (also done automatically by every write command)
  publish [--origin]        git rebase csharp-migration && git push . HEAD:csharp-migration (clean tree only)
  archive                   move Done ticket bodies to backlog-archive.md, leaving a stub
"""
import os
import re
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

HERE = Path(os.environ.get("TICKET_MIGRATION_DIR") or Path(__file__).resolve().parent.parent)
BACKLOG, ARCHIVE = HERE / "backlog.md", HERE / "backlog-archive.md"
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


def tickets():
    return [(i, t, field(b, "State"), field(b, "Depends"), b) for i, t, b in blocks(BACKLOG)]


def deps_of(text):
    return re.findall(r"\b[A-Z]\d{3}\b", text)


def all_states():
    """id -> state for backlog and archive (archived tickets are Done)."""
    st = {i: s for i, _, s, _, _ in tickets()}
    for i, _, _ in blocks(ARCHIVE):
        st.setdefault(i, "Done")
    return st


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


def ready_list(ts):
    """Reviews first, then workable tickets ordered by how many incomplete tickets they unblock
    (direct and downstream), ties by id. Rows: (kind, id, title, state, downstream_count)."""
    done = {i for i, s in all_states().items() if s.startswith("Done")}
    counts = downstream_counts(ts)
    out = [("REVIEW", i, t, s, counts.get(i, 0)) for i, t, s, _, _ in ts if s.startswith("Needs-Review")]
    impl = []
    for i, t, s, d, _ in ts:
        deps = deps_of(d)
        if s == "Available" and set(deps) <= done and (deps or d.lower().startswith("none")):
            impl.append(("IMPLEMENT", i, t, s, counts.get(i, 0)))
    return out + sorted(impl, key=lambda r: (-r[4], r[1]))


# ---------------------------------------------------------------- mutation core

def locate(lines, tid):
    start = next((n for n, l in enumerate(lines) if l.startswith(f"### {tid} ") or l.rstrip() == f"### {tid}"), None)
    if start is None:
        die(f"{tid} not found in backlog.md (archived tickets are Done and cannot be edited)")
    end = next((n for n in range(start + 1, len(lines)) if lines[n].startswith("#")), len(lines))
    return start, end


def write_backlog(lines):
    tmp = BACKLOG.with_suffix(".md.tmp")
    tmp.write_text("\n".join(lines), encoding="utf-8")
    os.replace(tmp, BACKLOG)
    refresh_map()


TRACKS = {"T": "Engine track", "C": "Client track (Godot)", "A": "Game AI track"}
STYLES = {  # kind -> (class name, mermaid style)
    "Available": ("avail", "fill:#d4f8d4,stroke:#2a8a2a,color:#000"),
    "In-Progress": ("prog", "fill:#fff3b0,stroke:#b58900,color:#000"),
    "Needs-Review": ("review", "fill:#cfe4ff,stroke:#1f6feb,color:#000"),
    "In-Review": ("review", "fill:#cfe4ff,stroke:#1f6feb,color:#000"),
    "Blocked": ("blocked", "fill:#ffd6d6,stroke:#c62828,color:#000"),
    "Done": ("done", "fill:#eeeeee,stroke:#999999,color:#666,stroke-dasharray:4 3"),
    "Won't": ("done", "fill:#eeeeee,stroke:#999999,color:#666,stroke-dasharray:4 3"),
}


def render_map():
    """GitHub-renderable mermaid map: every incomplete ticket plus its direct dependencies (done or not)."""
    ts = tickets()
    info = {i: (t, s, d) for i, t, s, d, _ in ts}
    for i, _, b in blocks(ARCHIVE):
        info.setdefault(i, (next(t for j, t, _ in blocks(ARCHIVE) if j == i), "Done", ""))
    open_ids = [i for i, _, s, _, _ in ts if not s.startswith(("Done", "Won't"))]
    shown = list(open_ids)
    for i in open_ids:
        for d in deps_of(info[i][2]):
            if d in info and d not in shown:
                shown.append(d)
    shown.sort()

    def label(i):
        t = info[i][0].replace('"', "'").replace("<", "(").replace(">", ")")
        return f"{i}<br/>{t[:60] + '...' if len(t) > 60 else t}"

    out = ["# Migration dependency map", "",
           "Generated by `tools/ticket.py` on every backlog change; do not edit by hand. "
           "Shows every incomplete ticket and its direct dependencies (completed ones are grey and dashed). "
           "An arrow `A --> B` means B depends on A.", "",
           "| Colour | Meaning |", "|---|---|",
           "| green | Available (workable once dependencies are done) |", "| yellow | In-Progress |",
           "| blue | Needs-Review / In-Review |", "| red | Blocked |", "| grey dashed | Done (shown as a direct dependency only) |", "",
           "```mermaid", "flowchart TD"]
    for prefix, name in TRACKS.items():
        ids = [i for i in shown if i.startswith(prefix)]
        if not ids:
            continue
        out.append(f'  subgraph {prefix}TRACK["{name}"]')
        out += [f'    {i}["{label(i)}"]' for i in ids]
        out.append("  end")
    edges = [(d, i) for i in open_ids for d in deps_of(info[i][2]) if d in info]
    out += [f"  {d} --> {i}" for d, i in edges]
    for cls, style in dict(STYLES.values()).items():
        out.append(f"  classDef {cls} {style}")
    for i in shown:
        out.append(f"  class {i} {STYLES.get(kind(info[i][1]), STYLES['Blocked'])[0]}")
    out += ["```", "", "## Incomplete tickets", "", "| Ticket | State | Depends on | Title |", "|---|---|---|---|"]
    for i in open_ids:
        t, st, d = info[i]
        out.append(f"| {i} | {st} | {', '.join(deps_of(d)) or 'none'} | {t.replace('|', '/')} |")
    return "\n".join(out) + "\n"


def refresh_map():
    text = render_map()
    if not DEPMAP.exists() or DEPMAP.read_text(encoding="utf-8") != text:
        DEPMAP.write_text(text, encoding="utf-8")


def mutate(tid, fn):
    """Apply fn(block_lines) -> new block_lines to one ticket and write atomically.
    Everything outside the block is compared before writing."""
    lines = BACKLOG.read_text(encoding="utf-8").split("\n")
    start, end = locate(lines, tid)
    new_block = fn(lines[start:end])
    if not new_block or not new_block[0].startswith(f"### {tid}"):
        die(f"internal error: edit of {tid} lost its header")
    out = lines[:start] + new_block + lines[end:]
    if out[:start] != lines[:start] or out[start + len(new_block):] != lines[end:]:
        die("internal error: edit leaked outside the ticket block")
    write_backlog(out)


def set_line(block, name, value):
    block = list(block)
    for n, l in enumerate(block):
        if l.startswith(f"- {name}:"):
            block[n] = f"- {name}: {value}" if value else f"- {name}:"
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
    die(f"{tid} not found in backlog.md")


def git(*args, check=True):
    r = subprocess.run(["git", "-C", str(HERE), *args], capture_output=True, text=True)
    if check and r.returncode:
        die(f"git {' '.join(args)} failed:\n{r.stdout}{r.stderr}")
    return r


def tree_clean():
    return not git("status", "--porcelain").stdout.strip()


def commit_backlog(msg, everything=False):
    """Commit only backlog.md, or (everything) all of the worktree's changes in one commit."""
    if everything:
        git("add", "-A")
        git("commit", "-q", "-m", msg)
    else:
        git("add", "--", str(BACKLOG), str(DEPMAP))
        git("commit", "-q", "-m", msg, "--", str(BACKLOG), str(DEPMAP))


def publish(origin=False):
    if not tree_clean():
        die("publish needs a clean worktree: commit your other changes first")
    for _ in range(5):
        r = git("rebase", BRANCH, check=False)
        if r.returncode:
            git("rebase", "--abort", check=False)
            die(f"rebase onto {BRANCH} conflicted; resolve by hand (backlog.md: take {BRANCH}'s text, "
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
    commit_backlog(f"migration: claim {tid} [{agent}] {stamp}")
    if publish_it:
        publish()
        s = current_state(tid)
        if s != state:
            die(f"lost the race for {tid} (now '{s}'): drop it and pick another", 2)
    print(f"{tid}: {state} {stamp}")


def cmd_state(argv):
    msg = None
    do_publish = do_origin = do_all = False
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
    mutate(tid, lambda b: set_line(b, "State", state))
    commit_backlog(msg or f"migration: {tid} -> {state}", everything=do_all)
    if do_publish or do_origin:
        publish(origin=do_origin)
    print(f"{tid}: {state}")


def cmd_reset(argv):
    tid = argv[0]
    reason = argv[argv.index("-m") + 1] if "-m" in argv else "reset"
    cur = current_state(tid)
    if kind(cur) not in ("In-Progress", "In-Review", "Needs-Review", "Blocked"):
        die(f"{tid} is '{cur}': nothing to reset")
    mutate(tid, lambda b: add_note(set_line(set_line(b, "State", "Available"), "Claimed", ""),
                                   f"Reset ({reason}); was '{cur}'."))
    commit_backlog(f"migration: reset {tid} ({reason})")
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
    tid = f"{prefix}{max([int(i[1:]) for i in known if i.startswith(prefix)] or [0]) + 1:03d}"
    deps = opts.get("--depends", "none")
    bad = [d for d in deps_of(deps) if d not in known]
    if bad:
        die(f"unknown dependency ids: {', '.join(bad)}")
    block = [f"### {tid} {opts['--title']}", "- State: Available", f"- Depends: {deps}",
             f"- Source: {opts.get('--source', '')}", f"- Fixtures: {opts.get('--fixtures', 'none')}",
             f"- Deliverables: {opts.get('--deliverables', '')}", f"- Gate: {opts.get('--gate', '')}",
             "- Claimed:", f"- Notes: {opts.get('--notes', '')}"]
    block = [l.rstrip() for l in block]
    lines = BACKLOG.read_text(encoding="utf-8").split("\n")
    last = max((n for n, l in enumerate(lines) if re.match(rf"### {prefix}\d+\b", l)), default=None)
    if last is None:
        die(f"no existing {prefix}-tickets to append after")
    end = next((n for n in range(last + 1, len(lines)) if lines[n].startswith("#")), len(lines))
    while end > last + 1 and not lines[end - 1].strip():
        end -= 1
    lines[end:end] = [""] + block
    write_backlog(lines)
    print(tid)


def cmd_check():
    errs = []
    seen = set()
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
    text = BACKLOG.read_text(encoding="utf-8")
    moved = []
    for i, t, b in list(blocks(BACKLOG)):
        if field(b, "State") == "Done":
            body = "".join(b)
            stub = f"### {i} {t}\n- State: Done (full ticket: backlog-archive.md)\n\n"
            text = text.replace(body, stub, 1)
            moved.append(body.rstrip() + "\n\n")
    BACKLOG.write_text(text, encoding="utf-8")
    refresh_map()
    head = "" if ARCHIVE.exists() else "# Migration Backlog Archive\n\nDone tickets moved out of `backlog.md`. Read with `tools/ticket.py show T###`.\n\n"
    with ARCHIVE.open("a", encoding="utf-8") as f:
        f.write(head + "".join(moved))
    print(f"archived {len(moved)} tickets")


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
        for path in (BACKLOG, ARCHIVE):
            for i, _, b in blocks(path):
                if i == args[0]:
                    print(field(b, args[1]) if len(args) > 1 else "".join(b).rstrip())
                    return
        die(f"{args[0]} not found")
    elif cmd == "ready":
        for k, i, t, s, n in ready_list(ts):
            print(f"{k} {i} [unblocks {n}] {t}" + (f" ({s})" if k == "REVIEW" else ""))
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
    elif cmd == "claim":
        cmd_claim(args)
    elif cmd == "state":
        cmd_state(args)
    elif cmd == "reset":
        cmd_reset(args)
    elif cmd == "note":
        cmd_note(args)
    elif cmd == "set":
        cmd_set(args)
    elif cmd == "add":
        cmd_add(args)
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


main()
