using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/ModCompatibility.kt
// Deviation: FileHandle.list("atlas") (files with that extension) is IRulesetFolder.List() filtered by name.

/// <summary>
///  Helper collection dealing with declarative Mod compatibility
///
///  Implements: ModRequires, ModIncompatibleWith, ModIsAudioVisual, ModIsNotAudioVisual, ModIsAudioVisualOnly
///
///  Methods:
///  - <see cref="MeetsBaseRequirements"/> - to build a checkbox list of Extension mods
///  - <see cref="MeetsAllRequirements"/> - to see if a mod is allowed in the context of a complete mod selection
/// </summary>
public static class ModCompatibility
{
    /// <summary>
    ///  Should the "Permanent Audiovisual Mod" checkbox be shown for <paramref name="mod"/>?
    ///  Note: The guessing part may potentially be deprecated and removed if we get our Modders to complete declarative coverage.
    /// </summary>
    public static bool IsAudioVisualMod(Ruleset mod) => IsAudioVisualDeclared(mod) ?? IsAudioVisualGuessed(mod);

    private static bool? IsAudioVisualDeclared(Ruleset mod)
    {
        var modOptions = (IHasUniques)mod.ModOptions;
        if (modOptions.HasUnique(UniqueType.ModIsAudioVisualOnly)) return true;
        if (modOptions.HasUnique(UniqueType.ModIsAudioVisual)) return true;
        if (modOptions.HasUnique(UniqueType.ModIsNotAudioVisual)) return false;
        return null;
    }

    // If there's media (audio folders or any atlas), show the PAV choice...
    private static bool IsAudioVisualGuessed(Ruleset mod)
    {
        var folder = mod.FolderLocation;
        if (folder == null) return false; // Also catches isBuiltin

        static bool IsSubFolderNotEmpty(IRulesetFolder modFolder, string name)
        {
            var file = modFolder.Child(name);
            if (!file.Exists) return false;
            if (!file.IsDirectory) return false;
            return file.List().Count != 0;
        }
        if (IsSubFolderNotEmpty(folder, "music")) return true;
        if (IsSubFolderNotEmpty(folder, "sounds")) return true;
        if (IsSubFolderNotEmpty(folder, "voices")) return true;
        return ListAtlases(folder).Count != 0;
    }

    private static List<IRulesetFolder> ListAtlases(IRulesetFolder folder) =>
        folder.List().Where(it => it.Name.EndsWith(".atlas", StringComparison.Ordinal)).ToList();

    public static bool IsExtensionMod(Ruleset mod) =>
        !mod.ModOptions.IsBaseRuleset
        && !string.IsNullOrWhiteSpace(mod.Name)
        && !((IHasUniques)mod.ModOptions).HasUnique(UniqueType.ModIsAudioVisualOnly);

    public static bool IsConstantsOnly(Ruleset mod)
    {
        var folder = mod.FolderLocation;
        if (folder == null) return false;
        if (ListAtlases(folder).Count != 0) return false;
        var jsonFolder = folder.Child("jsons");
        if (!jsonFolder.Exists || !jsonFolder.IsDirectory) return false;
        return jsonFolder.List().Select(it => it.Name).SequenceEqual(["ModOptions.json"]);
    }

    /// <summary>A mod name matches a filter when equal, or when the filter is <c>*partial*</c> and the lower-cased name contains it.</summary>
    public static bool ModNameFilter(string modName, string filter)
    {
        if (modName == filter) return true;
        if (filter.Length < 3 || !filter.StartsWith('*') || !filter.EndsWith('*')) return false;
        string partialName = filter.Substring(1, filter.Length - 2).ToLowerInvariant();
        return modName.ToLowerInvariant().Contains(partialName, StringComparison.Ordinal);
    }

    private static bool IsIncompatibleWith(Ruleset mod, Ruleset otherMod) =>
        ((IHasUniques)mod.ModOptions).GetMatchingUniques(UniqueType.ModIncompatibleWith)
            .Any(it => ModNameFilter(otherMod.Name, it.Params[0]));

    private static bool IsIncompatible(Ruleset mod, Ruleset otherMod) =>
        IsIncompatibleWith(mod, otherMod) || IsIncompatibleWith(otherMod, mod);

    /// <summary>
    ///  Implement ModRequires and ModIncompatibleWith for selecting extension mods to show - after a <paramref name="baseRuleset"/> was chosen.
    ///
    ///  - Extension mod is incompatible with baseRuleset -> Nope
    ///  - Extension mod has no ModRequires unique -> OK
    ///  - For each ModRequires: Not (baseRuleset meets filter OR any other cached _extension_ mod meets filter) -> Nope
    ///  - All ModRequires tested -> OK
    /// </summary>
    public static bool MeetsBaseRequirements(Ruleset mod, Ruleset baseRuleset)
    {
        if (IsIncompatible(mod, baseRuleset)) return false;

        var allOtherExtensionModNames = RulesetCache.Values
            .Where(it => !ReferenceEquals(it, mod) && !it.ModOptions.IsBaseRuleset && it.Name.Length != 0)
            .Select(it => it.Name)
            .ToList();

        foreach (var unique in ((IHasUniques)mod.ModOptions).GetMatchingUniques(UniqueType.ModRequires))
        {
            string filter = unique.Params[0];
            if (ModNameFilter(baseRuleset.Name, filter)) continue;
            if (!allOtherExtensionModNames.Any(it => ModNameFilter(it, filter))) return false;
        }
        return true;
    }

    /// <summary>
    ///  Implement ModRequires and ModIncompatibleWith for _enabling_ shown extension mods depending on other extension choices
    ///
    ///  <paramref name="selectedExtensionMods"/>: all "active" mods for the compatibility tests - including the testee <paramref name="mod"/> itself in this is allowed, it will be ignored.
    ///
    ///  - No need to test: Extension mod is incompatible with baseRuleset - we expect <see cref="MeetsBaseRequirements"/> did exclude it from the UI entirely
    ///  - Extension mod is incompatible with any _other_ **selected** extension mod -> Nope
    ///  - Extension mod has no ModRequires unique -> OK
    ///  - For each ModRequires: Not(baseRuleset meets filter OR any other **selected** extension mod meets filter) -> Nope
    ///  - All ModRequires tested -> OK
    /// </summary>
    public static bool MeetsAllRequirements(Ruleset mod, Ruleset baseRuleset, IEnumerable<Ruleset> selectedExtensionMods)
    {
        var otherSelectedExtensionMods = selectedExtensionMods.Where(it => !ReferenceEquals(it, mod)).ToList();
        if (otherSelectedExtensionMods.Any(it => IsIncompatible(mod, it))) return false;

        foreach (var unique in ((IHasUniques)mod.ModOptions).GetMatchingUniques(UniqueType.ModRequires))
        {
            string filter = unique.Params[0];
            if (ModNameFilter(baseRuleset.Name, filter)) continue;
            if (!otherSelectedExtensionMods.Any(it => ModNameFilter(it.Name, filter))) return false;
        }
        return true;
    }
}
