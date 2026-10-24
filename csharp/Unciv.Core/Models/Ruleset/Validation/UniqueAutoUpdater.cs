using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/UniqueAutoUpdater.kt
// Deviations:
// - IRulesetFolder is read-only (no libGDX FileHandle.writeString), so AutoupdateUniques returns the new file texts for the caller to write instead of writing them.
// - Log.error / Log.debug of the Kotlin version are dropped (no output feeds the result).

public static class UniqueAutoUpdater
{
    /// <summary>
    ///  Apply Unique replacements.
    /// </summary>
    /// <param name="mod">The Ruleset to process. It must have a FolderLocation.</param>
    /// <param name="replaceableUniques">A map old to new. The ModCheck UI will have produced this map using a combined Ruleset for replacement validation when appropriate.</param>
    /// <returns>The jsons file name and new text for every ruleset file that exists in the mod's jsons folder (Kotlin writes these back to the file).</returns>
    public static List<(string FileName, string NewText)> AutoupdateUniques(Ruleset mod, JavaHashMap<string, string>? replaceableUniques = null)
    {
        replaceableUniques ??= GetDeprecatedReplaceableUniques(mod, mod);
        var filesToReplace = RulesetFile.Values.Select(it => it.Filename);
        var jsonFolder = mod.FolderLocation!.Child("jsons");
        var result = new List<(string, string)>();
        foreach (string fileName in filesToReplace)
        {
            var file = jsonFolder.Child(fileName);
            if (!file.Exists || file.IsDirectory) continue;
            string newFileText = file.ReadString();
            foreach (var (original, replacement) in replaceableUniques)
            {
                newFileText = newFileText.Replace($"\"{original}\"", $"\"{replacement}\"", StringComparison.Ordinal);
                newFileText = newFileText.Replace($"<{original}>", $"<{replacement}>", StringComparison.Ordinal); // For modifiers
            }
            result.Add((fileName, newFileText));
        }
        return result;
    }

    /// <summary>
    ///  Determine possible auto-corrections from Deprecation annotations.
    /// </summary>
    /// <param name="mod">The Ruleset to scan</param>
    /// <param name="rulesetForValidation">The Ruleset to test potential fixed Uniques against. Can be same as <paramref name="mod"/>,
    /// otherwise it should be a complex Ruleset combining <paramref name="mod"/> with an appropriate base ruleset.</param>
    /// <returns>A map of old to new (a java.util.HashMap, so in Java hash order). Will not include 'new' values that don't pass Unique validation against <paramref name="rulesetForValidation"/>.</returns>
    public static JavaHashMap<string, string> GetDeprecatedReplaceableUniques(Ruleset mod, Ruleset rulesetForValidation)
    {
        var allUniques = mod.AllUniques().ToList();
        var allDeprecatedUniques = new HashSet<string>();
        var deprecatedUniquesToReplacementText = new JavaHashMap<string, string>();
        var validator = new UniqueValidator(rulesetForValidation);
        bool reportRulesetSpecificErrors = rulesetForValidation.ModOptions.IsBaseRuleset;

        var deprecatedUniques = allUniques.Where(it => it.GetDeprecationAnnotation() != null);

        var deprecatedConditionals = allUniques
            .SelectMany(it => it.Modifiers)
            .Where(it => it.GetDeprecationAnnotation() != null);

        foreach (var deprecatedUnique in deprecatedUniques.Concat(deprecatedConditionals).ToList())
        {
            if (!allDeprecatedUniques.Add(deprecatedUnique.Text)) continue;

            string uniqueReplacementText = deprecatedUnique.GetReplacementText(mod);
            while (new UniqueObject(uniqueReplacementText).GetDeprecationAnnotation() != null)
                uniqueReplacementText = new UniqueObject(uniqueReplacementText).GetReplacementText(mod);

            foreach (var conditional in deprecatedUnique.Modifiers)
                uniqueReplacementText += $" <{conditional.Text}>";
            var replacementUnique = new UniqueObject(uniqueReplacementText);

            var modErrors = validator.CheckUnique(replacementUnique, null,
                reportRulesetSpecificErrors ? UniqueValidator.AllParameterSeverities : UniqueValidator.ExtensionModParameterSeverities);
            if (modErrors.Count != 0) continue; // errors means no autoreplace

            deprecatedUniquesToReplacementText[deprecatedUnique.Text] = uniqueReplacementText;
        }

        return deprecatedUniquesToReplacementText;
    }
}
