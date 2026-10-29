# Unciv.Cli: the headless runner

A console program on top of `Unciv.Core`: start a game, play N turns with the AI automation, save and load, print the state hash of every turn.
It is the entry point for determinism checks, soak runs and demos. It has no UI and no network; everything is a function of its arguments and the files it reads.

## Build and run

All dotnet commands run in the `dotnet-dev` distrobox, from the `csharp/` folder of your worktree:

    distrobox enter dotnet-dev -- bash -c 'cd <worktree>/csharp && dotnet build Unciv.Cli -warnaserror'
    distrobox enter dotnet-dev -- bash -c 'cd <worktree>/csharp && dotnet run --project Unciv.Cli -- <command> [options]'

(or run `Unciv.Cli/bin/Debug/net10.0/Unciv.Cli` directly after building). The runner finds the rulesets in `android/assets/jsons` by searching upwards
from the working directory; pass `--jsons DIR` to name the folder. The project is part of `Unciv.sln`, so the usual
`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` covers it (its tests are `Unciv.App.Tests/Cli`).

## Commands

| command | what it does |
| --- | --- |
| `new --out FILE` | Start a game and save it. Options: `--nations Babylon,Greece,Rome` (the first is the human, played by the automation), `--city-states N` (2), `--map-size Tiny\|Small\|Medium\|Large\|Huge` (Tiny), `--map-type` (Pangaea), `--shape Rectangular\|Hexagonal\|"Flat Earth Hexagonal"` (Rectangular), `--difficulty` (Prince), `--speed` (Quick), `--seed N` (1, the game seed), `--map-seed N` (= seed), `--ruleset NAME` (`Civ V - Vanilla`; `Civ V - Gods & Kings` is the other), `--mods A,B` (needs `--mods-dir`), `--map FILE` (start on a saved map; a map with pre-placed units is refused with a clear error: Kotlin's GameStarter fails on it too, so strip the units first), `--zip` |
| `record --out FILE --log FILE --turns N` | Like `new` (same options), but the human civ is played for N turns by the scripted human through the command layer (no auto-play); writes the command log (JSON Lines: header, commands, a checkpoint hash after every turn change) to `--log` and the game to `--out`. Not with `--map` (a log cannot replay a saved map). |
| `replay --log FILE` | Rebuild the game from the log header and re-apply every command, comparing the full state hash at each checkpoint. `--hashes` prints the checkpoints. Exit 0 and `replay ok ...`, or exit 1 and `replay DIVERGED at seq=...`. A log only replays on the engine version and ruleset (and mods, `--mods-dir`) it was written with. |
| `submit --in FILE --commands FILE` | Submit the commands of a file (one JSON line `{"type":"end-turn","payload":{}}` each) as the human civ (`--actor CIV` to choose another), stopping with exit 1 at the first refused command; `--out FILE` saves the result. |
| `play --in FILE --turns N` | Play N turns. `--hashes` prints `turn T HASH` for the start and after every turn; `--out FILE` saves the result (`--zip` to compress). |
| `save --in FILE --out FILE` | Load and write again, as plain JSON or `--zip`. |
| `load --in FILE` | Load a save; print a summary line and its hash (a quick "does this save still open" check). |
| `hash --in FILE` | Print the state hash. |
| `info --in FILE` | Print the turn, the map and one line per civilization. |

Every command also takes `--mods-dir DIR` (a folder of mods, each with a `jsons` subfolder) and `--jsons DIR`.

`play` stops early (with a message, exit 0) when the game is won or ended. Option values may not start with `--`. Saves are written to `FILE.tmp` and moved over `FILE`.

Exit codes: 0 ok, 1 a failure while running (missing or corrupt save), 2 a bad command line (usage text on stderr).

Example:

    dotnet run --project Unciv.Cli -- new --out /tmp/g.json --seed 7 --map-size Small
    dotnet run --project Unciv.Cli -- play --in /tmp/g.json --turns 50 --hashes --out /tmp/g50.json
    dotnet run --project Unciv.Cli -- info --in /tmp/g50.json

## Determinism

The same arguments always give the same output, byte for byte: the game seed and map seed are inputs, the clock is a pure function of the save
(a new game starts at 1000000 ms and each turn advances it by 1000 ms), and text is written with the invariant culture. The hash is `StateHasher` (T011)
over the plain JSON serialisation of the whole game, so a save written zipped or plain hashes the same, and `play 60` gives the same final hash as
`play 25` then `play 35` from the saved result (tested over a 60-turn run, T215). Compare two runs by diffing their `--hashes` output.
