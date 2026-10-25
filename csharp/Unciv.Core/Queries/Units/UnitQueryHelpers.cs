using Unciv.Logic.Map;
using Unciv.View;

namespace Unciv.Queries.Units;

// T195: shared lookups of the unit queries. Every lookup goes through the viewer's GameView, so ids of things the viewer does
// not own are answered like ids that do not exist (no probing of foreign unit ids).
public static class UnitQueryCodes
{
    public const string UnknownUnit = "unknown-unit";
    public const string UnknownCity = "unknown-city";
    public const string UnknownTile = "unknown-tile";
    public const string NoTarget = "no-target";
    public const string BadRequest = "bad-request";
    public const string InvalidAttackFrom = "invalid-attack-from";
}

/// <summary>A tile of the map by its coordinates.</summary>
public sealed record TilePos(int X, int Y);

internal static class UnitQueryHelpers
{
    public static TilePos Pos(HexCoord position) => new(position.X, position.Y);

    public static MapUnitView OwnUnit(GameView view, int unitId) =>
        view.CivView.GetUnits().FirstOrDefault(it => it.Id == unitId)
        ?? throw new QueryException(UnitQueryCodes.UnknownUnit, $"There is no unit {unitId} of yours");

    public static CityView OwnCity(GameView view, string cityId) =>
        view.CivView.Cities().FirstOrDefault(it => it.Unwrap().Id == cityId)
        ?? throw new QueryException(UnitQueryCodes.UnknownCity, $"There is no city \"{cityId}\" of yours");

    /// <summary>The tile at the coordinates, only if the viewer has explored it.</summary>
    public static TileView ExploredTile(GameView view, int x, int y) =>
        view.TileMapView.GetTile(new HexCoord(x, y))
        ?? throw new QueryException(UnitQueryCodes.UnknownTile, $"The tile ({x}, {y}) is not known to you");

    /// <summary>Exactly one of unit id and city id must be given.</summary>
    public static void RequireOneAttacker(int? unitId, string? cityId)
    {
        if ((unitId == null) == (cityId == null))
            throw new QueryException(UnitQueryCodes.BadRequest, "Give exactly one of UnitId and CityId");
    }
}
