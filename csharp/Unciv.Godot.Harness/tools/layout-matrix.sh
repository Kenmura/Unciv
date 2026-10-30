#!/usr/bin/env bash
# T312: the multi-resolution layout matrix (scenario layout-matrix, see LayoutMatrixScenario.cs) on the real GPU: every main screen at 1366x768, 1920x1080, 2560x1440, 3440x1440 and 3840x2160,
# checks from the real controls plus one PNG per screen and size. Same host rules as scale-sheet.sh (host, not dotnet-dev; build first inside dotnet-dev). Without a display use run-scenario.sh layout-matrix (asserts only).
#
#   tools/layout-matrix.sh <out-dir> [sizes]      sizes: comma separated WxH or WxH@osScale
set -euo pipefail
[[ $# -ge 1 ]] || { echo "usage: $0 <out-dir> [sizes]" >&2; exit 64; }
out="$(mkdir -p "$1" && cd "$1" && pwd)"
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
export UNCIV_MATRIX_OUT="$out"
[[ -n "${2:-}" ]] && export UNCIV_MATRIX_RES="$2"
timeout "${HARNESS_TIMEOUT:-900}" "$godot" --path "$project" --rendering-method forward_plus --rendering-driver vulkan --disable-vsync res://ScenarioRunner.tscn \
    -- --scenario=layout-matrix --out="$out/runner" --golden-dir="$out/no-goldens" 2>&1 | grep -E "^\[(layout-matrix|scenario)\]|SCRIPT ERROR|ERROR:" || true
ls "$out"/layout-*.png | wc -l
