using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Picks a promotion for a unit (T189). Kotlin: PromotionPickerScreen, which offers the promotions in
/// <c>unit.promotions.getAvailablePromotions()</c>, selectable when the unit can afford it or the promotion is free, then
/// <c>MapUnitView.tryAddPromotion</c>; <see cref="SaveAsDefault"/> is its "default promotions" button (<c>trySaveDefaultPromotions</c>,
/// needs the unit to stand in a city).
/// </summary>
[CommandType("promote-unit")]
public sealed class PromoteUnit : GameCommand
{
    public int UnitId { get; init; }
    public string Promotion { get; init; } = "";
    public bool SaveAsDefault { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!unit.HasMovement() || unit.AttacksThisTurn != 0 || !unit.Promotions.CanBePromoted())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit cannot be promoted now");
        var promotion = unit.Promotions.GetAvailablePromotions().FirstOrDefault(it => it.Name == Promotion);
        if (promotion == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{Promotion}\" is not an available promotion of this unit");
        if (!((IHasUniques)promotion).HasUnique(UniqueType.FreePromotion) && !unit.Promotions.CanAffordPromotions(1))
            return CommandValidation.Fail(CommandErrors.NotEnough("xp"), "The unit has not enough experience for a promotion");
        var city = unit.CurrentTile.GetCity();
        if (SaveAsDefault && (city == null || !ReferenceEquals(city.Civ, unit.Civ) || city.IsPuppet))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Default promotions can only be saved in one of your own, non-puppet cities");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        unit.Promotions.AddPromotion(Promotion);
        if (SaveAsDefault)
        {
            var city = unit.CurrentTile.GetCity().NotNull();
            city.UnitShouldUseSavedPromotion[unit.BaseUnit.Name] = true;
            city.UnitToPromotions[unit.BaseUnit.Name] = unit.Promotions;
        }
        return [];
    }
}
