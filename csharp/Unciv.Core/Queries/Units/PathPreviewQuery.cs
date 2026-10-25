using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.View;

namespace Unciv.Queries.Units;

// Turn: 1 is this turn
// Tiles: where the unit ends that turn (the engine's path holds one tile per turn, so one entry)
public sealed record PathLeg(int Turn, IReadOnlyList<TilePos> Tiles);

// Reachable: Whether the engine finds a path at all
// CanEnter: Whether the unit could end its move on the target (false e.g. for a tile with an enemy)
// Turns: Number of turns the path takes (0 when already there or unreachable)
// FirstStep: The tile a move command would go to this turn (HeadTowards), null when none
public sealed record PathPreviewResult(int UnitId, TilePos Target, bool Reachable, bool CanEnter, int Turns, TilePos? FirstStep, IReadOnlyList<PathLeg> Legs);

// T195: multi-turn path preview. The path is UnitMovement.GetShortestPath, which uses the A* pathing when the client state has
// it switched on, so the preview and the move command agree. The engine's path holds the end tile of each turn, so legs are
// read off it without moving the unit. The target must be explored by the viewer.
// Known limits (shared with the engine and the Kotlin client): paths and reach treat tiles holding a hidden unit as blocked
// (CanEnter, which is derived here, does not show it); and the pathfinder treats unexplored tiles as passable at cost 1 and its tie-breaking looks at
// the real terrain, so a path through unexplored territory can depend on what the viewer has not seen. Only coordinates are returned.
[QueryType("units.PathPreview")]
public sealed record PathPreviewQuery(int UnitId, int X, int Y) : IQuery<PathPreviewResult>
{
    public string Type => "units.PathPreview";

    public PathPreviewResult Run(GameView view)
    {
        var unitView = UnitQueryHelpers.OwnUnit(view, UnitId);
        var target = UnitQueryHelpers.ExploredTile(view, X, Y);
        var unit = unitView.Unwrap();
        var destination = target.Unwrap();
        var targetPos = UnitQueryHelpers.Pos(destination.Position);

        if (destination == unit.CurrentTile)
            return new PathPreviewResult(UnitId, targetPos, true, true, 0, targetPos, []);

        bool canEnter = UnitQueryHelpers.CanEnterAsSeen(target, unit);
        if (unit.Cache.CannotMove) return new PathPreviewResult(UnitId, targetPos, false, canEnter, 0, null, []);

        if (unit.BaseUnit.IsAirUnit() || unit.IsPreparingParadrop())
        {
            // The move is a single hop, see UnitMovement.GetTileToMoveToThisTurn
            bool reachable = unit.Movement.CanReach(destination);
            return reachable
                ? new PathPreviewResult(UnitId, targetPos, true, canEnter, 1, targetPos, [new PathLeg(1, [targetPos])])
                : new PathPreviewResult(UnitId, targetPos, false, canEnter, 0, null, []);
        }

        var path = unit.Movement.GetShortestPath(destination);
        if (path.Count == 0) return new PathPreviewResult(UnitId, targetPos, false, canEnter, 0, null, []);

        var legs = OneLegPerTile(path);
        TilePos? firstStep = null;
        try
        {
            firstStep = UnitQueryHelpers.Pos(unit.Movement.GetTileToMoveToThisTurn(destination).Position);
        }
        catch (UnitMovement.UnreachableDestinationException)
        {
        }
        return new PathPreviewResult(UnitId, targetPos, true, canEnter, legs.Count == 0 ? 0 : legs[^1].Turn, firstStep, legs);
    }

    // Both pathfinders (classic breadth first layers, A* turn changes) return one tile per turn: the end tile of that turn.
    private static List<PathLeg> OneLegPerTile(List<Tile> path) =>
        path.Select((tile, index) => new PathLeg(index + 1, [UnitQueryHelpers.Pos(tile.Position)])).ToList();
}
