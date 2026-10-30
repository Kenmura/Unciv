#!/usr/bin/env bash
# T277: install a packaged APK on a headless x86_64 emulator, start it and wait for the main-menu marker in logcat (the Android twin of smoke-export.sh).
# Usage: tools/android-smoke.sh [apk]   (default build/android/Unciv-emulator.apk from `tools/package.sh android-emulator`; it must contain x86_64)
# Needs ANDROID_HOME (or ANDROID_SDK_ROOT) with platform-tools, emulator and the system image below, and /dev/kvm (the x86_64 emulator is unusably slow without it:
# exit 4 says so and the caller decides). Install the pieces with:
#   sdkmanager 'emulator' 'platform-tools' 'system-images;android-34;default;x86_64'
# Env: ANDROID_SMOKE_IMAGE (system image), ANDROID_SMOKE_TIMEOUT (seconds for the menu, default 240), ANDROID_SMOKE_BOOT_TIMEOUT (seconds for the boot, default 480), ANDROID_SMOKE_OUT (folder for the screenshot and logs,
# default build/android/smoke). The AVD lives in a throw-away folder, the developer's ~/.android is not touched.
# Exit: 0 menu reached, 1 failed, 3 SDK pieces missing, 4 no KVM.
set -euo pipefail

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
apk="${1:-$project/build/android/Unciv-emulator.apk}"
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
[[ -z "$sdk" ]] || sdk="$(realpath -m "$sdk")"
image="${ANDROID_SMOKE_IMAGE:-system-images;android-34;default;x86_64}"
timeout_s="${ANDROID_SMOKE_TIMEOUT:-240}"
out="${ANDROID_SMOKE_OUT:-$project/build/android/smoke}"
marker="UNCIV_MENU_READY"
package="org.unciv.godot"

missing=()
[[ -n "$sdk" ]] || missing+=("ANDROID_HOME")
[[ -f "$apk" ]] || missing+=("the APK $apk (tools/package.sh android-emulator)")
if [[ -n "$sdk" ]]; then
    for tool in platform-tools/adb emulator/emulator cmdline-tools/latest/bin/avdmanager "${image//;//}/system.img"; do
        [[ -e "$sdk/$tool" ]] || missing+=("$sdk/$tool")
    done
fi
if ((${#missing[@]})); then printf 'missing: %s\n' "${missing[@]}" >&2; exit 3; fi
if [[ ! -r /dev/kvm || ! -w /dev/kvm ]]; then echo "/dev/kvm is not usable: cannot run the x86_64 emulator here" >&2; exit 4; fi

adb="$sdk/platform-tools/adb"
work="$(mktemp -d)"
mkdir -p "$out"
export ANDROID_AVD_HOME="$work/avd" ANDROID_USER_HOME="$work/user" ANDROID_SDK_ROOT="$sdk" ANDROID_HOME="$sdk"
mkdir -p "$ANDROID_AVD_HOME" "$ANDROID_USER_HOME"
port=5580   # emulator console port (adb serial emulator-5580); an odd/unusual pair keeps clear of a developer's own emulator on 5554
serial="emulator-$port"
emupid=""
cleanup() {
    if [[ -n "$emupid" ]]; then   # only ever our own emulator, never one a developer already runs
        timeout 10 "$adb" -s "$serial" emu kill >/dev/null 2>&1 || true
        for _ in $(seq 1 20); do kill -0 "$emupid" 2>/dev/null || break; sleep 0.5; done
        kill "$emupid" 2>/dev/null || true
    fi
    rm -rf "$work"
}
trap cleanup EXIT

echo "no" | "$sdk/cmdline-tools/latest/bin/avdmanager" create avd -n unciv-smoke -k "$image" -d pixel_5 --force >"$out/avdmanager.log" 2>&1
"$sdk/emulator/emulator" -avd unciv-smoke -port "$port" -no-window -no-audio -no-boot-anim -no-snapshot -no-metrics -gpu swangle_indirect \
    -memory 2560 >"$out/emulator.log" 2>&1 &
emupid=$!

echo "waiting for the emulator to boot (up to ${ANDROID_SMOKE_BOOT_TIMEOUT:-480} s)..." >&2
deadline=$((SECONDS + ${ANDROID_SMOKE_BOOT_TIMEOUT:-480}))
booted=""
while ((SECONDS < deadline)); do
    if ! kill -0 "$emupid" 2>/dev/null; then echo "FAIL: the emulator exited early (see $out/emulator.log)" >&2; tail -20 "$out/emulator.log" >&2; exit 1; fi
    booted="$(timeout 20 "$adb" -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || true)"
    [[ "$booted" == 1 ]] && break
    sleep 3
done
if [[ "$booted" != 1 ]]; then echo "FAIL: the emulator did not finish booting" >&2; exit 1; fi

"$adb" -s "$serial" uninstall "$package" >/dev/null 2>&1 || true   # the throw-away keystore changes with every build
timeout 300 "$adb" -s "$serial" install -r "$apk" >"$out/install.log" 2>&1 || { cat "$out/install.log" >&2; echo "FAIL: adb install" >&2; exit 1; }
"$adb" -s "$serial" logcat -c
timeout 30 "$adb" -s "$serial" shell am start -n "$package/com.godot.game.GodotAppLauncher" >"$out/start.log" 2>&1 || { cat "$out/start.log" >&2; echo "FAIL: am start" >&2; exit 1; }

echo "waiting for $marker in logcat (up to $timeout_s s)..." >&2
deadline=$((SECONDS + timeout_s))
found=0
crashed=0
while ((SECONDS < deadline)); do
    timeout 30 "$adb" -s "$serial" logcat -d >"$out/logcat.txt" 2>&1 || true
    if grep -q "$marker" "$out/logcat.txt"; then found=1; break; fi
    # only a crash of the app itself counts (system processes on the bare image log their own now and then)
    if grep -A3 "FATAL EXCEPTION" "$out/logcat.txt" | grep -q "Process: $package" || grep -q ">>> $package <<<" "$out/logcat.txt"; then
        echo "FAIL: the app crashed" >&2; grep -m3 -A12 "FATAL EXCEPTION\|Fatal signal" "$out/logcat.txt" >&2; crashed=1; break
    fi
    sleep 3
done
timeout 30 "$adb" -s "$serial" exec-out screencap -p >"$out/menu.png" 2>/dev/null || true
if [[ $found -ne 1 ]]; then echo "FAIL: main menu marker not seen (files in $out)" >&2; [[ $crashed -eq 1 ]] || tail -40 "$out/logcat.txt" >&2; exit 1; fi
if [[ ! -s "$out/menu.png" ]] || [[ "$(head -c 4 "$out/menu.png" | od -An -tx1 | tr -d ' \n')" != 89504e47 ]]; then echo "FAIL: no screenshot taken" >&2; exit 1; fi
echo "OK: the APK reached the main menu on an x86_64 emulator (screenshot $out/menu.png)"
