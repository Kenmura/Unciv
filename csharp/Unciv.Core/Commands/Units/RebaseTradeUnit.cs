using Unciv.Logic.Trade.Routes;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// An idle trade unit standing in one of the actor's cities moves its home to another own city (bnw-spec 4.1.2 <c>ChangeTradeUnitHome</c>,
/// without the popup). Not in Kotlin. Rules and refusals: <see cref="TradeRouteRules.CheckRebase"/>; the unit has no movement left afterwards.
/// </summary>
[CommandType("rebase-trade-unit")]
public sealed class RebaseTradeUnit : GameCommand
{
    public int UnitId { get; init; }

    public string TargetCityId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        var target = TradeRouteRules.FindCity(context.Game, TargetCityId);
        if (target == null) return CommandValidation.Fail(CommandErrors.UnknownCity, $"There is no city \"{TargetCityId}\"");
        return TradeRouteRules.CheckRebase(unit, target) is { } refusal ? CommandValidation.Fail(refusal.Code, refusal.Message) : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        TradeRouteRules.Rebase(unit, TradeRouteRules.FindCity(context.Game, TargetCityId).NotNull());
        return [];
    }
}
