using Unciv.Logic.City;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.UI.Components.Extensions;
using Unciv.UI.Screens.Civilopedia;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/BaseUnitDescriptions.kt (UnitType.getUnitTypeCivilopediaTextLines is in UnitTypeDescriptions.cs).
// Not ported: `addPixelUnitImage` (reads the client's unitSet / pediaUnitArtSize settings and ImageGetter.imageExists: client concern, so the Civilopedia
// text has no pixel-art image line) and `getUpgradeInfoTable` (builds a libGDX Table with MarkupRenderer; the lines it renders are `GetDifferences` below).

public static class BaseUnitDescriptions
{
    /// <summary>Generate short description as comma-separated string for Technology description "Units enabled" and GreatPersonPickerScreen</summary>
    public static string GetShortDescription(BaseUnit baseUnit, Func<UniqueObject, bool>? uniqueExclusionFilter = null)
    {
        var infoList = new List<string>();
        if (baseUnit.Strength != 0) infoList.Add($"{baseUnit.Strength.Tr()}{Fonts.Strength}");
        if (baseUnit.RangedStrength != 0) infoList.Add($"{baseUnit.RangedStrength.Tr()}{Fonts.RangedStrength}");
        if (baseUnit.Movement != 2) infoList.Add($"{baseUnit.Movement.Tr()}{Fonts.Movement}");
        foreach (var promotion in baseUnit.Promotions)
            infoList.Add(promotion.Tr());
        if (baseUnit.ReplacementTextForUniques != "") infoList.Add(baseUnit.ReplacementTextForUniques);
        else baseUnit.UniquesToDescription(infoList, uniqueExclusionFilter);
        return string.Join(", ", infoList);
    }

    /// <summary>Generate description as multi-line string for CityScreen addSelectedConstructionTable</summary>
    /// <param name="baseUnit">The unit</param>
    /// <param name="city">Supplies civInfo to show available resources after resource requirements</param>
    public static string GetDescription(BaseUnit baseUnit, City city)
    {
        var lines = new List<string>();
        var availableResources = city.Civ.GetCivResourcesByName();
        var tileResources = city.Civ.GameInfo.Ruleset.TileResources;

        // Consumes
        foreach (var (resourceName, amount) in baseUnit.GetResourceRequirementsPerTurn(city.Civ.State))
        {
            var available = availableResources.TryGetValue(resourceName, out var availableAmount) ? availableAmount : 0;
            if (!tileResources.TryGetValue(resourceName, out var resource)) continue;
            lines.Add(resourceName.GetConsumesAmountString(amount, resource.IsStockpiled, available).Tr());
        }

        // Costs
        foreach (var (resourceName, amount) in ((IConstruction)baseUnit).GetStockpiledResourceRequirements(city.Civ.State))
        {
            var available = city.GetAvailableResourceAmount(resourceName);
            if (!tileResources.ContainsKey(resourceName)) continue;
            lines.Add(resourceName.GetCostsAmountString(amount, available).Tr());
        }

        var strengthLine = "";
        if (baseUnit.Strength != 0)
        {
            strengthLine += $"{KotlinNumberText.Int(baseUnit.Strength)}{Fonts.Strength}, ";
            if (baseUnit.RangedStrength != 0)
                strengthLine += $"{KotlinNumberText.Int(baseUnit.RangedStrength)}{Fonts.RangedStrength}, {KotlinNumberText.Int(baseUnit.Range)}{Fonts.Range}, ";
        }
        lines.Add($"{strengthLine}{KotlinNumberText.Int(baseUnit.Movement)}{Fonts.Movement}");

        if (baseUnit.ReplacementTextForUniques != "") lines.Add(baseUnit.ReplacementTextForUniques);
        else
        {
            baseUnit.UniquesToDescription(lines, it =>
                it.Type == UniqueType.Unbuildable
                // Already displayed in the resource requirements
                || it.Type == UniqueType.ConsumesResources
                || it.Type == UniqueType.CostsResources);
        }

        if (baseUnit.Promotions.Count != 0)
        {
            var prefix = "Free promotion" + (baseUnit.Promotions.Count == 1 ? "" : "s") + ":";
            lines.Add(prefix.Tr() + " " + string.Join(", ", baseUnit.Promotions.Select(it => it.Tr())));
        }

        return string.Join("\n", lines);
    }

    public static List<FormattedLine> GetBaseUnitCivilopediaTextLines(this BaseUnit unit, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        return FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            // Don't call baseUnit.getType() here - coming from the main menu baseUnit isn't fully initialized
            var unitTypeObject = ruleset.UnitTypes.GetValueOrDefault(unit.UnitType);
            var unitTypeLink = unitTypeObject?.MakeLink() ?? "";
            b.Add($"{{Unit type}}: {{{unit.UnitType}}}", unitTypeLink);

            var stats = new List<string>();
            if (unit.Strength != 0) stats.Add($"{KotlinNumberText.Int(unit.Strength)}{Fonts.Strength}");
            if (unit.RangedStrength != 0)
            {
                stats.Add($"{KotlinNumberText.Int(unit.RangedStrength)}{Fonts.RangedStrength}");
                stats.Add($"{KotlinNumberText.Int(unit.Range)}{Fonts.Range}");
            }
            if (unit.Movement != 0 && unitTypeObject?.IsAirUnit() != true)
                stats.Add($"{KotlinNumberText.Int(unit.Movement)}{Fonts.Movement}");
            if (stats.Count != 0)
                b.Add(string.Join(", ", stats));

            if (unit.Cost > 0)
            {
                stats.Clear();
                stats.Add($"{KotlinNumberText.Int(unit.Cost)}{Fonts.Production}");
                if (unit.CanBePurchasedWithStat(null, Stat.Gold))
                    stats.Add($"{KotlinNumberText.Int(((INonPerpetualConstruction)unit).GetCivilopediaGoldCost())}{Fonts.Gold}");
                b.Add("{Cost}: " + string.Join("/", stats));
            }

            if (unit.InterceptRange > 0)
                b.Add($"Air Intercept Range: [{KotlinNumberText.Int(unit.InterceptRange)}]");

            if (unit.ReplacementTextForUniques.Length != 0)
            {
                b.Space();
                b.Add(unit.ReplacementTextForUniques);
            }
            else
            {
                b.AddUniques(unit, colorConsumesResources: true);
            }

            if (unit.RequiredResource != null)
            {
                b.Space();
                var resource = ruleset.TileResources[unit.RequiredResource];
                b.Add(
                    unit.RequiredResource.GetConsumesAmountString(1, resource.IsStockpiled),
                    link: $"Resources/{unit.RequiredResource}", color: "#F42");
            }

            if (unit.UniqueTo != null)
            {
                b.Space();
                b.Add($"Unique to [{unit.UniqueTo}]", link: $"Nation/{unit.UniqueTo}");
                if (unit.Replaces != null)
                    b.Add($"Replaces [{unit.Replaces}]", link: $"Unit/{unit.Replaces}", indent: 1);
            }

            if (unit.RequiredTech != null || unit.UpgradesTo != null || unit.ObsoleteTech != null)
                b.Space();
            if (unit.RequiredTech != null)
                b.Add($"Required tech: [{unit.RequiredTech}]", link: $"Technology/{unit.RequiredTech}");

            var canUpgradeFrom = ruleset.Units
                .Where(it =>
                    (it.Value.UpgradesTo == unit.Name || (it.Value.UpgradesTo != null && it.Value.UpgradesTo == unit.Replaces))
                    && (it.Value.UniqueTo == null || it.Value.UniqueTo == unit.UniqueTo))
                .Select(it => it.Key)
                .ToList();
            if (canUpgradeFrom.Count != 0)
            {
                if (canUpgradeFrom.Count == 1)
                    b.Add($"Can upgrade from [{canUpgradeFrom[0]}]", link: $"Unit/{canUpgradeFrom[0]}");
                else
                {
                    b.Space();
                    b.Add("Can upgrade from:");
                    foreach (var unitName in canUpgradeFrom.Order(StringComparer.Ordinal))
                        b.Add(unitName, link: $"Unit/{unitName}", indent: 2);
                    b.Space();
                }
            }

            if (unit.UpgradesTo != null)
                b.Add($"Upgrades to [{unit.UpgradesTo}]", link: $"Unit/{unit.UpgradesTo}");
            if (unit.ObsoleteTech != null)
                b.Add($"Obsolete with [{unit.ObsoleteTech}]", link: $"Technology/{unit.ObsoleteTech}");

            if (unit.Promotions.Count != 0)
            {
                b.Space();
                var index = 0;
                foreach (var promotion in unit.Promotions)
                {
                    var prefix = unit.Promotions.Count == 1 ? "{Free promotion:} "
                        : index == 0 ? "{Free promotions:} "
                        : "";
                    b.Add(
                        prefix + "{" + promotion.Tr() + "}" + // tr() not redundant as promotion names now can use []
                        (unit.Promotions.Count == 1 || index == unit.Promotions.Count - 1 ? "" : ","),
                        link: $"Promotions/{promotion}",
                        indent: index == 0 ? 0 : 1);
                    index++;
                }
            }

            var seeAlso = ruleset.Units.Values.Where(it => it.Replaces == unit.Name || ((IHasUniques)it).HasUniquesMentioning(unit.Name));
            b.AddSeeAlso(seeAlso);
        });
    }

    /// <summary>
    /// Lists differences e.g. for help on an upgrade, or how a nation-unique compares to its replacement to a FormattedLineListBuilder.
    /// Cost is <b>not</b> included.
    /// </summary>
    /// <param name="b">The builder</param>
    /// <param name="ruleset">The ruleset</param>
    /// <param name="originalUnit">The "older" unit</param>
    /// <param name="betterUnit">The "newer" unit</param>
    /// <param name="linked">Whether the lines link (rather than only show an icon)</param>
    /// <param name="indent">Indentation of the lines</param>
    public static void AddUnitDifferences(this IFormattedLineListBuilder b, Ruleset ruleset, BaseUnit originalUnit, BaseUnit betterUnit, bool linked = false, int indent = 0)
    {
        foreach (var (text, link) in GetDifferences(ruleset, originalUnit, betterUnit))
        {
            if (link == null) b.Add(text, indent: indent);
            else if (linked) b.Add(text, link: link, indent: indent);
            else b.Add(text, icon: link, indent: indent);
        }
    }

    private static IEnumerable<(string Text, string? Link)> GetDifferences(Ruleset ruleset, BaseUnit originalUnit, BaseUnit betterUnit)
    {
        static string N(int value) => KotlinNumberText.Int(value);

        if (betterUnit.Strength != originalUnit.Strength)
            yield return ($"{Fonts.Strength} {{[{N(betterUnit.Strength)}] vs [{N(originalUnit.Strength)}]}}", null);

        if (betterUnit.RangedStrength > 0 && originalUnit.RangedStrength == 0)
            yield return ($"[Gained] {Fonts.RangedStrength} [{N(betterUnit.RangedStrength)}] {Fonts.Range} [{N(betterUnit.Range)}]", null);
        else if (betterUnit.RangedStrength == 0 && originalUnit.RangedStrength > 0)
            yield return ($"[Lost] {Fonts.RangedStrength} [{N(originalUnit.RangedStrength)}] {Fonts.Range} [{N(originalUnit.Range)}]", null);
        else
        {
            if (betterUnit.RangedStrength != originalUnit.RangedStrength)
                yield return ($"{Fonts.RangedStrength} " + $"{{[{N(betterUnit.RangedStrength)}] vs [{N(originalUnit.RangedStrength)}]}}", null);
            if (betterUnit.Range != originalUnit.Range)
                yield return ($"{Fonts.Range} {{[{N(betterUnit.Range)}] vs [{N(originalUnit.Range)}]}}", null);
        }

        if (betterUnit.Movement != originalUnit.Movement)
            yield return ($"{Fonts.Movement} {{[{N(betterUnit.Movement)}] vs [{N(originalUnit.Movement)}]}}", null);

        foreach (var (resource, _) in originalUnit.GetResourceRequirementsPerTurn(GameContext.IgnoreConditionalsState))
        {
            if (!betterUnit.GetResourceRequirementsPerTurn(GameContext.IgnoreConditionalsState).ContainsKey(resource))
                yield return ($"[{resource}] not required", $"Resource/{resource}");
        }
        // We return the unique text directly, so Nation.getUniqueUnitsText will not use the
        // auto-linking FormattedLine(Unique) - two reasons in favor:
        // Would look a little chaotic as unit uniques unlike most uniques are a HashSet and thus do not preserve order
        // No (Unique, all other val's) constructor on FormattedLine
        if (betterUnit.ReplacementTextForUniques.Length != 0)
        {
            yield return (betterUnit.ReplacementTextForUniques, null);
        }
        else
        {
            bool NewAbility(UniqueObject it) => originalUnit.Uniques.Contains(it.Text) || it.IsHiddenToUsers();
            foreach (var unique in betterUnit.UniqueObjects.Where(it => !NewAbility(it)))
                yield return (unique.Text, null);
        }

        bool LostAbility(UniqueObject it) => betterUnit.Uniques.Contains(it.Text) || it.IsHiddenToUsers();
        foreach (var unique in originalUnit.UniqueObjects.Where(it => !LostAbility(it)))
        {
            // Need double translation of the "ability" here - unique texts may contain nuts - pardon, square brackets
            yield return ($"Lost ability (vs [{originalUnit.Name}]): [{unique.GetDisplayText().Tr()}]", null);
        }
        foreach (var promotionName in betterUnit.Promotions.Where(it => !originalUnit.Promotions.Contains(it)))
        {
            var promotion = ruleset.UnitPromotions[promotionName];
            var effects = string.Join(", ", promotion.UniquesToDescription());
            yield return ($"{{{promotionName}}} ({effects})", promotion.MakeLink());
        }
    }
}
