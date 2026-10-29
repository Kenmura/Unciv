#!/usr/bin/env bash
# C008 gate: run one client scenario headlessly (Xvfb + software GL). The scenario drives a seeded GameSession through the real
# UI (clicks, keys), asserts engine state first, captures PNGs second and compares each to golden/<scenario>/<step>.png. The
# state hash it ends with must equal the headless replay of its command log (checked in-process, no Godot code involved).
#
#   tools/run-scenario.sh <name>                         run it; exit 0 = pass
#   tools/run-scenario.sh <name> --update-golden         re-record goldens (review the PNGs before committing)
#   tools/run-scenario.sh <name> --expect-golden-failure self-test: against deliberately wrong goldens the run must FAIL
#
# HARNESS_GOLDEN_TOLERANCE=renderer compares with the cross-renderer tolerance of GoldenTolerance.Renderer (CI, T246); default strict.
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
[[ -n "${HARNESS_GOLDEN_TOLERANCE:-}" ]] && extra+=("--golden-tolerance=$HARNESS_GOLDEN_TOLERANCE")
case "$mode" in
    "") ;;
    --update-golden) extra+=(--update-golden) ;;
    --expect-golden-failure)
        # Every golden of the scenario replaced by one unrelated image: a comparison that cannot fail would pass here
        golden="$(mktemp -d)"; cleanup_golden="$golden"
        mkdir -p "$golden/$name"
        for g in "$project/golden/$name"/*.png; do cp "$project/golden/smoke.png" "$golden/$name/$(basename "$g")"; done ;;
    *) echo "unknown option $mode" >&2; exit 64 ;;
esac

# C058: own X server per run, display number chosen by Xvfb itself (-displayfd). `xvfb-run -a` picks a free number then starts the server, so two
# runs starting together (several agents share this machine) could get the same display and kill each other's server mid-scenario.
xvfb_pid=""; godot_pid=""; display_file=""; cleanup_golden=""
cleanup() {
    [[ -n "$godot_pid" ]] && { kill "$godot_pid" 2>/dev/null || true; wait "$godot_pid" 2>/dev/null || true; }
    [[ -n "$xvfb_pid" ]] && { kill "$xvfb_pid" 2>/dev/null || true; wait "$xvfb_pid" 2>/dev/null || true; }
    [[ -n "$display_file" ]] && rm -f "$display_file"
    [[ -n "$cleanup_golden" ]] && rm -rf "$cleanup_golden"
    return 0
}
# also on Ctrl-C / kill of the script alone (the signal would otherwise leave the Xvfb it started running)
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

run() {
    local display="" status
    display_file="$(mktemp)"
    Xvfb -displayfd 3 -nolisten tcp -screen 0 1280x720x24 3>"$display_file" 2>/dev/null &
    xvfb_pid=$!
    for _ in $(seq 100); do
        display="$(head -n1 "$display_file")"
        [[ -n "$display" ]] && break
        sleep 0.1
    done
    if [[ -z "$display" ]]; then echo "Xvfb did not start" >&2; return 1; fi
    # background + wait, so a TERM/INT sent to this script alone runs the cleanup trap at once instead of after the scenario ends
    DISPLAY=":$display" timeout "${HARNESS_TIMEOUT:-120}" \
        "$godot" --path "$project" --rendering-driver opengl3 --resolution 640x480 res://ScenarioRunner.tscn \
        -- --scenario="$name" --out="$out" --golden-dir="$golden" "${extra[@]}" &
    godot_pid=$!
    status=0
    wait "$godot_pid" || status=$?
    godot_pid=""
    return "$status"
}

if [[ "$mode" == "--expect-golden-failure" ]]; then
    if run; then echo "SELF-TEST FAILED: a wrong golden was accepted" >&2; exit 1; fi
    echo "self-test ok: wrong goldens were rejected"
else
    run
fi
