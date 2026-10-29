# Reproducing the BNW extraction

<!-- toc -->
## Contents

- [1. Data (no decompiler needed)](#1-data-no-decompiler-needed)
- [2. Engine (decompiler)](#2-engine-decompiler)
<!-- /toc -->

All scripts are plain Python 3 (stdlib only). Run them with `python3 -I`. Paths below assume a Steam install on Linux;
`CIV` = `~/.local/share/Steam/steamapps/common/Sid Meier's Civilization V` and
`CACHE` = `~/.local/share/Steam/steamapps/compatdata/8930/pfx/drive_c/users/steamuser/Documents/My Games/Sid Meier's Civilization 5/cache`.

## 1. Data (no decompiler needed)

Start any game with Brave New World enabled once, so the game writes its merged database to `CACHE`. Then:

```
cp "$CACHE/Civ5DebugDatabase.db" "$CACHE/Localization-Merged.db" /some/scratch/   # work on copies
python3 -I extract.py /some/scratch/Civ5DebugDatabase.db /some/scratch/Localization-Merged.db ../data
python3 -I schemadiff.py "$CIV" > ../data/derived/schema_gk_to_bnw.txt
python3 -I defdiff.py "$CIV/Assets/DLC/Expansion/Gameplay/XML/GlobalDefines.xml|..." "$CIV/Assets/DLC/Expansion2/Gameplay/XML/GlobalDefines.xml|..."
python3 -I objdiff.py /some/scratch/Civ5DebugDatabase.db /some/scratch/Localization-Merged.db "<repo>/android/assets/jsons/Civ V - Gods & Kings"
```

`defdiff.py` takes `|`-separated lists of XML files. The run behind `defines_gk_to_bnw.tsv` used `GlobalDefines.xml`,
`AI/GlobalAIDefines.xml` and `AI/GlobalDiplomacyAIDefines.xml` from each expansion. Float defines (such as
`DIPLO_VICTORY_*`) are only in the database `Defines` table (`data/Defines.tsv`), not in that diff.

## 2. Engine (decompiler)

The BNW engine is `$CIV/Assets/DLC/Expansion2/CvGameCore_Expansion2.dll`
(SHA-256 `c5483b5857a6fb7ef107af93c5d34282eb713472e2a84d0eb9d3fdc104e155a5`, 19,551 functions, no symbols).

1. Decompile to one C text file with Ghidra 12.1.x headless and `../../ai-spec/tools/DumpDecomp.java`
   (see `../../ai-spec/tools/README.md`). Also produce `calls.json` (caller→callees) and `defines.json` with
   `../../ai-spec/tools/defs.py`. `defines.json` maps the engine's cached-define struct offsets to names, so global
   reads print as `D.NAME`.
2. `luamap.py <dir>` writes `luamap.json` (Lua method name → wrapper → engine callees). The Lua binding names are the
   best available function names.
3. `fn.py <dir> str REGEX | def REGEX | show ADDR[,ADDR] | callers ADDR | callees ADDR` finds functions by string
   literal or define use, and prints them with define names substituted.
4. `rd.py <dll> f|d|i ADDR...` reads a static float/double/int from the DLL image. It only works for `.rdata`
   constants; runtime-initialised globals read as junk.
