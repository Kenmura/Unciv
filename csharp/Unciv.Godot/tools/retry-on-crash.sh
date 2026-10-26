#!/usr/bin/env bash
# T169: run a command and retry it when it CRASHES (SIGSEGV 139, SIGABRT 134, SIGILL 132), the way tools/release.py does for the client export.
# Godot's headless export intermittently dies in its threaded font import (about 1 in 15 fresh imports). Any other failure is a real failure and is
# returned at once. Before each retry the import/export caches of Unciv.Godot (.godot/imported, .godot/exported) are dropped, because a crash can
# leave them half written. Usage: tools/retry-on-crash.sh <retries> <command> [args...]   (retries = extra attempts after the first)
set -uo pipefail

if [[ $# -lt 2 || ! "$1" =~ ^[0-9]+$ ]]; then echo "usage: $0 <retries> <command> [args...]" >&2; exit 64; fi
retries="$1"; shift
project="${RETRY_GODOT_PROJECT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"

attempt=0
while true; do
    "$@"
    code=$?
    case "$code" in
        132|134|139) ;;
        *) exit "$code" ;;
    esac
    if (( attempt >= retries )); then
        echo "retry-on-crash: '$1' crashed (exit $code) on all $((attempt + 1)) attempts" >&2
        exit "$code"
    fi
    attempt=$((attempt + 1))
    echo "retry-on-crash: '$1' crashed (exit $code); clearing the Godot import cache, retry $attempt of $retries" >&2
    rm -rf "$project/.godot/imported" "$project/.godot/exported"
done
