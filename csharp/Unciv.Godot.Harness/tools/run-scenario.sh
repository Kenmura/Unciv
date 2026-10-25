#!/usr/bin/env bash
# C008 gate: run one client scenario headlessly (Xvfb + software GL). The scenario drives a seeded GameSession through the real
# UI (clicks, keys), asserts engine state first, captures PNGs second and compares each to golden/<scenario>/<step>.png. The
# state hash it ends with must equal the headless replay of its command log (checked in-process, no Godot code involved).
#
#   tools/run-scenario.sh <name>                         run it; exit 0 = pass
#   tools/run-scenario.sh <name> --update-golden         re-record goldens (review the PNGs before committing)
#   tools/run-scenario.sh <name> --expect-golden-failure self-test: against deliberately wrong goldens the run must FAIL
#
# Output: out/<name>/<step>.png and out/<name>/result.json (git-ignored). Run inside dotnet-dev (see csharp/migration/GODOT.md).
set -euo pipefail

if [[ $# -lt 1 || "$1" == -* ]]; then echo "usage: $0 <scenario> [--update-golden | --expect-golden-failure]" >&2; exit 64; fi
name="$1"; mode="${2:-}"
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
out="${HARNESS_OUT:-$project/out}"
golden="$project/golden"
mkdir -p "$out"

if [[ -z "${HARNESS_SKIP_BUILD:-}" ]]; then (cd "$project" && dotnet build -warnaserror); fi

extra=()
case "$mode" in
    "") ;;
    --update-golden) extra+=(--update-golden) ;;
    --expect-golden-failure)
        # Every golden of the scenario replaced by one unrelated image: a comparison that cannot fail would pass here
        golden="$(mktemp -d)"; trap 'rm -rf "$golden"' EXIT
        mkdir -p "$golden/$name"
        for g in "$project/golden/$name"/*.png; do cp "$project/golden/smoke.png" "$golden/$name/$(basename "$g")"; done ;;
    *) echo "unknown option $mode" >&2; exit 64 ;;
esac

run() {
    timeout "${HARNESS_TIMEOUT:-120}" xvfb-run -a -s "-screen 0 1280x720x24" \
        "$godot" --path "$project" --rendering-driver opengl3 --resolution 640x480 res://ScenarioRunner.tscn \
        -- --scenario="$name" --out="$out" --golden-dir="$golden" "${extra[@]}"
}

if [[ "$mode" == "--expect-golden-failure" ]]; then
    if run; then echo "SELF-TEST FAILED: a wrong golden was accepted" >&2; exit 1; fi
    echo "self-test ok: wrong goldens were rejected"
else
    run
fi
