#!/usr/bin/env bash
# T271: real-GPU frame-time and look pass (scenario gpu-bench, see GpuBenchScenario.cs). NOT part of CI and never writes goldens: it needs a real display and GPU.
#
#   tools/gpu-bench.sh <forward_plus|gl_compatibility> <out-dir> [gpu-index|nvidia|intel]
#
# forward_plus runs Vulkan (--gpu-index picks the device; the index order is Godot's, the adapter name is printed in <out>/info.txt).
# gl_compatibility runs OpenGL 3 on the display's GL vendor; "nvidia" / "intel" select it through the PRIME offload variables.
# Environment: UNCIV_BENCH_MODE (levels|ablation|warrior, T294: pictures of the Warrior, see UNIT-STYLE.md), UNCIV_BENCH_SIZE, UNCIV_BENCH_RES, UNCIV_BENCH_FRAMES, UNCIV_BENCH_VIEWS, UNCIV_BENCH_SEQ, UNCIV_BENCH_CLEAN (see GpuBenchScenario; the default view list now also has coast30, coast55, coast75, T289).
# Run on the host (not in dotnet-dev, which has no NVIDIA userspace): the GPU needs the host driver. Godot .NET needs a dotnet runtime there: set DOTNET_ROOT to a copy of
# the container's /usr/lib64/dotnet (dotnet, host/, shared/Microsoft.NETCore.App/<version>). Build first inside dotnet-dev: (cd Unciv.Godot.Harness && dotnet build).
# Use a quiet machine (check uptime): timings under load are noisy; repeat and report the spread.
set -euo pipefail
[[ $# -ge 2 ]] || { echo "usage: $0 <forward_plus|gl_compatibility> <out-dir> [gpu-index|nvidia|intel]" >&2; exit 64; }
method="$1"; out="$(mkdir -p "$2" && cd "$2" && pwd)"; device="${3:-}"
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
args=(--path "$project" --rendering-method "$method" --disable-vsync)
case "$method" in
    forward_plus) args+=(--rendering-driver vulkan); [[ "$device" =~ ^[0-9]+$ ]] && args+=(--gpu-index "$device") ;;
    gl_compatibility) args+=(--rendering-driver opengl3)
        case "$device" in
            nvidia) export __NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia ;;
            intel) export DRI_PRIME=0 __GLX_VENDOR_LIBRARY_NAME=mesa ;;
        esac ;;
    *) echo "unknown renderer $method" >&2; exit 64 ;;
esac
UNCIV_BENCH_OUT="$out" timeout "${HARNESS_TIMEOUT:-1500}" "$godot" "${args[@]}" res://ScenarioRunner.tscn -- --scenario=gpu-bench --out="$out/runner" --golden-dir="$out/no-goldens" 2>&1 | grep -E "^\[(bench|scenario)\]|ERROR: .*(shader|Shader)|SCRIPT ERROR"
