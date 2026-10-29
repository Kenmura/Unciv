# Godot client: minimum playable slices

<!-- toc -->
## Contents

- [Principles](#principles)
- [Placeholder assets (C007)](#placeholder-assets-c007)
- [Verification tooling (C008)](#verification-tooling-c008)
- [Slice list](#slice-list)
  - [Slice details](#slice-details)
- [Scope coverage of the old tickets](#scope-coverage-of-the-old-tickets)
- [What only a human can verify (Owner review)](#what-only-a-human-can-verify-owner-review)
- [Unverified assumptions](#unverified-assumptions)
<!-- /toc -->

Design for the C track (client). Replaces the one-line scopes of C002-C005 with a sequence of slices, each small enough for one agent
and verifiable with the C000 headless harness (GODOT.md). Written while the command layer (T164 split, `design/COMMANDS-IMPLEMENTATION.md`) was still being designed: slices depend on **T164 as an umbrella ID**; when it is split,
re-point each `Depends` to the specific command/query/turn-flow ticket named in the "Engine needs" column (a `ticket.py set`, nothing else changes). The server work is already split
(T165 is Won't port; see T172-T182 and `design/SERVER.md`). C006 depends on T172, T173, T179, T180. T180 (`Unciv.Client.Net`, ClientSession) is the remote counterpart of the local `GameSession`
the slices use: the client's `IGameSession` interface (queries + submit command + event stream) must be implemented by both, so C009/C010 code against the interface, never against `GameInfo`.

**Hosting update (owner decisions, `design/SERVER.md`).** Online play is player-hosted (Terraria-style): one player presses Host, friends join by `address:port` or an invite code; turns are simultaneous. New slices: C034 Host screen, C035 Lobby screen, C036 multiplayer turn bar; C006 is now the join side (address/invite, fingerprint prompt, reconnect). A flat hex map with placeholder art is fine for the MVP; the placeholder-art plan below is unchanged. Hotseat (C029) stays a Sequential-mode feature.

## Principles

1. **Vertical first, then widen.** Slice V1 (C007-C010) is a crude but complete loop: new game -> map -> select -> move -> end turn -> AI turns -> repeat.
   Everything later replaces a crude part with a better one or adds a screen. Flat hexes and cubes first; 3D polish is a later, independent swap.
2. **The client is a thin view.** It reads state only through the T164 query API and changes state only by submitting T164 commands. No client code
   mutates `GameInfo`; no rule is re-implemented in the client (action availability, costs, yields, combat preview all come from queries). A scene that
   needs data the queries lack files a T164-side ticket instead of reaching into Core.
3. **Scenes are scriptable.** Every screen is a `Control`/`Node3D` that can be instantiated alone with a seeded `GameSession` (fixed ruleset, seed, small map),
   so the harness can capture it without clicking through the menus.
4. **Assets by name only.** No scene or script references a file path, colour or mesh directly; see "Placeholder assets".
5. **Pixel goldens are weak evidence.** Each slice asserts engine/UI *state* first (query results, node tree, picked tile, button enabled) and uses the golden
   PNG only to catch layout and colour regressions. A human still judges look and feel (see "Owner review").

## Placeholder assets (C007)

No art exists; none may be copied from Firaxis/Civ 5. Approach: **AssetRegistry + ProceduralAssets**, in `Unciv.Godot/Assets/`.

- `AssetRegistry.Get<T>(AssetKey key)`: the only way client code obtains a mesh, material, texture, icon, font, theme or sound. `AssetKey` is a typed
  name such as `terrain/grass`, `unit/Warrior`, `resource/Horses`, `improvement/Farm`, `icon/stat/Gold`, `ui/panel`. Keys are derived from ruleset
  names (`Terrain.name`, `BaseUnit.name`, ...) with fallback chains: `unit/Warrior` -> `unitType/Melee` -> `unit/_default`, so every ruleset and mod renders
  something with no art for it.
- Resolution order per key: (1) `res://assets/override/<key>.{tres,glb,png,ogg}` if present (this is where real art or CC0 packs go, listed in ASSETS.md);
  (2) a pack manifest `assets/packs/<pack>/manifest.json` mapping keys to files (a pack can be selected in options, mods can ship one); (3) the procedural generator.
  Swapping art = dropping a file or pack, no code change. A test enumerates all keys used by Vanilla/bundled rulesets and asserts each resolves
  (non-null, no exception), and a lint test greps `Unciv.Godot` for `res://` literals and colour literals outside `Assets/`.
- `ProceduralAssets` generates, deterministically from the key (hash of the name picks hue; no `System.Random`; same output on every machine):
  hex prism meshes (flat top; later displaced for hills/mountains), terrain materials (flat albedo plus a tiny generated noise texture, per-terrain base colour table in
  one `PlaceholderPalette` file), unit "blocks" (a coloured primitive per unit type: box/cylinder/cone/sphere, civ-colour banner, a generated letter-glyph billboard
  of the unit's initials), building/improvement/resource props from primitives (e.g. Horses = two small boxes + sphere), 64x64 generated icons (coloured disc/shape with a
  glyph from the font) for stats, resources, techs, buildings, policies, promotions, and a procedural `Theme` of `StyleBoxFlat` panels (dark slate + gold accent, Civ-5-like
  in layout only).
- Fonts: the Godot default font is Latin only. C-l10n adds Noto Sans / Noto Sans CJK / Noto Sans Arabic / Noto Sans Thai/Devanagari etc. (SIL OFL; confirm each licence and record
  it) as fallbacks through the registry key `font/default`.
- Every shipped (non-generated) file gets a row in `ASSETS.md` (source URL, licence, ticket). Procedural assets are recorded once as a single row
  ("generated at runtime by ProceduralAssets, no files"). C-assets seeds those rows.

## Verification tooling (C008)

`Unciv.Godot.Harness/tools/run-scenario.sh <name>` runs a **scenario**: a C# class with a seeded `GameSession` (fixed seed, Vanilla ruleset, Tiny map, 2 civs + 1 AI,
fixed camera) and an ordered script: `Click(tile)`, `Key(...)`, `Press("Button name")`, `WaitFrames(n)`, `AssertQuery(...)`, `Capture("step")`. Rules:
- Each `Capture` writes `out/<scenario>/<step>.png` and compares to `golden/<scenario>/<step>.png` (GoldenComparer, existing tolerances); `--update-golden` as in C000.
- Each scenario ends with a **state assertion** from the query API (unit position, turn number, production queue contents), not just pixels.
- Same seed + same script must give the same state hash (T011) as the same script run headless with no Godot (commands replay). This proves the UI submits exactly the commands
  it claims to.
- `tools/review-sheet.sh` (also C008) runs every scenario and builds `out/review/index.md` + `contact-sheet.png`: a grid of every captured step with its caption and the checklist
  for that slice. That is what the owner opens (see "Owner review").
- Scenarios use Xvfb 640x480 by default, 1280x720 for screens that need it. Keep each under ~30 s on llvmpipe.

## Slice list

Size: S = under half a day, M = about a day, L = would exceed a day (already split to M parts here). "Engine needs" are tickets that must be Done for the data/commands.
Gate for every slice: `dotnet build Unciv.Godot -warnaserror` plus `Unciv.Godot.Harness/tools/run-scenario.sh <name>` exit 0 (and Standard checks for any `Unciv.Core` change, which these
slices should not make).

| # | Ticket | Slice | Engine needs | Size |
|---|---|---|---|---|
| 0 | C007 | Asset registry + procedural placeholder generator | none | M |
| 0 | C008 | Scenario runner and owner review sheet | none | M |
| 1 | C009 | Map view v1: flat hex map from queries, camera, tile picking, fog tint | T164 (map/visibility query), T099 | M |
| 1 | C010 | **V1 vertical**: unit blocks, select, move, end turn, AI turns | T164 (move, end turn, human turn flow) | M |
| 2 | C005 | UI kit: theme, screen stack, popups, tooltip host | none | M |
| 2 | C011 | HUD: top bar yields, turn banner, next-action button | T164 queries, T062 | M |
| 2 | C012 | Unit panel and orders (attack, fortify, found city, workers, automate, upgrade, ...) | T164 unit actions, T080, T083 | M |
| 2 | C013 | Notifications and popup alerts | T051, T055, T164, T171 | M |
| 2 | C014 | Minimap | T164 | S |
| 3 | C015 | Main menu and game setup (nations, difficulty, speed, map options) | T099, T045, T021, T022, T034 | M |
| 3 | C016 | Save/load screen and autosave | T100 | S |
| 3 | C017 | City screen A: info, tiles, specialists, focus | T164, T060-T062 | M |
| 3 | C018 | City screen B: production queue, purchase, buildings | T164, T063 | M |
| 3 | C019 | Tech screen | T164, T056 | M |
| 3 | C020 | Policy screen | T164, T056 | S |
| 4 | C021 | Diplomacy screen | T164, T059, T068 | M |
| 4 | C022 | Trade screen | T164, T059 | M |
| 4 | C023 | Overview screens (empire, cities, units, resources, ranking, religion, espionage) | T164, T057 | M |
| 4 | C024 | Victory / defeat screen and game over | T166, T164 | S |
| 4 | C025 | Localization: translations in the client, language picker, non-Latin fonts | T023, T170 | M |
| 4 | C026 | Tutorials | T030, T049 | M |
| 4 | C027 | Tooltips and Civilopedia | C005, C025 | M |
| 4 | C028 | Options screen, hotkeys, edge-pan | none | S |
| 4 | C029 | Hotseat player switch | T164 (human turn flow) | S |
| 4 | C030 | Placeholder audio (UncivSound -> generated tones) | T049 | S |
| 5 | C002 | 3D terrain: elevation, per-terrain materials, water depth | C009 | M |
| 5 | C031 | Terrain blending, fog of war polish, chunked meshes | C002 | M |
| 5 | C003 | Resource and feature props (incl. horses) | C002 | M |
| 5 | C032 | Improvements, roads, cities, borders, in-world yield icons | C003 | M |
| 5 | C004 | Unit visuals: models, animations, formations, combat presentation | C012, C002 | L split below |
| 5 | C033 | Unit animation + combat presentation (idle/move/attack/damage/death) | C004 | M |
| 6 | C006 | Networked play client: join screen (address:port / invite), TOFU prompt, reconnect overlay | T179, T180, C010, C016 | M |
| 6 | C034 | Host screen: lobby settings, address list, Copy invite, start/resume hosting | T204, T205, C015 | M |
| 6 | C035 | Lobby screen: slots, nation pick, ready-up, chat, kick, start | T180, T204 | M |
| 6 | C036 | Multiplayer turn bar: who ended, deadline, End/Undo end turn, resolving overlay | T180, T202, T175 | S |

C004 is the model/formation part (M); C033 is the animation and combat part (M). Slices within a tier are independent of each other unless the Depends says otherwise
(each depends on the previous tier's anchor). The `Depends` fields in the backlog are authoritative.

### Slice details

**C007 Asset registry + procedural generator.** Scope: `AssetKey`, `AssetRegistry`, override/pack/procedural resolution, `PlaceholderPalette`, hex prism mesh, terrain materials,
unit block builder, generic prop builder, icon generator, theme generator, ASSETS.md rows, key-coverage test, no-literal lint. Assets: this is the asset ticket.
Verification: scenario `asset-sheet` renders a labelled swatch sheet (every terrain tile, one block per unit type, 12 icons, the theme's button and panel) and asserts
registry resolution for all Vanilla ruleset names; golden of the sheet. Human: do the colours read as the right terrain, are blocks distinguishable at game camera distance.

**C008 Scenario runner and review sheet.** Scope as above plus `GameSession` test factory. Verification: a trivial scenario (click a button in a test scene, state changes)
passes, and a deliberately wrong golden fails. Human: the contact sheet is readable.

**C009 Map view v1.** Scope: `MapView` (Node3D, orthographic-ish tilted camera) builds one flat hex per tile via registry (`terrain/<name>`, base+feature tint, simple hill/mountain
as raised prism), fog tint for unexplored (black) and explored-not-visible (dim), pan (drag/WASD), zoom (wheel), `HexPicking` -> tile coordinate -> `TileHighlight`.
Data only through the map query. Verification: scenario `map-v1`: seeded Tiny map, capture at default camera and zoomed; click three known pixels and assert picked coordinates
equal the expected hex coordinates (via `HexMath`); assert explored/visible counts from the query equal the number of tiles drawn at each tint. Human: readability of terrain, camera feel.

**C010 V1 vertical.** Scope: `UnitView` (block + civ-colour banner) from the units-on-tile query; click selects (highlight + minimal text label of name/movement via plain Label),
right-click or second left-click on a reachable tile submits Move command (path from the pathing query, drawn as line of dots); "Next turn" plain button submits End Turn, waits for AI turns to finish
(human turn flow), refreshes view; repeat. Minimal quick-start entry: `QuickStart.tscn` creates a Tiny seeded game via GameStarter and enters the map (full menu is C015).
Verification: scenario `v1-vertical`: capture start; click settler/warrior, capture selection; click destination, assert unit tile changed to the destination and movement spent; press Next turn
5 times, assert turn == 6, every AI civ has acted (unit/city state hash differs from turn 1 and equals the headless replay of the same commands), assert unit still selectable; capture after each turn.
Human: does it feel playable, are AI moves visible (units move on the map).

**C005 UI kit** (narrowed from the old UI-shell scope). Scope: `UiTheme` from registry, `ScreenManager` (push/pop full-screen, Esc back), `PopupStack` (modal, queued, OK/choice), `TooltipHost`
(delay, follows mouse, rich text), reusable `ListPanel`, `IconLabel` (stat icon + number), `TabBar`, scroll lists, UI scale. Verification: scenario `ui-kit` captures a screen with each widget;
assert popup queue ordering and that Esc pops. Human: layout, font sizes, contrast.

**C011 HUD.** Top bar (gold, science, culture, happiness, faith, golden age, resources, turn/year, menu button) from the player-stats query; turn banner shown on turn start ("Your turn", year); a "next action"
button cycling Next Unit / Next City choice / Research / End Turn depending on what needs attention (from the same query the engine uses for "turn over"). Verification: scenario `hud`: values equal the query numbers
(assert label text against `CivInfoStatsForNextTurn`), button text changes as the last unit is ordered, capture. Human: Civ-5-feel of the bar.

**C012 Unit panel and orders.** Scope: unit panel (name, strength, moves, promotions, health), action menu built from the unit-actions query (T080): found city, build improvement/road, fortify, sleep, explore,
automate (auto-work/auto-explore), pillage, upgrade, disband, promote, great-person actions, skip. Attack: click enemy shows combat preview from the battle-preview query (damage, odds) and attack-range overlay; confirm submits Attack.
Path preview and reachable-tile overlay. Verification: scenario `unit-orders`: for each action, assert the menu lists it exactly when the query reports it available, execute Found City and assert a city exists on the tile,
Fortify and assert state, Attack against a placed enemy and assert HP changes equal the engine result. Human: menu discoverability, preview legibility. (Covers old C004 selection, path preview and attack-range overlays.)

**C013 Notifications and popups.** Notification list (clickable: jump camera to tile / open screen), turn-start summaries, PopupAlerts (`PopupAlert`, T055): ruin reward, great person choice,
found religion, city captured, quest, event choice (T171). Verification: scenario `notifications`: seeded game with injected notifications of each type; assert list length, click centres camera on the expected tile, popup choice submits the right command. Human: urgency of presentation.

**C014 Minimap.** Generated `ImageTexture` from the map query (explored colour per terrain, civ border colour), viewport rectangle, click-to-jump. Verification: `minimap` golden plus assert clicking a pixel moves the camera to the matching tile. Human: readability.

**C015 Main menu and game setup.** Menu (New game, Load, Multiplayer (C006), Options, Quit). Setup: nations (grid with civ colour, random, human/AI toggle, player count, city-state count), difficulty, game speed,
era, map type/shape/size/world wrap, resource and terrain settings, seed, victory types, base ruleset and mod checkboxes (mod loading T170), advanced (no barbarians, ...). Builds `GameSetupInfo` (T045) and calls GameStarter.
Verification: `setup` scenario: choose options by clicking, press Start, assert `GameInfo.gameParameters` / map size / civ list equal the choices, and that the new game's state hash equals headless `GameStarter` with the same `GameSetupInfo`; capture each tab. Human: option clarity.

**C016 Save/load.** List saves (UncivFiles, T100) with metadata (turn, civ, date, mod list), load, save as, delete, autosave each turn, quick save/quick load, "resume last". Verification: `saveload`: save, advance a turn, load, assert turn and state hash equal the saved ones; autosave file appears after End Turn. Human: none beyond layout.

**C017 City screen A.** Opens by clicking a city. Header (name, pop, growth, happiness), tile grid with worked/available/locked tiles from the city query, click to assign/lock tile (command), specialists add/remove,
focus dropdown, stat breakdown tooltip. **C018 City screen B.** Production queue (add, reorder, remove, `Perpetual` items), buildable list filtered by the query with costs and turns, buy with gold/faith (command; disabled with reason), building/wonder
list, sell building, raze/puppet/annex/resistance states. Verification (both): scenario with a city of known contents: assert queue after commands, tile assignment changes `CityStats`, purchase reduces gold by the query's cost; capture. Human: information density.

**C019 Tech screen.** Scrollable tree laid out by `TechColumn` rows/columns, state colours (researched/available/queued/locked), choose research (command), queue, what-it-unlocks list, eureka-style boosts are not in Unciv. **C020 Policy screen.** Branch columns, adopt (command), cost, ideology/tenets if ruleset has them, golden age info.
Verification: assert adoptable/available sets equal queries, choose command changes `TechManager.currentTechnology`; capture. Human: tree layout legibility with many techs.

**C021 Diplomacy screen.** Civ list (met civs), status (war/peace/DoF/open borders), relationship modifiers text, declare war, peace offers, denounce, city-state screen (influence, quests, gift gold, bully, protect). **C022 Trade screen.** Offer builder (both sides: gold, gold/turn, luxuries, strategics, cities, techs, treaties, open borders), AI accepts/declines via engine, demands from AI shown as popups. Verification: assert offered legal set equals `TradeEvaluation`/query and accepted trade changes both civs' state; capture. Human: flow.

**C023 Overview screens.** Empire overview tabs: stats, cities table, units table (select/jump), resources, ranking, religion (beliefs), espionage (spies, assign), global politics. Verification: tables equal query rows; capture. Human: layout.

**C024 Victory/defeat.** Triggered by engine victory/defeat events (T166 scorer, `VictoryManager`): victory screen with type, ranking, replay timeline graph (CivRankingHistory), "continue playing"/"main menu"; defeat screen; spectator mode after defeat. Verification: force victory via a scenario ruleset with a 1-turn conquest condition; assert screen and that "continue" resumes End Turn commands. Human: tone.

**C025 Localization.** Wire real translation files (T170) into the client: all labels through `Tr(key)` (Unciv `Translations`), language picker (persisted), fonts: registry `font/default` with fallback chain for CJK, Cyrillic, Arabic, Hebrew, Thai, Devanagari, Korean using OFL fonts recorded in ASSETS.md. Right-to-left and shaping: rely on Godot TextServer (needs ICU/HarfBuzz build: verify in the headless install, if not, ticket note and Latin+CJK+Cyrillic only). Verification: scenario `l10n-sheet` renders the same string in each shipped language;
assert `Font.HasChar` for every character of every translation value in every shipped language (no tofu), assert no raw key shown (no string equal to a known key); golden per script. Human: that the shaping/RTL actually looks right (only a native-script reader can fully judge).

**C026 Tutorials.** Engine has the `Tutorial` model (T030) and `TutorialTrigger` (T049). Client: tutorial popup queue fired by triggers (first unit, first city, ...), "Don't show again" persistence, tutorial list in menu (Civilopedia link), tutorial tasks overlay. Verification: scenario triggers `Tutorial` at the right game event; assert shown id and persisted flag. Human: wording.

**C027 Tooltips and Civilopedia.** Tooltip content builders for tiles, units, buildings, stats (breakdown from `CityStats`, `TileStatFunctions`), and a Civilopedia screen (category lists, entries with icons, linked references) generated from ruleset objects (`Ruleset`, text from uniques `getDescription`). Verification: assert text for a few fixed objects equals the engine's description; capture. Human: reading comfort.

**C028 Options.** Sound volume, language (links C025), UI scale, window mode, key rebinding (persisted), edge-pan/scroll speed, show tile yields/resource icons toggles, pack selection (asset packs). **C029 Hotseat** (Sequential mode only; hosted games are simultaneous and do not use it). "Pass the device" screen hiding the map between human players (spec by T164 human turn flow ticket). **C030 Audio.** `UncivSound` -> generated sine/click `AudioStreamWav` via registry `sound/<name>`; verification: each UncivSound enum value resolves to a non-empty stream. Human: none (placeholders).

**C002 3D terrain** (narrowed): replace C009's flat hex with displaced meshes: elevation per terrain, hill/mountain shapes, water depth colour and shore, per-terrain materials and the generated noise texture; MultiMesh for repeated instances (grass vegetation as instanced tufts, real shaded terrain, not flat sprites). **C031**: blending between adjacent terrains (vertex colours or splat edge), fog of war and visibility tint polish, chunked mesh with frustum culling and a performance check (Standard map 1000+ tiles within a frame budget, measured by a scenario that reports mean frame time; threshold generous because llvmpipe). Verification: golden at 3 camera angles and sizes; pick tests still pass (picking uses terrain height); assert the triangle/instance counts stay under budget. Human: the look.

**C003 Props** (narrowed): feature props (forest, jungle, marsh trees), resource props incl. horses (posed herd of 3-5 blocks; animation can be added later), cattle, sheep, wheat, iron, etc. built by the generator and overridable; strategic/luxury icons in the tile. **C032**: improvements (farms, pastures, mines, camps, plantations...), roads/railroads as meshes between tile centres, city models/banners and border lines per civ colour, in-world yield icons (toggle). Verification: scenario `props` places every resource and improvement in the seeded game via the test factory, asserts one prop node per tile feature, golden; swap test: a test override file for `resource/Horses` replaces the procedural prop without code change. Human: recognisability.

**C004 Unit visuals** (narrowed): unit models per unit type via registry (multi-figure formations: N figures by `BaseUnit` strength tier, arrangement on tile), civ colouring, health bar, status icons (fortified, sleeping, automated, promotion available). **C033**: idle, move (along path, tile by tile), attack, damaged, death animations as simple procedural tweens (bob, lunge, flash, fall) and combat presentation (floating damage numbers, ranged projectile, city bombard flash), driven by the combat results the engine already computed; the client animates, never decides. Verification: scenario `combat-presentation` runs an attack, asserts the animation queue drained, the dead unit node freed, and the numbers shown equal the engine's `BattleResult`. Human: feel, timing.

**C006 Networked client (join side)**: join screen (address:port, or pasted invite `unciv://host:port/fingerprint[/token]`, password field), fingerprint prompt for typed addresses with a hard-stop screen when a known host's key changed, known hosts and identity key under `user://`, reconnect overlay (retry with backoff to the last address, `Resume`), "host went away" state. Scenario `join`: in-memory host, wrong password refused, fingerprint mismatch blocks, drop and resume restores the mirror (viewHash equal). Human: error wording.

**C034 Host screen**: reuses the C015 setup widgets for `LobbySettings` (map, speed, difficulty, civ and city-state counts, AI fill, mods with ruleset hash, turn timer, password, max players), lists local addresses and a port-forward hint, Copy invite, Start hosting (in-process `GameHost`, T205), and a list of saved hosted games to resume. Scenario `host-screen` as in the ticket. **C035 Lobby screen**: slot table, nation picker (rules from T199), colour, ready, chat; host-only kick, close slot and Start; joiners see settings read-only and the refusal reason on ruleset mismatch. **C036 Multiplayer turn bar**: player list with ended/connected marks, countdown from `TurnStatus.deadline`, End turn / Undo end turn, "processing turn" overlay during Resolving, toast for another human's visible action. Verification by scripted multi-client scenarios over the in-memory transport; human review of the waiting-for-players clarity.

## Scope coverage of the old tickets

| Old item | Where |
|---|---|
| C002 3D tiles, per-terrain materials, elevation, water depth | C002 |
| C002 blending, fog/visibility tint | C031 (basic tint in C009) |
| C002 tile yields and improvements as in-world props | C032 |
| C003 resource props incl. horses, improvements, roads | C003, C032 |
| C004 unit models, formations | C004 |
| C004 idle/move/attack/damage/death animation, combat presentation | C033 |
| C004 selection, path preview, attack-range overlays | C010, C012 |
| C005 top bar | C011 |
| C005 minimap | C014 |
| C005 unit panel | C012 |
| C005 city screen | C017, C018 |
| C005 tech/policy trees | C019, C020 |
| C005 diplomacy | C021, C022 |
| C005 turn banner | C011 |
| C005 notifications | C013 |
| C005 tooltips | C005 (host), C027 (content) |
| Review additions: setup, save/load, victory, localization, tutorials, hotseat, options, audio | C015, C016, C024, C025, C026, C029, C028, C030 |

## What only a human can verify (Owner review)

Goldens prove "did not change", not "looks right". Reserved for the owner: art direction and colour readability, camera and input feel, animation timing, UI density and hierarchy, tooltips wording,
non-Latin script shaping and RTL correctness, audio, and whether the game is *fun and understandable* from the first turn.
`tools/review-sheet.sh` (C008) produces one contact sheet and `out/review/index.md` per run. Checklist the sheet carries for each slice (answer yes/no/notes):
1. Can I tell terrain types apart at default zoom? Are fogged tiles clearly dimmer than visible ones?
2. Can I tell my units from enemy units, and unit types from each other, at a glance?
3. Is the selected unit, its reachable area and its path obvious?
4. After End Turn, can I see what the AI did (moves, attacks, new cities)?
5. Is every number on screen explained by a tooltip on hover?
6. Can I do the thing without a mouse accident (undo/confirm for destructive actions: disband, declare war, raze)?
7. Does every screen close with Esc and return to the map in the same camera position?
8. Any text clipped, overlapping or in a fallback box (tofu) in the language I read?

## Unverified assumptions

- T164's query API exposes: visible/explored tiles per player, units on tile, per-unit available actions, reachable tiles and paths, combat preview, city detail (tiles, queue, buildable), player stats, notifications, and an "awaiting human input" signal. Each is a requirement on the T164 split, not yet confirmed.
- The AI turns run synchronously inside End Turn (or via a callback the client can await) so the client can show them; if turn processing is long the client needs a progress overlay (not scheduled).
- Godot's Compatibility renderer under llvmpipe supports MultiMesh, vertex colours and the TextServer features the slices use; Complex-text-layout (RTL/Indic) depends on the Godot build having HarfBuzz+ICU, not yet checked in the installed 4.7.2 .NET build.
- 640x480 llvmpipe frame times are good enough to run all scenarios under the Standard-checks time budget; the per-scenario ~30 s figure is a guess.
- Godot C# can load the chosen OFL fonts at runtime from `assets/` in a headless export-less run.
- Pack/mod art override by `res://assets/override` is acceptable to the owner as "swappable from one place".
