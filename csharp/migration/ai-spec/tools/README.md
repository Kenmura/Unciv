# Reproducing the analysis

1. Install JDK 21 and Ghidra 12.1.x (user-space is fine; no system layering needed).
2. Decompile an engine DLL to C text (one file, ~15-19 MB):
   ```
   analyzeHeadless <projdir> <name> -import "<path>/CvGameCore_Expansion2.dll" \
       -scriptPath tools -postScript DumpDecomp.java <out>/decomp.c -deleteProject
   ```
   Engine DLLs: `CvGameCoreDLLFinal Release.dll` (base), `Assets/DLC/Expansion/CvGameCore_Expansion1.dll` (G&K),
   `Assets/DLC/Expansion2/CvGameCore_Expansion2.dll` (BNW).
3. `defs.py <decomp.c> <outdir>` builds the define-name table from the global defines init function (finds it via the
   `"AI_GS_RAND_ROLL"` load). The global-struct **base address must be set by hand** in `<outdir>/defines.json`
   (`"base"`): anchor = the address pair read by the "gold gift to a minor" code, minus the offsets of
   `MC_ALWAYS_GIFT_DIPLO_THRESHOLD` / `MC_SOMETIMES_GIFT_RAND_MULTIPLIER`. Known bases: G&K `0x10325f68`, BNW `0x103bd250`
   (base game `0x102b3d40`). Verify: ~15/16 `MC_GIFT_WEIGHT_*` globals should appear in that function.
4. `label.py <scratch> <1|2> <addr[,addr]>` prints functions with `DAT_xxxxxxxx` replaced by define names;
   `label.py <scratch> <1|2> find <regex>` ranks functions by how many matching defines they read.
   (paths in the scripts assume `<scratch>/out1` = G&K, `<scratch>/out2` = BNW)

`decomp.c` is derived from proprietary code and is intentionally not committed.
