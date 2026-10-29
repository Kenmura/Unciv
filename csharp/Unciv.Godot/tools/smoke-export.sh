#!/usr/bin/env bash
# C042 gate: run the packaged Linux build from a fresh folder OUTSIDE the repository checkout (and with an empty home/user data folder) and check
# that it reaches the main menu. Usage: tools/smoke-export.sh [--bundle] [build-dir]  (default Unciv.Godot/build/linux, run tools/package.sh first).
# T276 --bundle: simulate the Android case (no data folder beside the executable): data/, Packs/ and Assets/ are deleted from the copy and the content comes from a bundle
# made by tools/stage-content.py (UNCIV_CONTENT_ZIP), extracted to user://content; the log must show the jsons loaded and the extracted folder must exist.
set -euo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bundle=0
if [[ "${1:-}" == "--bundle" ]]; then bundle=1; shift; fi
build="${1:-$project/build/linux}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp -r "$build" "$work/app"
mkdir -p "$work/home"
if [[ $bundle -eq 1 ]]; then
    rm -rf "$work/app/data" "$work/app/Packs" "$work/app/Assets"
    python3 -I "$project/tools/stage-content.py" "$work/content/unciv-content.zip"
    export UNCIV_CONTENT_ZIP="$work/content/unciv-content.zip"
fi

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
if [[ $bundle -eq 1 ]] && [[ ! -f "$(find "$work/home/.local/share/godot/app_userdata" -type f -name .content-version | head -1)" ]]; then
    echo "FAIL: content bundle was not extracted to user://content" >&2; exit 1
fi
echo "OK: exported build reached the main menu from $work/app (outside the checkout)"
