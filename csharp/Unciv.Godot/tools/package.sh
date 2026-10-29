#!/usr/bin/env bash
# C042: export the client for one platform and bundle its data. Usage: tools/package.sh [linux|windows|macos] (default linux).
# Output: Unciv.Godot/build/<platform>/ holding the executable, data/jsons (rulesets + translations), LICENSE, THIRD_PARTY_NOTICES.md
# (macos: the same files inside Unciv.app/Contents/MacOS, next to the executable, plus FIRST_RUN_MACOS.txt), and, T258, the release archive
# Unciv.Godot/build/Unciv-client-<platform>.<ext> (linux .tar.gz, windows/macos .zip, top-level folder Unciv-<platform>/). T279 attaches these to a release.
# macOS is exported unsigned and un-notarised from Linux (Godot zips the .app); it has never been run by us (see GODOT.md).
# Needs the Godot editor (Unciv.Godot.Harness/tools/setup-godot.sh) and the matching export templates (tools/install-export-templates.sh).
# Local mods are not bundled: players drop mod folders into the user data folder's mods directory (user://mods).
set -euo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
platform="${1:-linux}"
case "$platform" in
    linux) preset="Linux"; exe="Unciv.x86_64" ;;
    windows) preset="Windows"; exe="Unciv.exe" ;;
    macos) preset="macOS"; exe="Unciv.app/Contents/MacOS/Unciv" ;;
    *) echo "unknown platform '$platform' (linux|windows|macos)" >&2; exit 2 ;;
esac
godot="${GODOT:-$("$project/../Unciv.Godot.Harness/tools/setup-godot.sh" --print-path)}"
repo="$(cd "$project/../.." && pwd)"
out="$project/build/$platform"

rm -rf "$out"
mkdir -p "$out"
(cd "$project/.." && dotnet build Unciv.Godot -warnaserror -c Release)
# Godot's .NET export insists on a solution file next to project.godot; generate a throw-away one (git-ignored) rather than keep one in the tree.
if [[ ! -f "$project/Unciv.Godot.sln" ]]; then
    (cd "$project" && dotnet new sln -n Unciv.Godot --format sln >/dev/null && dotnet sln Unciv.Godot.sln add Unciv.Godot.csproj >/dev/null)
fi
if [[ "$platform" == macos ]]; then
    # the preset exports a zip holding Unciv.app; unpack it so the content can go beside the executable like on the other platforms
    "$godot" --headless --path "$project" --export-release "$preset" "$out/Unciv.zip"
    (cd "$out" && unzip -q Unciv.zip && rm Unciv.zip)
    [[ -f "$out/$exe" ]] || { echo "macOS export produced no $exe" >&2; exit 1; }
    chmod +x "$out/$exe"
    root="$out/Unciv.app/Contents/MacOS"
else
    "$godot" --headless --path "$project" --export-release "$preset" "$out/$exe"
    root="$out"
fi
if ! compgen -G "$root/data_*" >/dev/null && ! compgen -G "$out/Unciv.app/Contents/Resources/data_*" >/dev/null; then echo "export produced no .NET assemblies folder (data_*): see the Godot errors above" >&2; exit 1; fi
mkdir -p "$root/data"
cp -r "$repo/android/assets/jsons" "$root/data/jsons"
# C025: fonts for non-Latin languages are read from disk (FontChain), so they ship next to the executable, with their licence.
mkdir -p "$root/Assets"
cp -r "$project/Assets/fonts" "$root/Assets/fonts"
rm -f "$root/Assets/fonts/subset-fonts.py" "$root"/Assets/fonts/*.import
# C063: the bundled icon pack (PNGs and manifest, read from disk by GodotAssetFiles; .gdignore keeps it out of the pck) ships next to the executable with its credits.
mkdir -p "$root/Packs"
cp -r "$project/Packs/unciv" "$root/Packs/unciv"
rm -f "$root/Packs/unciv/.gdignore"
# C083: the bundled unit models pack (glb files and manifest), same way
cp -r "$project/Packs/units" "$root/Packs/units"
cp "$repo/LICENSE" "$root/LICENSE"
cp "$project/THIRD_PARTY_NOTICES.md" "$root/THIRD_PARTY_NOTICES.md"
if [[ "$platform" == macos ]]; then
    cat > "$out/FIRST_RUN_MACOS.txt" <<'TXT'
Unciv (Godot client) for macOS: unsigned, not notarised, never tested by the developers on a Mac.
Gatekeeper will refuse the first launch ("cannot be opened because the developer cannot be verified" or "is damaged").
Move Unciv.app out of the download folder, then either right-click it and choose Open (twice if asked), or run in Terminal:
    xattr -dr com.apple.quarantine Unciv.app
Universal binary (Apple Silicon and Intel). If it fails to start, run Unciv.app/Contents/MacOS/Unciv from Terminal and report the output.
TXT
fi
# T258: the release archive (T279 uploads these). One top-level folder so extraction never spills into the current directory.
archive_dir="$project/build"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
ln -s "$out" "$stage/Unciv-$platform"
if [[ "$platform" == linux ]]; then
    archive="$archive_dir/Unciv-client-linux.tar.gz"
    rm -f "$archive"
    tar -C "$stage" -czhf "$archive" "Unciv-$platform"
else
    archive="$archive_dir/Unciv-client-$platform.zip"
    rm -f "$archive"
    (cd "$stage" && zip -qr "$archive" "Unciv-$platform")
fi
[[ -s "$archive" ]] || { echo "archive $archive is empty" >&2; exit 1; }
echo "Archive $archive ($(du -h "$archive" | cut -f1))"
echo "Packaged $platform build in $out"
