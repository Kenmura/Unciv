#!/usr/bin/env python3
"""T276: build the content bundle for builds that have no data folder beside the executable (Android).

Usage: tools/stage-content.py [output.zip]   (default Content/unciv-content.zip in the project; git-ignored)

The zip holds the tree tools/package.sh puts next to a desktop executable (data/jsons, Packs, Assets/fonts) so the client can extract it once to user://content and treat
that folder as the executable folder (ContentRoot.cs). A sibling <name>.version file holds the version (a hash of the contents), read on every start without opening the zip.
The editor ignores the res:// bundle (a staged Content/ never shadows the checkout). The Android export preset must include both files in the pck (include_filter="Content/*"). Deterministic: sorted entries, fixed timestamps.
"""
import hashlib
import sys
import zipfile
from pathlib import Path

project = Path(__file__).resolve().parent.parent
repo = project.parent.parent
out = Path(sys.argv[1]) if len(sys.argv) > 1 else project / "Content" / "unciv-content.zip"

SKIP_NAMES = {"subset-fonts.py", ".gdignore", ".DS_Store"}
SKIP_SUFFIXES = {".import", ".uid"}
SOURCES = [
    (repo / "android" / "assets" / "jsons", "data/jsons"),
    (project / "Packs" / "unciv", "Packs/unciv"),
    (project / "Packs" / "units", "Packs/units"),
    (project / "Assets" / "fonts", "Assets/fonts"),
]

entries = []
for src, dest in SOURCES:
    if not src.is_dir():
        sys.exit(f"missing content folder {src}")
    for f in sorted(p for p in src.rglob("*") if p.is_file()):
        if f.name in SKIP_NAMES or f.suffix in SKIP_SUFFIXES:
            continue
        entries.append((f"{dest}/{f.relative_to(src).as_posix()}", f))

digest = hashlib.sha256()
for name, f in entries:
    digest.update(name.encode() + b"\0")
    digest.update(hashlib.sha256(f.read_bytes()).digest())
version = digest.hexdigest()[:16]

out.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for name, f in entries:
        info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, f.read_bytes())
out.with_suffix(".version").write_text(version)
print(f"{out}: {len(entries)} files, {out.stat().st_size // 1024} KiB, version {version}")
