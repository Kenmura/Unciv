using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/ImprovementDescriptions.kt

public static class ImprovementDescriptions
{
    /// <summary>
    /// Lists differences: how a nation-unique Improvement compares to its replacement.
    /// Result as indented, non-linking <see cref="FormattedLine"/>s.
    /// </summary>
    public static IEnumerable<FormattedLine> GetDifferences(
        Ruleset ruleset, TileImprovement originalImprovement, TileImprovement replacementImprovement)
    {
        foreach (var stat in StatExtensions.Entries) // Do not iterate on object since that excludes zero values
            if (replacementImprovement[stat] != originalImprovement[stat])
                yield return new FormattedLine(stat.ToString().Tr() + " " + $"[{KotlinMath.FloatToInt(replacementImprovement[stat])}] vs [{KotlinMath.FloatToInt(originalImprovement[stat])}]".Tr(), indent: 1);

        foreach (var terrain in replacementImprovement.TerrainsCanBeBuiltOn)
            if (!originalImprovement.TerrainsCanBeBuiltOn.Contains(terrain))
                yield return new FormattedLine($"Can be built on [{terrain}]", link: ruleset.Terrains.GetValueOrDefault(terrain)?.MakeLink() ?? "", indent: 1);
        foreach (var terrain in originalImprovement.TerrainsCanBeBuiltOn)
            if (!replacementImprovement.TerrainsCanBeBuiltOn.Contains(terrain))
                yield return new FormattedLine($"Cannot be built on [{terrain}]", link: ruleset.Terrains.GetValueOrDefault(terrain)?.MakeLink() ?? "", indent: 1);

        if (replacementImprovement.TurnsToBuild != originalImprovement.TurnsToBuild)
            yield return new FormattedLine("{Turns to build} ".Tr() + $"[{replacementImprovement.TurnsToBuild}] vs [{originalImprovement.TurnsToBuild}]".Tr(), indent: 1);

        foreach (var unique in replacementImprovement.UniqueObjects.Where(it => !originalImprovement.Uniques.Contains(it.Text) && !it.IsHiddenToUsers()))
            yield return new FormattedLine(unique.GetDisplayText(), indent: 1); // FormattedLine(unique) would look worse - no indent and auto-linking could distract

        foreach (var unique in originalImprovement.UniqueObjects.Where(it => !replacementImprovement.Uniques.Contains(it.Text) && !it.IsHiddenToUsers()))
        {
            // Need double translation of the "ability" here - unique texts may contain square brackets
            yield return new FormattedLine($"Lost ability (vs [{originalImprovement.Name}]): [{unique.GetDisplayText().Tr()}]", indent: 1);
        }
    }

    public static List<FormattedLine> GetCivilopediaTextLines(TileImprovement improvement, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var textList = new List<FormattedLine>();

        var statsDesc = improvement.CloneStats().ToString();
        if (!string.IsNullOrEmpty(statsDesc)) textList.Add(new FormattedLine(statsDesc));

        if (improvement.UniqueTo != null)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine($"Unique to [{improvement.UniqueTo}]", link: $"Nation/{improvement.UniqueTo}"));
        }
        if (improvement.Replaces != null)
        {
            var replaceImprovement = ruleset.TileImprovements.GetValueOrDefault(improvement.Replaces);
            textList.Add(new FormattedLine($"Replaces [{improvement.Replaces}]", link: replaceImprovement?.MakeLink() ?? "", indent: 1));
        }

        var constructorUnits = improvement.GetConstructorUnits(ruleset);
        var creatingUnits = improvement.GetCreatingUnits(ruleset);
        var creatorExists = constructorUnits.Count != 0 || creatingUnits.Count != 0;

        if (creatorExists && improvement.TerrainsCanBeBuiltOn.Count != 0)
        {
            textList.Add(new FormattedLine());
            if (improvement.TerrainsCanBeBuiltOn.Count == 1)
            {
                var terrain = improvement.TerrainsCanBeBuiltOn[0];
                textList.Add(new FormattedLine($"{{Can be built on}} {{{terrain}}}", link: $"Terrain/{terrain}"));
            }
            else
            {
                textList.Add(new FormattedLine("{Can be built on}:"));
                foreach (var terrain in improvement.TerrainsCanBeBuiltOn)
                    textList.Add(new FormattedLine(terrain, link: $"Terrain/{terrain}", indent: 1));
            }
        }

        var addedLineBeforeResourceBonus = false;
        foreach (var resource in ruleset.TileResources.Values)
        {
            if (resource.ImprovementStats == null || !resource.IsImprovedBy(improvement.Name)) continue;
            if (!addedLineBeforeResourceBonus)
            {
                addedLineBeforeResourceBonus = true;
                textList.Add(new FormattedLine());
            }
            var statsString = resource.ImprovementStats.ToString();
            // Line intentionally modeled as UniqueType.Stats + ConditionalInTiles
            textList.Add(new FormattedLine($"[{statsString}] <in [{resource.Name}] tiles>", link: resource.MakeLink()));
        }

        if (improvement.TechRequired != null)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine($"Required tech: [{improvement.TechRequired}]", link: $"Technology/{improvement.TechRequired}"));
        }

        improvement.UniquesToCivilopediaTextLines(textList, context);

        // Be clearer when one needs to chop down a Forest first... A "Can be built on Plains" is clear enough,
        // but a "Can be built on Land" is not - how is the user to know Forest is _not_ Land?
        if (creatorExists &&
            !improvement.IsEmpty() && // Has any Stats
            !((IHasUniques)improvement).HasUnique(UniqueType.NoFeatureRemovalNeeded) &&
            !((IHasUniques)improvement).HasUnique(UniqueType.RemovesFeaturesIfBuilt) &&
            !improvement.TerrainsCanBeBuiltOn.Any(it => ruleset.Terrains.ContainsKey(it)))
        {
            textList.Add(new FormattedLine("Needs removal of terrain features to be built"));
        }

        if (improvement.IsAncientRuinsEquivalent() && ruleset.RuinRewards.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("The possible rewards are:"));
            foreach (var reward in ruleset.RuinRewards.Values)
            {
                if (((IHasUniques)reward).IsHiddenFromCivilopedia(context.GameInfo, ruleset)) continue;
                textList.Add(new FormattedLine(reward.Name, starred: true, color: reward.Color));
                textList.AddRange(reward.CivilopediaText);
            }
        }

        if (creatorExists)
            textList.Add(new FormattedLine());
        foreach (var unit in constructorUnits)
            textList.Add(new FormattedLine($"{{Can be constructed by}} {{{unit.Name}}}", unit.MakeLink()));
        foreach (var unit in creatingUnits)
            textList.Add(new FormattedLine($"{{Can be created instantly by}} {{{unit.Name}}}", unit.MakeLink()));

        var seeAlso = new List<FormattedLine>();
        foreach (var alsoImprovement in ruleset.TileImprovements.Values)
        {
            if (alsoImprovement.Replaces == improvement.Name)
                seeAlso.Add(new FormattedLine(alsoImprovement.Name, link: alsoImprovement.MakeLink(), indent: 1));
        }

        seeAlso.AddRange(Belief.GetCivilopediaTextMatching(improvement.Name, ruleset, false, context.GameInfo));

        if (seeAlso.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{See also}:"));
            textList.AddRange(seeAlso);
        }

        return textList;
    }

    public static List<FormattedLine> GetCivilopediaTextLines(TileImprovement improvement, Ruleset ruleset) =>
        GetCivilopediaTextLines(improvement, new CivilopediaContext(ruleset));

    public static string GetDescription(TileImprovement improvement, Ruleset ruleset)
    {
        var lines = new List<string>();

        var statsDesc = improvement.CloneStats().ToString();
        if (!string.IsNullOrEmpty(statsDesc)) lines.Add(statsDesc);
        if (improvement.UniqueTo != null) lines.Add($"Unique to [{improvement.UniqueTo}]".Tr());
        if (improvement.Replaces != null) lines.Add($"Replaces [{improvement.Replaces}]".Tr());
        if (improvement.TerrainsCanBeBuiltOn.Count != 0)
        {
            var terrainsCanBeBuiltOnString = new List<string>();
            foreach (var i in improvement.TerrainsCanBeBuiltOn)
                terrainsCanBeBuiltOnString.Add(i.Tr());
            lines.Add("Can be built on".Tr() + " " + string.Join(", ", terrainsCanBeBuiltOnString)); // language can be changed when setting changes.
        }
        foreach (var resource in ruleset.TileResources.Values.Where(it => it.IsImprovedBy(improvement.Name)))
        {
            if (resource.ImprovementStats == null) continue;
            var statsString = resource.ImprovementStats.ToString();
            lines.Add($"[{statsString}] <in [{resource.Name}] tiles>".Tr());
        }
        if (improvement.TechRequired != null) lines.Add($"Required tech: [{improvement.TechRequired}]".Tr());

        improvement.UniquesToDescription(lines);

        return string.Join("\n", lines);
    }

    public static IEnumerable<FormattedLine> GetShortDescription(TileImprovement improvement, CivilopediaContext? context = null)
    {
        if (improvement.TerrainsCanBeBuiltOn.Count != 0)
        {
            var index = 0;
            foreach (var terrain in improvement.TerrainsCanBeBuiltOn)
            {
                yield return new FormattedLine(
                    index == 0 ? $"{{Can be built on}} {{{terrain}}}" : $"or [{terrain}]",
                    link: $"Terrain/{terrain}",
                    indent: index == 0 ? 1 : 2
                );
                index++;
            }
        }
        foreach (var unique in improvement.UniqueObjects)
        {
            if (unique.IsHiddenToUsers()) continue;
            yield return context != null
                ? new FormattedLine(unique, context, indent: 1)
                : new FormattedLine(unique, indent: 1);
        }
    }
}
