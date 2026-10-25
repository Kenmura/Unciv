#!/usr/bin/env bash
# C042: export the client for one platform and bundle its data. Usage: tools/package.sh [linux|windows] (default linux).
# Output: Unciv.Godot/build/<platform>/ holding the executable, data/jsons (rulesets + translations), LICENSE, THIRD_PARTY_NOTICES.md.
# Needs the Godot editor (Unciv.Godot.Harness/tools/setup-godot.sh) and the matching export templates (tools/install-export-templates.sh).
# Local mods are not bundled: players drop mod folders into the user data folder's mods directory (user://mods).
set -euo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
platform="${1:-linux}"
case "$platform" in
    linux) preset="Linux"; exe="Unciv.x86_64" ;;
    windows) preset="Windows"; exe="Unciv.exe" ;;
    *) echo "unknown platform '$platform' (linux|windows)" >&2; exit 2 ;;
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
"$godot" --headless --path "$project" --export-release "$preset" "$out/$exe"
if ! compgen -G "$out/data_*" >/dev/null; then echo "export produced no .NET assemblies folder (data_*): see the Godot errors above" >&2; exit 1; fi
mkdir -p "$out/data"
cp -r "$repo/android/assets/jsons" "$out/data/jsons"
cp "$repo/LICENSE" "$out/LICENSE"
cp "$project/THIRD_PARTY_NOTICES.md" "$out/THIRD_PARTY_NOTICES.md"
echo "Packaged $platform build in $out"
