#!/usr/bin/env bash
# T311: the UI scale sheet (scenario scale-sheet, see ScaleSheetScenario.cs): the game screen and its menu at each common window size (and OS display scale), rendered off-screen, one PNG each plus index.md.
# NOT part of CI and never writes goldens: it needs a real display and GPU. The same host rules as unit-sheet.sh apply (host, not dotnet-dev; DOTNET_ROOT copy of the container's /usr/lib64/dotnet;
# build first inside dotnet-dev: (cd Unciv.Godot.Harness && dotnet build)).
#
#   tools/scale-sheet.sh <out-dir> [sizes]      sizes: comma separated WxH or WxH@osScale, default 1366x768,1920x1080,1920x1200,2560x1440,3440x1440,3840x2160,3840x2160@2
set -euo pipefail
[[ $# -ge 1 ]] || { echo "usage: $0 <out-dir> [sizes]" >&2; exit 64; }
out="$(mkdir -p "$1" && cd "$1" && pwd)"
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
export UNCIV_SCALE_OUT="$out"
[[ -n "${2:-}" ]] && export UNCIV_SCALE_RES="$2"
timeout "${HARNESS_TIMEOUT:-600}" "$godot" --path "$project" --rendering-method forward_plus --rendering-driver vulkan --disable-vsync res://ScenarioRunner.tscn \
    -- --scenario=scale-sheet --out="$out/runner" --golden-dir="$out/no-goldens" 2>&1 | grep -E "^\[(scale-sheet|scenario)\]|SCRIPT ERROR|ERROR:" || true
ls "$out"/scale-*.png
