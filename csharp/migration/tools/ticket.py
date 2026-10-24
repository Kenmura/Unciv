#!/usr/bin/env python3
"""Read the migration backlog without loading it whole (it is large).

  ticket.py list [STATE]    one line per ticket: id, state, deps, title
  ticket.py show T027       one ticket (searches backlog.md, then backlog-archive.md)
  ticket.py next [AGENT]    what the Work Loop would pick: a Needs-Review ticket not by AGENT,
                            else the first Available ticket whose deps are all Done
  ticket.py ready           every ticket that can be worked now (parallel dispatch): Needs-Review first,
                            then each Available ticket whose deps are all Done
  ticket.py claim T### AGENT [STATE]
                            set ONLY that ticket's State (default "In-Progress [AGENT]") and Claimed (UTC now)
  ticket.py archive         move Done ticket bodies to backlog-archive.md, leaving a stub
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
BACKLOG, ARCHIVE = HERE / "backlog.md", HERE / "backlog-archive.md"
HEADER = re.compile(r"^### ([A-Z]\d+)\b(.*)$")


def blocks(path):
    """Yield (id, title, lines) for every ticket block; a block ends at the next '#'/'##'/'###' heading."""
    if not path.exists():
        return
    cur = None
    for line in path.read_text(encoding="utf-8").splitlines(keepends=True):
        m = HEADER.match(line)
        if m or line.startswith("## ") or line.startswith("# "):
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


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    ts = tickets()
    if cmd == "list":
        want = sys.argv[2].lower() if len(sys.argv) > 2 else None
        for i, t, s, d, _ in ts:
            if want is None or s.lower().startswith(want):
                print(f"{i}\t{s}\t[{d}]\t{t}")
    elif cmd == "show":
        tid = sys.argv[2]
        for path in (BACKLOG, ARCHIVE):
            for i, _, b in blocks(path):
                if i == tid:
                    print("".join(b).rstrip())
                    return
        sys.exit(f"{tid} not found")
    elif cmd == "next":
        me = sys.argv[2].lower() if len(sys.argv) > 2 else ""
        for i, t, s, d, _ in ts:
            if s.startswith("Needs-Review") and me not in s.lower():
                print(f"REVIEW {i} {t} ({s})")
                return
        mine = [(i, t, s) for i, t, s, _, _ in ts if s.startswith("Needs-Review") and me in s.lower()]
        if mine and me:
            i, t, s = mine[0]
            print(f"SELF-REVIEW {i} {t}: sole agent, review via isolated adversarial subagent (PROTOCOL Single-Agent Mode)")
            return
        done = {i for i, _, s, _, _ in ts if s.startswith("Done")}
        for i, t, s, d, _ in ts:
            deps = re.findall(r"\b[A-Z]\d{3}\b", d)
            if s == "Available" and set(deps) <= done and (deps or d.lower().startswith("none")):
                print(f"IMPLEMENT {i} {t}")
                return
        print("NONE: nothing reviewable and nothing Available with deps Done")
    elif cmd == "ready":
        done = {i for i, _, s, _, _ in ts if s.startswith("Done")}
        for i, t, s, d, _ in ts:
            if s.startswith("Needs-Review"):
                print(f"REVIEW {i} {t} ({s})")
        for i, t, s, d, _ in ts:
            deps = re.findall(r"\b[A-Z]\d{3}\b", d)
            if s == "Available" and set(deps) <= done and (deps or d.lower().startswith("none")):
                print(f"IMPLEMENT {i} {t}")
    elif cmd == "claim":
        from datetime import datetime, timezone
        tid, agent = sys.argv[2], sys.argv[3]
        state = sys.argv[4] if len(sys.argv) > 4 else f"In-Progress [{agent}]"
        stamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%MZ")
        lines = BACKLOG.read_text(encoding="utf-8").split("\n")
        start = next((n for n, l in enumerate(lines) if l.startswith(f"### {tid} ")), None)
        if start is None:
            sys.exit(f"{tid} not found in backlog.md")
        end = next((n for n in range(start + 1, len(lines)) if lines[n].startswith("#")), len(lines))
        for n in range(start + 1, end):
            if lines[n].startswith("- State:"):
                lines[n] = f"- State: {state}"
            elif lines[n].startswith("- Claimed:"):
                lines[n] = f"- Claimed: {stamp}"
        BACKLOG.write_text("\n".join(lines), encoding="utf-8")
        print(f"{tid}: {state} {stamp}")
    elif cmd == "archive":
        text = BACKLOG.read_text(encoding="utf-8")
        moved = []
        for i, t, b in list(blocks(BACKLOG)):
            if field(b, "State") == "Done":
                body = "".join(b)
                stub = f"### {i} {t}\n- State: Done (full ticket: backlog-archive.md)\n\n"
                text = text.replace(body, stub, 1)
                moved.append(body.rstrip() + "\n\n")
        BACKLOG.write_text(text, encoding="utf-8")
        head = "" if ARCHIVE.exists() else "# Migration Backlog Archive\n\nDone tickets moved out of `backlog.md`. Read with `tools/ticket.py show T###`.\n\n"
        with ARCHIVE.open("a", encoding="utf-8") as f:
            f.write(head + "".join(moved))
        print(f"archived {len(moved)} tickets")
    else:
        sys.exit(__doc__)


main()
