# Ultra graphics: what "ray tracing / AAA shaders" can mean for this client (T264)

Source: GitHub issue #3 ("rtx on feature", owner unsure how feasible). This is a spike: it states what Godot 4.7.2 offers, what we could and could not verify, and recommends a minimum-scope preset. Ticket C097 implements it.

Decision: go, for a cheap Ultra preset on the Forward+ renderer (shadows, tone mapping, glow, SSAO, optional MSAA/TAA and water/terrain shader polish); no-go on hardware ray tracing.

## 0. How this was verified (and what was not)

- Pinned engine: Godot 4.7.2 .NET (`Unciv.Godot.Harness/tools/setup-godot.sh`). Project renderer: `gl_compatibility`, also for mobile (`project.godot`). Map scene: `Map/MapView.cs` (one `DirectionalLight3D`, flat ambient `Environment`, terrain chunk meshes with vertex colours, `Assets/MapMaterials.cs`).
- Prototype (evidence only, never merged): throw-away branch `proto/T264-ultra` (local, commit 86b640000c) adds env-var driven `Environment` effects to `MapView._Ready`. It was run through the existing `terrain-3d` scenario with the real client code, `--rendering-method` and `--rendering-driver` overridden by hand.
- **Every render below came from Mesa software rasterizers, never from a real GPU**: OpenGL 3 on llvmpipe for `gl_compatibility`, and Vulkan 1.4 on **lavapipe** (Mesa's CPU Vulkan driver, `lvp_icd`) for Forward+ and Mobile. Godot reported `Forward+ - Using Device #1: Unknown - llvmpipe`. lavapipe proves the effects compile and run, nothing about speed or about hardware ray tracing.
- Not verified, because no GPU exists on this machine: frame cost of any effect (all numbers in the cost column are estimates from general experience and are labelled so), behaviour on real Vulkan/GL drivers, Android, macOS (MoltenVK), hardware ray tracing at all.
- Ray tracing API presence was checked by listing the symbols in the pinned `GodotSharp.dll`, not by running anything (see section 1).

## 1. Feasibility of hardware ray tracing (honest answer)

What exists in the pinned engine:

- `RenderingDevice` (the low-level Vulkan/D3D12/Metal abstraction, not available on `gl_compatibility`) in 4.7.2 has ray tracing entry points: `BlasCreate/BlasBuild`, `TlasCreate/TlasBuild`, `RaytracingPipelineCreate`, `RaytracingList*` and `RaytracingListTraceRays`, plus acceleration-structure flag enums (present in `GodotSharp.dll`; we did not call them, so their semantics, required device feature checks and stability are **unverified**). It is a compute-style API: we would write our own ray-generation/closest-hit shaders and our own renderer pass, then composite the result into the viewport.
- Godot's built-in renderers do not use it for lighting. Nothing in `Environment`, `WorldEnvironment`, `LightmapGI`, `VoxelGI` or the standard materials is "ray traced": global illumination is baked (LightmapGI, VoxelGI) or real-time probe/SDF based (SDFGI), reflections are screen-space (SSR) or probe based, shadows are shadow-mapped.
- It would need a GPU and driver exposing Vulkan ray tracing (KHR extensions; roughly RTX 20+, RDNA2+, Arc, recent Apple GPUs are the usual set, which we cannot confirm per vendor here), the Forward+ renderer, and does not exist on `gl_compatibility` or Android's default path. lavapipe does not offer it (assumed, not tested).
- The ticket forbids vendor-only RT extensions and proprietary SDKs, and "own the renderer pass" is a large, test-hostile project for a hex-tile strategy game whose camera is mostly orthogonal and top-down.

Conclusion: real hardware ray tracing is technically reachable only as custom RenderingDevice work, is unverifiable in CI and on this machine, and has near-zero visible payoff on a flat tile map with small figures. **No-go.** "Ultra" should mean the cheaper effects below, honestly labelled; the UI must not say "ray tracing".

What Godot does provide (all built-in, no vendor code): shadow-mapped directional/omni/spot shadows, tone mapping (Filmic/ACES/AgX), glow/bloom, SSAO and SSIL (screen-space, Forward+ only), SSR (Forward+ only), SDFGI and VoxelGI (Forward+ only, expensive), volumetric fog (Forward+ only), depth-of-field and auto-exposure (camera attributes), MSAA/FXAA/TAA/FSR2 (availability depends on renderer), custom `.gdshader` materials (all renderers, with feature subsets).

## 2. Ranked effects: payoff versus cost

Cost classes are estimates for a mid-range GPU at 1080p, **not measured** (no GPU here). Prototype column says what the llvmpipe/lavapipe render showed on the `terrain-3d` scene (a small flat island; mean absolute RGB difference per pixel against the matching baseline, 0-765 scale).

| Rank | Effect | Visual payoff for this game | Est. cost | Renderer needed | Prototype observation |
|---|---|---|---|---|---|
| 1 | Directional shadows (4-split, soft filtering) | High once figures, props and forest stand on terrain (grounds units) | low-medium | works in `gl_compatibility`, Mobile, Forward+ | rendered in compat (diff 15.9); terrain is flat so only figures/relief cast |
| 2 | Tone mapping (ACES/AgX) plus mild glow | High, cheap "film look" | low | compat (glow in compat since 4.3; unverified for 4.7.2 mobile GPUs), Mobile, Forward+ | rendered in compat (tone diff 15.8, glow diff 2.2) |
| 3 | Custom water shader (animated normal, fresnel, depth tint, shoreline foam) | High: most of the map is ocean/coast | low-medium | all renderers (spatial shader) | not prototyped; payoff by reasoning only |
| 4 | Terrain shader polish (triplanar noise detail, slope blend, wind on vegetation) | Medium-high | low | all renderers | not prototyped |
| 5 | SSAO (and SSIL) | Medium on figures/props, little on flat terrain | medium | Forward+ and Mobile only (not compat) | rendered under Forward+/lavapipe but the change on flat terrain was tiny (0.09 / 0.24 against the Forward+ baseline); needs a scene with props to judge. In compat the flag had no visible intent (a 1.8 difference appeared that we did not explain) |
| 6 | MSAA 2x/4x or TAA | Medium: hex edges, thin lines | low (MSAA) to medium | MSAA: compat/Mobile/Forward+; TAA/FSR2: Forward+ | not prototyped |
| 7 | SSR on water | Medium-low at top-down angles | medium | Forward+ only | not prototyped |
| 8 | Volumetric fog / god rays | Low for the game, risky | high | Forward+ only | washed the whole scene to grey with density 0.02 (render diff 217); needs real tuning, looks wrong on an orthogonal map camera |
| 9 | SDFGI / VoxelGI | Low: static lit tiles, no interiors | very high | Forward+ only | not tried, rejected on cost/benefit |
| 10 | Hardware ray tracing (section 1) | Very low here | very high | custom RenderingDevice, Vulkan RT GPUs | not possible to test |

Platform notes: Forward+ is Vulkan/D3D12/Metal on desktop, it is not offered on mobile defaults and not on web; Android (T259) and any browser build stay on `gl_compatibility`. macOS runs Vulkan via MoltenVK or Godot's Metal driver (unverified here).

## 3. Coexistence

### Harness goldens (llvmpipe)

- Goldens are recorded with `--rendering-driver opengl3` on llvmpipe under the compat renderer. Ultra effects **must be off in every scenario**: scenarios construct their scene with the low preset explicitly (C097 requirement) so no scenario reads user settings for graphics, and goldens stay unchanged.
- Important measured finding: just switching `gl_compatibility` to Forward+ **changes the colours of the unchanged scene** (mean difference 50.7 per pixel with no effects enabled, versus compat). Linear colour and default tonemapping/ambient handling differ per renderer, so the placeholder palette and the Civ-colours legibility guarantees were tuned on compat. An Ultra renderer therefore needs its own colour check, and a screen comparison of Ultra versus Low must never be a golden tolerance test.
- Ultra-look golden: possible only on lavapipe (it worked here in about 5 s for `terrain-3d`) and is not byte-stable across Mesa versions; keep it a non-gating, separately recorded image (C097 already calls it optional). Recommendation: do not add it to CI.
- CI runners differ in blending (see `GODOT.md`); Vulkan on lavapipe is a second moving part, so do not gate on it.

### Android (T259)

Soft link only. The Android build uses `gl_compatibility`; Ultra is unavailable there and the setting shows the "not supported on this device" note. The compat-safe subset (shadows, tone mapping, glow, shaders, MSAA) is a candidate "High" level there, subject to T259's frame-budget measurements.

### Quality setting model (C043)

`ClientSettings` is versioned JSON under `user://`; add `GraphicsQuality` (Low / High / Ultra) with a settings migration, default Low. It also feeds the C085 LOD caps (Done): Low = tighter caps, higher levels looser.

## 4. Recommended minimum-scope Ultra preset and fallback

Levels:

- **Low** (default; harness scenarios): today's look, compat renderer, no shadows.
- **High** (compat-safe, can ship on every platform including Android if T259 allows): directional shadows, tone mapping plus glow, MSAA 2x, water and terrain shader polish.
- **Ultra** (Forward+ only): everything in High, plus SSAO, MSAA 4x or TAA, SSR on water. Not included: volumetric fog, SDFGI/VoxelGI, ray tracing.

Renderer switch: Godot picks the renderer once at start, so choosing Ultra saves the setting and offers a **restart** (`OS.SetRestartOnExit` with `--rendering-method forward_plus`); picking Low/High restarts back to `gl_compatibility`. The `Main` scene must read the setting before building the scene; first-run behaviour is unchanged.

Fallback, in order: if the renderer after restart is not Forward+ (driver refused, `RenderingServer.GetCurrentRenderingMethod()` mismatch, software adapter name such as llvmpipe), apply High and show a visible, localised note in the settings screen ("Ultra needs a Vulkan or Direct3D 12 GPU; showing High"). Never crash, and persist an "ultra failed" flag so we do not restart-loop. A real-GPU check of the restart path is not verified.

Colour: add a per-level exposure/ambient constant so Ultra's palette matches Low within an agreed tolerance (the measured Forward+ shift above is the reason).

## 5. Prototype evidence

Four renders of the existing `terrain-3d` `angle-high` step (640x480), from the throw-away branch, all on software rasterizers (see section 0):

| File | Renderer / driver | Effects |
|---|---|---|
| `ultra-graphics/compat-baseline.png` | `gl_compatibility`, OpenGL 3, llvmpipe | none (today) |
| `ultra-graphics/compat-glow-shadow-tonemap.png` | same | shadows, ACES, glow |
| `ultra-graphics/forwardplus-no-effects.png` | Forward+, Vulkan, lavapipe | none |
| `ultra-graphics/forwardplus-ssao-glow-shadow-tonemap.png` | same | SSAO, glow, shadows, ACES |

What the images show: effects run; Forward+ with no effects is noticeably brighter and more saturated than compat (the colour-shift finding); ACES plus glow brightens and softens the island. They do not show a convincing AAA gain on this tiny flat scene; SSAO is nearly invisible here. No GPU timing exists.

## 6. Go/no-go and provisional ticket scope

Decision: go (cheap Ultra only; hardware ray tracing no-go).

C097 scope after this spike (also updated in the backlog):

1. Setting model: `GraphicsQuality` Low/High/Ultra in `ClientSettings`, migration, default Low, tests for the model and preset mapping (a pure mapping table: level to {shadows, tonemap, glow, msaa, ssao, ssr, shaderTier}).
2. Preset applier in the client: sets `Environment`, light shadows, `Viewport.Msaa3D` for Low/High/Ultra; scenarios and harness pin Low.
3. Renderer-switch flow with restart, the fallback to High, the visible note, and the persisted failure flag.
4. Shader polish (water, terrain) as a separate follow-up ticket because it needs art tuning; High and Ultra work without it.
5. Settings screen entry and scenario `settings-graphics` (renderer independent), as already specified.
6. Colour calibration per level, using renders from a real GPU before shipping the Ultra label.

Follow-ups worth filing: shader polish ticket; a real-GPU measurement pass (frame time per effect on Linux, Windows and macOS, and a mid-range Android phone with T259); optional Ultra-look lavapipe render outside CI.
