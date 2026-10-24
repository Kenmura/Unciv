using Unciv.View;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/AutomationCache.kt (T087).
// Kotlin HashMap<TileView, ...> is only looked up, never iterated, so a Dictionary is exact.

/// <summary>
/// Holds data that automation caches while a civ's moves are being automated.
/// Created in NextTurnAutomation.automateCivMoves and passed down to wherever it's needed - it is never stored on game state,
/// so it cannot go stale between turns.
/// </summary>
public class AutomationCache
{
    public class ClosestEnemyTileData(int distanceSearched, List<(TileView Tile, int Distance)> tilesWithEnemies)
    {
        /// <summary>The farthest radius in which we have checked tiles for enemies.
        /// A value of 2 means all enemies at a radius of 2 are in tilesWithEnemies.</summary>
        public int DistanceSearched { get; set; } = distanceSearched;

        /// <summary>Stores the location of the enemy tiles that we saw with the distance at which we saw them.
        /// Tiles are sorted by distance in increasing order.
        /// This allows us to quickly check if they are still alive and if we should search farther.
        /// It is not guaranteed that each tile in this list has an enemy (since they may have died).</summary>
        public List<(TileView Tile, int Distance)> TilesWithEnemies { get; set; } = tilesWithEnemies;
    }

    public Dictionary<TileView, ClosestEnemyTileData> DistanceToClosestEnemyTiles { get; } = [];
}
