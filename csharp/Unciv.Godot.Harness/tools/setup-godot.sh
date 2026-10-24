#!/usr/bin/env bash
# One-time setup of the Godot 4 .NET runtime used by the C000 harness. Idempotent.
# Downloads the pinned release from the official godotengine GitHub release, verifies it against the published SHA-512
# sum, and unpacks it under ~/.local/opt (outside the repository). `--print-path` prints the executable and nothing else.
set -euo pipefail

version="4.7.2-stable"
sha512="1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13"
name="Godot_v${version}_mono_linux_x86_64"
root="${GODOT_HOME:-$HOME/.local/opt/godot-${version%-stable}}"
exe="$root/$name/Godot_v${version}_mono_linux.x86_64"

if [[ ! -x "$exe" ]]; then
    echo "Installing Godot $version to $root" >&2
    work="$(mktemp -d)"
    trap 'rm -rf "$work"' EXIT
    curl -fsSL -o "$work/godot.zip" "https://github.com/godotengine/godot/releases/download/$version/$name.zip"
    echo "$sha512  $work/godot.zip" | sha512sum -c - >&2
    mkdir -p "$root"
    unzip -q "$work/godot.zip" -d "$root"
fi

if [[ "${1:-}" != "--print-path" ]]; then
    echo "Godot ready: $exe" >&2
    echo "Container packages needed once: sudo dnf install xorg-x11-server-Xvfb mesa-dri-drivers mesa-libGL mesa-vulkan-drivers libXcursor libXinerama libXi libXrandr fontconfig alsa-lib" >&2
fi
echo "$exe"
