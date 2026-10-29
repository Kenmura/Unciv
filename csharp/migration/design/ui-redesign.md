# UI/UX redesign plan (Godot client)

Status: tickets filed 2026-10-28 as C061-C079 (phase 4, held Blocked at the root C061 until phases 1-3 are clear). Written 2026-10-28 from a review of every golden in
`Unciv.Godot.Harness/golden/`. The full expert plan (diagnosis, tokens, wireframes, ticket details, risks) follows the summary.

## Summary

**Root cause.** The client already has a dark slate theme with a gold accent (`ProceduralAssets.BuildTheme`), but every
Button, panel, tab, list row and popup uses the same single stylebox. With no hierarchy, depth or emphasis, it reads as primitive.

**Bug found.** `UiTheme.SetScale` only scales `DefaultFontSize`. Margins, borders, radii and icon sizes stay at 1x, so
"Bigger UI" settings enlarge text but not padding or controls. Fixed as part of C062.

**Proposal:**
- **Design system:** dark slate palette with brass/gold accent; type scale with a serif display face; spacing grid;
  button variants (primary, secondary, ghost, icon, danger); steppers, tabs, progress bars, badges, tables, empty states.
  Tokens in `Unciv.Assets/UiTokens.cs`, theme built in code (`Unciv.Godot/Assets/ThemeBuilder.cs`) using theme type variations.
- **Icons:** import Unciv's own icon set, with procedural fallback shapes.
- **World HUD:** slim top bar with a ☰ menu; nav icon strip with badges in place of the two rows of buttons;
  research progress pill; notification column; framed minimap; compact unit panel with a 4-wide icon action grid
  showing hotkeys; one primary "Next unit / End turn" button. Turn banner becomes a toast; duplicate status line removed.
- **City:** cards with progress bars, drag-to-reorder queue, confirmation before razing.
- **Tech tree:** node cards, era bands that don't clip, research path preview.
- **Setup:** nation emblem grid with detail pane, steppers instead of "Fewer/More", footer with game summary.
- **Other screens:** policies, diplomacy, trade, overviews, save/load, options, Civilopedia, popups and victory move onto
  the shared components. Responsive layouts from 640x480 to 1920x1080.

**Ticket order (C061–C079):**
1. C061: harness finds buttons by label (prevents icon-only buttons from breaking scenario scripts).
2. C062: design tokens + theme builder v2 (includes the scale bug fix; re-records all goldens once for one owner review).
3. C063 icons, C064 unique display text fix, C065 shared components, C066 screen frame/modal/tooltip.
4. Screens by player impact: C067 world HUD, C068 unit panel, C069 city, C070 tech, C071 policies, C072 setup,
   C073 diplomacy/trade, C074 overview, C075 menu/save/options, C076 Civilopedia, C077 victory, C078 hotkeys,
   C079 responsive + localization pass.

**Risks:** heavy golden churn; text-matched `Press` in scenarios; longer translated strings; `LiteralLint`
(colour literals only in `Unciv.Godot/Assets/`, all text via `.Tr()`); new strings need translation keys.

---

# Full plan

I looked at every golden under `Unciv.Godot.Harness/golden/` that the brief asked for, plus the Kotlin reference screenshots. I also read `Ui/*`, `HudView`, `UnitPanelView`, `GameScreen` layout, `ProceduralAssets.BuildTheme`, `PlaceholderPalette`, `LiteralLint`, `AssetResolver`/packs, the harness `ScenarioRunner`/`ScenarioStep`, and PROTOCOL.md.

## 0. Ground truth: what's actually there and what constrains the work

- **The client isn't on the default Godot grey.** `ProceduralAssets.BuildTheme()` already makes a dark slate theme with a gold accent from `PlaceholderPalette` + `ThemeSpec` (radius 4, border 2, margin 8, font 16).
  - The problem is that it's one style for everything. Every Button, panel, tab, list row and popup gets the same stylebox. So there's no hierarchy, depth or emphasis, and that reads as "primitive".
- **Colour literals are banned outside `Unciv.Godot/Assets/`.** `LiteralLint` enforces this, and it also requires `.Tr()` on text literals. So:
  - All design tokens have to live in `Unciv.Assets` (testable, Godot-free).
  - Styleboxes have to be built in `Unciv.Godot/Assets/`.
  - Exceptions already in the code: `CityScreen.cs:308` and `NewGameScreen.cs:280` build `StyleBoxFlat`s inline from palette values (no literals).
- **`UiTheme.SetScale` only scales `DefaultFontSize`.** Margins, borders, radii and icon sizes don't scale. At a "Bigger UI" setting, padding stays at 1x. That's a real bug.
- **Every view takes its data from a view model in `Unciv.Assets/*Model.cs`.** Examples: `HudModel`, `UnitOrdersModel`, `CityProductionModel`, `TechScreenModel`. Most of the redesign is view-only. A few models need structured fields where they currently return pre-joined strings:
  - `CityTileCell.Label = "P2 F1"`
  - `UnitPanelInfo` fields that are already-formatted lines
  - `TechNode.Label = "Pottery\n22 science"`
- **Layout is absolute.** `HudView.LayoutIn` and `GameScreen.cs:797-838` position every nav button from the previous button's `GetCombinedMinimumSize()`. That's why the nav wraps into a second, right-aligned row. `GameScreen` is 1950 lines and owns all of the HUD placement.
- **Harness coupling (biggest risk):**
  - `Press("Text")` (124 uses) finds a button by its exact visible `Text` and clicks the centre of its rect. Icon-only buttons and renamed labels break scenarios. The common ones: "Next turn", "Menu", "Close city", "Close technology", "Close overview", "Close trade", "Fewer players", "Map", "Players", "Saves", "Overview".
  - Scenarios also read C# properties: `screen.TechButton`, `PediaButton`, `PromoteButton`, `Hud.AutoPlayButton`, `Notifications.ToggleButton`, `GameScreen.StatusText`, `Hud.BannerText/ActionText`, `UnitPanel.TitleText`. Those properties must survive.
  - A few `GetNode("Model"|"Flag"|"Yields"|"Glyphs"|"Banner")` calls only touch 3D, so UI work won't hit them.
- **Goldens:** 46 scenarios, 640x480, compared by `Unciv.Harness/GoldenComparer` (hash distance, mean diff, differing-pixel fraction). Each UI ticket must also update `migration/screenshots.json` captions and regenerate `SCREENSHOTS.md` (`python3 -I tools/screenshots.py --check`). Reviewers check that the captions are true, so captions like "Fewer players button" have to be rewritten.
- **Icons:** today they're procedural 24px shapes with two-letter initials (`ProceduralImages.Icon`).
  - Unciv's own icon set is in the repo: `android/Images.Icons/{StatIcons,OtherIcons,UnitTypeIcons,NotificationIcons,ResourceIcons,ImprovementIcons,…}`, `Images.Tech`, `Images.PolicyIcons`, `Images.ConstructionIcons`. That's about 5 MB of white-on-transparent PNGs.
  - Licences are CC BY 3.0 / CC BY 4.0 / CC BY-SA, credited per icon in `docs/Credits.md`. PROTOCOL allows Unciv's own assets under their original licence.
  - `AssetResolver` already supports packs (`assets/packs/<pack>/manifest.json`, with the `swap-test` pack as precedent).
- New C tickets start at **C061**; C060 is the last one.

---

## 1. Diagnosis, per screen

**Global**
- One button style for everything: Diplomacy weighs the same as Declare war, and the same as a list row.
- No panels-within-panels and no section headers. Every screen is a column of same-size labels.
- No iconography. Stat icons are initials. Happiness and Golden Age both show an orange "HA" (`hud/start.png`). Faith is a gear-like blob.
- Inline glyphs (⁂ ¤ ♪ ⚙) come through the font fallback at the wrong size and baseline (`city-a/open.png`, `tooltips/search.png`).
- Raw unique syntax leaks to players:
  - `decisions/promotion.png`: "[+33]% Strength <vs [Armored]> units"
  - `decisions/pantheon.png`: "[+1 Culture] from every [Shrine]"
  - An AI-only unique "[-100]% weight to this choice for AI decisions" is shown. That's a bug: hidden-to-users uniques aren't filtered.
- Close buttons are wide text buttons ("Close technology"), not a consistent X.
- Nothing scales except font size.

**World HUD** (`hud/start.png`, `hud/ordered.png`, `v1-vertical/selected.png`, `unit-orders/*`, `minimap/start.png`, `alerts/list.png`, `next-turn/overlay.png`)
- 8 same-weight text buttons in two rows cover about 25% of the map. The second row (Espionage, Civilopedia) is right-aligned, which looks accidental.
- The "Your turn, Babylon / Turn 0 - 4000 BC" banner is 36px with an outline in the middle of the map. It sits under the unit panel (`v1-vertical/selected.png`) and stays visible under the next-turn overlay (`next-turn/overlay.png`).
- The minimap is about 30px, effectively an icon, at the top right (`minimap/start.png`).
- The unit panel is 230x~270:
  - Stats are plain lines; the order list is a scrolled column of full-width buttons, so only 3.5 orders are visible and "Explore"/"Skip turn" are cut off.
  - The bottom status line repeats it ("Warrior moves 2 health 100").
  - In `unit-orders/attack-preview.png` the status line runs under the combat preview ("confirm t… an").
- Notifications are a toggle button above a list whose rows are full-width buttons, with no icons or categories. "Notifications (5" is clipped by popups (`alerts/demand.png`).
- The next-action button is the right size, but "Next turn" and "Next unit" sit side by side at equal weight.
- `tooltips/tile.png`: the tile tooltip has **no background** at that moment, overlaps the top bar, and is a raw breakdown dump.
- `saveload/list.png`: the save dialog is a translucent sheet with no frame. Its toolbar is bare text, and the "Saves" button overlaps "Next unit".

**City** (`city-a/*`, `city-b/*`)
- Full-screen text with no map context. "Citizens" and "Production" are mode toggles styled like Rename and Close.
- A raw "k: lock." hint sits in the header.
- Yields are coloured text lines with glyph prefixes. Population/growth/borders are prose lines.
- The tile grid is squares labelled "P2 F1", not hexes with yield pips.
- The production queue has tiny Top/Up/Down/X per row. Buy buttons sit in a separate column and don't line up with their rows (`city-b/queued.png`).
- "Palace (national wonder)" wraps across three lines in a 130px column.
- "Raze city" is a neutral button.

**Tech** (`tech/open.png`, `tech/chosen.png`)
- The era header repeats per column ("Ancient era" ×3) and is clipped ("ent era").
- Nodes are 130px, so labels clip ("Animal Husban", "Horseback Ridi").
- State is shown only by fill colour: blue researchable, green researched, gold queued, grey locked. There's no icon, turns or progress.
- Edges are thin grey lines.
- The detail panel stacks three full-width buttons, then "Research queue / empty / Unlocks / Buildings enabled:" as prose.
- The horizontal scrollbar overlaps the bottom row.

**Policies** (`policy/*`)
- Only the Tradition branch is visible. Other branches need horizontal scrolling and "Mo…" is clipped.
- Nodes are wide flat bars with no tree lines. The branch opener and "Tradition Complete" span the full width.
- "Adopt" is far away at the bottom right. Header stats are prose.

**Diplomacy and trade** (`diplomacy/*`, `trade/*`, `proposals/*`)
- Civ list rows read "Rome - War". There's no portrait or colour swatch, and status is text.
- Every action is a full-width button at equal weight, including Declare war and Bully.
- The trade selection marker is literal "[x] 20 gold". Unavailable items are shown as disabled inputs reading "(not possible)".
- "They would likely accept." is plain text.

**Overview and espionage** (`overview/*`, `overview-espionage/*`, `espionage/*`)
- Tables are unaligned label grids. Stats is a text dump.
- Columns aren't right-aligned for numbers, rows have no icons, and "Go to" buttons are mixed into the data.
- "Stage a coup" is a full-width button between table rows.

**Setup and main menu** (`setup/*`)
- The nation picker is a flow of 30 chips filled with nation outer/inner colours. Many fail contrast: Japan red-on-white, Egypt purple-on-yellow, Germany grey-on-grey.
- There's no leader, unique-ability preview or selection state beyond a fill.
- "Fewer players / 2 players / More players" are buttons rather than steppers.
- Human/AI are buttons. "Start game / Back" float outside the panel at the bottom left.
- Map size and resources are segmented chips (good idea) but look identical to every other button.
- The main menu (`setup/menu.png`) is a small "Unciv" label over 6 buttons on flat navy, with no identity.

**Game menu, options, save/load** (`menu/*`, `options/*`, `settings/start.png`, `saveload/*`)
- The menu popup is acceptable but flat.
- Options are one long full-screen list of full-width checkbox buttons with no categories, plus "Bigger UI" and "Smaller UI" as buttons.
- `settings/start.png` has no panel at all.
- The save list is plain text columns with no selection affordance.

**Popups and decisions** (`alerts/*`, `decisions*/*`, `tutorial/*`, `proposals/*`)
- Structure is consistent (title, body, stacked full-width choices) but there's no icon, no primary/secondary distinction, and choices stack vertically even when there are only two.
- The promotion and pantheon pickers have a 180px scroller next to empty space. The description text is raw unique syntax.

**Victory** (`victory/victory.png`): good copy, but the score graph is two flat lines in a big empty box, the legend is tiny, and there's no ranking table.

**Tooltips and Civilopedia** (`tooltips/*`, `ui-kit/tooltip.png`)
- The ui-kit tooltip is fine.
- The Pedia has a horizontally scrolling tab strip ("Uni…" clipped), a "------" rule made of characters, and underlined link lines that look like hyperlinks in a 2004 web page.

---

## 2. Design system

### 2.1 Where it lives (one recommendation)

**Theme built in code from tokens, not a `.tres`.**
- **`Unciv.Assets/UiTokens.cs`** (new, Godot-free, unit-tested): colours as `Rgb`, type scale, spacing, radii, border widths, component metrics. It replaces `ThemeSpec` and the UI entries of `PlaceholderPalette`. Terrain, unit and stat colours stay where they are.
- **`Unciv.Godot/Assets/ThemeBuilder.cs`** (new; `ProceduralAssets.BuildTheme` delegates to it): `Build(float scale) → Theme`.
  - Everything is multiplied through `Px`, with borders clamped to at least 1.
  - Defines base types plus **theme type variations** (`theme.SetTypeVariation("ButtonPrimary", "Button")`, and so on).
- **`Unciv.Godot/Ui/UiVariants.cs`**: string constants (`ButtonPrimary`, `PanelCard`, …). Controls set `ThemeTypeVariation = UiVariants.ButtonPrimary`, so no view ever builds a stylebox.
- **`UiTheme.SetScale`** rebuilds the styleboxes and constants into the *same* `Theme` instance (clear and re-set), so live controls update.
  - Rebuild only on scale change; that's about 100 `StyleBoxFlat`s, which is cheap.
  - `assets/override/ui/theme.tres` keeps working as an override: when a file is found, skip the rebuild and scale only fonts.

### 2.2 Colour tokens (dark strategy palette, brass/gold accent)

| Token | Hex | Use |
|---|---|---|
| `Bg0` | #0B0F15 | window backdrop, map void |
| `Bg1` | #121822 | full-screen screen background |
| `Surface1` | #18202B | panels |
| `Surface2` | #202A38 | cards, rows, inputs |
| `Surface3` | #2A3647 | hover, raised, tooltip |
| `Inset` | #0F141C | wells: lists, tree canvas, text fields |
| `BorderSubtle` | #2C3746 | dividers, card edges |
| `BorderStrong` | #44536A | panel frames, focused inputs |
| `Accent` | #D6AA3C | primary buttons, selection, focus ring (existing) |
| `AccentHi` | #F0C85A | hover on accent |
| `AccentLo` | #7A5E1E | pressed accent, accent tint backgrounds at 25% alpha |
| `OnAccent` | #1A1307 | text on gold |
| `TextPrimary` | #ECE8DF | body (warm white) |
| `TextSecondary` | #A8B0BC | labels, captions |
| `TextMuted` | #6F7988 | overlines, hints, key chips |
| `TextDisabled` | #545D6A | |
| `Info` | #4A90E2 | links, research |
| `Success` | #5DBB63 | researched, positive deltas |
| `Warning` | #E3A33B | unhappy, at-risk |
| `Danger` | #D6544A | war, destructive, negative deltas |
| `Scrim` | #06080C @ 72% | modal backdrop |

- **Stat colours** stay as they are in `PlaceholderPalette.StatColours`, used for icon badges and progress fills.
- **Semantic deltas:** +N uses `Success` and −N uses `Danger`, always as `[color]` spans built from tokens by a `UiText.Delta(int)` helper, never literals.
- **Nation colours:** never use them as large text-on-fill. Use a 6px left stripe or a circular emblem only (fixes the contrast failures).
- **Test** in `Unciv.Core.Tests/Assets/UiTokensTests.cs`: WCAG contrast of at least 4.5 for TextPrimary/Secondary on Surface1–3, at least 3 for TextMuted, at least 4.5 for OnAccent on Accent.

### 2.3 Typography

- Body is Noto Sans, the existing variable font, using `FontVariation` weights (`variation_opentype {"wght": 400/600/700}`).
- Add `NotoSerif` (OFL, same Fedora `google-noto-*` source; record it in ASSETS.md) for **screen titles and banners only**. It falls back to the existing chain for non-Latin scripts.
- Numbers use `FontVariation.OpentypeFeatures {"tnum":1}` (tabular figures) in the top bar, tables and costs.

| Token | px @1x | Face | Use |
|---|---|---|---|
| `Display` | 34 | Serif 700 | turn banner, victory title |
| `Title` | 24 | Serif 700 | screen title in ScreenFrame |
| `Heading` | 18 | Sans 600 | panel and card titles, modal title |
| `Body` | 15 | Sans 400 | default |
| `BodyStrong` | 15 | Sans 600 | row titles, values |
| `Small` | 13 | Sans 400 | secondary row text, costs |
| `Overline` | 11 | Sans 700, +1px tracking, uppercase in Latin | section headers ("PRODUCTION QUEUE") |

Rule: every screen has at most one Title, Headings for panels, Overlines for sections. Body drops from 16 to 15 to recover density at 640x480.

### 2.4 Spacing, radii, elevation

- **4px grid:** `S1=4, S2=8, S3=12, S4=16, S6=24, S8=32`.
  - Panel padding is S3 (S2 in compact).
  - Row height: 32 (compact 28). Row padding S2/S3. Gap between cards S3. Screen edge margin S4 (compact S2).
- **Radii:** controls 4, cards 6, panels 8, pills/chips 12, icon badge = circle.
- **Borders:** 1px everywhere; 2px only for the focus/selection ring.
- **Elevation:** panels have no shadow. Modals, tooltips and the HUD bottom dock use `shadow_size 8`, `shadow_color Bg0@60%`, offset (0,2). Never put shadows on list rows (performance).
- **Top accent hairline:** ScreenFrame headers and modals get a 2px `Accent` top border (`border_width_top`). That's the cheap "brass trim" identity element.

### 2.5 Components (all in `Unciv.Godot/Ui/`)

| Component | Spec |
|---|---|
| **Button variants** (type variations) | `ButtonPrimary`: Accent fill, OnAccent text, AccentHi hover, AccentLo pressed, disabled Surface2 with TextDisabled. `ButtonSecondary` (the new base `Button`): Surface2 fill, BorderSubtle, hover Surface3 + BorderStrong, pressed Inset. `ButtonGhost`: transparent, TextSecondary, hover Surface2. `ButtonDanger`: Surface2 with Danger border and text, hover Danger fill. `ButtonIcon`: square 32 (compact 28), ghost style, icon tinted TextSecondary and AccentHi on hover. `ButtonTab`: transparent, 2px Accent underline when pressed. `ButtonChip`: pill, toggled shows AccentLo@35% fill and Accent border. Focus is always a 2px Accent ring (`focus` stylebox, StyleBoxFlat with `draw_center=false`). Min height 32. `TextOverrunBehavior = Ellipsis` with the full text as tooltip. |
| `IconButton` | Icon plus optional text; exposes `Label` (an untranslated key used as accessible name and harness handle) and `Hotkey`. The tooltip is "Label (Key)". |
| `Stepper` | `[−] value [+]` with ButtonIcon minus/plus and a tabular value with min/max. Replaces Fewer/More players and city-states, specialists ±, trade amount ±, UI scale. |
| `Segmented` | `ChoiceRow` restyled: chips joined in an Inset track, the selected one AccentLo. |
| `Toggle` | CheckButton styled as a switch (icons generated in ThemeBuilder), used in Options instead of full-width checkbox buttons. |
| `ScreenFrame` | Header bar (Surface1, Accent hairline): Title on the left, optional `TabStrip` in the centre, ButtonIcon X (Close, Esc) on the right. Then content, then an optional footer bar with right-aligned actions (primary right-most). Every full-screen screen uses it. |
| `Card` / `PanelInset` | Card is Surface2 with radius 6 and BorderSubtle. Inset is the well for lists and canvases. |
| `SectionHeader` | Overline text plus a hairline rule filling the remaining width. |
| `ListRow` | `[icon 24][title / subtitle][trailing value or actions]`. Hover Surface3. Selected shows a 3px Accent left bar plus Surface3. Disabled: TextDisabled with the reason in the subtitle. Replaces ListPanel's raw buttons. |
| `StatChip` | Icon badge (circle in stat colour, white glyph) plus tabular value plus optional delta, coloured by sign. Used in the top bar, cards and costs ("46 ⚙ · 12 ⧗"). |
| `StatBar` | 6px (or 14px labelled) progress bar: Inset track, fill in the stat colour, optional "N turns" text on the right. Used for growth, borders, production, research and golden age. |
| `Badge` | Count pill (Danger for alerts, Accent for available picks) at the top right of an IconButton. |
| `KeyHint` | Small Inset chip with TextMuted key text, e.g. "T", shown in tooltips and next to menu items. |
| `Tooltip` | Surface3, BorderStrong, radius 6, shadow, max width 340. Heading line, then Body lines, then StatChip lines, then an optional Small muted footer ("Requires Pottery"). Never above the top bar; clamp below `Hud.TopBarHeight`. |
| `Modal` | Replaces the `PopupStack.Display` layout and is shared by AlertPopupView, DecisionPickerView, TutorialPopupView and TradeProposalView. Scrim, then a centred panel (min 360, max 560 or 90% width) with the Accent hairline. Header: optional 40px icon, Heading title, Small subtitle. Body. Footer: buttons in a row, right-aligned, one Primary, Esc maps to the cancel choice. Choices stack only when there are more than 3 or they're longer than the width. |
| `DataTable` | Header row in Overline style, sortable on click (▲▼). Numbers right-aligned and tabular. Zebra rows Surface1/Surface2. Row actions are trailing ButtonIcons. Used by Overview, Espionage and Save/Load. |
| `EmptyState` | Muted icon, one Body line and an optional action ("No saves yet. Save game"). |

### 2.6 Icons

Recommendation: **an Unciv icon pack plus procedural fallbacks**, not hand-made art.

**Tool.** `Unciv.Godot/tools/import-unciv-icons.py`, run once and committed. It copies a whitelist from `android/Images.Icons/*`, `Images.Tech/TechIcons`, `Images.PolicyIcons`, `Images.ConstructionIcons`, `Images.UnitPromotionIcons`, `Images.NationIcons` (and `Images.ReligionIcons`) into `Unciv.Godot/assets/packs/unciv/<category>/<name>.png` and writes `manifest.json` mapping keys to files.

**Keys.**
- Existing keys: `icon/stat/*`, `icon/tech/*`, `icon/building/*`, `icon/unit/*`, `icon/resource/*`, `icon/improvement/*`, `icon/policy/*`, `icon/promotion/*`.
- New categories in `RulesetAssetKeys`:
  - `icon/ui/<name>` for chrome (`Menu, Tech, Policies, Diplomacy, Overview, Trade, Espionage, Pedia, Close, Plus, Minus, Up, Down, Top, Remove, Lock, Search, Settings, Back, Next, Save, Load, Delete, Capital, Puppet, Raze, Gift, War, Peace, Notifications, Victory, Specialist, Population, Turns(hourglass), Movement, Strength, RangedStrength, Health, XP`)
  - `icon/nation/<name>` for nation emblems
  - `icon/action/<UnitActionType>` for unit orders (Sleep, Fortify, Explore, Automate, FoundCity, ConstructImprovement, Pillage, Upgrade, Promote, Skip, Disband, …), sourced from `OtherIcons` and `UnitActionIcons` where present
  - `icon/notification/<category>`

**Rendering.**
- Unciv icons are white glyphs designed to sit on coloured circles. `IconBadge` draws a circle in the category colour (stat colour, tech blue, policy colour by branch, nation outer colour) and the icon tinted with `Modulate`. That reproduces Unciv's look without new art.
- Chrome icons (`icon/ui/*`) render as glyph-only tinted TextSecondary/Accent, with no circle.

**Fallback.** When a key isn't in the pack, keep `ProceduralImages.Icon`, but put the initials on the same circle badge. Give the procedural set distinct shapes so Happiness ≠ Golden Age: add `ShapeFor` overrides keyed by name.

**Licensing.** One ASSETS.md row and a `THIRD_PARTY_NOTICES.md` section pointing to `docs/Credits.md` (CC BY 3.0/4.0, CC BY-SA 4.0). The script refuses files whose folder has no Credits entry.

**Where the pack applies.** The client and the harness both load pack `unciv` by default, so goldens show the real look. `swap-test` stays harness-only. Size is about 5 MB of PNGs, so import with `compress/mode=lossless` and mipmaps on, for crisp downscale to 16–24px.

**Inline glyphs.** Replace ⁂ ¤ ♪ ⚙ in labels with `RichTextLabel` `[img=16]` icons through a `UiText.Stats("…")` helper. It turns Unciv's stat-emoji characters (`Fonts.food` and so on) into inline icon images. If a plain `Label` is required, map them to a tiny icon font rendered from the pack at load (optional, later).

### 2.7 Responsive rules and UI scale

- **Effective scale** = `user scale (0.5–3, existing) × auto`, where `auto = clamp(floor4(min(w/1280, h/720)), 1.0, 2.0)` and `floor4` rounds down to a 0.25 step. 1280x720 → 1.0, 1920x1080 → 1.5, 2560x1440 → 2.0. Never auto-shrink below 1.
- **Breakpoints** use logical width = `window width / effective scale`:
  - `Compact` below 1000 (includes the 640x480 harness and phones): nav is icon-only, unit panel ~260px, side panels become overlays or tabs, padding S2.
  - `Regular` 1000–1600: icon + text where noted.
  - `Wide` above 1600: three-column city screen, tech detail docked.
- **Implementation.** `UiTheme` exposes `Breakpoint` and raises `LayoutChanged` on viewport `size_changed` and on scale change. Layout code switches structure only on breakpoint change, not every frame.
- **No absolute `Position =` math in views.** Use anchors, `MarginContainer`, `BoxContainer`s and `Control.SetAnchorsAndOffsetsPreset`. The one allowed exception is world-anchored widgets (city banners, unit flags).
- Text buttons never get fixed widths: minimum width only, plus ellipsis.

---

## 3. Layouts

### 3.1 World HUD

**Regular (1280x720):**
```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│[≡] (G)500 +5  (S)+5  (C)+1 ▮▮▯ 50/115  (H)☺1  (F)+0  (GA)0/341 │ Iron 2 Horses 1 │ T12 · 3600 BC │  top bar 36px
├──────────────────────────────────────────────────────────────────────────────────────┤
│[Tech: Pottery ▮▮▮▯ 3⧗][Policy ✦ ready]                         [⚑][⚖][✉][👁][📖][📊] │  left: research/policy pills; right: nav icon strip
│                                                                     ┌────────────┐   │
│                                                                     │Notifications│   │  right column, newest top, ≤5 visible,
│                                                                     │(🏛) Babylon… │   │  icon + one line, click = go to
│                                M A P                                │(⚔) Enemy …  │   │
│                                                                     └────────────┘   │
│ ┌──────────────────────────────┐                                ┌─────────────────┐  │
│ │(⚔) Warrior        ◀ 2/5 ▶  ✕ │                                │                 │  │
│ │ ⚔6  ➹2/2  ♥▮▮▮▮▮100  XP 0/10 │                                │    MINIMAP      │  │  200x140 regular
│ │ [Fort F][Sleep][Expl X][Auto]│                                │                 │  │
│ │ [Skip ␣][Upg U][⋯ more]      │                                └─────────────────┘  │
│ └──────────────────────────────┘                          [ NEXT UNIT  ▸ ] [End turn] │  primary 200x48, secondary ghost
└──────────────────────────────────────────────────────────────────────────────────────┘
```

**Compact (640x480):**
```
┌─────────────────────────────────────────────────────────────┐
│[≡](G)500+5 (S)+5 (C)+1 (H)1 (F)+0 (GA)0/341        T0 4000BC│ 32px
│[⚗ Pottery 3⧗]                       [⚖][✉][👁][📖][📊][⚑]  │ 32px icon strip (8 icons × 28 = 224px)
│                                                             │
│                       M A P                     [notif ▾ 7] │
│┌───────────────────────────┐                  ┌──────────┐  │
││(⚔)Warrior ⚔6 ➹2/2 ♥100 ✕ │                  │ minimap  │  │ 140x100
││[F][Z][X][M][␣][⋯]         │                  └──────────┘  │
│└───────────────────────────┘          [ NEXT UNIT ▸ ][⏭]    │
└─────────────────────────────────────────────────────────────┘
```

- **Top bar.**
  - `[≡]` opens the game menu (replaces the "Menu" button, Esc/Tab).
  - Stat chips have tooltips with the breakdown.
  - Culture shows a mini StatBar toward the next policy.
  - Strategic resources sit in a separate group.
  - Turn and year are on the right in tabular figures.
  - Click Gold, Science or Culture to open Overview Stats, Tech or Policies.
- **Nav strip.** IconButtons for Tech ⚗, Policies ✦, Diplomacy ⚖, Trade ✉, Espionage 👁 (only when espionage is enabled), Civilopedia 📖, Overview 📊, Victory ⚑.
  - Each has tooltip "Technology (F6)" and a Badge: tech has an Accent dot when nothing is researched, Policies one when a policy is adoptable.
  - The research pill on the left is a mini card (current tech icon, progress bar, turns). Clicking it opens Tech. It is the main way to reach Tech, as in Civ 5.
- **Turn banner.** It becomes a *toast*: a Display-size serif, top-centre under the bar, on a Surface1@85% pill, fading after 150 frames. It never sits at 28% height over the map, and it's hidden while any panel or overlay is open. `HoldBanner` and `BannerText` are kept.
- **Unit panel.**
  - A header row: unit icon badge in nation colour, name, ◀ ▶ cycle-idle buttons, ✕ deselect.
  - Stat chips: strength, ranged, moves, health bar, XP bar.
  - The action bar is a **grid of IconButtons, 4 per row**, each with a key letter in its corner. Primary actions (Found city, Build improvement) span 2 cells as ButtonPrimary with text. Up to 2 rows show; the rest go behind "⋯ more", which opens a popover list.
  - Disabled actions stay visible, dimmed, with the reason in the tooltip.
  - Promotions available: a gold "Promote" chip in the header (replaces the free-floating PromoteButton). The `PromoteButton` property is kept.
  - Confirmation (disband) is an inline danger strip in the panel: "Disband Warrior? [Disband] [Cancel]".
- **Status line.** The bottom-left `StatusLabel` is removed as a permanent element. It becomes a 1-line hint above the unit panel only when there's something to say (path preview turns, "click a tile to attack"). The `StatusText` property stays.
- **Next action.**
  - One ButtonPrimary with the HudModel label ("Next unit", "Pick a technology", …) and an icon.
  - "End turn" becomes a ghost IconButton ⏭ next to it, visible only when `EndTurnShown`, with label "Next turn". The harness can still `Press("Next turn")` through `IconButton.Label`.
  - AutoPlay goes above it as a ghost button.
- **Minimap.** A real minimap, bottom-right: 200x140 regular, 140x100 compact. Framed in a Card with zoom +/− and toggles for yields, resources and borders (eye icons, mapped to the existing settings).
- **Notifications.** A right-column stack of ListRows (category icon, one line, ellipsized). Compact: collapsed into a "🔔 7" Badge button that drops a panel down. The "Review alerts (N)" button becomes a Danger Badge on the bell.
- **Tile tooltip.** Standard Tooltip with terrain icon and name heading, owner line, units, then yield chips in one row (not a breakdown). The breakdown only appears with Shift held.

### 3.2 City screen

Recommendation: **show the map, Civ 5-style**. The city screen becomes an overlay over a camera centred on the city, not a black full-screen. Compact keeps the existing schematic tile grid but draws hexes.

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ◀  ★ BABYLON  (5)  Capital   ▶                [✎ Rename]   [⚑ Raze]          [✕ Exit] │ header: serif title, city cycle arrows
├────────────────────────┬────────────────────────────────────────┬────────────────────┤
│ PRODUCTION             │                                        │ YIELDS             │
│┌──────────────────────┐│                                        │ (F)+3 (P)+4 (G)+7.5│
││(⚒) Warrior           ││          hex tiles with yield pips     │ (S)+4 (C)+1 (☺)-4  │
││ ▮▮▮▮▯▯▯ 12/26  3⧗    ││          worked = filled ring,         │ ───────────────── │
││ [Buy 140 (G)]   [✕]  ││          locked = gold padlock         │ GROWTH  ▮▮▯ 4⧗     │
│└──────────────────────┘│          click = toggle, shift = lock  │ BORDERS ▮▯▯ 7⧗     │
│ QUEUE                  │                                        │ ───────────────── │
│ 2 (🏛)Monument  7⧗ ⇅ ✕ │                                        │ CITIZENS           │
│ 3 (∞)Nothing       ⇅ ✕ │                                        │ Focus [Default ▾]  │
│ ───────────────────── │                                        │ Merchant [−] 0/1[+]│
│ [Units|Buildings|Wonders]                                       │ [Reset citizens]   │
│ (⚒)Worker   46(P) 12⧗ [220(G)]                                  │ ───────────────── │
│ (⚑)Settler  needs 2 pop   (dim)                                 │ BUILDINGS (1)  ▾   │
│ (🛡)Scout    16(P) 4⧗ [100(G)]                                   │ (🏛)Palace         │
└────────────────────────┴────────────────────────────────────────┴────────────────────┘
```

- The Citizens/Production mode toggle disappears: both are always visible. Compact turns the left and right columns into tabs `[Production | Citizens | Buildings]` beside a 300px hex grid.
- The current construction is a hero Card with progress, turns, buy and remove.
- Queue rows show index, icon, name, turns, a drag handle ⇅ (drag to reorder) and ✕. Top/Up/Down live in a right-click or `⋯` menu and on Ctrl+↑/↓, not as four buttons per row.
- Buildable rows show icon, name, cost chip, turns, and a trailing buy chip. Clicking a row queues it; Shift+click puts it first. An unavailable row is dim with its reason as the subtitle ("Requires 2 population").
- The buy button sits in the same row (fixes the misaligned column).
- **Raze** is ButtonDanger with a confirmation modal. "k: lock." becomes a KeyHint in the tile tooltip.
- **Model change:** `CityTileCell` gets `IReadOnlyList<(string Stat, int Amount)> Yields` and keeps `Label` for compatibility. `QueueRow` and `BuildableRow` get structured `Cost`, `Turns`, `Progress` and `IconKey`.

### 3.3 Tech tree

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ TECHNOLOGY      Researching: (⚗) Mining  ▮▮▮▯ 2⧗   (S) +5/turn           [🔍] [✕]   │
├──────────────────────────────────────────────────────────────────┬───────────────────┤
│ ANCIENT ERA ─────────────────────── │ CLASSICAL ERA ───────────── │ (⚗) POTTERY       │ era band headers span their columns,
│ ┌──────────────┐   ┌──────────────┐ │ ┌──────────────┐            │ Ancient · 22 (S)  │ sticky at top while scrolling
│ │(⚗)Agriculture✓├──▶│(⚗)Pottery  3⧗│─┼▶│(⚗)Calendar 7⧗│            │ 3 turns           │
│ │ ▮▮▮▮▮▮▮▮      │ ┌▶│ ◦◦◦  (🏛)(⚒)  │ │ └──────────────┘            │ ───────────────── │
│ └──────────────┘ │ └──────────────┘ │                             │ UNLOCKS           │
│                  │ ┌──────────────┐ │                             │ (🏛) Granary      │
│                  └▶│(⚗)Animal Hus…│ │                             │   +2 (F) …        │
│                    └──────────────┘ │                             │ (⚒) Shrine        │
│                                                                  │ ───────────────── │
│                                                                  │ QUEUE  1 Mining   │
│                                                                  │        2 Bronze W │
│                                                                  ├───────────────────┤
│                                                                  │[Add to queue][Research]│ footer: primary right
└──────────────────────────────────────────────────────────────────┴───────────────────┘
```

- **Node card** is 200x56, 220 wide on Wide. Contents:
  - tech icon badge
  - name in BodyStrong, ellipsized, with the full name in the tooltip
  - turns, top right
  - a row of 16px unlock icons, so you can see what a tech gives without clicking
  - a 3px progress bar at the bottom
- **State** is shown by border and badge, not fill:
  - Researched: Success left bar plus ✓, dimmed body.
  - Current: Accent 2px border plus glow (shadow in Accent@40%).
  - Queued: Accent number badge "2".
  - Researchable: Surface2 with TextPrimary.
  - Locked: Surface1 with TextMuted.
- **Edges** are drawn in one custom `_Draw` control as orthogonal elbows: 2px, BorderStrong, and Accent for edges on the path to the queued target.
- **Era headers** are one band per era (computed from `ColumnEras` runs), sticky at the top while scrolling vertically.
- **Click behaviour:**
  - Click selects and shows details.
  - Double-click, or the footer "Research", researches now.
  - Shift+click adds to the queue.
  - Clicking a far tech previews the whole prerequisite path highlighted, and the button reads "Research path (4 techs, 23⧗)".
- The scrollbar sits inside the canvas Inset with a bottom margin, so it never overlaps nodes. There's also mouse drag-pan, wheel for vertical scroll and Shift+wheel for horizontal.
- **Model:** `TechNode` gets `Turns`, `Progress`, `IconKey` and `UnlockIcons`. `TechLayout` gets `EraSpans`.

### 3.4 Policies

- **Wide/Regular:** a grid of **branch cards**, 3×3 (Tradition, Liberty, Honor / Piety, Patronage, Aesthetics / Commerce, Exploration, Rationalism). Ideologies appear as a fourth row when unlocked.
- **Each card** has:
  - a header: branch icon, name, era, "2/6", and a state (Adopted, Available, Locked with its reason)
  - inside, the policy nodes as 40px round icon buttons laid out by the ruleset row/column, with 1px connector lines
  - a "complete" bonus strip at the bottom
- **Compact:** a Segmented branch selector at the top and one branch card full width.
- **Right panel:** policy icon and name, effect bullets with inline icons (via `UiText`), then a sticky ButtonPrimary "Adopt Tradition" directly under the description, not at the far bottom.
- **Header strip:** a culture StatBar "50/15 — policy available", plus golden age progress.

### 3.5 Diplomacy and trade

- **Left list:** civ ListRows with a nation emblem badge, name, a status pill (War = Danger, Peace = Success, Denounced = Warning, City-state type icon) and a relationship score bar for city-states.
- **Right:** a header card (emblem 64px, leader/civ name, attitude line), then "RELATIONS" key-value rows, then actions grouped by section:
  - TRADE: "Trade…" ButtonPrimary
  - GIFTS: Gift 250/500/1000 as one row of 3 chips with gold icons
  - INFLUENCE: Pledge/Bully as Secondary, Bully disabled with a reason
  - danger zone: Declare war as ButtonDanger at the bottom, separated by a rule, always behind a confirm modal (already exists: `diplomacy/confirm.png`)
- **Trade:** two columns, "You give" and "They give", each a list of tradable items with icons (gold, gold/turn, resources, embassy, open borders, cities).
  - Clicking an item moves it into the centre "Offer" table (two lists). The "[x]" marker goes away.
  - Gold amounts use a Stepper plus slider.
  - Items not possible are hidden by default, behind a "Show unavailable (3)" toggle, with the reason in a tooltip.
  - Footer: an AI-acceptance hint as a pill (Success "Likely accepted" or Warning "Unlikely"), [Clear] as ghost, [Propose] as primary.
- **Proposal popups:** a Modal with the proposer's emblem, a two-column give/get table with icons, and footer buttons in a row: "Accept" Primary, "Counter…" Secondary, "Refuse" Ghost, "Later" Ghost.

### 3.6 Overview and espionage

- ScreenFrame with tabs: Stats, Cities, Units, Resources, Ranking, Religion, Espionage, Politics. Tab icons appear on Regular; in compact the strip scrolls horizontally with arrow buttons at the edges.
- **Stats:** a 2-column grid of Cards. Economy: gold with a breakdown table. Science, Culture, Happiness and Score as StatChips with breakdowns.
- **Cities, Units, Resources, Ranking:** `DataTable`s. Name cells have icons. Numbers are right-aligned. "Go to" becomes a trailing ButtonIcon ⌖. Sortable.
- **Espionage:**
  - Spies table: spy, rank stars, location, action pill, trailing [Move].
  - City table with a "spy present" icon.
  - The coup is a row action (ButtonDanger "Coup 35%") on the spy's row with a confirm, not a full-width bar.

### 3.7 Setup, nation picker and main menu

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ NEW GAME                                                                     [✕]     │
├───────────────────────────────────────┬──────────────────────────────────────────────┤
│ [Players] [Game] [Map] [Rules] [Advanced]                                            │ tabs (underline)
│ PLAYERS ───────────────────────────── │ NATION                                       │
│ Major civs        [−]  4  [+]         │ [🔍 search…]                                 │
│ City-states       [−]  6  [+]         │ ┌────┐┌────┐┌────┐┌────┐┌────┐┌────┐        │ emblem tiles 72x88:
│ ───────────────────────────────────── │ │ ◉  ││ ◉  ││ ◉  ││ ◉  ││ ◉  ││ ?  │        │ emblem circle in nation colours
│ ▌1 (◉) Babylon     [Human|AI]    ✕    │ │Baby││Gree││Chin││Egyp││Engl││Rand│        │ + name below, selected = gold ring
│ ▌2 (?) Random      [Human|AI]    ✕    │ └────┘└────┘└────┘└────┘└────┘└────┘        │ taken = dim + player number badge
│ ▌3 (?) Random      [Human|AI]    ✕    │ …                                            │
│ ▌4 (?) Random      [Human|AI]    ✕    │ ───────────────────────────────────────────  │
│                                       │ (◉) BABYLON — Nebuchadnezzar II              │ detail: UA name + text,
│                                       │ Ingenuity: free Great Scientist when…        │ unique unit/building rows with icons
│                                       │ (⚔) Bowman  (🏛) Walls of Babylon            │
├───────────────────────────────────────┴──────────────────────────────────────────────┤
│ Tiny · Hexagonal · Abundant · Seed 3                          [Back]   [ START GAME ▸ ] │ footer bar: summary + actions
└──────────────────────────────────────────────────────────────────────────────────────┘
```

- **Compact:** the left column becomes the whole Players tab. Clicking a player row opens the nation grid as a full-panel picker (a second step with Back).
- **Player rows:** a nation-colour stripe on the left (`▌`), emblem, name, Segmented Human/AI, ✕ to remove.
- Game, Map and Rules tabs use labelled form rows: label on the left in TextSecondary (180px), control on the right (Segmented, Toggle, Stepper, LineEdit).
- **Main menu:**
  - Full-screen backdrop: a slowly panning procedurally generated map (the existing terrain renderer at a fixed seed, blurred and dimmed by Scrim). A static screenshot is the fallback.
  - The Unciv logo is CC BY 4.0, per Credits, so it's usable with attribution.
  - A left-aligned menu column of large Secondary buttons with icons: New game is Primary, Resume is Primary when available.
  - Version is in Small muted text at the bottom right.

### 3.8 In-game menu, save/load, options

- **Game menu:** a Modal 320 wide. Rows are ListRow-style buttons with icons and KeyHints: Resume (Esc), Save (Ctrl+S), Load (Ctrl+L), Victory status (F8), Options (Ctrl+O), then a rule, then Main menu, then Resign (Danger, with confirm).
- **Save/Load:** a ScreenFrame-styled modal (80% × 80%).
  - Left: a DataTable of saves (name, civ emblem, turn, ruleset, date) with sort and search.
  - Right: a details Card (turn, civ, players, mini map thumbnail if cheap, otherwise civ emblem), a save-name LineEdit, then [Delete] Danger with confirm, [Load]/[Save] Primary.
  - Damaged saves get a Warning row with the reason (`saveload/damaged.png`).
  - Empty state when there are no saves.
- **Options:** a Modal with **vertical tabs** on the left: Display, Gameplay, Map, Sound, Keys, Language, Advanced.
  - Rows: label plus description on the left, control on the right (Toggle, slider with a value readout, OptionButton).
  - UI scale is a Stepper plus slider with a live preview. "Bigger UI" and "Smaller UI" go away.
  - Keys: a DataTable of action, key chip and [Rebind], with a conflict warning inline.

### 3.9 Popups, decisions, tooltips, Civilopedia, victory

- **Decision pickers** (promotion, pantheon, great person, beliefs, reform): a Modal 560 wide with two panes.
  - Left: a ListRow list with icons.
  - Right: the selected item's icon, name and **display-text** effects (bracket-stripped; hidden-to-users uniques filtered — see C064).
  - Footer: [Decide later] Ghost, [Found pantheon] Primary.
  - Promotion picker: a mini tree when prerequisites exist.
- **Alerts:** a Modal with an icon per alert type (war, demand, friendship, city conquered). Choices sit in one row when there are 3 or fewer (`alerts/demand.png`: Accept Primary, Refuse Secondary, Decide later Ghost).
- **Civilopedia:**
  - ScreenFrame with a category **sidebar**, not a scrolling tab strip (icons plus names; compact shows icons only).
  - A search box in the header, results as a ListRow list, and an entry page with a header card (icon 64, name, category, cost chips).
  - Sections use SectionHeaders. Links are Info-coloured with no underline, plus a hover underline.
  - The "-----" rule is removed.
- **Victory and defeat:**
  - Modal width 640. Display serif title with the victory type icon. Flavour text. Then a ranking DataTable (rank, emblem, civ, score).
  - The graph sits in a Card with axes, turn ticks and 2px lines in nation colours, plus a legend of chips.
  - Footer: [Main menu] Secondary, [One more turn] Primary.
  - Vote result and defeat use the same frame.

---

## 4. Interaction rules

**Hotkeys.** Extend `HotkeyAction` and `Hotkeys.Defaults` with Kotlin's defaults from `KeyboardBinding.kt`:

| Action | Default |
|---|---|
| Menu | Tab (Esc closes) |
| OpenOverview | F2 |
| OpenPolicies | F5 |
| OpenTech | F6 |
| Notifications | F7 |
| VictoryStatus | F8 |
| QuickSave / QuickLoad | F11 / F12 |
| ViewCapital | Home |
| Options | Ctrl+O |
| Save / Load | Ctrl+S / Ctrl+L |
| ToggleResources | Ctrl+R |
| ToggleYields | Ctrl+Y |
| PrevIdle / NextIdle | , and . |
| Unit actions | Sleep/Fortify F, Explore X, Automate M, Skip Space-in-unit-context, Promote O, Upgrade U, Pillage P, Connect road C, Disband Del |

- Today T, P and F are also bound to Tech, Policies and Diplomacy, and F collides with Fortify. **Decision: switch screens to F-keys (Kotlin parity) and give letters to unit actions.** Record this in the ticket Notes. It changes `OptionsScenario` (`options/rebound.png`, `listening.png`) and `Hotkeys` tests.
- `UnitOrdersModel.MenuEntry` gets a `Key` field so the action bar shows key letters.
- Every IconButton tooltip shows its KeyHint. The game menu shows KeyHints too.

**States.** Every interactive control has 5 visibly distinct states: normal, hover (Surface3 plus a lighter border), pressed (Inset), focus (Accent ring, keyboard nav works with Tab/arrows inside lists), and disabled.

**Selection feedback.**
- Lists, tech and policy nodes: a 3px Accent bar or 2px ring.
- On the map: the selected unit gets an Accent hex ring, and the hovered tile a 1px TextPrimary@40% outline.
- City tiles: a filled ring for worked tiles, a padlock for locked ones.

**Disabled-with-reason.** Never hide an action the player might look for. Show it disabled, with the reason as (a) the tooltip and (b) a Small muted subtitle where there's room ("Requires 2 population", "Not enough gold (140)"). Models already carry `Reason`/`Why`/`QueueWhy`; surface them.

**Empty states.** Saves, notifications ("Nothing new this turn"), research queue ("Queue empty — Shift+click techs to queue"), trade ("Nothing to trade"), Overview tables.

**Confirmation.**
- Destructive or irreversible actions (Declare war, Raze, Disband, Resign, Delete save, Sell building, Coup) open a Modal with the title "Declare war on Rome?", one consequence line, and [Cancel] Secondary on the left, [Declare war] Danger on the right. Esc means cancel.
- Reversible actions (queue changes, citizen moves) never confirm; they get Ctrl+Z where the engine supports undo.

**Feedback.** A toast for transient results ("Game saved", "Warrior fortified") at top-centre under the bar, replacing the permanent status label. Use the existing placeholder UI sounds on press.

**Escape semantics.** Close the popover, then the modal, then the screen, then deselect the unit, then open the game menu. This extends `ScreenManager.Back`.

---

## 5. Implementation tickets (C-track, ordered)

Each ticket follows PROTOCOL: Deliverables, Gate (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test`, plus `tools/run-scenario.sh <each listed>`), a `screenshots.json` caption update and `screenshots.py --check`. Re-record goldens with `run-scenario.sh <name> --update-golden`, open the PNGs, rewrite the captions, and run the review sheet (`tools/review-sheet.sh`) for owner sign-off.

**C061 — Harness: label-based Press and multi-resolution captures** (no visual change; do this first so later tickets don't break scripts)
- **Files:** `Unciv.Godot.Harness/ScenarioRunner.cs` (`FindButton` also matches `IconButton.Label`, and `GetMeta("ui_label")` for plain Buttons), `Scenario.cs` (`virtual Vector2I Viewport => new(640,480)`, and the runner resizes the window), `ScenarioRunner.cs` capture path.
- **Acceptance:**
  - An icon-only button with Label "Overview" is pressable by `Press("Overview")`.
  - A test scenario at 1280x720 captures correctly.
  - All 46 scenarios still pass with no golden changes.
- **Goldens:** none.

**C062 — Design tokens and theme builder v2** (foundation)
- **Files:** new `Unciv.Assets/UiTokens.cs`; trim `ThemeSpec` and the UI entries of `PlaceholderPalette`; new `Unciv.Godot/Assets/ThemeBuilder.cs`; `ProceduralAssets.BuildTheme` delegates to it; `Unciv.Godot/Ui/UiTheme.cs` (scale rebuilds everything, `Breakpoint`, `LayoutChanged`); new `Unciv.Godot/Ui/UiVariants.cs`; `Assets/FontChain.cs` (weight variations, tnum, Serif face); add `fonts/NotoSerif*.ttf`; `ASSETS.md`, `THIRD_PARTY_NOTICES.md`; tests `UiTokensTests` (contrast) and `ThemeBuilderTests` (every variation defined; Px scaling of margins).
- **Acceptance:**
  - Every type variation listed in 2.5 exists.
  - At scale 2, panel content margins double.
  - Contrast tests pass.
  - LiteralLint stays clean.
- **Goldens:** essentially all of them, because the base Button, Panel and font size change. Re-record all 46 in this ticket and have the owner review one contact sheet. Later tickets then only touch their own screens.

**C063 — Unciv icon pack and icon keys**
- **Files:** `Unciv.Godot/tools/import-unciv-icons.py` (stdlib only, `-I`); `Unciv.Godot/assets/packs/unciv/**` plus `manifest.json`; `Unciv.Assets/RulesetAssetKeys.cs` (`icon/ui`, `icon/nation`, `icon/action`, `icon/notification`, plus coverage); `ProceduralImages.Icon` (circle badge fallback, distinct shapes); default pack wiring in `App.cs`/`QuickStart.cs` and `ScenarioRunner`; new `Ui/IconBadge.cs`; `ASSETS.md`, notices.
- **Acceptance:**
  - Every `icon/stat/*` resolves from the pack.
  - A missing key falls back procedurally.
  - The licence check in the script passes.
  - The key-coverage test passes.
- **Goldens:** hud, ui-kit, tooltips, city-a, city-b, overview, pedia/search (tooltips), anything showing stat icons. That's nearly everything with a top bar, so batch the re-record.

**C064 — Unique display text and hidden uniques** (correctness bug, small)
- **Files:** wherever `DecisionPickerModel`, `PediaModel`, `TooltipModel` and `PolicyScreenModel` produce effect lines. Use the ported equivalent of Kotlin `Unique.getDisplayText()` and filter `isHiddenToUsers()`/`HiddenFromUsers`.
- **Acceptance:** unit tests show that "[+33]% Strength <vs [Armored]> units" renders as "+33% Strength vs Armored units", and that the AI-weight unique isn't shown.
- **Goldens:** decisions, decisions-religion, decisions-reform, tooltips, policy.

**C065 — Shared components**
- **Files:** `Ui/IconButton.cs`, `Stepper.cs`, `Segmented.cs` (ChoiceRow restyle, keeping its API), `SectionHeader.cs`, `ListRow.cs` (ListPanel uses it), `StatChip.cs`, `StatBar.cs`, `Badge.cs`, `KeyHint.cs`, `EmptyState.cs`, `DataTable.cs`, `TabBar.cs` (underline tabs, overflow arrows), `UiText.cs` (delta colouring, inline stat icons in BBCode).
- **Harness:** `UiKitScenario` gains a `components` capture showing every component in every state, plus a 1280x720 capture.
- **Acceptance:**
  - Each component has a unit or harness assertion (Stepper clamps; DataTable sorts and keeps numbers right-aligned; ListRow shows the disabled reason).
  - No stylebox is constructed outside `Assets/`.
- **Goldens:** ui-kit.

**C066 — ScreenFrame, Modal, Tooltip**
- **Files:** `Ui/ScreenFrame.cs`; `Ui/PopupStack.cs` (Modal layout, row footer, primary/cancel roles in `PopupChoice`, e.g. `PopupChoice.Role`); `TooltipHost.cs` (styled panel, clamp below the top bar, max width); move `AlertPopupView`, `DecisionPickerView`, `TutorialPopupView`, `TradeProposalView`, `CityRenameView`, `HandOverView` onto Modal.
- **Acceptance:**
  - All popups share one frame.
  - Esc means cancel.
  - The tooltip never overlaps `Hud.BarRect`. This needs a harness assert; it fixes `tooltips/tile.png`.
- **Goldens:** alerts, decisions*, proposals, tutorial, tutorial-tasks, city-rename, rename-city, hotseat, vote-result, ui-kit, tooltips.

**C067 — World HUD layout** (highest player impact)
- **Files:** new `Play/WorldHud.cs`, which owns the top bar, nav strip, research pill, notifications column, minimap dock, next-action dock and toast. Remove `GameScreen.cs:797-838` absolute layout but keep the public `*Button` properties, now the IconButtons. Also `HudView.cs` (StatChips, banner as toast, no per-update rebuild: update the text of existing chips), `NotificationListView.cs`, `MinimapView.cs` (size per breakpoint, controls), `NextTurnProgressView.cs`.
- **Acceptance:**
  - At 640x480 no HUD element overlaps another, checked by a harness assert over the HUD rects.
  - The map is uncovered between the bar and the docks.
  - Existing HudScenario assertions (`StatText`, `ActionText`, `BannerText`) pass unchanged.
  - The minimap is at least 140x100.
- **Goldens:** hud, v1-vertical, unit-orders, minimap, next-turn, alerts, menu, autoplay, hotseat, tutorial, tutorial-tasks, combat-presentation, flat-city, improvements (if the HUD is visible), console, defeat. Add 1280x720 `hud/wide.png`.

**C068 — Unit panel and action bar**
- **Files:** `Play/UnitPanelView.cs` (header, stat chips, icon grid, overflow popover, inline danger confirm, promote chip); `Unciv.Assets/UnitOrdersModel.cs` (`MenuEntry.Key`, `IconKey`, primary flag; `UnitPanelInfo` structured numbers, keeping the existing strings); `CombatPreviewView.cs` (a VS card with two unit columns, strength numbers, predicted health bars, Attack Primary / Cancel Ghost); remove the duplicate status line.
- **Acceptance:**
  - Every action of a worker fits without scrolling in 2 rows plus "more".
  - The `ButtonFor(label)` and `Entries` APIs are kept.
- **Goldens:** unit-orders, combat-presentation, decisions/promotion, v1-vertical.

**C069 — City screen**
- **Files:** `CityScreen.cs`, `CityProductionView.cs`; `CityScreenModel.cs`/`CityProductionModel.cs` (structured yields, cost, turns, progress, icon); remove the per-cell `StyleBoxFlat` allocation at line 308 (use theme variations `CityTileWorked`, `CityTileAvailable`, …); hex cells; drag-reorder; Raze as Danger with confirm.
- **Acceptance:**
  - CityA and CityB assertions still pass: "Close city" is kept as the X's Label, "Reset citizens" and "Rename" are kept.
  - Queue reorder works by keyboard (Ctrl+↑/↓) and by drag.
- **Goldens:** city-a, city-b, flat-city, city-rename, rename-city. Add `city-b/wide.png` at 1280x720.

**C070 — Tech tree**
- **Files:** `TechScreen.cs` (node cards, `_Draw` edge layer, sticky era bands, path preview, footer actions); `TechScreenModel.cs` (`Turns`, `Progress`, `IconKey`, `UnlockIcons`, `EraSpans`, `PathTo(tech)`).
- **Acceptance:**
  - No node label clips at 640x480 for the vanilla ruleset: a harness assert that each node's label minimum width is at most the node width, or that the label is ellipsized with a tooltip.
  - Each era header is drawn once.
  - "Close technology" remains a Label.
- **Goldens:** tech, plus `tech/wide.png`.

**C071 — Policies:** `PolicyScreen.cs`, `PolicyScreenModel.cs` (branch grid, node icons, adopt next to the description). **Goldens:** policy.

**C072 — Setup, nation picker and main menu**
- **Files:** `Setup/NewGameScreen.cs` (remove inline styleboxes; emblem grid; detail pane; Steppers; footer with summary), `Setup/MainMenu.cs` (backdrop, logo, column), `Unciv.Assets/Setup/NewGameForm.cs` (nation detail text).
- **Harness:** `SetupScenario` currently presses "Fewer players". Give the Stepper's − button the Label "Fewer players" so the script keeps working, or update the script in this ticket.
- **Goldens:** setup (all 9), settings (main-menu options entry), `hotseat/cover` if it's affected.

**C073 — Diplomacy and trade:** `DiplomacyScreen.cs`, `TradeScreen.cs`, `TradeProposalView.cs`, `DiplomacyScreenModel.cs`/`TradeScreenModel.cs` (item icons, an acceptance-hint enum instead of prose). **Goldens:** diplomacy, trade, proposals.

**C074 — Overview and espionage on DataTable:** `OverviewScreen.cs`, `EspionageScreen.cs`, `OverviewModel.cs`/`EspionageModel.cs` (typed columns). **Goldens:** overview, overview-espionage, espionage.

**C075 — Game menu, save/load, options:** `GameMenuView.cs`, `Saves/SaveController.cs`, `Settings/OptionsScreen.cs`, `SettingsPanel.cs` (vertical tabs, Toggles, scale Stepper). **Goldens:** menu, saveload, options, settings, autoplay.

**C076 — Civilopedia and tooltip content:** `PediaScreen.cs` (sidebar, entry header card, link style), `TooltipModel.cs` (structured lines: heading, stat chips, footer). **Goldens:** tooltips, plus a pedia capture.

**C077 — Victory, defeat, vote result:** `GameOverScreen.cs`, `VictoryStatusView.cs`, `VoteResultScreen.cs` (ranking table, graph axes). **Goldens:** victory, defeat, vote-result, menu/victory-status.

**C078 — Hotkeys v2 and key hints:** `Unciv.Assets/Settings/Hotkeys.cs` (new actions and Kotlin defaults, recorded as a deliberate deviation from the current T/P/F), `GameScreen.TryHotkey`, unit action dispatch, KeyHints everywhere. **Goldens:** options (rebound, listening). Update `Hotkeys` tests.

**C079 — Responsive and localization pass**
- Run hud, city, tech, setup and options at 640x480, 1280x720 and 1920x1080, and with a long-string locale (German and Russian through the `l10n-sheet` machinery).
- Add an RTL check: set `LayoutDirection` from the locale on the ScreenManager root.
- **Acceptance:** no overlaps or clipping without ellipsis (harness rect-overlap asserts), and Arabic/Hebrew mirror correctly.
- **Goldens:** new `*/wide*.png`, `*/fhd*.png`, l10n-sheet.

**Dependencies:**
- C061 comes first.
- C062 needs C061. C063 and C064 can run in parallel after C062.
- C065 needs C062 and C063. C066 needs C065.
- C067 and C068 need C066; C068 also needs C067.
- C069 through C077 each need C066 and can run in parallel, except that C069 and C070 both touch `UiText`, so run them in sequence.
- C078 and C079 come last.

---

## 6. Risks and mitigations

1. **Text-matched `Press` and C# properties in scenarios.**
   - C061 adds Label-based matching.
   - Every redesigned control that a scenario presses keeps its old string as its `Label` ("Close city", "Close technology", "Next turn", "Menu", "Fewer players", "Saves", "Overview"). Before each ticket, list them with `grep -ho 'new Press("[^"]*")'`.
   - Keep public properties (`TechButton`, `PromoteButton`, `StatusText`, `BannerText`, `ActionText`, `TitleText`, `Notifications.ToggleButton/ReviewButton`, `Hud.AutoPlayButton`), even when the control behind them changes.
   - `Click(() => Centre(...))` steps depend on the control being visible and unobscured. The overlap asserts in C067 protect them.
2. **Golden churn.**
   - C062 and C063 change every image. Do one re-record per ticket in a dedicated commit, and the owner signs off through the review sheet before the next ticket starts.
   - Keep `GoldenComparer` tolerances unchanged; don't loosen them to hide churn.
   - Captions in `screenshots.json` must be rewritten to describe the new look, because reviewers check that captions are true.
3. **Localization length.**
   - Icon-only nav and ellipsis-with-tooltip on every text button.
   - No fixed widths on text.
   - Tab strips get overflow arrows.
   - German and Russian runs in C079.
   - Uppercase Overline only for Latin scripts (`TextServer` check), never for CJK, Thai or Arabic.
   - The CJK subset font must be re-subset if new UI strings add characters (`subset-fonts.py`).
4. **`LiteralLint`.** Tokens in `Unciv.Assets`, styleboxes only in `Unciv.Godot/Assets/ThemeBuilder.cs`, every label through `.Tr()`. New UI strings need translation keys. Check the template file used by the l10n tests.
5. **Performance.**
   - `HudView.Rebuild()` frees and recreates the top bar on every update. Switch to update-in-place.
   - CityScreen allocates a `StyleBoxFlat` per cell per state. Switch to shared variations.
   - The tech tree draws all edges in one `_Draw`.
   - Pedia search results: cap or virtualize at about 200 rows.
   - Shadows only on modals and tooltips.
   - Theme rebuild only on scale or breakpoint change.
   - The main menu's animated map backdrop must respect the existing frame budget scenario (C040 `LongestFrameMs`). Fall back to a static frame on gl_compatibility/llvmpipe if needed.
6. **Licences.** The icon pack import must keep the per-file attribution from `docs/Credits.md`. CC BY-SA icons carry share-alike for that file only. Record them in ASSETS.md and `THIRD_PARTY_NOTICES.md`, and never pull from Firaxis material.
7. **Model API growth.** Add structured fields and keep the old string fields until all views have migrated, so headless model tests stay green ticket by ticket.

### Critical files for implementation
- `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot/Assets/ProceduralAssets.cs` (`BuildTheme`, which becomes `ThemeBuilder.cs`) and `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Assets/PlaceholderPalette.cs` / `ModelSpecs.cs` (`ThemeSpec`, which becomes `UiTokens.cs`)
- `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot/Ui/UiTheme.cs` (scaling and breakpoints) and `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot/Ui/PopupStack.cs`, `TooltipHost.cs`, `ListPanel.cs`, `TabBar.cs`, `ChoiceRow.cs`
- `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot/Play/GameScreen.cs` (lines 659-990: HUD construction and absolute layout), `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot/Play/HudView.cs`, `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot/Play/UnitPanelView.cs`
- `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot.Harness/ScenarioRunner.cs` (`FindButton`, line 224) and `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Godot.Harness/Scenario.cs`
- `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Assets/RulesetAssetKeys.cs` and `/var/home/nickmarc/repos/Unciv/csharp/Unciv.Assets/AssetResolver.cs` (icon pack), with source icons in `/var/home/nickmarc/repos/Unciv/android/Images.Icons/` and licences in `/var/home/nickmarc/repos/Unciv/docs/Credits.md`
