#!/bin/sh
# T160: regenerate csharp/Unciv.Core.Tests/Determinism/kotlin-golden.txt by running the real Kotlin game (needs a JDK; no distrobox).
# usage: export.sh [dump-file]   (dump-file receives the full canonical text, for finding the first diverging line)
set -e
root=$(git rev-parse --show-toplevel)
cd "$root"
dest=tests/src/com/unciv/logic/KotlinGoldenExporter.kt
cp csharp/migration/golden/KotlinGoldenExporter.kt "$dest"
trap 'rm -f "$dest"' EXIT
out="$root/csharp/Unciv.Core.Tests/Determinism/kotlin-golden.txt"
GOLDEN_OUT="$out" GOLDEN_DUMP="${1:-}" ./gradlew --offline :tests:test --rerun --tests com.unciv.logic.KotlinGoldenExporter -i
