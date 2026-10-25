using Unciv.Logic.Map.Tiles;
using Unciv.View;

namespace Unciv.Queries.Units;

// X: Tile x
// Y: Tile y
// Cost: Movement points spent when arriving (air units: the distance)
// MovementLeft: Movement points left on arrival, never negative
// CanEnter: Whether the unit could end its move here (false for tiles it can only pass through)
public sealed record ReachableTile(int X, int Y, float Cost, float MovementLeft, bool CanEnter);

public sealed record ReachableTilesResult(int UnitId, TilePos From, float CurrentMovement, IReadOnlyList<ReachableTile> Tiles);

// T195: tiles the unit can reach this turn, with the movement it costs. Wraps UnitMovement.GetDistanceToTiles and
// GetReachableTilesInCurrentTurn (the same calls the move command checks), so the preview equals what a move would do.
// Only tiles the viewer has explored are listed: unexplored neighbours are reachable in the engine at a guessed cost of 1,
// but listing them would reveal where the explored border is crossed for free, and a client cannot show them anyway.
[QueryType("units.ReachableTiles")]
public sealed record ReachableTilesQuery(int UnitId) : IQuery<ReachableTilesResult>
{
    public string Type => "units.ReachableTiles";

    public ReachableTilesResult Run(GameView view)
    {
        var unitView = UnitQueryHelpers.OwnUnit(view, UnitId);
        var unit = unitView.Unwrap();
        var distances = unit.Movement.GetDistanceToTiles();
        var tiles = new List<ReachableTile>();
        foreach (var tile in unit.Movement.GetReachableTilesInCurrentTurn())
        {
            if (view.TileMapView.GetTile(tile.Position) == null) continue;
            float cost = distances.TryGetValue(tile, out var path) ? path.TotalMovement : AirCost(unit, tile);
            tiles.Add(new ReachableTile(tile.Position.X, tile.Position.Y, cost,
                Math.Max(unit.CurrentMovement - cost, 0f), unit.Movement.CanMoveTo(tile)));
        }
        return new ReachableTilesResult(UnitId, UnitQueryHelpers.Pos(unit.CurrentTile.Position), unit.CurrentMovement,
            tiles.OrderBy(it => it.X).ThenBy(it => it.Y).ToList());
    }

    private static float AirCost(Logic.Map.MapUnits.MapUnit unit, Tile tile) => unit.CurrentTile.AerialDistanceTo(tile);
}
