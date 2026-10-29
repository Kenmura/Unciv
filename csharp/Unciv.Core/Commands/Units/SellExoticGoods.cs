using Unciv.Logic.Trade.Routes;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// A unit that carries exotic goods (the Portuguese Nau) standing next to a city of another civilization sells one load there for gold and
/// experience (bnw-spec 4.12). Not in Kotlin. Rules and refusals: <see cref="ExoticGoods.Check"/>.
/// </summary>
[CommandType("sell-exotic-goods")]
public sealed class SellExoticGoods : GameCommand
{
    public int UnitId { get; init; }

    public string TargetCityId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        var target = TradeRouteRules.FindCity(context.Game, TargetCityId);
        if (target == null) return CommandValidation.Fail(CommandErrors.UnknownCity, $"There is no city \"{TargetCityId}\"");
        return ExoticGoods.Check(unit, target) is { } refusal ? CommandValidation.Fail(refusal.Code, refusal.Message) : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        ExoticGoods.Sell(unit, TradeRouteRules.FindCity(context.Game, TargetCityId).NotNull());
        return [];
    }
}
