#!/usr/bin/env bash
# T377: the real-GPU border review sheet (scenario borders-sheet, see BordersSheetScenario.cs): a mixed map with five territories at several window sizes and zooms, one PNG each.
# NOT part of CI and never writes goldens: it needs a real display and GPU. The same host rules as scale-sheet.sh apply (host, not dotnet-dev; DOTNET_ROOT copy of the container's
# /usr/lib64/dotnet; build first inside dotnet-dev: (cd Unciv.Godot.Harness && dotnet build)).
#
#   tools/borders-sheet.sh <out-dir> [sizes] [zooms]     sizes: comma separated WxH, zooms: comma separated camera sizes (default 6,10,14,24,40)
set -euo pipefail
[[ $# -ge 1 ]] || { echo "usage: $0 <out-dir> [sizes] [zooms]" >&2; exit 64; }
out="$(mkdir -p "$1" && cd "$1" && pwd)"
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
export UNCIV_BORDERS_OUT="$out"
[[ -n "${2:-}" ]] && export UNCIV_BORDERS_RES="$2"
[[ -n "${3:-}" ]] && export UNCIV_BORDERS_ZOOMS="$3"
timeout "${HARNESS_TIMEOUT:-600}" "$godot" --path "$project" --rendering-method forward_plus --rendering-driver vulkan --disable-vsync res://ScenarioRunner.tscn \
    -- --scenario=borders-sheet --out="$out/runner" --golden-dir="$out/no-goldens" 2>&1 | grep -E "^\[(borders-sheet|scenario)\]|SCRIPT ERROR|ERROR:" || true
ls "$out"/borders-*.png
