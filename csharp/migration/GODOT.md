# Godot headless test harness (C000)

<!-- toc -->
## Contents

- [One-time setup (inside `dotnet-dev`)](#one-time-setup-inside-dotnet-dev)
- [Running](#running)
- [Two instances on one computer (T334)](#two-instances-on-one-computer-t334)
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

## Two instances on one computer (T334)

Two copies of the client started normally share one user folder, so they are the same player (one `identity.key`, one settings file, one saves folder). To test multiplayer on one machine start the
second copy as another **profile**:

```
Unciv.x86_64 -- --profile=friend              # an exported client (Windows: Unciv.exe -- --profile=friend)
UNCIV_PROFILE=friend Unciv.x86_64             # the same through the environment (the argument wins if both are given)
godot --path Unciv.Godot -- --profile=friend  # from a checkout
```

Everything after Godot's own `--` is the game's. A profile keeps its identity key, host key, known hosts, last address, settings (player name included), saves, autosaves and hosted games under
`user://profiles/friend/`; with no profile everything stays where it always was, so existing players keep their identity. A name is 1 to 32 letters, digits, `-` or `_` (starting with a letter or
digit); anything else (`..`, `a/b`) makes the client print the reason and exit with code 2 rather than share another player's files. The profile name is the window title (`Unciv - friend`), a line
on the main menu and the player's name in lobbies. Local mods (`user://mods`) and the extracted content bundle are shared on purpose. Typical test: instance one (no profile or `--profile=host`)
hosts, instance two (`--profile=friend`) joins `127.0.0.1:47821`. Only one instance can host on a port: a second host must change the port in the Host screen's Network tab (a taken port is
refused with a message). Godot's log file (`user://logs/godot.log`) is shared and rotated by Godot; the client has no lock file and no listening port besides the host's. The harness
scenario `two-instances` hosts and joins as two profiles over real TCP and checks two distinct player keys, separate settings and saves.

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
adb install -r Unciv.Godot/build/android/Unciv.apk && adb shell am start -n org.unciv.godot/com.godot.game.GodotAppLauncher   # the exported launcher alias (GodotApp itself is not exported)
Unciv.Godot/tools/android-smoke.sh                 # T277: headless x86_64 emulator, installs Unciv-emulator.apk, waits for UNCIV_MENU_READY in logcat, screenshot to build/android/smoke/
```

- **Why net9.0.** The Godot 4.7.2 Android export template bundles .NET 9 and refuses a `net10.0` game assembly. `package.sh android` exports
  `UncivAndroid=true`, which makes `Unciv.Core`, `Unciv.Assets`, `Unciv.Client.Net`, `Unciv.Server` and `Unciv.Godot` build for `net9.0`
  (`$(UncivTfm)` in `Directory.Build.props`; `Unciv.Godot.csproj` keeps literal `net10.0`/`net9.0` elements because the Godot editor rewrites a TFM it cannot read as text).
  Desktop builds, the CLI, the harness and the tests stay `net10.0`. The only net10-only API was `JsonDocumentOptions.AllowDuplicateProperties` in `ProtocolCodec`
  (net9 path rejects duplicate keys with `ProtocolCodec.HasDuplicateProperties`, unit-tested). Building both TFMs in one checkout re-restores `obj/` on each switch (automatic, a few seconds).
- **Preflight.** The script checks for the SDK pieces, `keytool` and the Android templates first and exits 3 listing exactly what is missing. No NDK and no Gradle are needed (prebuilt templates, `gradle_build/use_gradle_build=false`).
- **Signing.** The script generates a throw-away keystore in `build/android/` for each build, hands it to Godot through `GODOT_ANDROID_KEYSTORE_DEBUG_*` and deletes it; nothing is committed. The APK is a debug build (`--export-debug`), good for sideloading, not for a store.
- **Preset.** `export_presets.cfg` `Android` / `Android (emulator)`: package `org.unciv.godot`, `INTERNET` and `CHANGE_WIFI_MULTICAST_STATE`, immersive mode, `window/handheld/orientation=4` (sensor landscape, in `project.godot`).
- **Headless SDK/JDK paths.** A headless Godot export reads the Android SDK and JDK only from editor settings (no `ANDROID_HOME`/`JAVA_HOME` fallback), so the script runs Godot with a throw-away `XDG_CONFIG_HOME` that sets `export/android/android_sdk_path` and `java_sdk_path` (from `ANDROID_HOME`/`ANDROID_SDK_ROOT` and `JAVA_HOME`, else the JDK that owns `keytool`). Your own editor settings are not touched.
- **Content.** The script runs `tools/stage-content.py` first; the presets include `Content/*` so the rulesets, packs and fonts travel in the pck (T276). Measured sizes (arm64 `Unciv.apk` 131 MiB, emulator variant 231 MiB): `design/ANDROID.md`, section 1.
- **Keystore plan (T280).** Release APKs are debug-signed with the same throw-away keystore, so every build has a different signature: users sideload, and must uninstall before installing another build. No signing secret exists in the repository or in the workflows (a test forbids `secrets.` and keystore files in the release workflow). If a store or update-in-place build is ever wanted, the plan is: generate one release keystore offline, keep it only as a GitHub Actions encrypted secret (plus the owner's offline backup), have the release job decode it into `$RUNNER_TEMP` and pass it through `GODOT_ANDROID_KEYSTORE_RELEASE_*` with `--export-release`, and delete it after the export. That needs a new ticket and the owner's decision; nothing of it is implemented.
- **Reinstall.** The keystore is regenerated on every build, so install a rebuilt APK with `adb uninstall org.unciv.godot` first (`install -r` fails on a signature mismatch).
- **Emulator smoke (T277).** `tools/android-smoke.sh [apk]` needs `sdkmanager 'emulator' 'platform-tools' 'system-images;android-34;default;x86_64'` and `/dev/kvm` (exit 3 lists missing pieces, exit 4 means no KVM). It makes a throw-away AVD (the developer's `~/.android` AVDs are untouched), boots it headless (`-gpu swangle_indirect`; `swiftshader_indirect` and `guest` segfaulted the emulator on the Fedora dev machine), installs the APK, starts it through the `GodotAppLauncher` alias and polls `adb logcat` for `UNCIV_MENU_READY` (the same marker `smoke-export.sh` waits for; Godot's `print` goes to logcat), then saves `menu.png`, `logcat.txt` and the emulator log in `build/android/smoke/`. Verified locally: boot about 40 s, whole run 46 s, screenshot shows the real main menu.
- **Editor.** To export from the editor instead: Editor Settings > Export > Android: set the Android SDK path, Java SDK path and a debug keystore, then Project > Export > Android.

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
with `data/jsons` and `LICENSE` bundled; only the Linux client (main menu under Xvfb) and the Linux CLI (tiny game, empty `PATH`) are run by the workflow. The server archives (T266) are `dotnet publish Unciv.Server -r <rid> --self-contained` for linux-x64 and win-x64 with `data/jsons`, `LICENSE` and `README.md` bundled; the Linux one is smoke-run (`release_assets.py smoke-server`: `--help`, start, `LISTENING` line, SIGTERM, exit 0), and `csharp.yml` job `server-publish` builds and smokes the same (nothing uploaded). `Unciv.Server.csproj` keeps `IsRidAgnostic` (the client export would otherwise fail with NETSDK1152); a direct `dotnet publish -r` of the server is unaffected (checked). T280 added the `android` job: `package.sh android` (arm64-v8a), ABI and `apksigner verify` checks, shipped as `Unciv-client-android-arm64.apk` with its line in `SHA256SUMS`; the notes mark it **NOT device-tested** (T278 has not run on a phone) and debug-signed.
Unverified until the first real tag run: the whole workflow (it cannot be run without pushing a tag); locally only `release_assets.py` (real CLI publishes for all three runtimes, packing, the Linux CLI smoke), the YAML syntax and the shape tests were checked.

## CI (T169)

**The workflow lives at `.github/workflows/csharp.yml`** (T239). It is separate from the Kotlin/Gradle workflows and runs only for changes under `csharp/**`, `android/assets/jsons/**`
(the rulesets the engine reads) or the workflow itself, on pushes to `csharp-migration` and on pull requests; plus a weekly schedule for the soak.
`permissions: contents: read`, no secrets, no `pull_request_target`, nothing published, every action pinned by commit SHA. The only uploads are 7-day workflow artifacts: the failed scenario renders (T255) and the Android debug APKs plus the emulator smoke output (T277).

| Job | Runs | What |
|---|---|---|
| `engine` | push, PR | the Green gate: `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`, `Unciv.Godot` and `Unciv.Godot.Harness` builds with `-warnaserror` (they need only the SDK and the `Godot.NET.Sdk` NuGet package), `coverage.py --check` (base chosen by `tools/ci_base.py`: PR merge-base, push `before`, else `--all`), `screenshots.py --check`, `ticket.py check`, the tools' Python unit tests |
| `client-export` | push, PR | pinned Godot + export templates (cached, SHA-512 checked), `retry-on-crash.sh 2 package.sh linux`, `smoke-export.sh` |
| `client-scenarios` | push, PR (non-blocking) | `run-smoke.sh`, every scenario in `scenarios.txt`, and `run-scenario.sh menu --expect-golden-failure`, under Xvfb |
| `android` | push, PR | T277: SDK pieces from the runner image's `sdkmanager` into a fresh root, `package.sh android` (arm64-v8a) and `android-emulator` (arm64-v8a + x86_64) with `retry-on-crash.sh`, ABI and `apksigner verify` checks, artifact `android-apk` (both debug-signed APKs, throw-away keystore, 7 days), then, if `/dev/kvm` can be opened (udev rule step), `android-smoke.sh` on an x86_64 emulator and artifact `android-smoke` (screenshot and logs). **No KVM: the smoke step is skipped with a warning, build and artifact still run. The smoke step has `continue-on-error: true` until its first green hosted run (a failure shows as a red step, the job stays green); remove that line then.** UNVERIFIED on a hosted runner (needs a push); the tagged release workflow builds its own arm64 APK (T280) |
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
