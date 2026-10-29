# Android client: export feasibility, device requirements and touch-UI gap list

<!-- toc -->
## Contents

- [1. Export feasibility and build recipe](#1-export-feasibility-and-build-recipe)
- [2. Renderer and frame budget](#2-renderer-and-frame-budget)
- [3. Screen and input audit: gap list](#3-screen-and-input-audit-gap-list)
  - [A. Platform and data (blocks any run)](#a-platform-and-data-blocks-any-run)
  - [B. Map and unit interaction (mouse/keyboard assumptions)](#b-map-and-unit-interaction-mousekeyboard-assumptions)
  - [C. Screens (layout and scrolling on a small display)](#c-screens-layout-and-scrolling-on-a-small-display)
  - [Totals](#totals)
- [4. TLS/TCP multiplayer on Android](#4-tlstcp-multiplayer-on-android)
- [5. Go/no-go and proposed ticket split](#5-gono-go-and-proposed-ticket-split)
<!-- /toc -->

Spike T259 (GitHub issue #1). Question: can the Godot .NET client ship as an Android app, what stands in the way, and how should the work be split?

**What was and was not done.** This environment (Linux, `dotnet-dev` container, no device, no emulator, no Android SDK, JDK 21 only) could run
the real Godot 4.7.2 editor's Android export validation, inspect the export templates and read every input path in the client. It could **not** produce an APK, install
anything, or measure anything on a phone. The attempt to install the Android command-line tools was refused by the sandbox policy, so the SDK-dependent steps are recorded as
UNVERIFIED with the exact blocker rather than guessed. Every claim below is tagged **Verified** (observed here), **From source** (read in this repository or in the shipped
export template) or **Estimate/UNVERIFIED**.

## 1. Export feasibility and build recipe

**Result: the current client cannot be exported today, for one concrete, verified reason. Past that, the export validator was not reached (it stops at the first configuration error) and the content-path gap A-1 will stop the app at startup.**

Verified (headless `Godot_v4.7.2-stable_mono --export-debug Android` against a temporary Android preset, then discarded):

```
ERROR: Cannot export project with preset "Android" due to configuration errors:
Exporting to Android when using C#/.NET is experimental.
C# project targets 'net10.0' but the export template only supports 'net9.0'. Consider using gradle builds instead.
```

So the pinned template (`4.7.2.stable.mono`, installed by `Unciv.Godot/tools/install-export-templates.sh`) bundles a .NET 9 runtime glue and refuses a `net10.0` game
assembly. Every project in `csharp/` targets `net10.0` (`Unciv.Core`, `Unciv.Assets`, `Unciv.Client.Net`, `Unciv.Godot`). Godot itself labels C# on Android **experimental**.
There are three ways out; T260 must pick one (they are ordered by how little they disturb the repository):

| Option | What changes | Risk |
|---|---|---|
| A. Multi-target `net9.0` for the Android build only | `Unciv.Core`, `Unciv.Assets`, `Unciv.Client.Net`, `Unciv.Godot` get `<TargetFrameworks>net10.0;net9.0</TargetFrameworks>` (or a property switched by the package script); the Android build passes `-p:TargetFramework=net9.0` | Any net10-only API in the four projects fails to compile; fixed by a small shim. A quick `dotnet build -p:TargetFramework=net9.0` was tried and failed only because restore had not been run for that framework (NETSDK1005), so whether the code compiles on net9 is **UNVERIFIED** |
| B. Gradle build | Install the Android build template (`android/build/`) into the project; `gradle_build/use_gradle_build=true`; Gradle 8.11.1 and the SDK/NDK fetch their own pieces | Heavier CI (Gradle, NDK 29), a generated folder in the repo (git-ignored), still subject to the runtime-pack match **UNVERIFIED** |
| C. Wait for a template with net10 | Bump the pinned Godot version when a release ships it | Unknown date; blocks the issue |

Recommended: A, with B as the fallback (see the decision in section 5).

**Template facts (From source, `android_source.zip` and `android_release.apk` of the pinned template):**

- `minSdk 24` (Android 7.0), `targetSdk 36`, `compileSdk 36`, build tools `36.1.0`, Android Gradle Plugin 8.6.1, Gradle 8.11.1, Kotlin 2.1.21, NDK `29.0.14206865`, Java 17 language level.
- The prebuilt APK template contains `arm64-v8a`, `armeabi-v7a`, `x86` and `x86_64` engine libraries (71 to 85 MB each uncompressed; the whole release template is 105 MB, debug 128 MB). A non-Gradle export
  uses these prebuilt templates and needs only the SDK build-tools (`apksigner`, `zipalign`), platform-tools (`adb`) and `keytool` from the JDK, no NDK and no Gradle.
- A Gradle export additionally needs the Android SDK platform 36, build-tools 36.1.0, NDK 29 and downloads Gradle and AAR dependencies.

**ABIs.** Ship `arm64-v8a` only for phones and tablets from the last decade; add `x86_64` for the emulator in CI. 32-bit ARM and x86 are not worth the size or the .NET runtime pack
(**Estimate**: each extra ABI adds roughly 25 to 40 MB of engine plus Mono runtime to the APK).

**Exact build recipe (proposed, the first two steps Verified, the rest UNVERIFIED until T260 runs them with an SDK):**

1. `Unciv.Godot/tools/install-export-templates.sh` (installs the template already pinned for Linux/Windows).
2. `Unciv.Godot/export_presets.cfg`: `[preset.2] platform="Android"`, `gradle_build/use_gradle_build=false`, `architectures/arm64-v8a=true` (others false, `x86_64=true` for the CI emulator build),
   `package/unique_name="org.unciv.godot"` (placeholder, not the Kotlin app id `com.unciv.app`), `permissions/internet=true`, `permissions/change_wifi_multicast_state=true` (LAN beacon, see section 4),
   `screen/immersive_mode=true`, `include_filter` for the data files once T276 decides how content gets into the pck (the jsons live in `android/assets/jsons`, outside `res://`, and the packs sit behind `Packs/.gdignore`, so a plain filter is not enough; section 3, gap A-1).
3. Android SDK: `cmdline-tools;latest`, `platform-tools`, `build-tools;36.1.0`, `platforms;android-36`; editor settings `export/android/android_sdk_path`, `java_sdk_path`, `debug_keystore` (a throw-away keystore made with `keytool -genkeypair`, never committed).
4. `godot --headless --path Unciv.Godot --export-debug Android build/android/Unciv.apk` after the same throw-away `.sln` step `package.sh` already does.
5. `adb install -r Unciv.apk && adb shell am start -n org.unciv.godot/com.godot.game.GodotApp`.

**Size of what is bundled (Verified by `du`, compression ratios are Estimate):**

| Content | Uncompressed | In the APK |
|---|---|---|
| Rulesets and translations (`android/assets/jsons`) | 24 MB | JSON compresses well, Estimate 4 to 6 MB |
| Unit models pack (`Packs/units`) | 41 MB | glb is partly compressed already, Estimate 25 to 35 MB |
| Fonts (`Assets/fonts`, Noto subsets) | about 4.3 to 5 MB | Estimate 3 MB |
| Icon pack (`Packs/unciv`) | 0.9 MB | about 0.9 MB |
| Engine library (arm64-v8a) | 71 MB | Estimate 25 to 30 MB |
| .NET runtime pack plus the game assemblies (arm64) | not measured | **UNVERIFIED**, Estimate 25 to 45 MB |

Estimate total for an arm64-only debug APK: 90 to 130 MB. That is below the 150 MB Play base limit but large for a sideloaded game; the 41 MB model pack is the lever (T260 may ship the
APK without it only if the client tolerates a missing pack, which it does for icons but is not verified for models).

## 2. Renderer and frame budget

**From source.** `project.godot` sets `renderer/rendering_method="gl_compatibility"` and `renderer/rendering_method.mobile="gl_compatibility"`, so Android already uses the OpenGL ES 3.0
Compatibility renderer. That is the right choice for mid-range phones (Vulkan Mobile is not needed and the ultra-graphics work, T264/C097, must treat Compatibility as the floor).
Godot's Compatibility renderer needs OpenGL ES 3.0 (every Android 7.0+ phone with a GPU from 2014 on).

**Estimate, not measured.** There is no device here and the harness runs on software GL under Xvfb, so no frame time could be taken. A mid-range phone (Snapdragon 6/7-series, Mali-G57 or
Adreno 610 class, 1080x2400) renders roughly a quarter of the fill rate of a low-end laptop iGPU. The expensive parts in this client are:

- the 3D terrain (chunked, `TerrainChunkLayer`) and, above all, **the unit figures: up to four animated models per tile at 2 to 5k triangles each** (`design/UNIT-MODELS.md`), so a screen of 150 units is up to 600 skinned meshes;
- real-time shadows and MSAA if the quality settings ever turn them on (C097);
- 2400x1080 native resolution: Godot on Android renders at native size unless the 3D scale is lowered (`rendering/scaling_3d/scale`).

Budget proposal for the follow-up (to be confirmed on a device, not assumed): 30 fps minimum, 16.6 ms for 60 fps on the map screen; start with `scaling_3d/scale=0.75`, MSAA off, one static
figure per tile on mobile, idle animation paused. C085 (Done, "Unit model LOD and performance caps") already pauses idle animation off-screen and lowers detail zoomed out; **its Notes record no
baseline frame time**, so the first measurable number must come from T261-style profiling on a device (ticket T278 below, shared with T271). Until then "playable on a mid-range phone" is an open claim.

## 3. Screen and input audit: gap list

Sizes: **S** = under a day for one agent session, **M** = one full session, **L** = more than one session (split).

Facts that shape every item:

- Godot's default `input_devices/pointing/emulate_mouse_from_touch=true` is not changed in `project.godot`, so a **tap arrives as a left click and a one-finger drag as a left drag**. This is why the map click and drag-pan code works unchanged. It is also why nothing else does: no code reads `InputEventScreenTouch`, `InputEventScreenDrag`, `InputEventMagnifyGesture` or `InputEventPanGesture` (From source: grep over `Unciv.Godot`).
- The UI scale is `AutoScale = min(w/1280, h/720)` clamped to `[1, 2]` (`UiTokens.AutoScale`), multiplied by the user's scale. A 2400x1080 phone gets 1.5, so the logical viewport is 1600x720 (a **Regular** layout, not Compact, because Compact is below 1000 logical px). The default control height is 32 logical px, **48 physical px**. At a typical 400 ppi phone that is about 3 mm. Android's guideline is 48 dp (about 9 mm, roughly 120 to 150 px). Controls are therefore **about 2.5 times too small**. Scaling everything to reach 48 dp from a 32-logical-px control needs a total scale of about 3.75 (120 / 32), which leaves a logical viewport of roughly 640x288, far below the 720 height the layouts were designed for. So a global scale alone cannot fix it: the density-derived scale should stay moderate (a total of about 2.0 to 2.6, i.e. a logical viewport of roughly 920 to 1200 wide and 415 to 540 high, where the 32 px control is 64 to 83 px, 25 to 33 dp) and the shared widgets must get a 48 dp *minimum height* on touch builds on top of that (gap B-1). The remaining shortfall of the layouts at that viewport is gap group C. Also note `AutoScale` floors to 0.25 steps and is clamped to [1, 2], so a density-derived value needs its own helper rather than a change of the clamp.
- A scale in dp must come from `DisplayServer.ScreenGetDpi`/`ScreenGetScale`, not from the pixel count.
- Text input uses `LineEdit` (join address, invite code, player name, console): the Android virtual keyboard works for `LineEdit` in Godot, but it covers the lower half of a 415-px-tall logical viewport.

### A. Platform and data (blocks any run)

| # | Gap | Evidence | Size |
|---|---|---|---|
| A-1 | **Content is read from disk next to the executable, which does not exist on Android.** Rulesets and translations (`RulesetLocator`, `TranslationLocator` through `ContentLocator.FindJsons(exeDir, ...)`), the pack folders (`GodotAssetFiles.ExternalRoots`, `Packs/unciv`, `Packs/units`, with `.gdignore` keeping them out of the pck) and fonts (`FontChain`, walks up from `GlobalizePath("res://")`) all use `System.IO`. In an APK `res://` is inside the pck, `GlobalizePath("res://")` is not a directory and `OS.GetExecutablePath()` is the app process. Needs either "export everything into the pck, extract to `user://` on first run and point the existing disk readers at it" (small change, a one-time copy of about 30 MB, versioned so an update re-extracts) or a read-through abstraction | `Assets/RulesetLocator.cs`, `Assets/GodotAssetFiles.cs`, `Assets/FontChain.cs`, `tools/package.sh` | M |
| A-2 | Export blocked by `net10.0` vs template `net9.0` (section 1) | Verified | M (option A) |
| A-3 | `user://` paths are fine (`UserPaths` globalizes them; Android maps them to app-private storage). Local mods, saves and settings work; **mods cannot be dropped in by hand** because `user://` is not reachable without root on Android 11+ | `Assets/UserPaths.cs` | S (document; in-app mod install is a later feature) |
| A-4 | Android back button: nothing handles `NOTIFICATION_WM_GO_BACK_REQUEST`; Escape-based navigation (`ScreenManager.Back`, `TryEscape`) is never triggered by it, and by default Godot quits the app on back | grep: only `Key.Escape` handlers | S |
| A-5 | App lifecycle: pause/resume (`NOTIFICATION_APPLICATION_PAUSED/RESUMED`) is not handled. The TCP connection dies when Android freezes the app in the background; the reconnect overlay exists (`ReconnectOverlay`, T247 reconnect) but has not been exercised on a lifecycle event. Autosave on pause is missing | `Play/ReconnectOverlay.cs` | M |
| A-6 | Safe area and display cutouts: no use of `DisplayServer.GetDisplaySafeArea`/`GetDisplayCutouts`; the top bar (`HudView`) and the minimap would sit under a notch or the gesture bar | none in code | S |
| A-7 | Orientation: only a landscape layout is designed. `project.godot` has no `display/window/handheld/orientation`; the export preset must pin `sensor_landscape` (portrait is a non-goal for v1) | `project.godot` | S |
| A-8 | Window settings: the options screen has window mode and resolution-style controls that are meaningless on Android (`ClientSettingsApplier` window mode), and the UI scale stepper range is `0.5` to `4` in `0.25` steps | `Settings/ClientSettingsApplier.cs`, `Settings/SettingsPanel.cs` | S |
| A-9 | Clipboard and sharing the invite code: `DisplayServer.ClipboardSet` works on Android; a "share" intent would need a Kotlin/Java plugin, out of scope | `App.cs` | S (copy only) |
| A-10 | Permissions: `INTERNET` is mandatory for joining (not set by default), `CHANGE_WIFI_MULTICAST_STATE` for LAN beacon listening (`Net/Discovery/LanBeaconListener`, UDP) which also needs a `WifiManager.MulticastLock` that Godot does not take; **LAN discovery on Android is expected not to work** and the join-by-address path is the supported one | `Unciv.Core/Net/Discovery` | S (document) / M (plugin to take the lock) |

**A-1 resolved by T276.** One bundle, not a read-through layer: `Unciv.Godot/tools/stage-content.py` zips the tree a desktop export keeps beside the executable (`data/jsons`, `Packs`, `Assets/fonts`) into `Content/unciv-content.zip` plus a `.version` file (git-ignored; the Android preset must `include_filter="Content/*"`, T260). `ContentRoot` (`Assets/ContentRoot.cs`, logic in `Unciv.Assets/Packaging/ContentBundle.cs`) uses the executable folder when it holds `data/jsons` (desktop unchanged), else extracts the bundle once to `user://content` (stamped; a new version is extracted into a temp folder and swapped in; a crash in between is recovered by re-extracting on the next start) and returns that folder. `RulesetLocator`, `TranslationLocator`, `GodotAssetFiles` and `FontChain` treat it as the executable folder. `smoke-export.sh --bundle` runs the packaged Linux build with its data folders deleted to exercise the path on desktop. Not verified on a device: reading the zip from the APK's pck, extraction time and free-space behaviour on a phone.

### B. Map and unit interaction (mouse/keyboard assumptions)

| # | Gap | Evidence | Size |
|---|---|---|---|
| B-1 | Hit targets and scale: control height 32 logical px (`ControlMinHeight`), row 32 (`RowHeight`), compact 28, icon 24. Needs a density-derived auto scale and a minimum 48 dp target on buttons, list rows, tab bars, tech/policy cards, steppers | `UiTokens.cs`, `UiTheme.cs` | L (C096 does the scale and the shared widgets; per-screen layout is C101) |
| B-2 | **Right click moves and attacks the selected unit** (`MapView` raises `TileRightClicked` on right-button release; `GameScreen.OnTileRightClicked` runs `RightClickMove` or the attack preview, or opens the marker menu when no unit is selected). A phone has no right button. Needs: tap a tile with a unit selected shows the move/attack target with a confirm (or a long-press = right click), plus an explicit "move here" affordance | `Map/MapView.cs:473`, `Play/GameScreen.cs:1324` | M |
| B-3 | Pinch zoom and two-finger pan: zoom is mouse wheel only (`MapView.SetZoom` on `WheelUp/Down`); pan is left-drag with a threshold, or arrow/WASD keys, or edge pan | `Map/MapView.cs:464` | M |
| B-4 | Drag threshold: `DragThresholdPixels` is a desktop value; a finger needs about 8 dp (about 20 px) to avoid turning every tap into a drag, and the press must cancel when a second finger lands | `Map/MapView.cs` | S |
| B-5 | Hover tooltips: tile info (`GameScreen.UpdateHover` on `InputEventMouseMotion` via `TooltipHost`, 0.5 s delay) and about 50 other `TooltipHost`/`TooltipText` uses (stat chips, rival ribbon, unit panel chips, promotions, city stats). With touch there is no hover; the information is not reachable. Needs tap-to-show (and long-press) with a dismiss on next tap, and tile info in the unit/tile panel | `Ui/TooltipHost.cs`, `Play/GameScreen.cs:438`, `MouseEntered` in `DecisionPickerView.cs:166` | M |
| B-6 | Hover-to-preview in the decision picker (`button.MouseEntered` fills the detail text) | `Play/DecisionPickerView.cs:166` | S |
| B-7 | Shift-click: group-move modifier (`MapView.LastClickShift`, `GameScreen:1297`) and queue-instead-of-set in the tech tree (`TechNodeCard:115`) | those lines | S (toggle button) |
| B-8 | Keyboard-only affordances: every hotkey (`HotkeyInput`, `GameScreen.TryHotkey`, `KeyHint` chips on buttons), Ctrl+Up/Down to reorder the production queue (`CityProductionView:318`), Tab/Up/Down in the console (`ConsoleView:181`), Escape to close | grep | M (each needs an on-screen control; most already have a button) |
| B-9 | Right-click in the city screen (`CityScreen:367`: lock/unlock tile work) | `Play/CityScreen.cs:367` | S (long-press) |
| B-10 | Marker menu and pings open on right click and close on any click outside | `Play/GameScreen.cs:439`, `MapMarkerMenuView` | S (follows B-2) |
| B-11 | Unit panel popover closes on any mouse click outside (`UnitPanelView._Input`) | `Play/UnitPanelView.cs:204` | S |
| B-12 | Minimap: click-to-recenter works through emulation; drag on it conflicts with map pan if the minimap is under the finger (assigned to C100) | `Play/MinimapView.cs` | S |

**B-1 (scale and shared widgets), B-3 and B-4 resolved by C096.** `DisplayProfile` (touch = `OS.HasFeature("mobile")`, density from `ScreenGetDpi`) switches `UiTheme` to `UiTokens.TouchAutoScale` (dpi/160, capped at 2.6, floored to a quarter step, never below a 900x415 logical viewport; a 400 ppi 2400x1080 phone gets 2.5) and gives the theme a 48 dp floor (`MinTargetPx`) for every control height (buttons, tabs, segments, steppers, line edits) and `ListRow`. `MapView` tracks fingers (`PinchTracker`): a second finger cancels the one-finger press and starts a pinch (zoom about the midpoint) with two-finger pan; the drag threshold of an emulated touch press is 8 dp; a finger held for 0.5 s raises `TileRightClicked` (long press = right click, the hook for C100). Taps keep using the emulated mouse. Desktop (not a touch build) is unchanged. Not verified on a device: real multitouch event order, `ScreenGetDpi` values on real phones, content-scale interaction with the user's UI scale.

### C. Screens (layout and scrolling on a small display)

All full-screen views were designed for 1280x720 logical at least with a Compact breakpoint under 1000 logical px that only lowers control height from 32 to 28 and hides a few columns. None has a phone
layout; scroll containers do scroll by touch (Godot's `ScrollContainer` supports touch drag-scroll through the emulated mouse), but a drag that starts on a button is consumed by the button.

| # | Screen group | Concern | Size |
|---|---|---|---|
| C-1 | Main menu, new game, host, join, lobby, load/save, options | Forms with `LineEdit`, steppers, dropdowns; the virtual keyboard hides the focused field; steppers and segmented controls under 48 dp | M |
| C-2 | World HUD: top bar, next-action dock, notifications, rival ribbon, victory status, turn bar, social panel | Top-bar icon chips are tiny; the dock and the unit panel (bottom) compete with the thumbs' reach; notification list must become tap targets | M |
| C-3 | City screen and production, tile buttons | Dense two-column layout, per-tile toggles, queue editing | L |
| C-4 | Tech tree and policy tree | Large pannable graphs of cards; pan needs two-finger or drag-on-background distinct from card taps; cards need the long-press detail | M |
| C-5 | Diplomacy, trade, espionage, overview tables (`DataTable`) | Wide tables, many columns; need horizontal scroll or column hiding | L |
| C-6 | Pedia | Two-pane master/detail; on a phone becomes a navigation stack | M |
| C-7 | Modals and popups (`Modal`, `PopupStack`) | Size to window and scroll internally already (`Modal` caps height); buttons must reach 48 dp | S |

### Totals

Gap items: A has 10, B has 12, C has 7 screen groups. Order-of-magnitude **Estimate**: platform and data (A) 3 to 4 sessions, map and unit interaction (B) 3 to 4, screen layouts (C) 5 to 8, plus
device verification and tuning. These are agent-session sizes from reading code, not measurements.

## 4. TLS/TCP multiplayer on Android

T249 left Android UNVERIFIED (`design/SERVER.md`, "Unverified platforms"). This spike adds what can be established without a device and states precisely what cannot.

**Verified/From source (new evidence):**

- The client transport is `TcpClient` + `SslStream` with a pinned-fingerprint `RemoteCertificateValidationCallback` (`Unciv.Client.Net/Transport/TcpTlsClientTransport.cs`), `Tls13 | Tls12`, no platform trust store involved (the host key is self-signed and pinned by fingerprint). That is the best case for Android: it does not depend on Android's system CA store or network security config.
- Android is **client-only in v1** (decision recorded in `design/SERVER.md`). `HostScreen`/`HostingSession` (listen socket, `HostCertificate.Generate`, `X509Certificate2` PFX load/save, `host-key.pfx`) must be **hidden on Android**; they are not exercised and cleartext listening sockets on a phone behind carrier NAT are not useful.
- The Godot .NET export for Android runs the Mono runtime (the `dotnet/runtime` Android runtime pack). Its `System.Net.Security` and `System.Security.Cryptography` use the Android platform provider (a native shim that calls into the Java TLS stack through JNI). The Godot export template ships its own Java activity, not the .NET-for-Android Java glue, so whether `SslStream` works in this configuration is **not established by anything in this repository or by this environment**. This is the single largest unknown for multiplayer and is the reason T278 exists.

**UNVERIFIED (cannot be run here, no device/emulator/SDK):** the on-device handshake, the negotiated protocol, the fingerprint refusal path, the `X509Certificate2` creation used for the client's identity key (`ClientIdentity`, `identity.key`), reconnect after Doze/background, and UDP beacon reception.

**Manual verification recipe (for T278, nothing here has been run):**

1. Host from Linux: `dotnet run --project Unciv.Server` (or the Linux client's Host screen); note the invite and the fingerprint.
2. `adb install` the APK on a device or an x86_64 emulator on the same network (emulator: the host is reachable at `10.0.2.2`).
3. Join by address from the Android client: expect a successful handshake, `TcpTlsTransport.NegotiatedProtocol` of TLS 1.2 or 1.3 in `adb logcat`, and a refused connection with a wrong fingerprint.
4. Background the app for 60 s, bring it back: the reconnect overlay must resume the session.

Android 9+ blocks cleartext traffic by default for the Java stack but this transport is a raw socket under TLS, so the cleartext policy is not expected to matter (**UNVERIFIED**).

## 5. Go/no-go and proposed ticket split

Decision: go (conditional). Nothing found rules Android out: the renderer is already Compatibility, the minimum SDK is 24, the transport is TLS with a pinned key, and the content problem (A-1) is a contained code change.
The condition is that T260 first resolves the `net9.0` template mismatch (option A, falling back to B); if neither yields a launching APK within one session, stop and reconsider before filing the interaction tickets C099 to C101.
The UI gap (touch interaction, density scale, phone layout) is large but mostly additive and testable in the existing harness with synthetic touch events.

Dependency order and sizes (implemented as tickets by this spike; T260 and C096 were rewritten, the rest are new):

| Ticket | Scope | Size | Needs a device? |
|---|---|---|---|
| T260 | Unblock the export (net9.0 or Gradle, section 1), Android preset, `package.sh android`, debug-signed arm64 APK locally (records the APK size; T278 only re-checks it on a device) | M | No SDK-only; install is manual |
| T276 | Content delivery without an executable folder: ship jsons/packs/fonts in the pck, extract to `user://`, point `RulesetLocator`/`GodotAssetFiles`/`FontChain` at it (gap A-1); unit tests with a fake file source | M | No |
| T277 | CI job: build the APK (arm64 + x86_64), upload as artifact; emulator smoke on `ubuntu` with KVM reaches the main menu | M | Emulator in CI |
| T278 | On-device verification: TLS join (section 4), lifecycle reconnect, frame time on a mid-range phone, APK size actuals; fill the UNVERIFIED rows of this file | M | Yes, human or lab device |
| C096 | Touch map input and density scale: pinch/pan, tap/long-press mapping, density-derived UI scale with 48 dp targets, `phone-world` scenario at 1080x2400 | M | No (synthetic touch events in the harness) |
| C099 | Android shell behaviour: back button, safe area, orientation, lifecycle autosave, virtual keyboard avoidance, hide Host on Android, permissions (gaps A-4 to A-10) | M | Harness for logic, device for feel |
| C100 | Replace mouse-only interaction: right-click move/attack and marker menu (B-2, B-10), tap tooltips (B-5, B-6), modifier toggles and on-screen buttons for keyboard-only actions (B-7, B-8, B-9, B-11), minimap drag (B-12) | M | No |
| C101 | Phone layouts of the screen groups (section 3 C-1 to C-7): split by screen group when claimed (HUD and menus first, then city, tech/policy, tables) | L, split on claim | No |

Not proposed: portrait support, a Play Store (AAB, store signing, listing) release, in-app mod installation, hosting from Android, LAN discovery on Android (needs a native multicast lock), a Kotlin/Java plugin of any kind.

Evidence references: C085 (unit model LOD and performance caps, mobile performance), T249 (TLS matrix), C043 (client settings, UI scale), C042 (export packaging), `design/UNIT-MODELS.md` (figure budget).
