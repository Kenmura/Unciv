#!/usr/bin/env python3
"""One-shot, repeatable conversion of the monolithic backlog into backlog/<ID>.md (one file per ticket).

    python3 -I tools/split_backlog.py [--dir csharp/migration] [--no-map]

Reads <dir>/backlog.md and <dir>/backlog-archive.md, writes <dir>/backlog/T001.md ... (each ticket block byte-for-byte:
from its `### T### Title` heading up to the next `#` heading) and <dir>/backlog/README.md (everything that is not a ticket
block: intro, `## Non-goals (Won't port)`, track headings and their intro text, in the original order), deletes the two
old files and regenerates DEPENDENCY_MAP.md. A ticket whose body lives in the archive (the `Done (full ticket:
backlog-archive.md)` stub in backlog.md) is written with its archived full text.

Idempotent: with no backlog.md present it does nothing; with an existing backlog/ it is rebuilt from the old files
(ticket files without a source are removed), so it can be re-run on the latest csharp-migration right before cut-over.
"""
import re
import subprocess
import sys
from pathlib import Path

HEADER = re.compile(r"^### ([A-Z]\d+)\b")
LAYOUT = """## Layout

One file per ticket: `backlog/T001.md`, `backlog/C005.md`, `backlog/A003.md` (the `### T### Title` heading and the `- State:`
lines are the whole file). Done tickets stay in their file; there is no archive. Tracks (below) are by id prefix: `T` Engine,
`C` Client, `A` Game AI. Ticket order is the track, then the number. Read and change tickets only through
`tools/ticket.py` (see `PROTOCOL.md`); this README holds the non-ticket text: the non-goals inventory and the track
introductions.

"""


def split(text):
    """-> ([(id, block_text)], remainder_text): blocks end at the next line starting with '#'; everything else is remainder."""
    blocks, rest, cur = [], [], None
    for line in text.splitlines(keepends=True):
        m = HEADER.match(line)
        if m or line.startswith("#"):
            if cur:
                blocks.append((cur[0], "".join(cur[1])))
            cur = (m.group(1), [line]) if m else None
            if not m:
                rest.append(line)
        elif cur:
            cur[1].append(line)
        else:
            rest.append(line)
    if cur:
        blocks.append((cur[0], "".join(cur[1])))
    return blocks, "".join(rest)


def read(path):
    with open(path, encoding="utf-8", newline="") as f:
        return f.read()


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def main(argv):
    mig = Path(__file__).resolve().parent.parent
    no_map = "--no-map" in argv
    if "--dir" in argv:
        mig = Path(argv[argv.index("--dir") + 1]).resolve()
    old, arch, out = mig / "backlog.md", mig / "backlog-archive.md", mig / "backlog"
    if not old.exists():
        print(f"{old} not found: already split (nothing to do)")
        return 0
    blocks, rest = split(read(old))
    archived = dict(split(read(arch))[0]) if arch.exists() else {}
    bodies, order = {}, []
    for tid, body in blocks:
        if tid in bodies:
            print(f"duplicate id {tid} in backlog.md", file=sys.stderr)
            return 1
        if tid in archived and "full ticket: backlog-archive.md" in body:
            body = archived[tid]
        bodies[tid] = body
        order.append(tid)
    for tid, body in archived.items():  # archived but no stub left in backlog.md
        if tid not in bodies:
            bodies[tid] = body
            order.append(tid)
    out.mkdir(exist_ok=True)
    keep = {f"{t}.md" for t in bodies} | {"README.md"}
    for p in out.glob("*.md"):
        if p.name not in keep:
            p.unlink()
    for tid, body in bodies.items():
        write(out / f"{tid}.md", body)
    first = re.search(r"^## ", rest, re.M)
    readme = rest[:first.start()] + LAYOUT + rest[first.start():] if first else rest + "\n" + LAYOUT
    write(out / "README.md", readme)
    old.unlink()
    arch.unlink(missing_ok=True)
    print(f"wrote {len(bodies)} tickets + README.md to {out}")
    if not no_map:
        subprocess.run([sys.executable, "-I", str(mig / "tools" / "ticket.py"), "graph"], check=True,
                       env={**__import__("os").environ, "TICKET_MIGRATION_DIR": str(mig)})
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
