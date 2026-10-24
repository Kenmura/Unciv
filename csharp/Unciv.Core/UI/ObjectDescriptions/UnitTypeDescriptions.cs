using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/BaseUnitDescriptions.kt, only UnitType.getUnitTypeCivilopediaTextLines (the rest of the file belongs to the BaseUnit descriptions).

public static class UnitTypeDescriptions
{
    public static List<FormattedLine> GetUnitTypeCivilopediaTextLines(this UnitType unitType, CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        return FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            void GetDomainLines()
            {
                b.Add("{Unit types}:", header: 4);
                var myMovementType = unitType.GetMovementType();
                foreach (var other in ruleset.UnitTypes.Values)
                {
                    if (other.GetMovementType() != myMovementType) continue;
                    if (!other.IsUsed(ruleset)) continue;
                    b.Add(other.Name, other.MakeLink());
                }
            }

            void GetUnitTypeLines()
            {
                if (unitType.GetMovementType() is { } movementType)
                {
                    var color = movementType switch
                    {
                        UnitMovementType.Land => "#ffc080",
                        UnitMovementType.Water => "#80d0ff",
                        UnitMovementType.Air => "#e0e0ff",
                        _ => throw new ArgumentOutOfRangeException(),
                    };
                    b.Add($"Domain: [{movementType}]", link: $"UnitType/Domain: [{movementType}]", color: color);
                    b.Separator();
                }
                b.Add("Units:", header: 4);
                foreach (var unit in ruleset.Units.Values)
                {
                    if (unit.UnitType != unitType.Name) continue;
                    b.Add(unit.Name, unit.MakeLink());
                }

                var relevantPromotions = ruleset.UnitPromotions.Values.Where(it => it.UnitTypes.Contains(unitType.Name)).ToList();
                if (relevantPromotions.Count != 0)
                {
                    b.Add("Promotions", header: 4);
                    foreach (var promotion in relevantPromotions)
                        b.Add(promotion.Name, promotion.MakeLink());
                }

                b.AddUniques(unitType, SeparatorType.Line);
            }

            if (unitType.Name.StartsWith("Domain: ", StringComparison.Ordinal)) GetDomainLines(); else GetUnitTypeLines();
        });
    }
}
