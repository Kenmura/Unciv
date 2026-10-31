#!/usr/bin/env bash
# C042: export the client for one platform and bundle its data. Usage: tools/package.sh [linux|windows|macos|android|android-emulator] (default linux).
# Output: Unciv.Godot/build/<platform>/ holding the executable, data/jsons (rulesets + translations), LICENSE, THIRD_PARTY_NOTICES.md
# (macos: the same files inside Unciv.app/Contents/MacOS, next to the executable, plus FIRST_RUN_MACOS.txt), and, T258, the release archive
# Unciv.Godot/build/Unciv-client-<platform>.<ext> (linux .tar.gz, windows/macos .zip, top-level folder Unciv-<platform>/). T279 attaches these to a release.
# macOS is exported unsigned and un-notarised from Linux (Godot zips the .app); it has never been run by us (see GODOT.md).
# Needs the Godot editor (Unciv.Godot.Harness/tools/setup-godot.sh) and the matching export templates (tools/install-export-templates.sh).
# T260 android: debug-signed arm64 APK build/android/Unciv.apk (android-emulator adds x86_64: build/android/Unciv-emulator.apk). Needs an Android SDK
# (ANDROID_HOME or ANDROID_SDK_ROOT with cmdline-tools/latest, platform-tools, build-tools/36.1.0), a JDK (keytool) and the net9.0 build (UncivAndroid=true):
# see GODOT.md. The rulesets, packs and fonts travel in the pck as Content/unciv-content.zip (T276).
# Local mods are not bundled: players drop mod folders into the user data folder's mods directory (user://mods).
set -euo pipefail

# T361 (T360 memory settings; the env vars alone do not cap the nodes, so every dotnet build below also passes -m: and -nodeReuse:false): one MSBuild node, no node reuse, no build server, no shared compiler, workstation GC; the servers dotnet may leave behind are shut down on exit
export MSBUILDDISABLENODEREUSE=1 MSBUILDNODECOUNT="${UNCIV_MSBUILD_NODES:-1}" MSBUILDUSESERVER=0 UseSharedCompilation=false DOTNET_gcServer=0
cfg_home=""; keystore=""; stage=""
cleanup() {
    [[ -z "$cfg_home" ]] || rm -rf "$cfg_home"
    [[ -z "$keystore" ]] || rm -f "$keystore"
    [[ -z "$stage" ]] || rm -rf "$stage"
    dotnet build-server shutdown >/dev/null 2>&1 || true
}
trap cleanup EXIT

project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
platform="${1:-linux}"
case "$platform" in
    linux) preset="Linux"; exe="Unciv.x86_64" ;;
    windows) preset="Windows"; exe="Unciv.exe" ;;
    macos) preset="macOS"; exe="Unciv.app/Contents/MacOS/Unciv" ;;
    android) preset="Android"; exe="Unciv.apk" ;;
    android-emulator) preset="Android (emulator)"; exe="Unciv-emulator.apk" ;;
    *) echo "unknown platform '$platform' (linux|windows|macos|android|android-emulator)" >&2; exit 2 ;;
esac
godot="${GODOT:-$("$project/../Unciv.Godot.Harness/tools/setup-godot.sh" --print-path)}"
repo="$(cd "$project/../.." && pwd)"
out="$project/build/$platform"
android=false
case "$platform" in android*) android=true; out="$project/build/android" ;; esac

if $android; then
    # T260: fail early with the exact missing piece instead of a Godot export error.
    sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
    missing=()
    [[ -n "$sdk" && -d "$sdk" ]] || missing+=("ANDROID_HOME (or ANDROID_SDK_ROOT) pointing at an Android SDK directory")
    if [[ -n "$sdk" && -d "$sdk" ]]; then
        [[ -d "$sdk/build-tools/36.1.0" ]] || missing+=("$sdk/build-tools/36.1.0 (sdkmanager 'build-tools;36.1.0')")
        [[ -x "$sdk/platform-tools/adb" ]] || missing+=("$sdk/platform-tools/adb (sdkmanager 'platform-tools')")
        [[ -d "$sdk/cmdline-tools/latest" ]] || missing+=("$sdk/cmdline-tools/latest (sdkmanager 'cmdline-tools;latest')")
    fi
    command -v keytool >/dev/null || missing+=("keytool (a JDK 17+; set JAVA_HOME)")
    templates="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/4.7.2.stable.mono"
    [[ -f "$templates/android_debug.apk" && -f "$templates/android_source.zip" ]] || missing+=("Android export templates in $templates (tools/install-export-templates.sh)")
    if (( ${#missing[@]} )); then
        printf 'Android build needs:\n' >&2
        printf '  - %s\n' "${missing[@]}" >&2
        exit 3
    fi
    export ANDROID_HOME="$sdk" ANDROID_SDK_ROOT="$sdk"
    [[ -n "${JAVA_HOME:-}" ]] || JAVA_HOME="$(dirname "$(dirname "$(readlink -f "$(command -v keytool)")")")"
    export JAVA_HOME
    # The Godot 4.7.2 Android template bundles .NET 9, so the client builds as net9.0 (Directory.Build.props reads UncivAndroid).
    export UncivAndroid=true
    # Godot reads the SDK and JDK paths only from its editor settings (there is no ANDROID_HOME/JAVA_HOME fallback in a headless export, verified
    # with 4.7.2: "A valid Java SDK path is required in Editor Settings"), so run it with a throw-away config home holding just those two paths.
    # The developer's own editor settings are left alone.
    cfg_home="$(mktemp -d)"; keystore=""
    mkdir -p "$cfg_home/godot"
    printf '[gd_resource type="EditorSettings" format=3]\n\n[resource]\nexport/android/java_sdk_path = "%s"\nexport/android/android_sdk_path = "%s"\n' \
        "$JAVA_HOME" "$sdk" > "$cfg_home/godot/editor_settings-4.7.tres"
fi

if $android; then mkdir -p "$out"; rm -f "$out/$exe"; else rm -rf "$out"; mkdir -p "$out"; fi
if $android; then build_config=Debug; else build_config=Release; fi
(cd "$project/.." && dotnet build Unciv.Godot -m:"${UNCIV_MSBUILD_NODES:-1}" -nodeReuse:false -p:UseSharedCompilation=false -warnaserror -c "$build_config")
# Godot's .NET export insists on a solution file next to project.godot; generate a throw-away one (git-ignored) rather than keep one in the tree.
if [[ ! -f "$project/Unciv.Godot.sln" ]]; then
    (cd "$project" && dotnet new sln -n Unciv.Godot --format sln >/dev/null && dotnet sln Unciv.Godot.sln add Unciv.Godot.csproj >/dev/null)
fi
if $android; then
    # Debug-signed with a throw-away keystore generated here (never committed; Godot reads it from these variables).
    keystore="$out/debug.keystore"
    rm -f "$keystore"
    keytool -genkeypair -keystore "$keystore" -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 \
        -validity 10000 -dname "CN=Unciv debug,O=Unciv,C=US" >/dev/null
    export GODOT_ANDROID_KEYSTORE_DEBUG_PATH="$keystore" GODOT_ANDROID_KEYSTORE_DEBUG_USER=androiddebugkey GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD=android
    # T276: no data folder beside an APK, so the content travels as a zip inside the pck (the presets include Content/*).
    python3 -I "$project/tools/stage-content.py"
    XDG_CONFIG_HOME="$cfg_home" "$godot" --headless --path "$project" --export-debug "$preset" "$out/$exe"
    if [[ ! -s "$out/$exe" ]]; then echo "Godot produced no $exe: see the errors above" >&2; exit 1; fi
    rm -f "$keystore" "$out/$exe.idsig"
    echo "Packaged Android build: $out/$exe ($(du -h "$out/$exe" | cut -f1))"
    exit 0
fi
if [[ "$platform" == macos ]]; then
    # the preset exports a zip holding Unciv.app; unpack it so the content can go beside the executable like on the other platforms
    "$godot" --headless --path "$project" --export-release "$preset" "$out/Unciv.zip"
    (cd "$out" && unzip -q Unciv.zip && rm Unciv.zip)
    [[ -f "$out/$exe" ]] || { echo "macOS export produced no $exe" >&2; exit 1; }
    chmod +x "$out/$exe"
    root="$out/Unciv.app/Contents/MacOS"
else
    "$godot" --headless --path "$project" --export-release "$preset" "$out/$exe"
    root="$out"
fi
if ! compgen -G "$root/data_*" >/dev/null && ! compgen -G "$out/Unciv.app/Contents/Resources/data_*" >/dev/null; then echo "export produced no .NET assemblies folder (data_*): see the Godot errors above" >&2; exit 1; fi
mkdir -p "$root/data"
cp -r "$repo/android/assets/jsons" "$root/data/jsons"
# C025: fonts for non-Latin languages are read from disk (FontChain), so they ship next to the executable, with their licence.
mkdir -p "$root/Assets"
cp -r "$project/Assets/fonts" "$root/Assets/fonts"
rm -f "$root/Assets/fonts/subset-fonts.py" "$root"/Assets/fonts/*.import
# C063: the bundled icon pack (PNGs and manifest, read from disk by GodotAssetFiles; .gdignore keeps it out of the pck) ships next to the executable with its credits.
mkdir -p "$root/Packs"
cp -r "$project/Packs/unciv" "$root/Packs/unciv"
rm -f "$root/Packs/unciv/.gdignore"
# C083: the bundled unit models pack (glb files and manifest), same way
cp -r "$project/Packs/units" "$root/Packs/units"
cp "$repo/LICENSE" "$root/LICENSE"
cp "$project/THIRD_PARTY_NOTICES.md" "$root/THIRD_PARTY_NOTICES.md"
if [[ "$platform" == macos ]]; then
    cat > "$out/FIRST_RUN_MACOS.txt" <<'TXT'
Unciv (Godot client) for macOS: unsigned, not notarised, never tested by the developers on a Mac.
Gatekeeper will refuse the first launch ("cannot be opened because the developer cannot be verified" or "is damaged").
Move Unciv.app out of the download folder, then either right-click it and choose Open (twice if asked), or run in Terminal:
    xattr -dr com.apple.quarantine Unciv.app
Universal binary (Apple Silicon and Intel). If it fails to start, run Unciv.app/Contents/MacOS/Unciv from Terminal and report the output.
TXT
fi
# T258: the release archive (T279 uploads these). One top-level folder so extraction never spills into the current directory.
archive_dir="$project/build"
stage="$(mktemp -d)"
ln -s "$out" "$stage/Unciv-$platform"
if [[ "$platform" == linux ]]; then
    archive="$archive_dir/Unciv-client-linux.tar.gz"
    rm -f "$archive"
    tar -C "$stage" -czhf "$archive" "Unciv-$platform"
else
    archive="$archive_dir/Unciv-client-$platform.zip"
    rm -f "$archive"
    (cd "$stage" && zip -qr "$archive" "Unciv-$platform")
fi
[[ -s "$archive" ]] || { echo "archive $archive is empty" >&2; exit 1; }
echo "Archive $archive ($(du -h "$archive" | cut -f1))"
echo "Packaged $platform build in $out"
