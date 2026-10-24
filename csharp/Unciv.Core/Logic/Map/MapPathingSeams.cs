using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

// Open owners: T046 (GameInfo), T065 (MapUnit); T104 lists survivors.
// T040 (BFS, AStar, MapPathing, CarrierSlotMatcher, MapVisualization): these classes talk to GameInfo, Civilization and MapUnit only
// through the members below (seam rule, T053 notes). The tickets that port those classes implement them. Member names mirror the Kotlin members.

namespace Unciv.Models.Ruleset.Unique;

public partial interface IGameContextGameInfo
{
    /// <summary><c>gameInfo.tileMap</c></summary>
    TileMap TileMap { get; }
}

public partial interface IGameContextCivilization
{
    /// <summary><c>civ.hasExplored(tile)</c></summary>
    bool HasExplored(TileClass tile);
    /// <summary><c>tile.improvementFunctions.canBuildImprovement(improvement, civ.state)</c> (TileImprovementFunctions: T054)</summary>
    bool CanBuildImprovementOn(TileClass tile, TileImprovement improvement);
}

public partial interface IGameContextUnit
{
    /// <summary><c>unit.movementMemories.map { it.position }</c></summary>
    IEnumerable<HexCoord> MovementMemoryPositions { get; }
    /// <summary><c>unit.isVisibleTo(civ)</c></summary>
    bool IsVisibleTo(IGameContextCivilization civ);
}
