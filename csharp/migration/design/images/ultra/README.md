# Ultra graphics: real-GPU documentation images (T291)

These are **documentation images, not goldens**. They were rendered on a real GPU, so they differ from the llvmpipe goldens in `Unciv.Godot.Harness/golden/`. Nothing in this folder is read by the scenario runner, `--update-golden`, or any CI comparison; never copy one into `golden/`.

| Field | Value |
|---|---|
| GPU | NVIDIA GeForce RTX 5060 Ti |
| Driver | NVIDIA proprietary 615.71.09, Vulkan 1.4.351 |
| Renderer | Godot 4.7.2 Forward+ (Vulkan), `--rendering-method forward_plus --rendering-driver vulkan --gpu-index 0`, vsync off |
| Resolution | rendered at 1920x1080; the single views are downscaled to 1600x900 JPEG, the comparison sheet is 3 x 640x360 tiles |
| Date | 2026-10-29 |
| Scene | real `GameScreen` over the T271 fixture: generated Large Pangaea (2838 tiles, 11 civs plus 4 city-states, 24 cities, 84 units, map explored) |
| Levels | `GraphicsModel.Preset` Low / High / Ultra with the T271 calibration (Ultra exposure 0.6, ambient 0.3, saturation 1.3) |
| Clean mode | `UNCIV_BENCH_CLEAN=1`: HUD, minimap, unit panel and yield chips hidden so the pictures show the map and its lighting only |

Regenerate (host, not the dotnet-dev container; see `Unciv.Godot.Harness/tools/gpu-bench.sh`):

    UNCIV_BENCH_CLEAN=1 UNCIV_BENCH_FRAMES=10 UNCIV_BENCH_VIEWS=mid,coast,coastlow,overview \
      Unciv.Godot.Harness/tools/gpu-bench.sh forward_plus <out-dir> 0

Views: `mid` is the default play zoom (14), `coast` zoom 9 over a shoreline, `coastlow` zoom 5 with a low 28 degree tilt (the water close-up), `overview` zoom 40 (whole map).

| File | What it shows |
|---|---|
| `compare-low-high-ultra.jpg` | 3 x 3 sheet: columns Low / High / Ultra, rows mid / coast / overview |
| `low-mid.jpg`, `high-mid.jpg`, `ultra-mid.jpg` | the default play view at each level |
| `ultra-coast.jpg` | Ultra over a shoreline |
| `ultra-water-closeup.jpg` | Ultra water at a low tilt (the C098 shader: depth tint and a faint ripple; subtle by design, see T289) |
| `ultra-overview.jpg` | Ultra with the whole Large map in view |

Not shown: Intel iGPU, the compatibility renderer, Windows, macOS, Android. Ultra's water animation cannot be seen in a still.
