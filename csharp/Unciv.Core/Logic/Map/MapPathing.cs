using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/MapPathing.kt (T040). Stripped: the purity annotations, the Log.debug calls.
// Civilization is typed by its seam IGameContextCivilization (T055 retypes it); the improvementFunctions.canBuildImprovement call
// is the seam IGameContextCivilization.CanBuildImprovementOn (TileImprovementFunctions is T054).
// Not ported: the private getPath(unit, ...) overload. It has no caller in Kotlin (and would only be reachable through
// UnitMovement, T067); T067 adds it if a caller appears.

public static class MapPathing
{
    /// <summary>
    /// We prefer the worker to prioritize paths connected by existing roads. If a tile has a road, but the civ has the ability
    /// to upgrade it to a railroad, we consider it to be a railroad for pathing since it will be upgraded.
    /// Otherwise, we set every tile to have equal value since building a road on any of them makes the original movement cost irrelevant.
    /// </summary>
    /// <remarks>The unused <c>from</c> parameter keeps the signature expected by the AStar and getConnection <c>cost</c> parameter.</remarks>
    internal static float RoadPreferredMovementCost(IGameContextCivilization civ, Tile _, Tile to)
    {
        // hasRoadConnection accounts for civs that treat jungle/forest as roads
        // Ignore road over river penalties.
        if (to.HasRoadConnection(civ, false) || to.HasRailroadConnection(false))
            return .5f;

        return 1f;
    }

    public static bool IsValidRoadPathTile(IGameContextCivilization civ, Tile tile)
    {
        var roadImprovement = tile.Ruleset.RoadImprovement;
        var railRoadImprovement = tile.Ruleset.RailroadImprovement;

        if (tile.IsWater) return false;
        if (tile.IsImpassible()) return false;
        if (!civ.HasExplored(tile)) return false;
        if (!tile.CanCivPassThrough(civ)) return false;

        return tile.HasRoadConnection(civ, false)
            || tile.HasRailroadConnection(false)
            || (roadImprovement != null && civ.CanBuildImprovementOn(tile, roadImprovement))
            || (railRoadImprovement != null && civ.CanBuildImprovementOn(tile, railRoadImprovement));
    }

    /// <summary>
    /// Calculates the path for a road construction between two tiles, using the A* search algorithm.
    /// </summary>
    /// <param name="civ">The civilization that will construct the road.</param>
    /// <param name="startTile">The starting tile of the path.</param>
    /// <param name="endTile">The destination tile of the path.</param>
    /// <returns>The tiles from <paramref name="startTile"/> to <paramref name="endTile"/>, or null if no valid path is found.</returns>
    public static List<Tile>? GetRoadPath(IGameContextCivilization civ, Tile startTile, Tile endTile) =>
        GetConnection(civ, startTile, endTile, IsValidRoadPathTile, RoadPreferredMovementCost, (_, _, _) => 0f);

    /// <summary>
    /// Gets the connection to the end tile. This does not take into account tile movement costs.
    /// Takes in a civilization instead of a specific unit.
    /// </summary>
    /// <param name="civ">The civilization the callbacks are evaluated for.</param>
    /// <param name="startTile">The starting tile of the path.</param>
    /// <param name="endTile">The destination tile of the path.</param>
    /// <param name="predicate">Whether a tile may be part of the path.</param>
    /// <param name="cost">Defaults to a constant 1f.</param>
    /// <param name="heuristic">Defaults to the aerial distance from the 'from' tile to the 'to' tile.</param>
    public static List<Tile>? GetConnection(
        IGameContextCivilization civ,
        Tile startTile,
        Tile endTile,
        Func<IGameContextCivilization, Tile, bool> predicate,
        Func<IGameContextCivilization, Tile, Tile, float>? cost = null,
        Func<IGameContextCivilization, Tile, Tile, float>? heuristic = null)
    {
        cost ??= (_, _, _) => 1f;
        heuristic ??= (_, from, to) => from.AerialDistanceTo(to);
        var astar = new AStar(
            startTile,
            tile => predicate(civ, tile),
            (from, to) => cost(civ, from, to),
            (from, to) => heuristic(civ, from, to));
        while (true)
        {
            if (astar.HasEnded())
            {
                // We failed to find a path
                return null;
            }
            if (!astar.HasReachedTile(endTile))
            {
                astar.NextStep();
                continue;
            }
            // Found a path.
            var path = astar.GetPathTo(endTile).ToList();
            path.Reverse();
            return path;
        }
    }
}
