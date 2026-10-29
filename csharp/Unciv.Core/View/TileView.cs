using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/TileView.kt (T084). Stripped: the purity annotations.
// DebugUtils.VISIBLE_MAP is the viewer game's EngineOptions.VisibleMap (T048).
// T085 added owningCity, getCity, getWorkingCity, civilianUnit, militaryUnit, getVisibleUnits, getCombatant, the CityView parameter of
// getTileStats/getTileStatsBreakdown and getImprovementBuildingProblems.

/// <summary>View of a <see cref="Tile"/> from the perspective of the viewer via the <see cref="TileMapView"/>.</summary>
public class TileView : View<Tile>
{
    private readonly Tile tile;

    internal TileView(Tile tile, TileMapView tileMapView, CivilizationClass? viewer, bool spectatorMode = false)
        : base(tile, viewer, spectatorMode)
    {
        this.tile = tile;
        TileMapView = tileMapView;
    }

    public TileMapView TileMapView { get; }

    // Navigation
    public CivView? GetCivView() => TileMapView.GameView?.CivView;

    public ForeignCivView? GetOwner()
    {
        var owner = (CivilizationClass?)tile.GetOwner();
        if (owner == null) return null;
        if (Viewer == null) return null;
        var gameView = TileMapView.GameView;
        return gameView?.GetForeignCivView(owner);
    }

    public ForeignCityView? OwningCity()
    {
        var city = tile.OwningCity;
        return city == null ? null : ToForeignCityView(city);
    }

    /// <summary>The city whose center is this tile, if any</summary>
    public ForeignCityView? GetCity()
    {
        var city = tile.GetCity();
        return city == null ? null : ToForeignCityView(city);
    }

    public ForeignCityView? GetWorkingCity()
    {
        var city = tile.GetWorkingCity();
        return city == null ? null : ToForeignCityView(city);
    }

    private ForeignCityView? ToForeignCityView(CityClass city)
    {
        if (Viewer == null) return null;
        var gameView = TileMapView.GameView;
        return gameView?.GetForeignCityView(city);
    }

    private bool IsVisible(MapUnit unit)
    {
        if (Viewer == null) return false;
        return IsVisibleMapDebug() || unit.IsVisibleTo(Viewer);
    }

    private ForeignMapUnitView ToForeignMapUnitView(MapUnit unit) => TileMapView.GameView.NotNull().GetForeignMapUnitView(unit);

    public ForeignMapUnitView? CivilianUnit
    {
        get
        {
            var unit = tile.CivilianUnit;
            if (unit == null) return null;
            if (!IsVisible(unit)) return null;
            return ToForeignMapUnitView(unit);
        }
    }

    public ForeignMapUnitView? MilitaryUnit
    {
        get
        {
            var unit = tile.MilitaryUnit;
            if (unit == null) return null;
            if (!IsVisible(unit)) return null;
            return ToForeignMapUnitView(unit);
        }
    }

    public List<ForeignMapUnitView> GetVisibleUnits()
    {
        if (Viewer == null) return [];
        return tile.GetUnits()
            .Where(it => IsVisible(it))
            .Select(it => ToForeignMapUnitView(it))
            .ToList();
    }

    public CombatantView? GetCombatant()
    {
        if (Viewer == null) return null;
        if (!IsExplored()) return null;
        var gameView = TileMapView.GameView;
        if (gameView == null) return null;
        if (tile.IsCityCenter())
            return gameView.GetForeignCityView(tile.GetCity().NotNull()).AsCombatant();

        var militaryUnit = tile.MilitaryUnit;
        if (militaryUnit != null && IsVisible(militaryUnit))
            return ToForeignMapUnitView(militaryUnit).AsCombatant();
        var civilianUnit = tile.CivilianUnit;
        if (civilianUnit != null && IsVisible(civilianUnit))
            return ToForeignMapUnitView(civilianUnit).AsCombatant();
        return null;
    }

    private bool IsVisibleMapDebug() => Viewer?.GameInfo.EngineOptions.VisibleMap == true;

    // Data retrieval
    public HexCoord Position() => tile.Position;

    /// <summary>
    /// Ideally this function should not exist - you should never be able to get a tileview of an unexplored tile.
    /// However, currently the way the map works is we set up a tilegroup for all players and use the tileview for that tile.
    /// That means that *in order to allow clicking on an unexplored tile* we currently need to accept tileviews of unexplored tiles.
    /// </summary>
    public bool IsExplored() => Viewer == null || tile.IsExplored(Viewer);

    /// <summary>Actively observed by the viewer (no fog of war)</summary>
    public bool IsVisible() => Viewer == null || tile.IsVisible(Viewer);

    /// <summary>
    /// <c>true</c> when this tile should be shown regardless of exploration/tech: there's no real viewer (e.g. civilopedia/map editor previews),
    /// debug mode has the whole map revealed, or <see cref="GetCivView"/> is watching a defeated game play out.
    /// Replace all usages with altering the view function called, to reveal data where relevant
    /// </summary>
    public bool IsForceVisible() => Viewer == null || IsVisibleMapDebug() || GetCivView()?.IsMapRevealed() == true;

    public IEnumerable<TileView> GetVisibleNeighbors() =>
        tile.Neighbors
            .Where(it => Viewer == null || it.IsExplored(Viewer))
            .Select(it => TileMapView.GetTile(it));

    // Maybe we can move this to hexmath, will require extra input of map width/radius
    public int GetNeighborClockPosition(TileView neighbor) => tile.TileMap.GetNeighborTileClockPosition(tile, neighbor.Unwrap());

    // This is an odd one - ideally the API should expose terrains, not...this, this is a specific performance boost
    // that's not really relevant for the API
    // We should see if we can avoid this entirely
    internal IReadOnlySet<string> GetCachedTerrainNameSet() => tile.CachedTerrainData.TerrainNameSet;

    public IEnumerable<TileView> GetExploredTilesAtDistance(int distance) =>
        tile.GetTilesAtDistance(distance)
            .Where(it => Viewer == null || it.IsExplored(Viewer))
            .Select(it => TileMapView.GetTile(it));

    /// <summary>
    /// Views only expose explored tiles, so unexplored tiles at <paramref name="distance"/> are estimated as a full hex ring (6 * distance) minus the explored ones.
    /// Overestimates near the true map edge, where tiles beyond the edge are counted as unexplored.
    /// </summary>
    public int EstimateUnexploredTilesAtDistance(int distance) =>
        Math.Max(6 * distance - GetExploredTilesAtDistance(distance).Count(), 0);

    public IEnumerable<TileView> GetExploredTilesInDistance(int distance) =>
        tile.GetTilesInDistance(distance)
            .Where(it => Viewer == null || it.IsExplored(Viewer))
            .Select(it => TileMapView.GetTile(it));

    public bool IsCityCenter() => tile.IsCityCenter();

    public bool IsWorked() => tile.IsWorked();

    public bool IsBlockaded() => tile.IsBlockaded();

    public bool ProvidesYield() => tile.ProvidesYield();

    public bool IsLocked() => tile.IsLocked();

    public bool IsImpassible() => tile.IsImpassible();

    public bool CanPillageTileImprovement() => tile.CanPillageTileImprovement();

    public bool CanPillageRoad() => tile.CanPillageRoad();

    public ForeignCivView? GetRoadOwner()
    {
        var owner = (CivilizationClass?)tile.GetRoadOwner();
        if (owner == null) return null;
        if (Viewer == null) return null;
        return TileMapView.GameView?.GetForeignCivView(owner);
    }

    public bool IsBarbarianEncampment() => tile.IsBarbarianEncampment();

    public bool IsFriendlyTerritory() => Viewer != null && tile.IsFriendlyTerritory(Viewer);

    public bool IsAdjacentTo(string terrainFilter) => tile.IsAdjacentTo(terrainFilter);

    public float GetDefensiveBonus() => tile.GetDefensiveBonus();

    public int AerialDistanceTo(TileView other) => tile.AerialDistanceTo(other.Unwrap());

    public string? GetShownImprovement() => tile.GetShownImprovement(Viewer);

    public string BaseTerrain => tile.BaseTerrain;

    public IReadOnlyList<string> TerrainFeatures => tile.TerrainFeatures;

    public TileResource? GetViewableResource(CivView? viewingCiv)
    {
        var resource = tile.TileResource;
        if (resource == null) return null;
        return viewingCiv == null || viewingCiv.CanSeeResource(resource) ? resource : null;
    }

    public string? Resource => tile.Resource;

    public int ResourceAmount => tile.ResourceAmount;

    public string? NaturalWonder => tile.NaturalWonder;

    public RoadStatus RoadStatus => tile.RoadStatus;

    public bool RoadIsPillaged => tile.RoadIsPillaged;

    public bool ImprovementIsPillaged => tile.ImprovementIsPillaged;

    public string? ImprovementInProgress => tile.ImprovementInProgress;

    public string? Improvement => tile.Improvement;

    public int TurnsToImprovement => tile.TurnsToImprovement;

    public int TileHeight => tile.TileHeight;

    public bool IsMarkedForCreatesOneImprovement() => tile.IsMarkedForCreatesOneImprovement();

    public bool IsLand => tile.IsLand;

    public bool HasBottomRightRiver => tile.HasBottomRightRiver;

    public bool HasBottomRiver => tile.HasBottomRiver;

    public bool HasBottomLeftRiver => tile.HasBottomLeftRiver;

    public Ruleset GetRuleset() => tile.Ruleset;

    public Stats GetTileStats(CivView? viewingCiv, CityView? cityView = null)
    {
        var city = cityView?.Unwrap() ?? tile.GetCity();
        return tile.Stats.GetTileStats(city, viewingCiv?.Unwrap());
    }

    /// <summary>C088: the yield of the bare land (terrain, features, river, the resource the viewer can see), no improvement or road, no city bonuses: it does not change with what is built out of sight.</summary>
    public Stats GetLandStats(CivView? viewingCiv) => tile.Stats.GetTileStats(null, viewingCiv?.Unwrap(), ignoreImprovements: true);

    public List<(string Cause, Stats Stats)> GetTileStatsBreakdown(CivView? viewingCiv, CityView? cityView = null)
    {
        var city = cityView?.Unwrap() ?? tile.GetCity();
        return tile.Stats.GetTileStatsBreakdown(city, viewingCiv?.Unwrap());
    }

    /// <summary>Base terrain, natural wonder (if any), then terrain features, in order - the last one is the "top" terrain</summary>
    public List<Terrain> GetAllTerrains() => tile.AllTerrains.ToList();

    /// <summary><paramref name="pretendLastTerrainRemoved"/> checks against a copy of this tile with its top terrain feature removed</summary>
    public HashSet<ImprovementBuildingProblem> GetImprovementBuildingProblems(TileImprovement improvement, MapUnitView unitView, bool pretendLastTerrainRemoved = false)
    {
        var tileToCheck = pretendLastTerrainRemoved ? GetTileWithoutLastTerrain() : tile;
        return tileToCheck.ImprovementFunctions.GetImprovementBuildingProblems(improvement, unitView.Unwrap().Cache.State).ToHashSet();
    }

    /// <summary>Fresh copy of the tile with its top terrain feature removed - not cached, since the tile can change</summary>
    private Tile GetTileWithoutLastTerrain()
    {
        var newTile = tile.Clone(addUnits: false);
        newTile.SetTerrainTransients();
        newTile.RemoveTerrainFeature(newTile.LastTerrain.Name);
        return newTile;
    }

    public Stats GetStatDiffForImprovement(TileImprovement improvement, CivView civView) =>
        tile.Stats.GetStatDiffForImprovement(improvement, civView.Unwrap(), tile.GetCity());

    /// <summary>
    /// Per-turn maintenance of <paramref name="improvement"/> on this tile as negative stats, matching CivInfoStatsForNextTurn.getTransportationUpkeep
    /// </summary>
    // Not centralized in [TileStatFunctions] because the actual upkeep calculation can optimize some things and rounding errors might accumulate differently
    public Stats GetImprovementMaintenance(TileImprovement improvement, CivView civView)
    {
        var civ = civView.Unwrap();
        var maintenance = new Stats();
        if (civ.GetMatchingUniques(UniqueType.NoImprovementMaintenanceInSpecificTiles)
            .Any(it => tile.MatchesFilter(it.Params[0], civ)))
            return maintenance;

        var context = new GameContext(civ, tile: tile);
        var maintenanceUniques = ((IHasUniques)improvement).GetMatchingUniques(UniqueType.ImprovementAllMaintenance, context).Concat(
            // ImprovementMaintenance only applies inside city territory; ImprovementAllMaintenance applies everywhere.
            ReferenceEquals(tile.GetOwner(), civ)
                ? ((IHasUniques)improvement).GetMatchingUniques(UniqueType.ImprovementMaintenance, context)
                : []);
        foreach (var maintenanceUnique in maintenanceUniques)
        {
            var amount = maintenanceUnique.Params[0].ToFloat();
            var statName = StatExtensions.SafeValueOf(maintenanceUnique.Params[1]);
            if (statName == null) continue;
            maintenance.Add(statName.Value, -amount);
        }

        civ.ForEachMatchingUnique(UniqueType.RoadMaintenance, unique =>
            maintenance.TimesInPlace(unique.Params[0].ToPercent()));
        return maintenance;
    }

    public bool ProvidesResources(CivView viewingCiv) => tile.ProvidesResources(viewingCiv.Unwrap());

    public TileMapView GetTileMap() => TileMapView;

    /// <summary>For icon/preview rendering of a single tile that has no backing <see cref="TileMap"/>.</summary>
    public static TileView ForSingleTile(Tile tile)
    {
        var tileMap = new TileMap(1);
        tileMap.TileList.Add(tile);
        return new TileMapView(tileMap, null).GetTile(tile);
    }
}

// Helpers built on top of the view API, rather than being part of it
public static class TileViewExtensions
{
    public static Terrain GetBaseTerrain(this TileView tileView) => tileView.GetRuleset().Terrains[tileView.BaseTerrain];
}
