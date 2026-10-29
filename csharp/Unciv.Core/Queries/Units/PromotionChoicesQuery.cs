using Unciv.Models.Ruleset.Unique;
using Unciv.UI.ObjectDescriptions;
using Unciv.View;

namespace Unciv.Queries.Units;

// Name: The promotion
// Selectable: PromoteUnit would accept it now (affordable or free, and the unit may be promoted now)
// Prerequisites: Promotions of which one must be adopted first
// Uniques: The promotion's effects as the player reads them (Unique.GetDisplayText, translated; uniques hidden from users left out)
public sealed record PromotionChoice(string Name, bool Selectable, IReadOnlyList<string> Prerequisites, IReadOnlyList<string> Uniques);

// Xp, XpForNextPromotion: The unit's experience and the cost of its next promotion
// CanBePromoted: Kotlin MapUnit.promotions.canBePromoted (the "promote" flag of the unit)
// CanSaveDefault: PromoteUnit.SaveAsDefault would be accepted
public sealed record PromotionChoicesResult(int UnitId, int Xp, int XpForNextPromotion, bool CanBePromoted, bool CanSaveDefault,
    IReadOnlyList<PromotionChoice> Promotions);

// T220: the promotion picker. Promote is a UI-only action in Kotlin, so UnitActionsQuery never lists it. Own units only.
[QueryType("units.PromotionChoices")]
public sealed record PromotionChoicesQuery(int UnitId) : IQuery<PromotionChoicesResult>
{
    public string Type => "units.PromotionChoices";

    public PromotionChoicesResult Run(GameView view)
    {
        var unitView = UnitQueryHelpers.OwnUnit(view, UnitId);
        var unit = unitView.Unwrap();
        bool mayPromote = unit.HasMovement() && unit.AttacksThisTurn == 0 && unit.Promotions.CanBePromoted();
        bool canAfford = unit.Promotions.CanAffordPromotions(1);
        var choices = mayPromote
            ? unit.Promotions.GetAvailablePromotions()
                .Select(it => new PromotionChoice(it.Name, ((IHasUniques)it).HasUnique(UniqueType.FreePromotion) || canAfford, it.Prerequisites.ToList(),
                    ((IHasUniques)it).UniquesToDescription().ToList()))
                .OrderBy(it => it.Name, StringComparer.Ordinal).ToList()
            : [];
        return new PromotionChoicesResult(UnitId, unitView.Xp, unitView.XpForNextPromotion(), unitView.CanBePromoted(), mayPromote && unitView.CanSaveDefaultPromotions(), choices);
    }
}

public sealed record PromotableUnitsResult(IReadOnlyList<int> UnitIds);

// T220: the viewer's units that PromoteUnit could promote now, ascending by id, so a client can show the promotion flag without asking per unit.
[QueryType("units.PromotableUnits")]
public sealed record PromotableUnitsQuery : IQuery<PromotableUnitsResult>
{
    public string Type => "units.PromotableUnits";

    public PromotableUnitsResult Run(GameView view) => new(view.CivView.GetUnits()
        .Where(it => it.Unwrap() is var unit && unit.HasMovement() && unit.AttacksThisTurn == 0 && it.CanBePromoted())
        .Select(it => it.Id).Order().ToList());
}
