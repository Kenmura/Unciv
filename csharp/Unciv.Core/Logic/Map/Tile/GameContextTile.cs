using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;

// T053 (Tile) is the bottom of the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo cycle: Tile compiles against the
// GameContext interfaces extended below, which GameInfo, Civilization, City and MapUnit implement. Member names mirror the Kotlin members.

namespace Unciv.Models.Ruleset.Unique;

public partial interface IGameContextGameInfo
{
    /// <summary><c>gameInfo.getCivilization(civID)</c></summary>
    IGameContextCivilization GetCivilization(string civId);
    /// <summary><c>gameInfo.ruleset.modOptions.constants</c></summary>
    ModConstants GetModConstants();
    /// <summary>The game's <c>DebugUtils</c> flags (see <see cref="EngineOptions"/>).</summary>
    EngineOptions EngineOptions { get; }
}

public partial interface IGameContextCivilization
{
    PlayerType PlayerType { get; }
    bool IsSpectator();
    /// <summary><c>civ.viewableTiles.contains(tile)</c></summary>
    bool IsTileViewable(Unciv.Logic.Map.Tiles.Tile tile);
    string? GetLastSeenImprovement(HexCoord position);
    bool Knows(IGameContextCivilization otherCiv);
    bool IsAtWarWith(IGameContextCivilization otherCiv);
    /// <summary><c>civ.getDiplomacyManager(otherCiv)!!.isConsideredFriendlyTerritory()</c></summary>
    bool IsConsideredFriendlyTerritoryOf(IGameContextCivilization otherCiv);
    bool CanSeeResource(TileResource? resource);
    /// <summary><c>civ.diplomacyFunctions.canPassThroughTiles(otherCiv)</c></summary>
    bool CanPassThroughTiles(IGameContextCivilization otherCiv);
    /// <summary><c>civ.nation.forestsAndJunglesAreRoads</c></summary>
    bool ForestsAndJunglesAreRoads { get; }
    /// <summary><c>civ.neutralRoads</c></summary>
    ISet<HexCoord> NeutralRoads { get; }
    /// <summary><c>civ.exploredRegion.checkTilePosition(tilePosition, explorerPosition)</c></summary>
    void CheckExploredRegionTilePosition(HexCoord tilePosition, HexCoord? explorerPosition);
    /// <summary><c>civ.matchesFilter(filter, state, multiFilter)</c></summary>
    bool MatchesFilter(string filter, GameContext? state, bool multiFilter);
}

public partial interface IGameContextCity
{
    string Name { get; }
    /// <summary><c>city.location.toHexCoord()</c></summary>
    HexCoord Location { get; }
    bool IsWorked(Unciv.Logic.Map.Tiles.Tile tile);
    /// <summary><c>city.lockedTiles.contains(position)</c></summary>
    bool IsTileLocked(HexCoord position);
    bool HasJustBeenConquered { get; }
    /// <summary><c>city.isCapital()</c></summary>
    bool IsCapital();
    void ReassignPopulationDeferred();
}

public partial interface IGameContextUnit
{
    /// <summary><c>unit.clone()</c></summary>
    IGameContextUnit Clone();
    /// <summary>The <c>currentTile</c> setter.</summary>
    void SetCurrentTile(Unciv.Logic.Map.Tiles.Tile tile);
    /// <summary><c>unit.owner</c> (the civ's id)</summary>
    string Owner { get; }
    void AssignOwner(IGameContextCivilization civInfo, bool updateCivInfo);
    void SetTransients(Ruleset ruleset);
    bool IsEmbarked();
    /// <summary><c>unit.cache.state</c></summary>
    GameContext CacheState { get; }
}
