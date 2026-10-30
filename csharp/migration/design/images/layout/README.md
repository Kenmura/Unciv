# Multi-resolution layout sheets (T312)

**Documentation images, not goldens.** Nothing here is read by the scenario runner or any CI comparison.

| Field | Value |
|---|---|
| GPU | NVIDIA GeForce RTX 5060 Ti, Vulkan 1.4, Godot 4.7.2 Forward+ |
| Scenario | `layout-matrix` (`Unciv.Godot.Harness/tools/layout-matrix.sh <out> [sizes]`), each screen in an off-screen viewport of the window size, OS scale 1 |
| Sizes | 1366x768, 1920x1080, 2560x1440, 3440x1440, 3840x2160 (the file name is the size; `-0` HUD/menu/city/tech/policies/diplomacy, `-1` trade/overview x3/espionage/pedia, `-2` main menu/new game/host/lobby/join/options) |
| Sheets | each PNG of the scenario downscaled to a 2x3 contact sheet (JPEG), so small text is only legible in the real run; open the scenario output for full size |
| Date | 2026-10-30 |
