using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.UI.Screens.Civilopedia;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/TechnologyDescriptions.kt
// Not ported: getTechEnabledIcons, buildTechIconsIndex and TechIconsIndex (they build libGDX portrait Actors with ImageGetter for the TechPickerScreen: client code).
// Deferred to T129 (they call Building.getShortDescription, owned by T096): the "Buildings enabled" and "Wonders enabled" lines of getDescription and
// getCivilopediaTextLines. The helpers they need (getEnabledBuildings) are here.

public static class TechnologyDescriptions
{
    // region Methods called from Technology

    /// <summary>
    /// Textual description used in TechPickerScreen and AlertPopup(AlertType.TechResearched) -
    /// Civilization always known and description tailored to
    /// </summary>
    public static string GetDescription(Technology technology, Civilization viewingCiv)
    {
        var name = technology.Name;
        var ruleset = viewingCiv.GameInfo.Ruleset;
        var lineList = new List<string>(); // more readable than StringBuilder, with same performance for our use-case

        foreach (var pediaText in technology.CivilopediaText)
        {
            // This is explicitly to get the "Who knows what the future holds" of Future Tech back into
            // the Tech Picker and Tech Researched Alert display, without making it an untyped Unique.
            // May need tuning for mods, in vanilla there is just the one case.
            if (pediaText.Text.Length == 0 || pediaText.Header != 0) continue;
            lineList.Add(pediaText.Text);
        }

        technology.UniquesToDescription(lineList);

        lineList.AddRange(
            GetAffectedImprovements(name, ruleset)
                .Where(it => it.Improvement.UniqueTo == null || viewingCiv.MatchesFilter(it.Improvement.UniqueTo))
                .Select(it => it.GetText()));

        var enabledUnits = GetEnabledUnits(name, ruleset, viewingCiv).ToList();
        if (enabledUnits.Count != 0)
        {
            lineList.Add("{Units enabled}: ");
            foreach (var unit in enabledUnits)
                lineList.Add($" • {unit.Name.Tr()} ({unit.GetShortDescription(technology.UniqueIsRequirementForThisTech)})\n");
        }

        // TODO(T129): "{Buildings enabled}: " and "{Wonders enabled}: " lines go here (need Building.getShortDescription, T096)

        foreach (var obj in GetObsoletedObjects(name, ruleset, viewingCiv))
            lineList.Add($"[{obj.Name}] obsoleted");

        foreach (var resource in ruleset.TileResources.Values.Where(it => it.RevealedBy == name).Select(it => it.Name))
            lineList.Add($"Reveals [{resource}] on the map");

        var tileImprovements = ruleset.TileImprovements.Values
            .Where(it => it.TechRequired == name)
            .Where(it => it.UniqueTo == null || viewingCiv.MatchesFilter(it.UniqueTo))
            .ToList();
        if (tileImprovements.Count != 0)
            lineList.Add("{Tile improvements enabled}: " + string.Join(", ", tileImprovements.Select(it => it.Name.Tr())));

        return string.Join("\n", lineList.Select(it => it.Tr()));
    }

    /// <summary>Implementation of <see cref="ICivilopediaText.GetCivilopediaTextLines"/></summary>
    public static List<FormattedLine> GetCivilopediaTextLines(Technology technology, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var name = technology.Name;
        var lineList = new List<FormattedLine>();

        var eraColor = ruleset.GetEra(technology.Era())?.GetHexColor() ?? "";
        lineList.Add(new FormattedLine(technology.Era(), header: 3, color: eraColor));
        lineList.Add(new FormattedLine());
        lineList.Add(new FormattedLine($"{{Cost}}: {KotlinNumberText.Int(technology.Cost)}{Fonts.Science}"));

        if (technology.Prerequisites.Count != 0)
        {
            lineList.Add(new FormattedLine());
            if (technology.Prerequisites.Count == 1)
            {
                var prerequisite = technology.Prerequisites.First();
                lineList.Add(new FormattedLine($"Required tech: [{prerequisite}]", link: $"Technology/{prerequisite}"));
            }
            else
            {
                lineList.Add(new FormattedLine("Requires all of the following:"));
                foreach (var prerequisite in technology.Prerequisites)
                    lineList.Add(new FormattedLine(prerequisite, link: $"Technology/{prerequisite}"));
            }
        }

        var leadsTo = ruleset.Technologies.Values.Where(it => it.Prerequisites.Contains(name)).ToList();
        if (leadsTo.Count != 0)
        {
            lineList.Add(new FormattedLine());
            if (leadsTo.Count == 1)
                lineList.Add(new FormattedLine($"Leads to [{leadsTo[0].Name}]", link: leadsTo[0].MakeLink()));
            else
            {
                lineList.Add(new FormattedLine("Leads to:"));
                foreach (var it in leadsTo)
                    lineList.Add(new FormattedLine(it.Name, link: it.MakeLink()));
            }
        }

        technology.UniquesToCivilopediaTextLines(lineList, context);

        var affectedImprovements = GetAffectedImprovements(name, ruleset).ToList();
        if (affectedImprovements.Count != 0)
        {
            lineList.Add(new FormattedLine());
            foreach (var entry in affectedImprovements)
                lineList.Add(new FormattedLine(entry.GetText(), link: entry.Improvement.MakeLink()));
        }

        var enabledUnits = GetEnabledUnits(name, ruleset, null).ToList();
        if (enabledUnits.Count != 0)
        {
            lineList.Add(new FormattedLine());
            lineList.Add(new FormattedLine("{Units enabled}:"));
            foreach (var unit in enabledUnits)
                lineList.Add(new FormattedLine(unit.Name.Tr(hideIcons: true) + " (" + unit.GetShortDescription(technology.UniqueIsRequirementForThisTech) + ")", link: unit.MakeLink()));
        }

        // TODO(T129): the "{Wonders enabled}:" and "{Buildings enabled}:" sections go here (need Building.getShortDescription, T096)

        var obsoletedObjects = GetObsoletedObjects(name, ruleset, null).ToList();
        if (obsoletedObjects.Count != 0)
        {
            lineList.Add(new FormattedLine());
            foreach (var it in obsoletedObjects)
                lineList.Add(new FormattedLine($"[{it.Name}] obsoleted", link: ((ICivilopediaText)it).MakeLink()));
        }

        var revealedResources = ruleset.TileResources.Values.Where(it => it.RevealedBy == name).ToList();
        if (revealedResources.Count != 0)
        {
            lineList.Add(new FormattedLine());
            foreach (var it in revealedResources)
                lineList.Add(new FormattedLine($"Reveals [{it.Name}] on the map", link: it.MakeLink()));
        }

        var tileImprovements = ruleset.TileImprovements.Values.Where(it => it.TechRequired == name).ToList();
        if (tileImprovements.Count != 0)
        {
            lineList.Add(new FormattedLine());
            lineList.Add(new FormattedLine("{Tile improvements enabled}:"));
            foreach (var it in tileImprovements)
                lineList.Add(new FormattedLine(it.Name, link: it.MakeLink()));
        }

        var seeAlsoObjects = GetSeeAlsoObjects(technology, ruleset);
        if (seeAlsoObjects.Count != 0)
        {
            lineList.Add(new FormattedLine());
            lineList.Add(new FormattedLine("{See also}:"));
            foreach (var it in seeAlsoObjects)
                lineList.Add(new FormattedLine(it.Name, link: it.MakeLink()));
        }

        return lineList;
    }

    // endregion
    // region Helpers

    /// <summary>
    /// Returns the <see cref="Building"/>s enabled by this Technology, filtered for <paramref name="civInfo"/>'s uniques,
    /// nuclear weapons and religion settings, and without those expressly hidden from Civilopedia.
    /// </summary>
    // Used for Civilopedia, Alert and Picker, so if any of these decide to ignore the "Will not be displayed in Civilopedia" unique this needs refactoring
    // Used by the T129 sections only
    internal static IEnumerable<Building> GetEnabledBuildings(string techName, Ruleset ruleset, Civilization? civInfo, CivilopediaContext? context = null) =>
        GetFilteredBuildings(ruleset, civInfo, context, it => ((IHasUniques)it).RequiredTechs().Contains(techName));

    /// <summary>
    /// Returns the <see cref="RulesetStatsObject"/>s obsoleted by this Technology, filtered for <paramref name="civInfo"/>'s uniques,
    /// nuclear weapons and religion settings, and without those expressly hidden from Civilopedia.
    /// </summary>
    // Used for Civilopedia, Alert and Picker, so if any of these decide to ignore the "Will not be displayed in Civilopedia" unique this needs refactoring
    private static IEnumerable<RulesetStatsObject> GetObsoletedObjects(string techName, Ruleset ruleset, Civilization? civInfo) =>
        GetFilteredBuildings(ruleset, civInfo, null, _ => true).Cast<RulesetStatsObject>()
            .Concat(ruleset.TileResources.Values)
            .Concat(ruleset.TileImprovements.Values.Where(it => it.UniqueTo == null || civInfo?.MatchesFilter(it.UniqueTo) == true))
            .Where(obj => ((IHasUniques)obj).GetMatchingUniques(UniqueType.ObsoleteWith).Any(it => it.Params[0] == techName));

    /// <summary>Common filtering for both <see cref="GetEnabledBuildings"/> and <see cref="GetObsoletedObjects"/>, difference via predicate parameter</summary>
    private static IEnumerable<Building> GetFilteredBuildings(Ruleset ruleset, Civilization? civInfo, CivilopediaContext? context, Func<Building, bool> predicate) =>
        ruleset.Buildings.Values.Where(it =>
            predicate(it) // expected to be the most selective, thus tested first
            && ((it.UniqueTo != null && civInfo?.MatchesFilter(it.UniqueTo) == true)
                || (it.UniqueTo == null && (civInfo == null || civInfo.GetEquivalentBuilding(it) == it)))
            && !((IHasUniques)it).IsHiddenFromCivilopedia(context?.GameInfo ?? civInfo?.GameInfo, ruleset));

    /// <summary>
    /// Returns the <see cref="BaseUnit"/>s enabled by this Technology, filtered for <paramref name="civInfo"/>'s uniques,
    /// nuclear weapons and religion settings, and without those expressly hidden from Civilopedia.
    /// </summary>
    // Used for Civilopedia, Alert and Picker, so if any of these decide to ignore the "Will not be displayed in Civilopedia"/HiddenFromCivilopedia unique this needs refactoring
    private static IEnumerable<BaseUnit> GetEnabledUnits(string techName, Ruleset ruleset, Civilization? civInfo) =>
        ruleset.Units.Values.Where(it =>
            ((IHasUniques)it).RequiredTechs().Contains(techName)
            && ((it.UniqueTo != null && civInfo?.MatchesFilter(it.UniqueTo) == true)
                || (it.UniqueTo == null && (civInfo == null || civInfo.GetEquivalentUnit(it) == it)))
            && !((IHasUniques)it).IsHiddenFromCivilopedia(civInfo?.GameInfo, ruleset));

    /// <summary>Tests whether a Unique means bonus Stats enabled by <paramref name="techName"/></summary>
    private static bool IsImprovementStatsEnabledByTech(UniqueObject unique, string techName) =>
        (unique.Type == UniqueType.Stats || unique.Type == UniqueType.ImprovementStatsOnTile)
        && unique.GetModifiers(UniqueType.ConditionalTech).Any(it => it.Params[0] == techName);

    /// <summary>Tests whether a Unique Conditional is enabling or disabling its parent by a tech</summary>
    private static bool IsTechConditional(UniqueObject unique) =>
        unique.Type == UniqueType.ConditionalTech || unique.Type == UniqueType.ConditionalNoTech;

    /// <summary>Tests whether a Unique is enabled or disabled by <paramref name="techName"/></summary>
    private static bool IsRelatedToTech(UniqueObject unique, string techName) =>
        unique.Modifiers.Any(it => IsTechConditional(it) && it.Params[0] == techName);

    /// <summary>Used by <see cref="GetAffectedImprovements"/> only</summary>
    private sealed record ImprovementAndUnique(TileImprovement Improvement, UniqueObject Unique)
    {
        public string GetText() => $"[{Unique.Params[0]}] from every [{Improvement.Name}]" +
            (Unique.Type == UniqueType.Stats ? "" : $" on [{Unique.Params[1]}] tiles");
    }

    /// <summary>Yields Improvements with bonus Stats enabled by <paramref name="techName"/> including the Unique doing it</summary>
    private static IEnumerable<ImprovementAndUnique> GetAffectedImprovements(string techName, Ruleset ruleset) =>
        ruleset.TileImprovements.Values.SelectMany(improvement =>
            improvement.UniqueObjects
                .Where(it => IsImprovementStatsEnabledByTech(it, techName))
                .Select(it => new ImprovementAndUnique(improvement, it)));

    /// <summary>
    /// Get other objects related to this tech by a unique,
    /// e.g. Petra/Archaeology or Work Boats/Astronomy
    /// but not including the Improvement Stats increases already listed
    /// </summary>
    private static List<IRulesetObject> GetSeeAlsoObjects(Technology technology, Ruleset ruleset) =>
        ruleset.AllRulesetObjects()
            .Where(rulesetObject => rulesetObject.UniqueObjects.Any(it =>
                IsRelatedToTech(it, technology.Name) && !IsImprovementStatsEnabledByTech(it, technology.Name)))
            .ToList();

    // endregion
}
