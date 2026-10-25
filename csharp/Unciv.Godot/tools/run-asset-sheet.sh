#!/usr/bin/env bash
# C007 gate: build Unciv.Godot with -warnaserror, boot Godot headlessly (Xvfb + software GL), resolve every Vanilla asset key through the
# registry, render the swatch sheet and save it to out/asset-sheet.png. Exit 0 = every key resolved and the frame is not blank.
# The golden comparison of the sheet is added by the scenario runner (C008), which wraps this scene as scenario "asset-sheet".
#
# Run inside the dotnet-dev container (see csharp/migration/PROTOCOL.md). Needs Unciv.Godot.Harness/tools/setup-godot.sh to have run once.
set -euo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/../Unciv.Godot.Harness/tools/setup-godot.sh" --print-path)}"
out="${ASSET_SHEET_OUT:-$project/out}"
mkdir -p "$out"

(cd "$project/.." && dotnet build Unciv.Godot -warnaserror)

timeout "${ASSET_SHEET_TIMEOUT:-180}" xvfb-run -a -s "-screen 0 1280x720x24" \
    "$godot" --path "$project" --rendering-driver opengl3 --resolution 1280x720 res://Assets/AssetSheet.tscn \
    -- --out="$out"
