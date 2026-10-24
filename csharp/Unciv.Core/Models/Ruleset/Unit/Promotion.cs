using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/Promotion.kt
// Not ported yet: getDescription (translation types, T034).
public class Promotion : RulesetObject, ICivilopediaText
{
    public override string MakeLink() => $"Promotion/{Name}";

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var textList = new List<FormattedLine>();

        this.UniquesToCivilopediaTextLines(textList, context, noLeadingSeparator: true);

        var filteredPrerequisites = Prerequisites
            .Select(it => ruleset.UnitPromotions.TryGetValue(it, out var promotion) ? promotion : null)
            .OfType<Promotion>().ToList();
        if (filteredPrerequisites.Count != 0)
        {
            textList.Add(new FormattedLine());
            if (filteredPrerequisites.Count == 1)
            {
                var it = filteredPrerequisites[0];
                textList.Add(new FormattedLine($"Requires [{it.Name}]", link: it.MakeLink()));
            }
            else
            {
                textList.Add(new FormattedLine("Requires at least one of the following:"));
                foreach (var it in filteredPrerequisites)
                    textList.Add(new FormattedLine(it.Name, link: it.MakeLink()));
            }
        }

        if (UnitTypes.Count != 0)
        {
            textList.Add(new FormattedLine());
            // This separates the linkable (corresponding to a BaseUnit name) unitFilter entries
            // from the others - `first` collects those for which the predicate is `true`.
            var baseUnitNames = UnitTypes.Where(it => ruleset.Units.ContainsKey(it)).ToList();
            var otherNames = UnitTypes.Where(it => !ruleset.Units.ContainsKey(it)).ToList();
            if (UnitTypes.Count == 1)
            {
                var it = UnitTypes[0];
                if (baseUnitNames.Count != 0)
                    textList.Add(new FormattedLine($"Available for [{it}]", link: $"Unit/{it}"));
                else
                    textList.Add(new FormattedLine($"Available for [{it}]", link: $"UnitType/{it}"));
            }
            else
            {
                textList.Add(new FormattedLine("Available for:"));
                foreach (var it in baseUnitNames)
                    textList.Add(new FormattedLine(it, indent: 1, link: $"Unit/{it}"));
                foreach (var it in otherNames)
                    textList.Add(new FormattedLine(it, indent: 1, link: $"UnitType/{it}"));
            }
        }

        var freeForUnits = ruleset.Units.Where(it => it.Value.Promotions.Contains(Name)).Select(it => it.Key).ToList();
        if (freeForUnits.Count != 0)
        {
            textList.Add(new FormattedLine());
            if (freeForUnits.Count == 1)
            {
                var it = freeForUnits[0];
                textList.Add(new FormattedLine($"Free for [{it}]", link: $"Unit/{it}"));
            }
            else
            {
                textList.Add(new FormattedLine("Free for:"));
                foreach (var it in freeForUnits)
                    textList.Add(new FormattedLine(it, link: $"Unit/{it}"));
            }
        }

        var grantors = ruleset.Buildings.Values.Cast<IRulesetObject>()
            .Where(building => building.GetMatchingUniques(UniqueType.UnitStartingPromotions).Any(it => it.Params[2] == Name))
            .Concat(ruleset.Terrains.Values.Cast<IRulesetObject>()
                .Where(terrain => terrain.GetMatchingUniques(UniqueType.TerrainGrantsPromotion).Any(it => Name == it.Params[0])))
            .ToList();
        if (grantors.Count != 0)
        {
            textList.Add(new FormattedLine());
            if (grantors.Count == 1)
            {
                var it = grantors[0];
                textList.Add(new FormattedLine($"Granted by [{it.Name}]", link: it.MakeLink()));
            }
            else
            {
                textList.Add(new FormattedLine("Granted by:"));
                foreach (var it in grantors)
                    textList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
            }
        }

        return textList;
    }

    public string? GetSubCategory(Ruleset ruleset) => UnitTypes.Count != 0 ? UnitTypes[0] : "Other";

    public int GetSortGroup(Ruleset ruleset)
    {
        if (UnitTypes.Count == 0) return 1000;
        var unitTypeName = UnitTypes[0];
        if (!ruleset.UnitTypes.ContainsKey(unitTypeName)) return 1000;
        return ruleset.UnitTypes.Keys.ToList().IndexOf(unitTypeName);
    }

    public List<string> Prerequisites { get; set; } = [];

    /// <summary>The json parser wouldn't agree to deserialize this as a list of UnitTypes. =(</summary>
    public List<string> UnitTypes { get; set; } = [];

    public List<int>? InnerColor { get; set; }
    public List<int>? OuterColor { get; set; }

    /// <summary>
    /// Used as **column** hint in the current PromotionPickerScreen.
    /// This is no longer a direct position, it is used to sort before an automatic distribution.
    /// -1 determines that the modder has not set a position
    /// </summary>
    public int Row { get; set; } = -1;

    /// <summary>
    /// Used as **row** hint in the current PromotionPickerScreen.
    /// This is no longer a direct position, it is used to sort before an automatic distribution.
    /// </summary>
    public int Column { get; set; }

    public EngineColor? GetInnerColorObject() => InnerColor == null ? null : ColorExtensions.ColorFromRGB(InnerColor);
    public EngineColor? GetOuterColorObject() => OuterColor == null ? null : ColorExtensions.ColorFromRGB(OuterColor);

    public Promotion Clone() => new()
    {
        // RulesetObject fields
        Name = Name,
        Uniques = Uniques,

        // Promotion fields
        Prerequisites = Prerequisites,
        UnitTypes = UnitTypes,
        Row = Row,
        Column = Column,
    };

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Promotion;

    public readonly record struct PromotionBaseNameAndLevel(string NameWithoutBrackets, int Level, string BasePromotionName);

    /// <summary>
    /// Split a promotion name into base and level, e.g. "Drill II" -> 2 to "Drill".
    /// Used by Portrait (where it only has the string, the Promotion object is forgotten) and PromotionPickerScreen.
    /// </summary>
    public static PromotionBaseNameAndLevel GetBaseNameAndLevel(string promotionName)
    {
        var nameWithoutBrackets = promotionName.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);
        var level = nameWithoutBrackets.EndsWith(" I", StringComparison.Ordinal) ? 1
            : nameWithoutBrackets.EndsWith(" II", StringComparison.Ordinal) ? 2
            : nameWithoutBrackets.EndsWith(" III", StringComparison.Ordinal) ? 3
            : 0;
        // Kotlin dropLast(n) never throws for n > length
        var drop = level == 0 ? 0 : level + 1;
        return new PromotionBaseNameAndLevel(nameWithoutBrackets, level,
            nameWithoutBrackets[..Math.Max(0, nameWithoutBrackets.Length - drop)]);
    }
}
