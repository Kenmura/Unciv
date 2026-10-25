using Unciv.Logic.Map;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Buy a tile for gold. Kotlin: <c>CityScreen.askToBuyTile</c> (<c>canBuyTile</c> and <c>hasStatToBuy(Gold, getGoldCostOfTile)</c>), then
/// <c>CityView.tryBuyTile</c>. The cost is computed here.
/// </summary>
[CommandType("buy-tile")]
public sealed class BuyTile : GameCommand
{
    public string CityId { get; init; } = "";
    public HexCoord Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: true, out var actor, out var city, out var failure)) return failure;
        if (!context.TileAt(Tile).TryGet(out var tile, out failure)) return failure;
        if (!city.Expansion.CanBuyTile(tile))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{city.Name} cannot buy that tile");
        int cost = city.Expansion.GetGoldCostOfTile(tile);
        return actor.HasStatToBuy(Stat.Gold, cost)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotEnough("gold"), $"{actor.CivId} cannot afford {cost} gold");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).Expansion.BuyTile(context.TileAt(Tile).Value.NotNull());
        return [];
    }
}
