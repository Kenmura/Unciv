using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Take the citizen off a worked tile. Kotlin: <c>CityScreen.tileWorkedIconOnClick</c> on a WORKABLE tile that provides yield
/// (not the city center, which is never in the worked set), i.e. <c>CityView.tryStopWorkingTile</c> (which also unlocks it), then <c>updateCityStats</c>.
/// </summary>
[CommandType("stop-working-tile")]
public sealed class StopWorkingTile : GameCommand
{
    public string CityId { get; init; } = "";
    public HexCoord Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!context.TryWorkableTile(city, Tile, out var tile, out failure)) return failure;
        return city.IsWorked(tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile is not worked");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.StopWorkingTile(context.TileAt(Tile).Value.NotNull());
        city.CityStats.Update();
        return [];
    }
}
