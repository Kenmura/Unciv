# Godot headless test harness (C000)

Lets an agent render Godot scenes, look at the result and click on them with no display, no GPU and no editor.

## One-time setup (inside `dotnet-dev`)

```
sudo dnf install xorg-x11-server-Xvfb mesa-dri-drivers mesa-libGL mesa-vulkan-drivers libXcursor libXinerama libXi libXrandr fontconfig alsa-lib
Unciv.Godot.Harness/tools/setup-godot.sh
```

`setup-godot.sh` installs the pinned Godot (4.7.2 .NET) under `~/.local/opt`, outside the repository, after checking the
SHA-512 published with the official release. Override the location with `GODOT_HOME`, or point `GODOT` at your own binary.

## Running

```
distrobox enter dotnet-dev -- bash -c 'cd <your-worktree>/csharp/Unciv.Godot.Harness && tools/run-smoke.sh'
```

Exit 0 means: the project builds with `-warnaserror`, Godot booted, the scene rendered, the captured frame is not blank and
shows the expected geometry, simulated clicks picked the expected ground points, and the frame matches `golden/smoke.png`.
The captured frame is left in `out/smoke.png` (git-ignored); **open it** when a visual ticket needs judging, not just the exit code.
After an intended visual change run `tools/run-smoke.sh --update-golden` and review the new PNG before committing it.

How it renders: Xvfb provides a virtual 24-bit screen and Godot's Compatibility renderer (`--rendering-driver opengl3`) runs on
Mesa llvmpipe, a CPU rasterizer. No GPU is used, so results are machine-independent apart from rasterizer rounding.
Pure `--headless` is not used because it installs a dummy renderer that produces no pixels.

## Pieces

| Piece | Where | Notes |
|---|---|---|
| Image comparison, perceptual hash, blank-frame checks | `Unciv.Harness` (plain net10.0, in `Unciv.sln`, tested by `Unciv.Core.Tests/Harness`) | No Godot dependency, runs in the normal Standard checks |
| `ViewportCapture.CaptureViewportPng` | `Unciv.Godot.Harness` | viewport -> PNG file and `RgbaImage` |
| `InputSimulator.SimulateMouseClick/MouseMove/Key` | `Unciv.Godot.Harness` | goes through `Viewport.PushInput`, like device input |
| `HexPicking.PickGroundPoint` | `Unciv.Godot.Harness` | screen position -> point on a horizontal plane |
| `SmokeTest` scene | `Unciv.Godot.Harness` | the gate; copy it as the template for a new visual test |

`GoldenComparer` combines three measures with tolerances (`GoldenTolerance`): perceptual-hash Hamming distance (layout moves),
mean per-channel difference (overall colour drift) and the fraction of pixels differing by more than a per-pixel tolerance.
The hash alone is weak on small images with a flat background, which is why all three must pass.

## Scenarios and the owner review sheet (C008)

```
Unciv.Godot.Harness/tools/run-scenario.sh <name> [--update-golden | --expect-golden-failure]
Unciv.Godot.Harness/tools/review-sheet.sh [name ...]
```

A scenario (`Scenario` subclass registered in `ScenarioCatalog`, name listed in `scenarios.txt`) builds its UI over the seeded
`ScenarioGame.Create()` session (Vanilla, Tiny map, two humans + one AI, fixed seeds) and returns a script of `Click`, `PressKey`,
`Press("Button text")`, `WaitFrames`, `AssertQuery` (engine state, asserted before pixels) and `Capture` (PNG compared with
`golden/<scenario>/<step>.png`). All state changes must go through `GameSession.Submit`; at the end the state hash must equal the hash of a
headless replay of the recorded command log (`ScenarioReplay`, plain C#, tested in `Unciv.Core.Tests/Harness`). `--expect-golden-failure`
proves a wrong golden fails. `review-sheet.sh` runs all scenarios and writes `out/review/contact-sheet.png` and `out/review/index.md`

**Determinism under load (C058).** A scenario must not depend on how fast frames render. `ScenarioRunner` freezes every real-time node as it enters the tree (`TooltipHost.Delay` unreachable, `MapView.RealTimePanning` off; scenarios advance tooltips and pan with explicit calls), the harness `project.godot` sets the native `gui/timers/tooltip_delay_sec` to 1e6, `CombatPresenter` is stepped by hand, and each run gets its own Xvfb display (`-displayfd`, no `xvfb-run -a` race). Only two wall-clock checks remain, both with large headroom: next-turn's longest-frame bound (750 ms, about 100 ms measured under heavy load) and terrain-blend's best-of-five frame-time budget. `Unciv.Core.Tests/Harness/HarnessDeterminismTests` fails on any new clock/timer/tween use or unfrozen real-time `_Process`.
(captions plus the owner checklist). The engine-only parts (`ScenarioGame`, `ScenarioReplay`, `ScenarioResult`, `ContactSheetLayout`,
`ReviewIndex`) live in `Unciv.Harness` and run in the Standard checks.

## Limits

- `Unciv.Godot.Harness` is not in `Unciv.sln`: it needs the Godot SDK and runtime, which the Standard checks must not require.
  Its gate is `tools/run-smoke.sh`.
- `Unciv.Godot` (C001) is the real client project: it references `Unciv.Core`, boots an empty `Main.tscn`, and is likewise kept out of `Unciv.sln`. CI builds it headlessly with `dotnet build Unciv.Godot -warnaserror` (needs only the .NET SDK and the `Godot.NET.Sdk` NuGet package; no editor or Godot binary). It uses no assets (see ASSETS.md).
- Linux x86-64 only (the install script and the Xvfb path). Windows/macOS are not covered.
- llvmpipe is slow; keep test scenes small and the resolution at 640x480 unless a ticket needs more.

## Packaging an exported client (C042)

```
Unciv.Godot/tools/install-export-templates.sh      # once: export templates matching the pinned editor (checksum verified)
Unciv.Godot/tools/package.sh [linux|windows]       # -> Unciv.Godot/build/<platform>/ (git-ignored)
Unciv.Godot/tools/smoke-export.sh                  # copies build/linux outside the checkout, runs it under Xvfb, expects the main menu
```

`package.sh` builds, runs `--export-release` with the presets in `Unciv.Godot/export_presets.cfg` (Linux, Windows; **macOS is not
a preset yet**: it needs a Mac host for signing/notarisation), then copies `android/assets/jsons` (rulesets and `translations/`) to
`<build>/data/jsons`, plus `LICENSE` and `THIRD_PARTY_NOTICES.md`. `RulesetLocator` looks for `data/jsons` beside the executable first and
falls back to the checkout's `android/assets/jsons` when run from the editor (`ContentLocator`). Godot's .NET export requires a solution file
beside `project.godot`; the script generates a throw-away, git-ignored `Unciv.Godot.sln`. Template version = editor version (`4.7.2-stable`),
pinned in `setup-godot.sh` and `install-export-templates.sh`; a unit test keeps the two equal.

Local mods: drop mod folders (same layout as Unciv's `mods/`) into the `mods` folder of the user data directory (`user://mods`, created on first start;
Linux `~/.local/share/godot/app_userdata/Unciv/mods`). They are loaded with the built-in rulesets at start-up.
No Firaxis/Civ 5 or Unciv atlas/sound/image is bundled (the client draws generated placeholders).

## How a release is produced (T169)

Releases are built **locally** and never by CI: `csharp-releases/` is git-ignored and CI publishes nothing.

- `python3 -I tools/release.py T### [--cli-only | --client-only] [--windows] [--keep N] [--force] [--dry-run]` (run automatically by `ticket.py` after
  `state T### Done`) builds the tree it is run on into `<main checkout>/csharp-releases/<ticket>-<shortsha>/`:
  `cli/` (`dotnet publish Unciv.Cli`, self-contained linux-x64, so `./Unciv.Cli` runs with no dotnet installed), `client-linux/` (the
  `package.sh` output below) and `manifest.json`. `csharp-releases/latest` only ever moves forward, the newest 5 versions are kept, and a tree
  already released (same git tree id) is not rebuilt without `--force`. After building, the CLI is smoke-run with an empty `PATH`.
- `Unciv.Godot/tools/package.sh [linux|windows]` does the client: `dotnet build -c Release`, `godot --headless --export-release` with the presets in
  `export_presets.cfg`, then copies `data/jsons`, fonts, `LICENSE` and `THIRD_PARTY_NOTICES.md` next to the executable. Output is
  `Unciv.Godot/build/<platform>/` (git-ignored). `smoke-export.sh` runs the Linux build from outside the checkout and expects the main menu.
- Needs: the pinned Godot 4.7.2 .NET editor (`setup-godot.sh`) and the matching export templates (`install-export-templates.sh`); both check SHA-512.
  Bump the version in those two scripts together (a unit test keeps them equal).

### Known Godot headless export crashes and the retry

The headless export crashes intermittently (about 1 in 15 fresh imports, more under load) in Godot's threaded font import: exit 139 (SIGSEGV),
134 (SIGABRT) or 132 (SIGILL), sometimes leaving `Unciv.Godot/.godot/imported` half written. It is not a project bug; the retry is the fix:

- `release.py` retries a crashed export twice after deleting `.godot/imported` and `.godot/exported` (exit codes 139, 134, 132 and raw signals).
- `Unciv.Godot/tools/retry-on-crash.sh <retries> <command...>` does the same for scripts and CI (`retry-on-crash.sh 2 Unciv.Godot/tools/package.sh linux`).
  Any other exit code is a real failure and is returned immediately.
- Other symptoms: "export produced no .NET assemblies folder (data_*)" means the export failed without a crash code, read the Godot output above it;
  a missing solution file error is handled by `package.sh` (it generates the git-ignored `Unciv.Godot.sln`).

## CI (T169)

**The workflow is parked at `csharp/ci/csharp-workflow.yml`** (GitHub refuses pushes that touch `.github/workflows/` from a token without the `workflow` scope). To activate it, `git mv csharp/ci/csharp-workflow.yml .github/workflows/csharp.yml` (and point `WORKFLOW` in `tools/test_ci.py` back) with a token that has that scope, then do T239. Once active it is separate from the Kotlin/Gradle workflows and runs only for changes under `csharp/**`, `android/assets/jsons/**`
(the rulesets the engine reads) or the workflow itself, on pushes to `csharp-migration` and on pull requests; plus a weekly schedule for the soak.
`permissions: contents: read`, no secrets, no `pull_request_target`, no artifact upload, every action pinned by commit SHA.

| Job | Runs | What |
|---|---|---|
| `engine` | push, PR | the Green gate: `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`, `Unciv.Godot` and `Unciv.Godot.Harness` builds with `-warnaserror` (they need only the SDK and the `Godot.NET.Sdk` NuGet package), `coverage.py --check` (base chosen by `tools/ci_base.py`: PR merge-base, push `before`, else `--all`), `screenshots.py --check`, `ticket.py check`, the tools' Python unit tests |
| `client-export` | push, PR | pinned Godot + export templates (cached, SHA-512 checked), `retry-on-crash.sh 2 package.sh linux`, `smoke-export.sh` |
| `client-scenarios` | push, PR (non-blocking) | `run-smoke.sh`, every scenario in `scenarios.txt`, and `run-scenario.sh menu --expect-golden-failure`, under Xvfb |
| `soak` | weekly schedule, manual | `UNCIV_SOAK=1 dotnet test --filter Category=Soak` (T167); the ordinary `dotnet test` skips it |

System packages for the Godot jobs on ubuntu-24.04: `xvfb xauth libgl1-mesa-dri libglx-mesa0 libxcursor1 libxinerama1 libxi6 libxrandr2 fontconfig libasound2t64`.
Caches: `~/.nuget/packages` (keyed on csproj/`Directory.Packages.props`) and the Godot editor/templates (keyed on the two install scripts).

`client-scenarios` has `continue-on-error: true`: the goldens were recorded on Fedora's Mesa llvmpipe, and ubuntu's rasterizer may differ by rounding
(the golden comparer has tolerances, but this has not been seen green on a runner). When it has, delete that line so it gates merges. A
scenario that fails there is reproduced locally with `run-scenario.sh <name>`. GitHub Actions cannot be run locally: the workflow was checked with
`actionlint`, `test_ci.py` (shape: paths, permissions, pinning, no publishing) and by running each of its commands in `dotnet-dev`.
