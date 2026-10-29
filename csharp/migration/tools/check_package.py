#!/usr/bin/env python3
"""T258: file-presence and size check of a packaged client archive (what package.sh produced). This is NOT a smoke test: it cannot run the client.

Usage: check_package.py <linux|windows|macos> [archive]     (default archive: Unciv.Godot/build/Unciv-client-<platform>.<ext>)

Archive names (stable, T279 uploads them): Unciv-client-linux.tar.gz, Unciv-client-windows.zip, Unciv-client-macos.zip, each with ONE top-level folder
Unciv-<platform>/. Exit 0 when every required entry is present and the executable is at least the minimum size, 1 otherwise.
"""
import re
import sys
import tarfile
import zipfile
from pathlib import Path

CSHARP = Path(__file__).resolve().parent.parent.parent
MB = 1024 * 1024

# (archive file name, executable path inside the top folder, minimum executable size in bytes, extra required entries as regexes on the path inside the top folder)
COMMON = [r"LICENSE", r"THIRD_PARTY_NOTICES\.md", r"Assets/fonts/NotoSans\.ttf", r"Packs/unciv/", r"Packs/units/", r"data/jsons/Civ V - Vanilla/Techs\.json", r"data/jsons/translations/"]
PLATFORMS = {
    "linux": ("Unciv-client-linux.tar.gz", "Unciv.x86_64", 40 * MB, COMMON + [r"Unciv\.pck", r"data_Unciv\.Godot_linuxbsd_x86_64/Unciv\.Godot\.dll"]),
    "windows": ("Unciv-client-windows.zip", "Unciv.exe", 40 * MB, COMMON + [r"Unciv\.pck", r"data_Unciv\.Godot_windows_x86_64/Unciv\.Godot\.dll"]),
    "macos": (
        "Unciv-client-macos.zip",
        "Unciv.app/Contents/MacOS/Unciv",
        80 * MB,  # universal binary
        [r"FIRST_RUN_MACOS\.txt", r"Unciv\.app/Contents/Info\.plist", r"Unciv\.app/Contents/Resources/Unciv\.pck"]
        + [f"Unciv\\.app/Contents/MacOS/{c}" for c in COMMON]
        + [r"Unciv\.app/Contents/Resources/data_Unciv\.Godot_macos_arm64/Unciv\.Godot\.dll", r"Unciv\.app/Contents/Resources/data_Unciv\.Godot_macos_x86_64/Unciv\.Godot\.dll"],
    ),
}


def entries(path):
    """{name: size} of an archive, names as stored."""
    if str(path).endswith(".tar.gz"):
        with tarfile.open(path) as t:
            return {m.name: m.size for m in t.getmembers()}
    with zipfile.ZipFile(path) as z:
        return {i.filename: i.file_size for i in z.infolist()}


def check(platform, archive):
    """List of problems (empty when the archive looks right)."""
    name, exe, minimum, required = PLATFORMS[platform]
    if not Path(archive).is_file():
        return [f"{archive} does not exist"]
    try:
        found = entries(archive)
    except (OSError, tarfile.TarError, zipfile.BadZipFile) as e:
        return [f"{archive} is not a readable archive: {e}"]
    top = f"Unciv-{platform}/"
    problems = []
    outside = [n for n in found if not n.startswith(top) and n != top.rstrip("/")]
    if outside:
        problems.append(f"entries outside the single top folder {top}: {outside[:3]}")
    inside = [n[len(top):] for n in found if n.startswith(top)]
    for pattern in required:
        if not any(re.match(pattern, n) for n in inside):
            problems.append(f"missing entry matching {pattern}")
    size = found.get(top + exe)
    if size is None:
        problems.append(f"missing executable {exe}")
    elif size < minimum:
        problems.append(f"{exe} is {size} bytes, expected at least {minimum}")
    return problems


def main(argv):
    if not argv or argv[0] not in PLATFORMS:
        print(__doc__, file=sys.stderr)
        return 2
    platform = argv[0]
    archive = Path(argv[1]) if len(argv) > 1 else CSHARP / "Unciv.Godot" / "build" / PLATFORMS[platform][0]
    problems = check(platform, archive)
    for p in problems:
        print(f"FAIL: {p}", file=sys.stderr)
    if not problems:
        print(f"OK: {archive} ({archive.stat().st_size // MB} MB) has every required entry (presence and size only; the client was NOT run)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
