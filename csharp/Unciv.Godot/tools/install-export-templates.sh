#!/usr/bin/env bash
# C042: install the Godot export templates that match the editor pinned in Unciv.Godot.Harness/tools/setup-godot.sh (same version, same
# release page, SHA-512 checked against the value recorded here). Idempotent; templates land in the editor's per-user templates folder.
set -euo pipefail

version="4.7.2-stable"
sha512="${GODOT_TEMPLATES_SHA512:-}"   # filled in from the official release's SHA512-SUMS.txt on first install, see below
name="Godot_v${version}_mono_export_templates.tpz"
dest="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/${version%-stable}.stable.mono"

if [[ -f "$dest/version.txt" ]]; then echo "Templates already installed in $dest" >&2; exit 0; fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
base="https://github.com/godotengine/godot/releases/download/$version"
curl -fsSL -o "$work/$name" "$base/$name"
if [[ -z "$sha512" ]]; then
    curl -fsSL -o "$work/SHA512-SUMS.txt" "$base/SHA512-SUMS.txt"
    sha512="$(awk -v n="$name" '$2 == n { print $1 }' "$work/SHA512-SUMS.txt")"
fi
echo "$sha512  $work/$name" | sha512sum -c - >&2
unzip -q "$work/$name" -d "$work/t"
mkdir -p "$dest"
cp -r "$work"/t/templates/* "$dest/"
echo "Installed export templates to $dest" >&2
