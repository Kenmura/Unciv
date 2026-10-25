using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Put a free citizen on a tile. Kotlin: <c>CityScreen.tileWorkedIconOnClick</c> on a WORKABLE tile that does not provide yield and with
/// <c>getFreePopulation() &gt; 0</c> (not a puppet; blockaded tiles and tiles without stats are not offered), i.e. <c>CityView.tryWorkTile</c>, then <c>updateCityStats</c>.
/// </summary>
[CommandType("work-tile")]
public sealed class WorkTile : GameCommand
{
    public string CityId { get; init; } = "";
    public HexCoord Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!context.TryWorkableTile(city, Tile, out var tile, out failure)) return failure;
        if (tile.ProvidesYield()) // the city center, a tile worked by any city, or one yielding without population
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile already provides yield");
        if (tile.Stats.GetTileStats(city, city.Civ).IsEmpty())
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile yields nothing");
        return city.Population.GetFreePopulation() > 0
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} has no free citizen");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.WorkTile(context.TileAt(Tile).Value.NotNull());
        city.CityStats.Update();
        return [];
    }
}
