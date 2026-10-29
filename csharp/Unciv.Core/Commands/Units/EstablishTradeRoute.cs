using Unciv.Logic;
using Unciv.Logic.Trade.Routes;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// A trade unit standing in one of the actor's cities starts a trade route to <see cref="TargetCityId"/> (bnw-spec 2.4 and 4.1.6 step 1).
/// Not in Kotlin. <see cref="Kind"/> is <c>International</c> for another civilization's city, <c>Food</c> or <c>Production</c> for an own city.
/// The unit leaves the map; it comes back to the origin city when the route completes. Rules and refusals: <see cref="TradeRouteRules.CheckUnit"/>.
/// </summary>
[CommandType("establish-trade-route")]
public sealed class EstablishTradeRoute : GameCommand
{
    public int UnitId { get; init; }

    public string TargetCityId { get; init; } = "";

    public TradeRouteKind Kind { get; init; } = TradeRouteKind.International;

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        var target = context.Game.GetCities().FirstOrDefault(it => it.Id == TargetCityId);
        if (target == null) return CommandValidation.Fail(CommandErrors.UnknownCity, $"There is no city \"{TargetCityId}\"");
        return TradeRouteRules.CheckUnit(unit, target, Kind, out _) is { } refusal
            ? CommandValidation.Fail(refusal.Code, refusal.Message)
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        var target = context.Game.GetCities().First(it => it.Id == TargetCityId);
        TradeRouteRules.CheckUnit(unit, target, Kind, out var plan);
        TradeRouteRules.Establish(unit, plan.NotNull());
        return [];
    }
}
