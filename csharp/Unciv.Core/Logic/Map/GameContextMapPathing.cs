using Unciv.Logic.Map;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

// Members of the permanent GameContext interfaces (GameContext.cs) that the map pathing reads: the game's TileMap and whether a civ has explored a tile.

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

