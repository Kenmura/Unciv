#!/usr/bin/env bash
# C008: run every scenario listed in scenarios.txt (a failing one does not stop the others) and build the owner's review
# artefacts: out/review/contact-sheet.png (every captured step with its caption) and out/review/index.md (checklist per slice).
# Exit 0 = all scenarios passed and the sheet was built; the sheet is built either way so the owner can see a failure.
#
#   tools/review-sheet.sh [scenario ...]    default: every scenario in scenarios.txt
set -uo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$("$project/tools/setup-godot.sh" --print-path)}"
export HARNESS_OUT="${HARNESS_OUT:-$project/out}"
out="$HARNESS_OUT"

if [[ $# -gt 0 ]]; then scenarios=("$@"); else mapfile -t scenarios < <(grep -v '^[[:space:]]*\(#\|$\)' "$project/scenarios.txt"); fi
if [[ ${#scenarios[@]} -eq 0 ]]; then echo "no scenarios" >&2; exit 64; fi

(cd "$project" && dotnet build -warnaserror) || exit 1
export HARNESS_SKIP_BUILD=1

status=0
for s in "${scenarios[@]}"; do
    echo "== scenario $s"
    "$project/tools/run-scenario.sh" "$s" || { echo "scenario $s FAILED" >&2; status=1; }
done

list="$(IFS=,; echo "${scenarios[*]}")"
timeout "${HARNESS_TIMEOUT:-120}" xvfb-run -a -s "-screen 0 1280x720x24" \
    "$godot" --path "$project" --rendering-driver opengl3 --resolution 640x480 res://ContactSheet.tscn \
    -- --out="$out" --scenarios="$list" || { echo "contact sheet FAILED" >&2; status=1; }

echo "review: $out/review/index.md and $out/review/contact-sheet.png"
exit $status
