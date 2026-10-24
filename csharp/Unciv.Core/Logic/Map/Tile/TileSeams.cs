using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.Tiles
{
    // T053 (Tile) is the bottom of the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo cycle: it compiles against the
    // interfaces below instead of the classes that are not ported yet. The seam rule (backlog, T053 notes): the ticket that ports a class
    // implements its seam and then retypes the seam-typed members of Tile to the concrete class. Member names mirror the Kotlin members.

    /// <summary>
    /// Kotlin <c>TileMap.TerrainListData</c> (a data class): the combined uniques of a list of terrains, cached per terrain-name list.
    /// T039 moves it into <c>TileMap</c>.
    /// </summary>
    public sealed record TerrainListData(UniqueMap Uniques, IReadOnlySet<string> TerrainNameSet)
    {
        public static readonly TerrainListData Empty = new(UniqueMap.Empty, new HashSet<string>());
    }

    /// <summary>What <see cref="Tile"/> reads from <c>TileMap</c>. T039 implements it and retypes <see cref="Tile.TileMap"/>.</summary>
    public interface ITileMapState
    {
        /// <summary><c>tileMap.mapParameters</c></summary>
        MapParameters MapParameters { get; }
        /// <summary><c>tileMap.width</c></summary>
        int Width { get; }
        /// <summary><c>tileMap.gameInfo</c> (a <c>lateinit</c>: throws while it is not set).</summary>
        IGameContextGameInfo GameInfo { get; }
        bool HasGameInfo();
        HexCoord GetUnwrappedPosition(HexCoord position);
        int GetNeighborTileClockPosition(Tile tile, Tile otherTile);
        List<Tile> GetViewableTiles(HexCoord position, int sightDistance);
        IEnumerable<Tile> GetTilesInDistance(HexCoord origin, int distance);
        IEnumerable<Tile> GetTilesInDistanceRange(HexCoord origin, IntRange range);
        IEnumerable<Tile> GetTilesAtDistance(HexCoord origin, int distance);
        void ForEachTileInDistance(HexCoord origin, int distance, Func<Tile, bool> filter, Action<Tile> op);
        void ForEachTileInDistanceRange(HexCoord origin, IntRange range, Func<Tile, bool> filter, Action<Tile> op);
        void ForEachTileAtDistance(HexCoord origin, int distance, Func<Tile, bool> filter, Action<Tile> op);
        /// <summary><c>tileMap.tileUniqueMapCache.getOrPut(terrainNameList) { create() }</c>, keyed by the list's contents.</summary>
        TerrainListData GetOrPutTileUniqueMap(IReadOnlyList<string> terrainNameList, Func<TerrainListData> create);
    }
}

namespace Unciv.Models.Ruleset.Unique
{

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
}
