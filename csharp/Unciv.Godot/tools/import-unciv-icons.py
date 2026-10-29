#!/usr/bin/env python3
"""C063: build the bundled icon pack `Unciv.Godot/Packs/unciv` from Unciv's own icon images (stdlib only; run with `python3 -I`).

    python3 -I Unciv.Godot/tools/import-unciv-icons.py           copy the icons, write manifest.json and CREDITS.md
    python3 -I Unciv.Godot/tools/import-unciv-icons.py --check   verify the committed pack matches (no writes); exit 1 on any problem

Source: the Kotlin repo's `android/Images.*` folders (Unciv's own art, credited in `docs/Credits.md`). Keys follow `Unciv.Assets/RulesetAssetKeys.cs`:
icon/stat/<Stat>, icon/nation/<nation>, icon/action/<action>, icon/notification/<name>, icon/ui/<name>.

The licence check runs on every invocation and refuses a source group whose licence is not on the allow list (no NC, ND or share-alike material), whose
credit section is missing from docs/Credits.md, or whose files are not PNGs. The credit sections are copied verbatim into the pack's CREDITS.md because
CC BY requires the attribution to travel with the images. Never add Firaxis/Civ 5 art here (migration/ASSETS.md).
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]  # repo root (csharp/Unciv.Godot/tools/..)
PACK = ROOT / "csharp" / "Unciv.Godot" / "Packs" / "unciv"
CREDITS = ROOT / "docs" / "Credits.md"

# SPDX-style ids. CC BY needs attribution (CREDITS.md travels with the pack); share-alike and non-commercial licences are not accepted.
ALLOWED_LICENCES = {"CC0-1.0", "CC-BY-3.0", "CC-BY-4.0", "Public-Domain"}

# Icons the UI asks for by name (icon/ui/<name>); a stem here that is not in the source folder fails the import.
UI_ICONS = [
    "ArrowRight", "BackArrow", "Blockade", "Camera", "Capital", "Chat", "Checkmark", "Cities", "CityState", "Close", "Diplomacy", "Espionage",
    "ExclamationMark", "Fire", "ForwardArrow", "Hexagon", "HexagonOutline", "Improvements", "Increase", "Keyboard", "Link", "Load", "LockSmall",
    "Mods", "Multiplayer", "Nations", "New", "Notifications", "Options", "Pause", "Pencil", "Policies", "Question", "Reduce",
    "Resources", "Resume", "Score", "Search", "Settings", "Shield", "Sleep", "Spy", "Star", "Stop", "Terrains", "Undo", "Wonders",
]

# Icons whose Credits.md entry is not a Noun Project CC BY 3.0 / public-domain work (Flaticon, including Premium; a repository with no licence statement;
# a Wikiquote file) and so is not proven redistributable here. They are not imported; the key falls back to the generated badge.
EXCLUDED = {
    "icon/ui/Timer": "Flaticon (Premium) stopwatch",
    "icon/ui/Politics": "Flaticon premium 'Political Science'",
    "icon/action/Skip": "Flaticon 'jump'",
    "icon/action/ConductTradeMission": "Flaticon 'trade'",
    "icon/nation/Persia": "Flaticon scimitars",
    "icon/nation/China": "Ravignir 5Hex-Tileset, no licence stated",
    "icon/nation/Inca": "Ravignir 5Hex-Tileset, no licence stated",
    "icon/nation/Iroquois": "Ravignir 5Hex-Tileset, no licence stated",
    "icon/nation/Songhai": "Ravignir 5Hex-Tileset, no licence stated",
    "icon/nation/The Huns": "Wikiquote file, licence not stated in Credits.md",
}

# (category, source folder under the repo root, licence, credit section of docs/Credits.md, stems to take or None for all)
GROUPS = [
    ("icon/stat", "android/Images.Icons/StatIcons", "CC-BY-3.0", "Stat icons", None),
    ("icon/nation", "android/Images.NationIcons/NationIcons", "CC-BY-3.0", "Nations", None),
    ("icon/action", "android/Images.ConstructionIcons/UnitActionIcons", "CC-BY-3.0", "Others", None),
    ("icon/notification", "android/Images.Icons/NotificationIcons", "CC-BY-3.0", "Others", None),
    ("icon/ui", "android/Images.Icons/OtherIcons", "CC-BY-3.0", "Others", UI_ICONS),
]

# C080: 3D models committed in the pack (made by make-unit-model.py, not copied from the Kotlin repo): key -> path in the pack. Their licence travels in CREDITS.md.
MODELS = {"unit/Warrior": "models/unit/Warrior.glb"}
MODEL_CREDITS = (
    "## Models\n\n"
    "-   `models/unit/Warrior.glb`: the Barbarian of the [KayKit Adventurers Character Pack 1.0](https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0) "
    "by Kay Lousberg (https://kaylousberg.com), licensed CC0 1.0 (no attribution required); reduced and renamed by `Unciv.Godot/tools/make-unit-model.py`\n"
)

PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def credit_section(text, heading):
    """Body of the `### <heading>` section of Credits.md (up to the next heading of the same or higher level), or None."""
    match = re.search(r"^### " + re.escape(heading) + r"\s*$", text, re.M)
    if not match:
        return None
    rest = text[match.end():]
    end = re.search(r"^#{1,3} ", rest, re.M)
    return (rest[:end.start()] if end else rest).strip("\n")


def licence_problems(credits_text):
    problems = []
    for category, folder, licence, section, _ in GROUPS:
        if licence not in ALLOWED_LICENCES:
            problems.append(f"{category}: licence {licence} is not allowed (allowed: {', '.join(sorted(ALLOWED_LICENCES))})")
        if credit_section(credits_text, section) is None:
            problems.append(f"{category}: docs/Credits.md has no '### {section}' section to attribute {folder}")
        if not (ROOT / folder).is_dir():
            problems.append(f"{category}: source folder {folder} not found")
    return problems


def collect():
    """[(key, source file, relative pack path)] sorted by key; raises ValueError for a missing stem or a non-PNG."""
    entries = []
    for category, folder, _, _, stems in GROUPS:
        source = ROOT / folder
        available = {p.stem: p for p in source.glob("*.png")}
        wanted = sorted(available) if stems is None else stems
        for stem in wanted:
            if f"{category}/{stem}" in EXCLUDED:
                continue
            if stem not in available:
                raise ValueError(f"{category}/{stem}: {folder}/{stem}.png does not exist")
            path = available[stem]
            if not path.read_bytes().startswith(PNG_MAGIC):
                raise ValueError(f"{path} is not a PNG")
            entries.append((f"{category}/{stem}", path, f"icons/{category.split('/')[1]}/{stem}.png"))
    entries.sort(key=lambda e: e[0])
    return entries


def render_manifest(entries):
    groups = {category: {"licence": licence, "credits": f"CREDITS.md#{section}"} for category, _, licence, section, _ in GROUPS}
    groups["unit"] = {"licence": "CC0-1.0", "credits": "CREDITS.md#Models"}
    doc = {"pack": "unciv", "keys": {**{key: rel for key, _, rel in entries}, **MODELS}, "groups": groups, "excluded": dict(sorted(EXCLUDED.items()))}
    return json.dumps(doc, indent=2, sort_keys=True, ensure_ascii=False) + "\n"


def render_credits(credits_text):
    lines = [
        "# Credits of the bundled `unciv` icon pack",
        "",
        "Generated by `Unciv.Godot/tools/import-unciv-icons.py` from Unciv's own images; the text below is copied verbatim from `docs/Credits.md` of the Unciv repository.",
        "Unless otherwise specified the icons are from [the Noun Project](https://thenounproject.com), licensed CC BY 3.0 (attribution required) or released into the Public Domain;",
        "Unciv's own work is CC BY 4.0. Licences: " + ", ".join(sorted(ALLOWED_LICENCES)) + ".",
        "",
    ]
    for section in dict.fromkeys(g[3] for g in GROUPS):
        lines += [f"## {section}", "", credit_section(credits_text, section), ""]
    return "\n".join(lines) + "\n" + MODEL_CREDITS


def main(argv):
    check = "--check" in argv
    if any(a not in ("--check",) for a in argv):
        print(__doc__)
        return 64
    if not CREDITS.is_file():
        print(f"missing {CREDITS}", file=sys.stderr)
        return 1
    credits_text = CREDITS.read_text(encoding="utf-8")
    problems = licence_problems(credits_text)
    if problems:
        print("licence check failed:\n  " + "\n  ".join(problems), file=sys.stderr)
        return 1
    try:
        entries = collect()
    except ValueError as e:
        print(f"import failed: {e}", file=sys.stderr)
        return 1
    manifest, credits = render_manifest(entries), render_credits(credits_text)
    if check:
        bad = []
        if not (PACK / "manifest.json").is_file() or (PACK / "manifest.json").read_text(encoding="utf-8") != manifest:
            bad.append("manifest.json is out of date")
        if not (PACK / "CREDITS.md").is_file() or (PACK / "CREDITS.md").read_text(encoding="utf-8") != credits:
            bad.append("CREDITS.md is out of date")
        for _, source, rel in entries:
            target = PACK / rel
            if not target.is_file() or target.read_bytes() != source.read_bytes():
                bad.append(f"{rel} is missing or differs from {source.relative_to(ROOT)}")
        extra = {p.relative_to(PACK).as_posix() for p in (PACK / "icons").rglob("*.png")} - {rel for _, _, rel in entries} if (PACK / "icons").is_dir() else set()
        bad += [f"{rel} is not in the manifest" for rel in sorted(extra)]
        bad += [f"{rel} is missing" for rel in MODELS.values() if not (PACK / rel).is_file()]
        if bad:
            print("pack check failed:\n  " + "\n  ".join(bad), file=sys.stderr)
            return 1
        print(f"pack ok: {len(entries)} icons, licences {sorted({g[2] for g in GROUPS})}")
        return 0
    wanted = {rel for _, _, rel in entries}
    if (PACK / "icons").is_dir():
        for stale in (PACK / "icons").rglob("*.png"):
            if stale.relative_to(PACK).as_posix() not in wanted:
                stale.unlink()
    for _, source, rel in entries:
        target = PACK / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source.read_bytes())
    (PACK / "manifest.json").write_text(manifest, encoding="utf-8")
    (PACK / "CREDITS.md").write_text(credits, encoding="utf-8")
    print(f"wrote {len(entries)} icons to {PACK.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
