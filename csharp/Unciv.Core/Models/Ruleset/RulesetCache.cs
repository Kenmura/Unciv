using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset.Validation;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/RulesetCache.kt
// Deviations:
// - the Kotlin object is a HashMap<String, Ruleset>; here the same map is the static state behind Get/Put/ContainsKey/Values/..., a JavaHashMap so that
//   Values iterates in the same order as the Kotlin HashMap.
// - loading is sequential (Kotlin loads in parallel into a ConcurrentHashMap, whose order is lost anyway), and mod folders are loaded in the ordinal
//   order of their names; the "Loaded N rulesets in Nms" console line is dropped, a wall-clock time can not be read.
// - the files come from an IRulesetFileSource instead of Gdx.files / UncivGame.Current.files; console mode is a source without a global jsons folder.

/// <summary>Loading mods is expensive, so let's only do it once and save all of the loaded rulesets somewhere for later use</summary>
public static class RulesetCache
{
    private static JavaHashMap<string, Ruleset> Cache = new();

    /// <summary>
    /// Similarity below which an untyped unique can be considered a potential misspelling.
    /// Roughly corresponds to the fraction of the Unique placeholder text that can be different/misspelled, but with some extra room for getRelativeTextDistance idiosyncrasies.
    /// Tweak as needed. Simple misspellings seem to be around 0.025, so would mostly be caught by 0.05. IMO 0.1 would be good, but raising to 0.15 also seemed to catch what may be an outdated Unique.
    /// </summary>
    public static double UniqueMisspellingThreshold { get; set; } = 0.15;

    // region the HashMap<String, Ruleset> API
    public static Ruleset? Get(string name) => Cache.TryGetValue(name, out var ruleset) ? ruleset : null;

    public static void Put(string name, Ruleset ruleset) => Cache.Put(name, ruleset);

    public static bool ContainsKey(string? name) => name != null && Cache.ContainsKey(name);

    public static bool Remove(string name) => Cache.Remove(name);

    public static void Clear() => Cache.Clear();

    public static int Count => Cache.Count;

    public static IEnumerable<string> Keys => Cache.Keys;

    public static IEnumerable<Ruleset> Values => Cache.Values;
    // endregion

    /// <summary>Returns error lines from loading the rulesets, so we can display the errors to users</summary>
    public static List<string> LoadRulesets(IRulesetFileSource files, bool noMods = false)
    {
        var newRulesets = new LinkedHashMap<string, Ruleset>();

        void LoadBuiltinRuleset(BaseRuleset ruleset)
        {
            var fullName = ruleset.FullName();
            var folder = files.GetBuiltinRulesetFolder(fullName);
            var loaded = new Ruleset { Name = fullName };
            loaded.Load(folder, files.GlobalJsonsFolder);
            newRulesets[fullName] = loaded;
        }

        // Base rulesets must be available for fallback for missing parts of custom rulesets
        foreach (var baseRuleset in Enum.GetValues<BaseRuleset>()) LoadBuiltinRuleset(baseRuleset);

        PutAll(newRulesets); // Make base rulesets available while loading other mods

        var errorLines = new List<string>();
        if (!noMods)
        {
            foreach (var modFolder in files.ModsFolder.List())
            {
                if (modFolder.Name.StartsWith('.')) continue;
                if (!modFolder.IsDirectory) continue;
                var modRuleset = LoadSingleRuleset(modFolder, errorLines, files);
                if (modRuleset != null) newRulesets[modRuleset.Name] = modRuleset;
            }
            if (Log.ShouldLog()) foreach (var line in errorLines) Log.Debug(line);
        }

        // We save the 'old' cache values until we're ready to replace everything, so that the cache isn't empty while we try to load ruleset files
        // - this previously lead to "can't find Vanilla ruleset" if the user had a lot of mods and downloaded a new one
        Clear();
        PutAll(newRulesets);

        return errorLines;
    }

    private static void PutAll(IEnumerable<KeyValuePair<string, Ruleset>> rulesets)
    {
        foreach (var (name, ruleset) in rulesets) Cache.Put(name, ruleset);
    }

    private static Ruleset? LoadSingleRuleset(IRulesetFolder modFolder, List<string> errorLines, IRulesetFileSource files)
    {
        try
        {
            var modRuleset = new Ruleset { Name = modFolder.Name };
            modRuleset.Load(modFolder.Child("jsons"), files.GlobalJsonsFolder);
            modRuleset.FolderLocation = modFolder;
            Log.Debug("Mod loaded successfully: %s", modRuleset.Name);
            return modRuleset;
        }
        catch (Exception ex)
        {
            errorLines.Add($"Exception loading mod '{modFolder.Name}':");
            errorLines.Add($"  {ex.Message}");
            var causes = new List<Exception>();
            for (Exception? cause = ex; cause != null; cause = cause.InnerException) causes.Add(cause);
            // Kotlin: the first cause whose message mentions "line", else the last cause from the libGDX package
            // (a JSON syntax error); System.Text.Json reports the position in the message ("LineNumber") of its JsonException.
            var found = causes.FirstOrDefault(it => it.Message.Contains("line", StringComparison.OrdinalIgnoreCase))
                ?? causes.LastOrDefault(it => it is System.Text.Json.JsonException);
            if (found != null) errorLines.Add($"  {found.Message}");
            return null;
        }
    }

    public static List<string> ReloadSingleRuleset(IRulesetFileSource files, string modName)
    {
        var modFolder = files.ModsFolder.Child(modName);
        if (!modFolder.Exists || !modFolder.IsDirectory) return ["No such mod"]; // No translation template - shouldn't ever happen?
        var errorLines = new List<string>();
        var newRuleset = LoadSingleRuleset(modFolder, errorLines, files);
        if (newRuleset != null) Put(modName, newRuleset);
        return errorLines;
    }

    /// <summary>Safeguard, so no-one edits the base ruleset by mistake: a clone.</summary>
    public static Ruleset GetVanillaRuleset() => Get(BaseRuleset.Civ_V_Vanilla.FullName()).NotNull().Clone();

    public static List<string> GetSortedBaseRulesets()
    {
        var baseRulesets = Values
            .Where(it => it.ModOptions.IsBaseRuleset)
            .Select(it => it.Name)
            .Distinct()
            .ToList();
        if (baseRulesets.Count < 2) return baseRulesets;

        // We sort the base rulesets such that the ones unciv provides are on the top,
        // and the rest is alphabetically ordered.
        var builtin = Enum.GetValues<BaseRuleset>();
        return baseRulesets
            .OrderBy(ruleset => builtin.Where(it => it.FullName() == ruleset).Select(it => (int?)(int)it).FirstOrDefault() ?? builtin.Length)
            .ThenBy(it => it, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Creates a combined <see cref="Ruleset"/> from a list of mods contained in <paramref name="parameters"/>.</summary>
    public static Ruleset GetComplexRuleset(MapParameters parameters) =>
        GetComplexRuleset(parameters.Mods, parameters.BaseRuleset);

    /// <summary>Creates a combined <see cref="Ruleset"/> from a list of mods contained in <paramref name="parameters"/>.</summary>
    public static Ruleset GetComplexRuleset(GameParameters parameters) =>
        GetComplexRuleset(parameters.Mods, parameters.BaseRuleset);

    /// <summary>
    /// Creates a combined <see cref="Ruleset"/> from a list of mods.
    /// If no baseRuleset is passed in <paramref name="optionalBaseRuleset"/> (or a non-existing one), then the vanilla Ruleset is included automatically.
    /// Any mods in the <paramref name="mods"/> parameter marked as base ruleset (or not loaded in <see cref="RulesetCache"/>) are ignored.
    /// </summary>
    public static Ruleset GetComplexRuleset(LinkedHashSet<string> mods, string? optionalBaseRuleset = null)
    {
        var baseRuleset =
            ContainsKey(optionalBaseRuleset) && Get(optionalBaseRuleset.NotNull()).NotNull().ModOptions.IsBaseRuleset
                ? Get(optionalBaseRuleset.NotNull()).NotNull()
                : GetVanillaRuleset();

        var loadedMods = mods
            .Where(it => ContainsKey(it))
            .Select(it => Get(it).NotNull())
            .Where(it => !it.ModOptions.IsBaseRuleset);

        return GetComplexRuleset(baseRuleset, loadedMods);
    }

    /// <summary>Creates a combined <see cref="Ruleset"/> from <paramref name="baseRuleset"/> and <paramref name="extensionRulesets"/> which must only contain non-base rulesets.</summary>
    public static Ruleset GetComplexRuleset(Ruleset baseRuleset, IEnumerable<Ruleset> extensionRulesets)
    {
        var newRuleset = new Ruleset();

        var loadedMods = extensionRulesets.Append(baseRuleset);

        foreach (var mod in loadedMods.OrderByDescending(it => it.ModOptions.IsBaseRuleset))
        {
            if (mod.ModOptions.IsBaseRuleset)
            {
                // This is so we don't keep using the base ruleset's uniques *by reference* and add to in ad infinitum
                newRuleset.ModOptions.Uniques = [];
                newRuleset.ModOptions.IsBaseRuleset = true;
                // Default tileset and unitset are according to base ruleset
                newRuleset.ModOptions.Tileset = mod.ModOptions.Tileset;
                newRuleset.ModOptions.Unitset = mod.ModOptions.Unitset;
            }
            newRuleset.Add(mod);
            newRuleset.Mods.Add(mod.Name);
        }
        newRuleset.UpdateBuildingCosts(); // only after we've added all the mods can we calculate the building costs
        newRuleset.UpdateResourceTransients();

        return newRuleset;
    }

    /// <summary>Runs <c>Ruleset.GetErrorList</c> on a temporary <see cref="GetComplexRuleset(LinkedHashSet{string}, string?)">combined Ruleset</see> for a list of <paramref name="mods"/></summary>
    public static (Ruleset? Ruleset, RulesetErrorList Errors) CheckCombinedModLinks(
        LinkedHashSet<string> mods,
        string? baseRuleset = null,
        bool tryFixUnknownUniques = false)
    {
        try
        {
            var newRuleset = GetComplexRuleset(mods, baseRuleset);
            newRuleset.ModOptions.IsBaseRuleset = true; // This is so the checkModLinks finds all connections
            return (newRuleset, newRuleset.GetErrorList(tryFixUnknownUniques));
        }
        catch (UncivShowableException ex)
        {
            // This happens if a building is dependent on a tech not in the base ruleset
            //  because newRuleset.updateBuildingCosts() in getComplexRuleset() throws an error
            return (null, RulesetErrorList.Of(ex.Message, RulesetErrorSeverity.Error));
        }
    }
}
