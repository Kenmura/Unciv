# Godot headless test harness (C000)

<!-- toc -->
## Contents

- [One-time setup (inside `dotnet-dev`)](#one-time-setup-inside-dotnet-dev)
- [Running](#running)
- [Pieces](#pieces)
- [Scenarios and the owner review sheet (C008)](#scenarios-and-the-owner-review-sheet-c008)
- [Limits](#limits)
- [Packaging an exported client (C042)](#packaging-an-exported-client-c042)
  - [Android APK (T260)](#android-apk-t260)
- [How a release is produced (T169)](#how-a-release-is-produced-t169)
  - [Known Godot headless export crashes and the retry](#known-godot-headless-export-crashes-and-the-retry)
- [Cutting a release (T279)](#cutting-a-release-t279)
- [CI (T169)](#ci-t169)
<!-- /toc -->

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
`GoldenTolerance.Renderer` (`HARNESS_GOLDEN_TOLERANCE=renderer` for `run-scenario.sh`) is the wider cross-renderer profile used by CI.
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
### Android APK (T260)

```
export ANDROID_HOME=$HOME/Android/Sdk      # cmdline-tools/latest, platform-tools, build-tools/36.1.0 (sdkmanager 'cmdline-tools;latest' 'platform-tools' 'build-tools;36.1.0' 'platforms;android-36')
Unciv.Godot/tools/install-export-templates.sh
Unciv.Godot/tools/package.sh android               # -> Unciv.Godot/build/android/Unciv.apk (arm64-v8a, debug-signed)
Unciv.Godot/tools/package.sh android-emulator      # -> build/android/Unciv-emulator.apk (arm64-v8a + x86_64, for the emulator)
adb install -r Unciv.Godot/build/android/Unciv.apk && adb shell am start -n org.unciv.godot/com.godot.game.GodotApp
```

- **Why net9.0.** The Godot 4.7.2 Android export template bundles .NET 9 and refuses a `net10.0` game assembly. `package.sh android` exports
  `UncivAndroid=true`, which makes `Unciv.Core`, `Unciv.Assets`, `Unciv.Client.Net`, `Unciv.Server` and `Unciv.Godot` build for `net9.0`
  (`$(UncivTfm)` in `Directory.Build.props`; `Unciv.Godot.csproj` keeps literal `net10.0`/`net9.0` elements because the Godot editor rewrites a TFM it cannot read as text).
  Desktop builds, the CLI, the harness and the tests stay `net10.0`. The only net10-only API was `JsonDocumentOptions.AllowDuplicateProperties` in `ProtocolCodec`
  (net9 path rejects duplicate keys with `ProtocolCodec.HasDuplicateProperties`, unit-tested). Building both TFMs in one checkout re-restores `obj/` on each switch (automatic, a few seconds).
- **Preflight.** The script checks for the SDK pieces, `keytool` and the Android templates first and exits 3 listing exactly what is missing. No NDK and no Gradle are needed (prebuilt templates, `gradle_build/use_gradle_build=false`).
- **Signing.** The script generates a throw-away keystore in `build/android/` for each build, hands it to Godot through `GODOT_ANDROID_KEYSTORE_DEBUG_*` and deletes it; nothing is committed. The APK is a debug build (`--export-debug`), good for sideloading, not for a store.
- **Preset.** `export_presets.cfg` `Android` / `Android (emulator)`: package `org.unciv.godot`, `INTERNET` and `CHANGE_WIFI_MULTICAST_STATE`, immersive mode, `window/handheld/orientation=4` (sensor landscape, in `project.godot`).
- **Reinstall.** The keystore is regenerated on every build, so install a rebuilt APK with `adb uninstall org.unciv.godot` first (`install -r` fails on a signature mismatch).
- **Editor.** To export from the editor instead: Editor Settings > Export > Android: set the Android SDK path, Java SDK path and a debug keystore, then Project > Export > Android.
- **Content.** The APK does not yet carry the ruleset jsons, fonts or packs (T276); expect it to stop at the content check on a device until then.

## How a release is produced (T169)

Local releases (`csharp-releases/`, git-ignored) are built by `release.py` below; CI publishes nothing from `csharp.yml`. Public GitHub releases are a separate, tag-triggered draft workflow: see "Cutting a release (T279)".

- `python3 -I tools/release.py T### [--cli-only | --client-only] [--windows] [--keep N] [--force] [--dry-run]` (run automatically by `ticket.py` after
  `state T### Done`) builds the tree it is run on into `<main checkout>/csharp-releases/<ticket>-<shortsha>/`:
  `cli/` (`dotnet publish Unciv.Cli`, self-contained linux-x64, so `./Unciv.Cli` runs with no dotnet installed), `server-linux-x64/` and `server-win-x64/`
  (`dotnet publish Unciv.Server`, self-contained, rulesets and the run guide bundled, T266; the Linux one is started and stopped by the smoke, the Windows one is UNVERIFIED and only warns on failure), `client-linux/` (the
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

## Cutting a release (T279)

`.github/workflows/csharp-release.yml` runs only when a tag `csharp-vMAJOR.MINOR.PATCH` (or `-prerelease`, e.g. `csharp-v0.1.0-rc.1`) is pushed. It creates a **draft**
GitHub release; it never publishes. It uses only `github.token` (`contents: write` in the `publish` job alone), no other secrets, and `gh` instead of a third-party action.
`csharp.yml` is unchanged (read-only) and does not run for tags.

1. Pick a commit on `csharp-migration` whose `csharp.yml` run is green (the release workflow does not re-run the tests). `git push origin csharp-migration` if needed.
2. Tag and push that commit: `git tag csharp-v0.1.0 <commit> && git push origin csharp-v0.1.0`. A tag that is not `csharp-v<semver>`, is not on `csharp-migration`, or already has a release makes the `verify` job fail and nothing is built.
3. Wait for the run (Actions, "C# release (draft)"; about 30 to 60 minutes: Godot export of three platforms, three CLI publishes). Jobs: `verify`, `cli`, `server`, `client`, then `publish`.
4. Open the draft under Releases. Check the files: `Unciv-client-{linux.tar.gz,windows.zip,macos.zip}`, `Unciv-cli-{linux-x64.tar.gz,win-x64.zip,osx-arm64.tar.gz}`, `Unciv-server-{linux-x64.tar.gz,win-x64.zip}` (T266, run guide `Unciv.Server/README.md`) and `SHA256SUMS` (`sha256sum -c SHA256SUMS` in the download folder). Read the notes: Windows and macOS builds are marked **UNVERIFIED**
   (never started on those systems; macOS is unsigned and not notarised), as is the Windows dedicated server (never run). Edit the notes if you have since tested them.
5. Publish by hand in the GitHub UI (tick "pre-release" for `-rc` tags). To redo a failed or wrong release: delete the draft and the tag (`git push origin :refs/tags/csharp-v0.1.0`), fix, and tag again; the workflow refuses to overwrite an existing release.

The archive names are the ones in the table above plus the CLI ones (`release_assets.py` owns the list; `test_ci.py` checks the workflow against it). The CLI archives are `dotnet publish -r <rid> --self-contained`
with `data/jsons` and `LICENSE` bundled; only the Linux client (main menu under Xvfb) and the Linux CLI (tiny game, empty `PATH`) are run by the workflow. The server archives (T266) are `dotnet publish Unciv.Server -r <rid> --self-contained` for linux-x64 and win-x64 with `data/jsons`, `LICENSE` and `README.md` bundled; the Linux one is smoke-run (`release_assets.py smoke-server`: `--help`, start, `LISTENING` line, SIGTERM, exit 0), and `csharp.yml` job `server-publish` builds and smokes the same (nothing uploaded). `Unciv.Server.csproj` keeps `IsRidAgnostic` (the client export would otherwise fail with NETSDK1152); a direct `dotnet publish -r` of the server is unaffected (checked). Android joins in T280 (marked place in the workflow).
Unverified until the first real tag run: the whole workflow (it cannot be run without pushing a tag); locally only `release_assets.py` (real CLI publishes for all three runtimes, packing, the Linux CLI smoke), the YAML syntax and the shape tests were checked.

## CI (T169)

**The workflow lives at `.github/workflows/csharp.yml`** (T239). It is separate from the Kotlin/Gradle workflows and runs only for changes under `csharp/**`, `android/assets/jsons/**`
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

`client-scenarios` gates merges like the other jobs (T239 saw it green and removed `continue-on-error`). The goldens were recorded on Fedora's Mesa llvmpipe and ubuntu's llvmpipe differs slightly in blending.
Measured over the 221 captures of a CI run: perceptual-hash distance at most 9, mean difference at most 0.019, at most 8.3% of pixels off by more than 16,
which the strict default (hash 6, 5% of pixels) rejects. T246 therefore runs the job with `HARNESS_GOLDEN_TOLERANCE=renderer`
(`GoldenTolerance.Renderer`: hash 12, mean 0.04, per-pixel 32, fraction 0.15, between that drift and the 19-30 hash / 0.045-0.12 mean of a wrong screen; the
self-test `--expect-golden-failure` proves a wrong screen is still rejected). Local runs keep the strict default, so the goldens stay valid on Fedora. A
scenario that fails there is reproduced locally with `run-scenario.sh <name>`. GitHub Actions cannot be run locally: the workflow was checked with
`actionlint`, `test_ci.py` (shape: paths, permissions, pinning, no publishing) and by running each of its commands in `dotnet-dev`.
