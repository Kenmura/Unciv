#!/usr/bin/env python3
"""T279: the pieces of .github/workflows/csharp-release.yml that are plain logic, so they can be run and tested locally.

    release_assets.py check-tag TAG                         exit 1 unless TAG is csharp-vMAJOR.MINOR.PATCH[-prerelease]; prints the version
    release_assets.py pack-cli RID PUBLISH_DIR OUT_DIR      archive a `dotnet publish` folder (+ data/jsons, LICENSE) as Unciv-cli-<RID>.tar.gz|.zip
    release_assets.py smoke-cli CLI_DIR                     run the published Linux CLI with an empty PATH (starts a tiny game), like release.py
    release_assets.py sums DIR                              write DIR/SHA256SUMS (sha256sum format) for every file in DIR
    release_assets.py verify DIR                            exit 1 unless DIR holds exactly the expected archives (+ SHA256SUMS) and each client archive passes check_package.py
    release_assets.py notes TAG COMMIT DATE OUT_FILE        write the release notes (what is verified, what is UNVERIFIED)

Archive names are stable across releases (the tag identifies the version): the client names come from check_package.PLATFORMS (T258), the CLI ones are below.
The workflow never builds a release from a tag that is not a version tag; it creates a DRAFT only and the owner publishes it by hand (migration/GODOT.md).
"""
import hashlib
import importlib.util
import os
import re
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
CSHARP = HERE.parents[1]
REPO = CSHARP.parent

TAG_RE = re.compile(r"csharp-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z][0-9A-Za-z.-]*)?")
CLI_RIDS = ["linux-x64", "win-x64", "osx-arm64"]  # self-contained `dotnet publish -r <rid>` (cross-published on the Linux runner)
SUMS = "SHA256SUMS"


def _load(name):
    spec = importlib.util.spec_from_file_location(name, HERE / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def cli_archive_name(rid):
    return f"Unciv-cli-{rid}.{'zip' if rid.startswith('win-') else 'tar.gz'}"


def client_archive_names():
    """{platform: archive name}, in release.CLIENT_PLATFORMS order."""
    platforms = _load("check_package").PLATFORMS
    return {p: platforms[p][0] for p in _load("release").CLIENT_PLATFORMS}


def expected_assets():
    """Every file the release must carry, without SHA256SUMS."""
    return list(client_archive_names().values()) + [cli_archive_name(r) for r in CLI_RIDS]


def check_tag(tag):
    """The version in a release tag, or None."""
    m = TAG_RE.fullmatch(tag)
    return tag[len("csharp-v"):] if m else None


def pack_cli(rid, publish_dir, out_dir, jsons=None, license_file=None):
    """Archive `publish_dir` (output of dotnet publish) plus data/jsons and LICENSE under one top folder Unciv-cli-<rid>/. Returns the archive path."""
    if rid not in CLI_RIDS:
        raise SystemExit(f"unknown runtime id {rid} ({', '.join(CLI_RIDS)})")
    publish_dir = Path(publish_dir)
    jsons = Path(jsons) if jsons else REPO / "android" / "assets" / "jsons"
    license_file = Path(license_file) if license_file else REPO / "LICENSE"
    exe = "Unciv.Cli.exe" if rid.startswith("win-") else "Unciv.Cli"
    if not (publish_dir / exe).is_file():
        raise SystemExit(f"{publish_dir} has no {exe}: not a dotnet publish output for {rid}")
    if not (jsons / "ModCategories.json").is_file():
        raise SystemExit(f"{jsons} is not the rulesets folder (no ModCategories.json)")
    top = f"Unciv-cli-{rid}"
    entries = []  # (path in archive, source file)
    for base, prefix in ((publish_dir, top), (jsons, f"{top}/data/jsons")):
        for p in sorted(base.rglob("*")):
            if p.is_file() and not p.is_symlink() and not p.name.endswith(".pdb"):
                entries.append((f"{prefix}/{p.relative_to(base).as_posix()}", p))
    entries.append((f"{top}/LICENSE", license_file))
    out = Path(out_dir) / cli_archive_name(rid)
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.suffix == ".zip":
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
            for arc, src in entries:
                z.write(src, arc)
    else:
        with tarfile.open(out, "w:gz") as t:
            for arc, src in entries:
                info = t.gettarinfo(str(src), arc)
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                info.mode = 0o755 if arc == f"{top}/{exe}" or os.access(src, os.X_OK) else 0o644
                with open(src, "rb") as f:
                    t.addfile(info, f)
    return out


def smoke_cli(cli_dir):
    """Problems (empty when fine): the published Linux CLI starts a tiny game with no dotnet on PATH, from a folder outside any checkout."""
    with tempfile.TemporaryDirectory() as work:
        env = {"PATH": "/usr/bin:/bin", "HOME": work, "DOTNET_ROOT": ""}
        save = Path(work) / "smoke.json"
        r = subprocess.run([str(Path(cli_dir) / "Unciv.Cli"), "new", "--out", str(save), "--map-size", "Tiny"], cwd=work, env=env, capture_output=True, text=True)
        if r.returncode or not save.exists():
            return [f"the published CLI does not run on its own (exit {r.returncode}): {(r.stdout + r.stderr)[-800:]}"]
    return []


def write_sums(directory):
    """Write SHA256SUMS ('<hex>  <name>' per file, sorted) for every file in the directory. Returns the lines."""
    directory = Path(directory)
    lines = []
    for p in sorted(directory.iterdir()):
        if p.is_file() and p.name != SUMS:
            lines.append(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}")
    (directory / SUMS).write_text("\n".join(lines) + "\n", encoding="utf-8")
    return lines


def verify(directory):
    """Problems (empty when fine): the file set is exactly the expected one plus SHA256SUMS, the sums match, client archives pass check_package."""
    directory = Path(directory)
    problems = []
    have = sorted(p.name for p in directory.iterdir() if p.is_file()) if directory.is_dir() else []
    want = sorted(expected_assets() + [SUMS])
    if have != want:
        problems.append(f"files differ: missing {sorted(set(want) - set(have))}, unexpected {sorted(set(have) - set(want))}")
        return problems
    for line in (directory / SUMS).read_text(encoding="utf-8").splitlines():
        digest, _, name = line.partition("  ")
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != digest:
            problems.append(f"{name}: checksum does not match {SUMS}")
    checker = _load("check_package")
    for platform, name in client_archive_names().items():
        problems += [f"{name}: {p}" for p in checker.check(platform, directory / name)]
    return problems


def notes(tag, commit, date):
    version = check_tag(tag)
    if version is None:
        raise SystemExit(f"{tag} is not a csharp-vX.Y.Z tag")
    clients = client_archive_names()
    return f"""Unciv Godot client and CLI, version {version} (tag `{tag}`).

- Commit: `{commit}`
- Built: {date} UTC by the `csharp-release` workflow (GitHub Actions, Ubuntu runner; every platform is cross-exported from Linux)
- Checksums: `{SUMS}` (`sha256sum -c {SUMS}` after downloading the files you want)

## What was tested, and what was not

| Archive | Status |
| --- | --- |
| `{clients['linux']}` | Smoke-tested in this workflow: started under Xvfb from a folder outside the checkout, reached the main menu. |
| `{cli_archive_name('linux-x64')}` | Smoke-tested in this workflow: started a tiny game with no .NET installed. |
| `{clients['windows']}` | **UNVERIFIED.** Exported and archive-checked (files present, sizes plausible) only. Never started on Windows. SmartScreen may warn. |
| `{clients['macos']}` | **UNVERIFIED.** Exported and archive-checked only. Never started on a Mac. Unsigned and not notarised: Gatekeeper blocks the first launch, see `FIRST_RUN_MACOS.txt` inside. |
| `{cli_archive_name('win-x64')}` | **UNVERIFIED.** Published self-contained, never run on Windows. |
| `{cli_archive_name('osx-arm64')}` | **UNVERIFIED.** Published self-contained, never run on a Mac. Unsigned and not notarised. |

This is a draft created by CI. The owner reviews it and publishes it by hand; nothing was published automatically.
"""


def main(argv):
    cmd = argv[0] if argv else ""
    args = argv[1:]
    if cmd == "check-tag" and len(args) == 1:
        version = check_tag(args[0])
        if version is None:
            print(f"'{args[0]}' is not a release tag (csharp-vMAJOR.MINOR.PATCH or csharp-vMAJOR.MINOR.PATCH-prerelease)", file=sys.stderr)
            return 1
        print(version)
        return 0
    if cmd == "pack-cli" and len(args) == 3:
        print(pack_cli(args[0], args[1], args[2]))
        return 0
    if cmd == "smoke-cli" and len(args) == 1:
        problems = smoke_cli(args[0])
    elif cmd == "sums" and len(args) == 1:
        print("\n".join(write_sums(args[0])))
        return 0
    elif cmd == "verify" and len(args) == 1:
        problems = verify(args[0])
    elif cmd == "notes" and len(args) == 4:
        Path(args[3]).write_text(notes(args[0], args[1], args[2]), encoding="utf-8")
        return 0
    else:
        print(__doc__, file=sys.stderr)
        return 2
    for p in problems:
        print(f"FAIL: {p}", file=sys.stderr)
    if not problems:
        print("OK")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
