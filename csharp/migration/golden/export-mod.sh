#!/bin/sh
# T217: regenerate csharp/Unciv.Core.Tests/Determinism/kotlin-golden-mod.txt (mod ruleset golden) by running the real Kotlin game.
# Separate from export.sh (T160) on purpose. usage: export-mod.sh [dump-file]
set -e
root=$(git rev-parse --show-toplevel)
cd "$root"
dest=tests/src/com/unciv/logic/KotlinModGoldenExporter.kt
cp csharp/migration/golden/KotlinModGoldenExporter.kt "$dest"
trap 'rm -f "$dest"' EXIT
out="$root/csharp/Unciv.Core.Tests/Determinism/kotlin-golden-mod.txt"
GOLDEN_MOD="$root/csharp/Unciv.Core.Tests/Determinism/golden-mod" GOLDEN_OUT="$out" GOLDEN_DUMP="${1:-}" ./gradlew --offline :tests:test --rerun --tests com.unciv.logic.KotlinModGoldenExporter -i
