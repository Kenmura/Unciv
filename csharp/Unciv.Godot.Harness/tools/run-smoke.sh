#!/usr/bin/env bash
# C000 gate: build the Godot harness project, boot Godot headlessly (Xvfb + software GL), render the smoke scene, capture a
# PNG, check the pixels/picking and compare against the golden image. Exit 0 = pass.
#
#   tools/run-smoke.sh                  run the gate
#   tools/run-smoke.sh --update-golden  re-record golden/smoke.png (review the PNG before committing)
#
# Run inside the dotnet-dev container (see csharp/migration/PROTOCOL.md). Needs tools/setup-godot.sh to have run once.
set -euo pipefail

# T361 (T360 memory settings; the env vars alone do not cap the nodes, so every dotnet build below also passes -m: and -nodeReuse:false): one MSBuild node, no node reuse, no build server, no shared compiler, workstation GC; the servers dotnet may leave behind are shut down on exit
export MSBUILDDISABLENODEREUSE=1 MSBUILDNODECOUNT="${UNCIV_MSBUILD_NODES:-1}" MSBUILDUSESERVER=0 UseSharedCompilation=false DOTNET_gcServer=0
trap 'dotnet build-server shutdown >/dev/null 2>&1 || true' EXIT

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
out="${HARNESS_OUT:-$project/out}"
mkdir -p "$out"

(cd "$project" && dotnet build -m:"${UNCIV_MSBUILD_NODES:-1}" -nodeReuse:false -p:UseSharedCompilation=false -warnaserror)

golden_args=(--golden="$project/golden/smoke.png")
[[ "${1:-}" == "--update-golden" ]] && golden_args+=(--update-golden)

# Fixed 640x480 viewport on a virtual 24-bit screen; the Compatibility renderer runs on Mesa llvmpipe, no GPU needed.
timeout "${HARNESS_TIMEOUT:-120}" xvfb-run -a -s "-screen 0 1280x720x24" \
    "$godot" --path "$project" --rendering-driver opengl3 --resolution 640x480 \
    -- --out="$out" "${golden_args[@]}"
