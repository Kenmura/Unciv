using Unciv.Logic.Map;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

// Open owners: T046 (GameInfo). T134 dropped the MapUnit and CanBuildImprovementOn seams.
// GameInfo TileMap and Civilization HasExplored remain until T046 retypes GameInfo.

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
}

