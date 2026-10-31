#!/usr/bin/env bash
# T295: the real-GPU recognisability sheet (scenario unit-sheet, see UnitSheetScenario.cs): every unit side by side at the game's default zoom, one PNG per page (at most 44 units, one figure each; UNCIV_SHEET_FORMATIONS=1 draws the formations, UNCIV_SHEET_ZOOM sets the camera size, the game's default is 14), plus index.md.
# NOT part of CI and never writes goldens: it needs a real display and GPU. The same host rules as gpu-bench.sh apply (host, not dotnet-dev; DOTNET_ROOT copy of the container's /usr/lib64/dotnet;
# build first inside dotnet-dev: (cd Unciv.Godot.Harness && dotnet build)).
#
#   tools/unit-sheet.sh <out-dir> [zoom] [yaw-degrees]
set -euo pipefail
# T309: fail fast (Godot hangs for the whole timeout without output when it finds no dotnet)
if [[ -n "${DOTNET_ROOT:-}" && -x "$DOTNET_ROOT/dotnet" ]]; then :; elif ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet not found: set DOTNET_ROOT to a copy of the dotnet-dev container's /usr/lib64/dotnet (see the header)" >&2; exit 69
fi
[[ $# -ge 1 ]] || { echo "usage: $0 <out-dir> [zoom] [yaw-degrees]" >&2; exit 64; }
out="$(mkdir -p "$1" && cd "$1" && pwd)"
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
export UNCIV_SHEET_OUT="$out"
[[ -n "${2:-}" ]] && export UNCIV_SHEET_ZOOM="$2"
[[ -n "${3:-}" ]] && export UNCIV_SHEET_YAW="$3"
timeout "${HARNESS_TIMEOUT:-600}" "$godot" --path "$project" --rendering-method forward_plus --rendering-driver vulkan --disable-vsync res://ScenarioRunner.tscn \
    -- --scenario=unit-sheet --out="$out/runner" --golden-dir="$out/no-goldens" 2>&1 | grep -E "^\[(unit-sheet|scenario)\]|SCRIPT ERROR|ERROR:" || true
ls "$out"/unit-sheet-*.png
