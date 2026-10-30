# Candidate tickets awaiting owner review

Brainstormed 2026-10-30. **None of these are filed as tickets.** Say which to file and an agent adds them to the backlog
(`tools/ticket.py add`). Suggested first batch: **4, 6, 9, 2, 3**.

How to read the status column: "Unfiled" means no ticket exists for it. "Partly covered" names the existing ticket and what is
still missing. Statuses were checked against ticket titles and design docs only, not by playing the game, so items marked
"unverified" may already exist in the client.

Items 1 to 16 come from the design docs' follow-up lists. Items 17 to 20 are guesses that appear in no doc.

## Bugs and risks

| # | Candidate | Status |
|---|-----------|--------|
| 1 | Unit recognizability for ships, siege and aircraft: each must be distinct at the default zoom (owner rule: a Settler must never be mistakable for an Archer). | **Covered** by T307 (Done), T324 (rigid second pass), T320 (similar foot infantry), T309 (measure at real zoom). |
| 2 | Two-human split-brain (`design/HOST-MIGRATION.md` known limit): with exactly two humans a network partition can give each machine a host. Add a playtest scenario and an in-game warning. | Unfiled. T274 (crash election) is in progress and may change the picture. |
| 3 | Right-to-left and complex-script text: unverified that Godot's text engine has HarfBuzz and ICU (`design/CLIENT-SLICES.md`, "not yet checked"). Arabic, Hebrew and Indic text may render broken. | Unfiled. |
| 4 | Recurring 300-turn soaks on other rulesets and mods, plus save-then-reload determinism checks at many points, not only at the end (T293 found a mismatch after turn 250). | Partly covered: T167 is a 300-turn headless soak on Small and Standard maps; mods, recurrence and mid-game reload checks are not. |
| 5 | Incremental host view diff (`design/PERFORMANCE.md` follow-up): late-game turns with several humans. | Unfiled. T297 added optional parallel view building, which is not the same thing. |

## Gameplay and UX

| # | Candidate | Status |
|---|-----------|--------|
| 6 | "What did the AI do?" replay after End Turn: moves, attacks, new cities. | Unfiled; unverified whether the client has it. |
| 7 | Confirm and undo for destructive actions: disband, declare war, raze. | Unfiled; unverified. |
| 8 | Tooltips on every on-screen number. | Unfiled; unverified. |
| 9 | Progress overlay for long AI turns (`design/CLIENT-SLICES.md`: "not scheduled"). | Unfiled. |
| 10 | Reconnect experience: status for dropped connections, a rejoin button, a visible "waiting for player X" in simultaneous turns. | Unfiled; unverified (C036 added a multiplayer turn bar with connection markers). |

## Multiplayer polish

| # | Candidate | Status |
|---|-----------|--------|
| 11 | Share-invite-code intent on Android (`design/ANDROID.md` A-9). Copying works; a share intent needs a plugin. | Unfiled. |
| 12 | Lobby comforts: chat, kick and ban, rejoin by name. | Partly covered: T204 has slots, nation pick, ready-up, AI fill, password. Chat and kick/ban not found. |
| 13 | NAT traversal is out of scope (needs a rendezvous server the owner ruled out). Write a "play with friends" guide for the overlay-VPN route instead. | Unfiled. |

## Performance and quality

| # | Candidate | Status |
|---|-----------|--------|
| 14 | Per-unit `GetViewableTiles` cache (top allocator per `design/PERFORMANCE.md`). | Unfiled. |
| 15 | Bench `--repeat N` that reports the median of several runs. | Unfiled. |
| 16 | Call-level profiling inside AI unit moves. | Unfiled. T261 covers per-phase turn time only. |

## Guesses (not in any doc)

| # | Candidate | Status |
|---|-----------|--------|
| 17 | Accessibility: colourblind-safe team colours, font scaling, keyboard-only play. | Unfiled; unverified. |
| 18 | Sound and music (the asset rules forbid Civ 5 audio; no sourcing plan seen). | Unfiled. |
| 19 | First-run experience: a new-game wizard with sensible defaults and a short tutorial pass. | Unfiled; unverified. |
| 20 | Mod support in the Godot client (unverified how mods load; T247 syncs mods in the lobby). | Unfiled. |

## Already tickets, listed for context

These came out of the same work and are in the backlog, so they need no review: T278 (Android on-device checks), T290 (Windows,
macOS and phone frame times, blocked on hardware), T305 (first tagged release run), T306 (C099 on-device checks), T313 (OS
display scale on Windows, macOS, X11), T314 (flaky tests), T317, T318, T321 to T323 (resolution and layout), T319 (failing
goldens), T325 (automatic release drafts).
