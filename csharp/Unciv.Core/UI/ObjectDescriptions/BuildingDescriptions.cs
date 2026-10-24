using System.Globalization;
using Unciv.Logic.City;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.UI.Components.Extensions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/BuildingDescriptions.kt

public static class BuildingDescriptions
{
    /// <summary>Used for AlertType.WonderBuilt, and as sub-text in Nation and Tech descriptions</summary>
    public static string GetShortDescription(Building building, bool multiline = false, Func<UniqueObject, bool>? uniqueInclusionFilter = null)
    {
        var infoList = new List<string>();
        var statsString = building.CloneStats().ToString();
        if (!string.IsNullOrEmpty(statsString)) infoList.Add(statsString);

        foreach (var (key, value) in building.GetStatPercentageBonuses(null))
            infoList.Add($"+{KotlinMath.FloatToInt(value)}% {key.ToString().Tr()}");

        if (building.RequiredNearbyImprovedResources != null)
            infoList.Add("Requires improved [" + string.Join("/", building.RequiredNearbyImprovedResources.Select(it => it.Tr())) + "] near city");

        if (building.Uniques.Count != 0)
        {
            if (!string.IsNullOrEmpty(building.ReplacementTextForUniques))
                infoList.Add(building.ReplacementTextForUniques);
            else
                infoList.AddRange(GetUniquesStringsWithoutDisablers(building, uniqueInclusionFilter));
        }

        if (building.CityStrength != 0.0) infoList.Add($"{{City strength}} +{building.CityStrength.ToString(CultureInfo.InvariantCulture)}");
        if (building.CityHealth != 0) infoList.Add($"{{City health}} +{building.CityHealth.ToString(CultureInfo.InvariantCulture)}");
        var separator = multiline ? "\n" : "; ";
        return string.Join(separator, infoList.Select(it => it.Tr()));
    }

    /// <summary>Used in CityScreen (ConstructionInfoTable)</summary>
    public static string GetDescription(Building building, City city, bool showAdditionalInfo)
    {
        var stats = building.GetStats(city);
        var translatedLines = new List<string>(); // Some translations require special handling
        var isFree = city.Civ.CivConstructions.HasFreeBuilding(city, building);
        if (building.UniqueTo != null)
            translatedLines.Add(building.Replaces == null ? $"Unique to [{building.UniqueTo}]".Tr() : $"Unique to [{building.UniqueTo}], replaces [{building.Replaces}]".Tr());
        if (building.IsWonder) translatedLines.Add("Wonder".Tr());
        if (building.IsNationalWonder) translatedLines.Add("National Wonder".Tr());
        if (!isFree)
        {
            // Consumes
            foreach (var (resourceName, amount) in building.GetResourceRequirementsPerTurn(city.State))
            {
                var available = showAdditionalInfo ? city.GetAvailableResourceAmount(resourceName) : -1;
                if (!city.GetRuleset().TileResources.TryGetValue(resourceName, out var resource)) continue;
                translatedLines.Add(resourceName.GetConsumesAmountString(amount, resource.IsStockpiled, available).Tr());
            }

            // Costs
            foreach (var (resourceName, amount) in ((IConstruction)building).GetStockpiledResourceRequirements(city.State))
            {
                if (!city.GetRuleset().TileResources.ContainsKey(resourceName)) continue;
                var available = showAdditionalInfo ? city.GetAvailableResourceAmount(resourceName) : -1;
                translatedLines.Add(resourceName.GetCostsAmountString(amount, available).Tr());
            }
        }

        if (building.Uniques.Count != 0)
        {
            if (!string.IsNullOrEmpty(building.ReplacementTextForUniques))
                translatedLines.Add(building.ReplacementTextForUniques.Tr());
            else
                translatedLines.AddRange(GetUniquesStringsWithoutDisablers(building, it =>
                    it.Type != UniqueType.ConsumesResources &&
                    it.Type != UniqueType.CostsResources
                ).Select(it => it.Tr()));
        }

        if (!stats.IsEmpty())
            translatedLines.Add(stats.ToString());

        foreach (var (stat, value) in building.GetStatPercentageBonuses(city))
            if (value != 0f) translatedLines.Add($"{KotlinMath.FloatToInt(value).ToStringSigned()}% {{{stat}}}".Tr());

        foreach (var (greatPersonName, value) in building.GreatPersonPoints)
            translatedLines.Add($"+{value} " + $"[{greatPersonName}] points".Tr());

        foreach (var (specialistName, amount) in building.NewSpecialists())
            translatedLines.Add($"+{amount} " + $"[{specialistName}] slots".Tr());

        if (building.RequiredNearbyImprovedResources != null)
            translatedLines.Add($"Requires improved [{string.Join("/", building.RequiredNearbyImprovedResources.Select(it => it.Tr()))}] near city".Tr());

        if (building.CityStrength != 0.0) translatedLines.Add($"{{City strength}} +{building.CityStrength.ToString(CultureInfo.InvariantCulture)}".Tr());
        if (building.CityHealth != 0) translatedLines.Add($"{{City health}} +{building.CityHealth.ToString(CultureInfo.InvariantCulture)}".Tr());
        if (building.Maintenance != 0 && !isFree) translatedLines.Add($"{{Maintenance cost}}: {building.Maintenance} {{Gold}}".Tr());
        if (showAdditionalInfo) AdditionalDescription(building, city, translatedLines);
        return string.Join("\n", translatedLines).Trim();
    }

    public static void AdditionalDescription(Building building, City city, List<string> lines)
    {
        // Inefficient in theory. In practice, buildings seem to have only a small handful of uniques.
        foreach (var unique in building.UniqueObjects)
        {
            if (unique.Type == UniqueType.OnlyAvailable || unique.Type == UniqueType.CanOnlyBeBuiltWhen)
                foreach (var conditional in unique.GetModifiers(UniqueType.ConditionalBuildingBuiltAll))
                {
                    MissingCityText(conditional.Params[0], city, conditional.Params[1], lines);
                }
        }
    }

    public static void MissingCityText(string building, City city, string filter, List<string> lines)
    {
        var missingCities = city.Civ.Cities.Where(it =>
            it.MatchesFilter(filter) && !it.CityConstructions.ContainsBuildingOrEquivalent(building)
        ).ToList();

        if (missingCities.Count != 0)
            lines.Add("\n" +
                $"[{city.Civ.GetEquivalentBuilding(building).Name}] required:".Tr() +
                " " + string.Join(", ", missingCities.Select(it => it.Name.Tr(hideIcons: true))));
    }

    /// <summary>
    /// Lists differences to a <see cref="IFormattedLineListBuilder"/>: how a nation-unique Building compares to its replacement.
    /// Cost is included.
    /// </summary>
    public static void AddBuildingDifferences(this IFormattedLineListBuilder b, Building originalBuilding, Building replacementBuilding)
    {
        foreach (var stat in StatExtensions.Entries) // Do not iterate on object since that excludes zero values
            if (replacementBuilding[stat] != originalBuilding[stat])
                b.Add(stat.ToString().Tr() + " " + $"[{KotlinMath.FloatToInt(replacementBuilding[stat])}] vs [{KotlinMath.FloatToInt(originalBuilding[stat])}]".Tr(), indent: 1);

        var originalStatBonus = originalBuilding.GetStatPercentageBonuses(null);
        var replacementStatBonus = replacementBuilding.GetStatPercentageBonuses(null);
        foreach (var stat in StatExtensions.Entries)
            if (replacementStatBonus[stat] != originalStatBonus[stat])
                b.Add($"[{KotlinMath.FloatToInt(replacementStatBonus[stat])}]% [{stat}] vs [{KotlinMath.FloatToInt(originalStatBonus[stat])}]% [{stat}]", indent: 1);

        if (replacementBuilding.Maintenance != originalBuilding.Maintenance)
            b.Add("{Maintenance} ".Tr() + $"[{replacementBuilding.Maintenance}] vs [{originalBuilding.Maintenance}]".Tr(), indent: 1);
        if (replacementBuilding.Cost != originalBuilding.Cost)
            b.Add("{Cost} ".Tr() + $"[{replacementBuilding.Cost}] vs [{originalBuilding.Cost}]".Tr(), indent: 1);
        if (replacementBuilding.CityStrength != originalBuilding.CityStrength)
            b.Add("{City strength} ".Tr() + $"[{replacementBuilding.CityStrength.ToString(CultureInfo.InvariantCulture)}] vs [{originalBuilding.CityStrength.ToString(CultureInfo.InvariantCulture)}]".Tr(), indent: 1);
        if (replacementBuilding.CityHealth != originalBuilding.CityHealth)
            b.Add("{City health} ".Tr() + $"[{replacementBuilding.CityHealth}] vs [{originalBuilding.CityHealth}]".Tr(), indent: 1);

        if (!string.IsNullOrEmpty(replacementBuilding.ReplacementTextForUniques))
        {
            b.Add(replacementBuilding.ReplacementTextForUniques, indent: 1);
        }
        else
        {
            foreach (var unique in replacementBuilding.UniqueObjects.Where(it => !originalBuilding.Uniques.Contains(it.Text) && !it.IsHiddenToUsers()))
                b.Add(unique.GetDisplayText(), indent: 1); // FormattedLine(unique) would look worse - no indent and autolinking could distract
        }

        foreach (var unique in originalBuilding.UniqueObjects.Where(it => !replacementBuilding.Uniques.Contains(it.Text) && !it.IsHiddenToUsers()))
        {
            // Need double translation of the "ability" here - unique texts may contain square brackets
            b.Add($"Lost ability (vs [{originalBuilding.Name}]): [{unique.Text.Tr()}]", indent: 1);
        }
    }

    public static List<FormattedLine> GetBuildingCivilopediaTextLines(this Building building, CivilopediaContext context) =>
        FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            var ruleset = context.Ruleset;

            static string FormatSignedInt(float value) => (value > 0f ? "+" : "") + KotlinMath.FloatToInt(value).Tr();

            if (building.IsAnyWonder())
            {
                b.Add(building.IsWonder ? "Wonder" : "National Wonder", color: "#CA4", header: 3);
            }

            if (building.UniqueTo != null)
            {
                b.Space();
                b.Add($"Unique to [{building.UniqueTo}]", link: $"Nation/{building.UniqueTo}");
                if (building.Replaces != null)
                {
                    var replacesBuilding = ruleset.Buildings.GetValueOrDefault(building.Replaces);
                    b.Add($"Replaces [{building.Replaces}]", link: replacesBuilding?.MakeLink() ?? "", indent: 1);
                }
            }

            if (building.Cost > 0)
            {
                var stats = new List<string> { $"{building.Cost}{Fonts.Production}" };
                if (building.CanBePurchasedWithStat(null, Stat.Gold))
                {
                    stats.Add($"{building.GetCivilopediaGoldCost()}{Fonts.Gold}");
                }
                b.Add("{Cost}: " + string.Join("/", stats));
            }

            if (building.RequiredTech != null)
                b.Add($"Required tech: [{building.RequiredTech}]", link: $"Technology/{building.RequiredTech}");

            if (building.RequiredBuilding != null)
            {
                var linkType = ruleset.Buildings.TryGetValue(building.RequiredBuilding, out var reqB) && reqB.IsWonder ? "Wonder" : "Building";
                b.Add(
                    $"Requires [{building.RequiredBuilding}] to be built in the city",
                    link: $"{linkType}/{building.RequiredBuilding}"
                );
            }

            if (building.RequiredResource != null)
            {
                b.Space();
                var resource = ruleset.TileResources.GetValueOrDefault(building.RequiredResource);
                b.Add(
                    building.RequiredResource.GetConsumesAmountString(1, resource?.IsStockpiled == true),
                    link: $"Resources/{building.RequiredResource}", color: "#F42");
            }

            var clonedStats = building.CloneStats();
            var percentStats = building.GetStatPercentageBonuses(null);
            var specialists = building.NewSpecialists();
            if (building.Uniques.Count != 0 || !clonedStats.IsEmpty() || !percentStats.IsEmpty() || building.GreatPersonPoints.Count != 0 || specialists.Count != 0)
                b.Space();

            if (!string.IsNullOrEmpty(building.ReplacementTextForUniques))
            {
                b.Add(building.ReplacementTextForUniques);
            }
            else
            {
                b.AddUniques(building, colorConsumesResources: true);
            }

            if (!clonedStats.IsEmpty())
            {
                b.Add(clonedStats.ToString());
            }

            if (!percentStats.IsEmpty())
            {
                foreach (var (key, value) in percentStats)
                {
                    if (value == 0f) continue;
                    b.Add(FormatSignedInt(value) + $"% {{{key}}}");
                }
            }

            foreach (var (greatPersonName, value) in building.GreatPersonPoints)
            {
                b.Add(
                    $"+{value} " + $"[{greatPersonName}] points".Tr(),
                    link: $"Unit/{greatPersonName}"
                );
            }

            if (specialists.Count != 0)
            {
                foreach (var (specialistName, amount) in specialists)
                    b.Add($"+{amount} " + $"[{specialistName}] slots".Tr());
            }

            if (building.RequiredNearbyImprovedResources != null)
            {
                b.Space();
                b.Add("Requires at least one of the following resources improved near the city:");
                foreach (var it in building.RequiredNearbyImprovedResources)
                {
                    b.Add(it, indent: 1, link: $"Resource/{it}");
                }
            }

            if (building.CityStrength != 0.0 || building.CityHealth != 0 || building.Maintenance != 0) b.Space();
            if (building.CityStrength != 0.0) b.Add($"{{City strength}} +{building.CityStrength.ToString(CultureInfo.InvariantCulture)}");
            if (building.CityHealth != 0) b.Add($"{{City health}} +{building.CityHealth.ToString(CultureInfo.InvariantCulture)}");
            if (building.Maintenance != 0) b.Add($"{{Maintenance cost}}: {building.Maintenance} {{Gold}}");

            bool FilterOtherBuilding(Building bld) => bld.Replaces == building.Name || ((IHasUniques)bld).HasUniquesMentioning(building.Name);
            var seeAlso = ruleset.Buildings.Values.Where(FilterOtherBuilding).Cast<IRulesetObject>()
                .Concat(Belief.GetBeliefsMatching(building.Name, ruleset, context.GameInfo));
            b.AddSeeAlso(seeAlso);
        });

    public static List<FormattedLine> GetBuildingCivilopediaTextLines(this Building building, Ruleset ruleset) =>
        GetBuildingCivilopediaTextLines(building, new CivilopediaContext(ruleset));

    private static IEnumerable<string> GetUniquesStrings(Building building, Func<UniqueObject, bool>? filterUniques = null)
    {
        var tileBonusMap = new Dictionary<string, List<string>>();
        foreach (var unique in building.UniqueObjects)
        {
            if (filterUniques != null && !filterUniques(unique)) continue;
            if (unique.Type == UniqueType.StatsFromTiles && unique.Params[2] == "in this city")
            {
                var stats = unique.Params[0];
                if (!tileBonusMap.TryGetValue(stats, out var list))
                {
                    list = new List<string>();
                    tileBonusMap[stats] = list;
                }
                list.Add(unique.Params[1]);
            }
            else
            {
                yield return unique.GetDisplayText();
            }
        }

        foreach (var (key, value) in tileBonusMap)
        {
            yield return "[stats] from [tileFilter] tiles in this city".FillPlaceholders(
                key,
                value.Count == 1 ? value[0] : string.Join(", ", value.Select(it => it.Tr()))
            );
        }
    }

    private static IEnumerable<string> GetUniquesStringsWithoutDisablers(Building building, Func<UniqueObject, bool>? filterUniques = null) =>
        GetUniquesStrings(building, it => !it.IsHiddenToUsers() && (filterUniques?.Invoke(it) ?? true));
}
