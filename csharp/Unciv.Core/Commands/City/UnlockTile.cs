using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.City;

/// <summary>Release a locked tile. Kotlin: <c>CityView.tryUnlockTile</c> (<c>city.unlockTile</c>); the double click in the UI toggles via <see cref="StopWorkingTile"/>.</summary>
[CommandType("unlock-tile")]
public sealed class UnlockTile : GameCommand
{
    public string CityId { get; init; } = "";
    public HexCoord Tile { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!context.TileAt(Tile).TryGet(out _, out failure)) return failure;
        return city.LockedTiles.Contains(Tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile is not locked");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).UnlockTile(context.TileAt(Tile).Value.NotNull());
        return [];
    }
}
