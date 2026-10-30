# Gritty Warrior design options: real-GPU documentation images (T294)

These are **documentation images, not goldens**. They were rendered on a real GPU. Nothing in this folder is read by the scenario runner, `--update-golden` or any CI comparison; never copy one into `golden/`.

| Field | Value |
|---|---|
| GPU | NVIDIA GeForce RTX 5060 Ti |
| Driver | NVIDIA proprietary 615.71.09, Vulkan 1.4.351 |
| Renderer | Godot 4.7.2 Forward+ (Vulkan), `--rendering-method forward_plus --rendering-driver vulkan`, vsync off |
| Date | 2026-10-29 |
| In-game pictures | real `GameScreen` over the T271 fixture (Large Pangaea, 84 units), `UNCIV_BENCH_MODE=warrior UNCIV_BENCH_CLEAN=1`, 1920x1080, then cropped; Ultra unless the name says `high`. The Warrior shown is the nearest own Warrior that is not inside a city; for each run `Packs/units/models/unit/Warrior.glb` was swapped for the option in a private worktree and restored with `git checkout` (the shipped file is unchanged) |
| Turntable pictures (`front`, `threeq`, `side`, `back`, `animation-check`) | a small studio scene on the same GPU and renderer (one sun with shadows, flat ambient, green floor, no Ultra post effects), 640x800 per view, so the models can be judged up close; **this is not the in-game lighting**, see the in-game crops for that |
| Files | `current-*` is the shipped Warrior; `d1-*` .. `d6-*` are the options |

| File | What it shows |
|---|---|
| `sheet.jpg` | the comparison sheet: columns CURRENT, 1..6; rows front, three-quarter, in-game default view crop, in-game closest-zoom crop |
| `dN-front.jpg`, `dN-threeq.jpg`, `dN-side.jpg`, `dN-back.jpg` | close-up turntable views of option N (also `current-*`) |
| `dN-ingame-default.jpg` | crop of the Ultra default play view (zoom 14): the figure among the other units of the fixture |
| `dN-ingame-zoomed.jpg`, `dN-ingame-zoomed-high.jpg` | crop of the closest camera zoom the game allows (4), Ultra and High |
| `dN-vs-current.jpg` | the current Warrior (left) against option N (right): front and three-quarter, and the zoomed in-game crop |
| `animation-check.jpg` | walk (top), attack (middle) and death (bottom) poses, columns 1..6 then the current Warrior (last) |

Regenerate the models: `python3 -I Unciv.Godot/tools/build-warrior-options.py <KayKit gltf folder>` (writes `migration/design/models/warrior-options/`). The in-game views come from `Unciv.Godot.Harness/tools/gpu-bench.sh` with `UNCIV_BENCH_MODE=warrior`.
