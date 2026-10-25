namespace Unciv.Commands.City;

/// <summary>
/// Sell a building for a tenth of its cost. Kotlin: <c>ConstructionInfoTable.showSellButton</c> enabled when the building <c>isSellable</c>, is
/// built, is not a free building, the city is no puppet and no building was sold this turn (unless god mode); then <c>CityView.trySellBuilding</c>.
/// </summary>
[CommandType("sell-building")]
public sealed class SellBuilding : GameCommand
{
    public string CityId { get; init; } = "";
    public string Building { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out var actor, out var city, out var failure)) return failure;
        if (!city.GetRuleset().Buildings.TryGetValue(Building, out var building) || !city.CityConstructions.IsBuilt(Building))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{city.Name} has not built \"{Building}\"");
        if (!building.IsSellable() || actor.CivConstructions.HasFreeBuilding(city, building))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Building} cannot be sold");
        return city.HasSoldBuildingThisTurn && !actor.GameInfo.GameParameters.GodMode
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} has already sold a building this turn")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).SellBuilding(Building);
        return [];
    }
}
