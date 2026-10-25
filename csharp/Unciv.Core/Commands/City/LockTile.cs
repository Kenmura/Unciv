using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>
/// Pin a worked tile so population reassignment keeps it. Kotlin: <c>CityScreen.tileWorkedIconDoubleClick</c> on a WORKABLE tile of a
/// non-puppet city, i.e. <c>CityView.tryLockTile</c> (the tile must be worked). Working an unworked tile first is a separate <see cref="WorkTile"/>.
/// </summary>
[CommandType("lock-tile")]
public sealed class LockTile : GameCommand
{
    public string CityId { get; init; } = "";
    public HexCoord Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!context.TryWorkableTile(city, Tile, out var tile, out failure)) return failure;
        if (!city.IsWorked(tile))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "Only a worked tile can be locked");
        return city.LockedTiles.Contains(Tile)
            ? CommandValidation.Fail(CommandErrors.NotAllowed, "That tile is already locked")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).LockTile(context.TileAt(Tile).Value.NotNull());
        return [];
    }
}
