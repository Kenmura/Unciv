#!/usr/bin/env python3
"""Table of contents for the migration's markdown files.

    python3 -I tools/toc.py            add or refresh the TOC block in every doc (from csharp/migration)
    python3 -I tools/toc.py --check    exit 1 listing docs whose TOC is missing or stale

The block sits right under the first `# title` between `<!-- toc -->` and `<!-- /toc -->`, and lists the `##` and `###` headings
(only `##` when that would exceed MAX_ENTRIES). Links use GitHub's heading anchors. Skipped: the per-ticket files in backlog/, skill
files (SKILL.md, they carry front matter and are loaded as instructions) and AGENTS.md/CLAUDE.md (loaded into every agent's context).
`screenshots.py` and `ticket.py` (DEPENDENCY_MAP.md) generate their pages with `add_toc` so a regeneration keeps the block.
"""
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CSHARP = HERE.parents[1]
MAX_ENTRIES = 60
START, END = "<!-- toc -->", "<!-- /toc -->"
BLOCK = re.compile(re.escape(START) + r".*?" + re.escape(END) + r"\n*", re.S)
SKIP_NAMES = {"SKILL.md", "AGENTS.md", "CLAUDE.md"}


def anchor(title, seen):
    """GitHub's slug: lower case, markup and punctuation dropped (letters, digits, `_`, `-`, spaces kept), spaces to `-`, repeats get -1, -2."""
    text = re.sub(r"[`*_~]|<[^>]+>", "", re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", title))
    slug = re.sub(r"[^\w\- ]", "", text.strip().lower()).replace(" ", "-")
    n = seen.get(slug, 0)
    seen[slug] = n + 1
    return slug if n == 0 else f"{slug}-{n}"


def headings(lines):
    """(level, title) of each `##`/`###` heading outside fenced code, with the anchors GitHub gives them."""
    out, seen, fence = [], {}, None
    for line in lines:
        m = re.match(r"\s*(```|~~~)", line)
        if m:
            fence = None if fence == m.group(1) else (fence or m.group(1))
            continue
        if fence:
            continue
        h = re.match(r"(#{1,6})\s+(.+?)\s*#*\s*$", line)
        if h:
            slug = anchor(h.group(2), seen)
            if len(h.group(1)) in (2, 3):
                out.append((len(h.group(1)), h.group(2), slug))
    return out


def render(entries):
    if len(entries) > MAX_ENTRIES:
        entries = [e for e in entries if e[0] == 2]
    return "\n".join(f"{'  ' * (level - 2)}- [{title}](#{slug})" for level, title, slug in entries)


def add_toc(text):
    """The text with its TOC block inserted under the first `# ` line, or refreshed; unchanged when there is nothing to list."""
    text = BLOCK.sub("", text)
    lines = text.split("\n")
    entries = headings(lines)
    title = next((i for i, l in enumerate(lines) if l.startswith("# ")), None)
    if title is None or not entries:
        return text
    block = f"{START}\n## Contents\n\n{render(entries)}\n{END}\n"
    return "\n".join(lines[: title + 1]) + "\n\n" + block + "\n" + "\n".join(lines[title + 1 :]).lstrip("\n")


def docs():
    out = subprocess.run(["git", "ls-files", "--", "*.md"], cwd=CSHARP, capture_output=True, text=True, check=True).stdout.split("\n")
    return [CSHARP / p for p in out if p and Path(p).name not in SKIP_NAMES and "migration/backlog/" not in p]


def main(argv):
    stale = []
    for path in docs():
        old = path.read_text(encoding="utf-8")
        new = add_toc(old)
        if new != old:
            stale.append(path.relative_to(CSHARP).as_posix())
            if "--check" not in argv:
                path.write_text(new, encoding="utf-8")
    if "--check" in argv:
        for p in stale:
            print("ERROR table of contents missing or stale: " + p + " (run python3 -I tools/toc.py)")
        return 1 if stale else 0
    print(f"updated {len(stale)} file(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
