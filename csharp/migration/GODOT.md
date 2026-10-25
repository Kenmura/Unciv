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
