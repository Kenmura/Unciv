#!/usr/bin/env bash
# C042 gate: run the packaged Linux build from a fresh folder OUTSIDE the repository checkout (and with an empty home/user data folder) and check
# that it reaches the main menu. Usage: tools/smoke-export.sh [build-dir]  (default Unciv.Godot/build/linux, run tools/package.sh first).
set -euo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
build="${1:-$project/build/linux}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp -r "$build" "$work/app"
mkdir -p "$work/home"

set +e
log="$(HOME="$work/home" XDG_DATA_HOME="$work/home/.local/share" timeout "${SMOKE_TIMEOUT:-120}" \
    xvfb-run -a -s "-screen 0 1280x720x24" "$work/app/Unciv.x86_64" --rendering-driver opengl3 -- --exit-on-menu 2>&1)"
code=$?
set -e
echo "$log" | tail -20
if [[ $code -ne 0 ]]; then echo "FAIL: exit code $code" >&2; exit 1; fi
if ! grep -q "UNCIV_MENU_READY" <<<"$log"; then echo "FAIL: main menu marker not printed" >&2; exit 1; fi
if [[ ! -d "$work/home/.local/share/godot/app_userdata" ]] || ! find "$work/home/.local/share/godot/app_userdata" -type d -name mods | grep -q .; then
    echo "FAIL: user mods folder was not created" >&2; exit 1
fi
echo "OK: exported build reached the main menu from $work/app (outside the checkout)"
