using System.Globalization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.Tiles;

// Ported from core/src/com/unciv/logic/map/tile/Tile.kt (T053), against the seams in TileSeams.cs: TileMap, Civilization, City and MapUnit
// are not ported yet, so those fields and parameters are typed by their IGameContext*/ITileMapState seam (Tile -> TileMap -> Civilization
// -> City -> MapUnit order; the ticket porting each class retypes them).
//
// Stripped: timeThis profiling wrappers, GUI.clearUndoCheckpoints (UI), the @Readonly/@Cache purity annotations, the Gdx Json
// private constructor of ImprovementQueueEntry. Fonts.turn is inlined.
// Deferred to T054 (they need TileImprovementFunctions / TileStatFunctions / TileNormalizer / TileImprovement.getTurnsToBuild /
// UnitMovement, none of which are ported): the `improvementFunctions` and `stats` fields, removeImprovement, both setImprovement
// overloads, setBaseTerrain (TileNormalizer.normalizeToRuleset), doWorkerTurn, setPillaged (+ clearAllPathfindingCaches),
// startWorkingOnImprovement and queueImprovement(TileImprovement, civ, unit), and the convertTerrains parameter of
// setConnectedByRiver (MapGenerator.Helpers.convertTerrains).

/// <summary>
/// A single hex of the map. Kotlin <c>Tile</c> has no <c>equals</c>/<c>hashCode</c>: this class keeps reference identity.
/// </summary>
public class Tile : IsPartOfGameInfoSerialization, IGameContextTile, ITileHistorySource
{
    #region Serialized fields
    public IGameContextUnit? MilitaryUnit { get; set; }
    public IGameContextUnit? CivilianUnit { get; set; }
    public List<IGameContextUnit> AirUnits { get; set; } = [];

    public HexCoord Position { get; set; } = new();
    private string? baseTerrain;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    public string BaseTerrain
    {
        get => baseTerrain ?? throw new InvalidOperationException("lateinit property baseTerrain has not been initialized");
        set => baseTerrain = value;
    }
    public IReadOnlyList<string> TerrainFeatures { get; private set; } = [];

    /// <summary>Should be immutable - never be altered in-place, instead replaced</summary>
    private HashSet<string> exploredBy = [];

    public string? NaturalWonder { get; set; }
    public string? Resource { get; private set; }
    public int ResourceAmount { get; set; }

    public string? Improvement { get; set; }
    public bool ImprovementIsPillaged { get; set; }

    internal class ImprovementQueueEntry(string improvement, int turnsToImprovement) : IsPartOfGameInfoSerialization
    {
        public string Improvement { get; } = improvement;
        public int TurnsToImprovement { get; private set; } = turnsToImprovement;
        public override string ToString() => $"{Improvement}: {TurnsToImprovement}⏳";
        /// <returns><c>true</c> if it's still counting and not finished</returns>
        public bool CountDown()
        {
            TurnsToImprovement = Math.Max(TurnsToImprovement - 1, 0);
            return TurnsToImprovement > 0;
        }
        public ImprovementQueueEntry Clone() => new(Improvement, TurnsToImprovement);
    }
    internal List<ImprovementQueueEntry> ImprovementQueue { get; } = [];

    public RoadStatus RoadStatus { get; set; } = RoadStatus.None;

    public bool RoadIsPillaged { get; set; }
    private string roadOwner = ""; // either who last built the road or last owner of tile

    private IGameContextCivilization? roadOwnerObject;

    public bool HasBottomRightRiver { get; set; }
    public bool HasBottomRiver { get; set; }
    public bool HasBottomLeftRiver { get; set; }

    public TileHistory History { get; set; } = new();

    private int continent = -1;
    #endregion

    #region Transient fields
    private ITileMapState? tileMap;
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws. T039 retypes it to <c>TileMap</c>.</summary>
    public ITileMapState TileMap
    {
        get => tileMap ?? throw new InvalidOperationException("lateinit property tileMap has not been initialized");
        set => tileMap = value;
    }

    public int ZeroBasedIndex { get; set; }

    private Ruleset? ruleset;
    /// <summary>A tile can be a tile with a ruleset, even without a map. Kotlin <c>lateinit var</c>.</summary>
    public Ruleset Ruleset
    {
        get => ruleset ?? throw new InvalidOperationException("lateinit property ruleset has not been initialized");
        set => ruleset = value;
    }

    // This is for performance - since we access the neighbors of a tile ALL THE TIME,
    // and the neighbors of a tile never change, it's much more efficient to save the list once and for all!
    private IReadOnlyList<Tile>? neighbors;
    public IReadOnlyList<Tile> Neighbors => neighbors ??= !IsTilemapInitialized() ? [] : GetTilesAtDistance(1).ToList();

    private bool isCityCenterInternal;

    /// <summary>T060 retypes it to <c>City</c>.</summary>
    public IGameContextCity? OwningCity { get; private set; }

    private Terrain? baseTerrainObject;

    // These are for performance - checked with every tile movement and "canEnter" check, which makes them performance-critical
    public bool IsLand { get; set; }
    public bool IsWater { get; set; }
    public bool IsOcean { get; set; }

    private bool? isAdjacentToCoastCache;
    private bool IsAdjacentToCoastLazy => isAdjacentToCoastCache ??= Neighbors.Any(it => it.GetBaseTerrain().IsCoast);

    public int UnitHeight { get; set; }
    public int TileHeight { get; set; }

    public IReadOnlyList<Terrain> TerrainFeatureObjects { get; private set; } = [];

    /// <summary>Saves a sequence of a list</summary>
    public IReadOnlyList<Terrain> AllTerrains { get; private set; } = [];

    private Terrain? lastTerrain;
    public Terrain LastTerrain
    {
        get => lastTerrain ?? throw new InvalidOperationException("lateinit property lastTerrain has not been initialized");
        private set => lastTerrain = value;
    }

    public TerrainListData CachedTerrainData { get; private set; } = TerrainListData.Empty;

    /// <summary>Between 0.0 and 1.0 - For map generation use only</summary>
    public double? Humidity { get; set; }

    /// <summary>Between -1.0 and 1.0 - For map generation use only</summary>
    public double? Temperature { get; set; }

    /// <summary>For map generation use only</summary>
    public bool HasVegetation { get; set; }

    public int Latitude => HexMath.GetLatitude(Position);
    public int Longitude => HexMath.GetLongitude(Position);

    private TileResource? tileResourceCache;
    public TileResource? TileResource
    {
        get
        {
            if (tileResourceCache == null && Resource != null)
                tileResourceCache = Ruleset.TileResources.GetValueOrDefault(Resource);
            return tileResourceCache;
        }
        set
        {
            if (value == null)
            {
                Resource = null;
                tileResourceCache = null;
            }
            else
            {
                Resource = value.Name;
                tileResourceCache = value;
            }
        }
    }

    private TileImprovement? tileImprovementField;
    public TileImprovement? TileImprovement
    {
        get
        {
            if (tileImprovementField == null && Improvement != null)
                tileImprovementField = Ruleset.TileImprovements.GetValueOrDefault(Improvement);
            return tileImprovementField;
        }
        set => tileImprovementField = value;
    }

    public string? ImprovementInProgress => ImprovementQueue.FirstOrDefault()?.Improvement;
    public int TurnsToImprovement => ImprovementQueue.FirstOrDefault()?.TurnsToImprovement ?? 0;

    internal GameContext StateThisTile { get; private set; } = GameContext.EmptyState;
    #endregion

    #region Seam members (IGameContextTile, ITileHistorySource)
    IGameContextGameInfo? IGameContextTile.GameInfo => IsTilemapInitialized() && TileMap.HasGameInfo() ? TileMap.GameInfo : null;
    IEnumerable<IGameContextTile> IGameContextTile.Neighbors => Neighbors;
    IEnumerable<IGameContextUnit> IGameContextTile.AirUnits => AirUnits;
    IEnumerable<IGameContextTile> IGameContextTile.GetTilesInDistance(int distance) => GetTilesInDistance(distance);
    bool IGameContextTile.MatchesFilter(string filter) => MatchesFilter(filter);
    int ITileHistorySource.Turns => TileMap.GameInfo.Turns;
    string? ITileHistorySource.OwnerCivID => GetOwner()?.CivId;
    bool ITileHistorySource.IsCityCenter => IsCityCenter();
    bool ITileHistorySource.IsCityCapital => GetCity()?.IsCapital() == true;
    #endregion

    #region Pure functions

    /// <param name="addUnits">For stat diff checks, units are meaningless</param>
    public Tile Clone(bool addUnits = true)
    {
        var toReturn = new Tile
        {
            TileMap = TileMap,
            Ruleset = Ruleset,
            isCityCenterInternal = isCityCenterInternal,
            OwningCity = OwningCity,
            baseTerrainObject = baseTerrainObject,
            IsLand = IsLand,
            IsWater = IsWater,
            IsOcean = IsOcean,
        };
        if (addUnits)
        {
            if (MilitaryUnit != null) toReturn.MilitaryUnit = MilitaryUnit.Clone();
            if (CivilianUnit != null) toReturn.CivilianUnit = CivilianUnit.Clone();
            toReturn.AirUnits = AirUnits.Select(it => it.Clone()).ToList();
        }
        toReturn.Position = Position;
        toReturn.BaseTerrain = BaseTerrain;
        toReturn.TerrainFeatures = TerrainFeatures; // immutable lists can be directly passed around
        toReturn.TerrainFeatureObjects = TerrainFeatureObjects;
        toReturn.NaturalWonder = NaturalWonder;
        toReturn.Resource = Resource;
        toReturn.ResourceAmount = ResourceAmount;
        toReturn.Improvement = Improvement;
        toReturn.ImprovementQueue.AddRange(ImprovementQueue.Select(it => it.Clone()));
        toReturn.ImprovementIsPillaged = ImprovementIsPillaged;
        toReturn.RoadStatus = RoadStatus;
        toReturn.RoadIsPillaged = RoadIsPillaged;
        toReturn.roadOwner = roadOwner;
        toReturn.HasBottomLeftRiver = HasBottomLeftRiver;
        toReturn.HasBottomRightRiver = HasBottomRightRiver;
        toReturn.HasBottomRiver = HasBottomRiver;
        toReturn.continent = continent;
        toReturn.exploredBy = exploredBy;
        toReturn.History = History.Clone();
        // Setting even though it's transient - where it's needed, it's a real performance saver
        toReturn.tileResourceCache = tileResourceCache;
        return toReturn;
    }

    public Terrain? GetHillTerrain()
    {
        var baseT = GetBaseTerrain();
        if (baseT.IsHill)
            return baseT;
        return TerrainFeatureObjects.FirstOrDefault(it => it.IsHill);
    }
    public bool IsHill() => GetHillTerrain() != null;

    /// <summary>Returns military, civilian and air units in tile</summary>
    public IEnumerable<IGameContextUnit> GetUnits()
    {
        // common case - do not allocate memory for a new sequence
        if (MilitaryUnit == null && CivilianUnit == null && AirUnits.Count == 0)
            return [];
        return GetUnitsIterator();
    }

    private IEnumerable<IGameContextUnit> GetUnitsIterator()
    {
        if (MilitaryUnit != null) yield return MilitaryUnit;
        if (CivilianUnit != null) yield return CivilianUnit;
        if (AirUnits.Count > 0)
        {
            foreach (var unit in AirUnits) yield return unit;
        }
    }

    /// <summary>This is for performance reasons of canPassThrough() - faster than getUnits().firstOrNull()</summary>
    public IGameContextUnit? GetFirstUnit()
    {
        if (MilitaryUnit != null) return MilitaryUnit;
        if (CivilianUnit != null) return CivilianUnit;
        if (AirUnits.Count > 0) return AirUnits[0];
        return null;
    }

    public IGameContextCity? GetCity() => OwningCity;

    internal Terrain GetNaturalWonder() =>
        NaturalWonder == null ? throw new InvalidOperationException("No natural wonder exists for this tile!")
        : Ruleset.Terrains[NaturalWonder];

    /// <summary>Actively observed tiles (no fog of war)</summary>
    public bool IsVisible(IGameContextCivilization player)
    {
        if (player.GameInfo?.EngineOptions.VisibleMap == true)
            return true;
        return player.IsTileViewable(this);
    }

    /// <summary>Tiles that at some point have been explored (may have fog of war)</summary>
    public bool IsExplored(IGameContextCivilization player)
    {
        if (player.GameInfo?.EngineOptions.VisibleMap == true || player.IsSpectator())
            return true;
        return exploredBy.Contains(player.CivId);
    }

    public bool IsCityCenter() => isCityCenterInternal;
    public bool IsNaturalWonder() => NaturalWonder != null;
    public bool IsImpassible() => LastTerrain.Impassable;

    public bool IsBarbarianEncampment() =>
        TileImprovement?.IsBarbarianCampEquivalent(StateThisTile) == true;

    public bool HasImprovementInProgress() => ImprovementQueue.Count > 0;

    public TileImprovement? GetTileImprovementInProgress() =>
        ImprovementQueue.Count == 0 ? null : Ruleset.TileImprovements.GetValueOrDefault(ImprovementQueue[0].Improvement);
    public bool IsPillaged() => ImprovementIsPillaged || RoadIsPillaged;

    public string? GetUnpillagedImprovement() => ImprovementIsPillaged ? null : Improvement;

    public TileImprovement? GetUnpillagedTileImprovement() => ImprovementIsPillaged ? null : TileImprovement;

    public TileImprovement? GetRoadTileImprovement() =>
        RoadStatus == RoadStatus.None ? null : Ruleset.TileImprovements.GetValueOrDefault(RoadStatus.ToString());

    /// <returns><see cref="RoadStatus"/> on this <see cref="Tile"/>, pillaged road counts as <see cref="RoadStatus.None"/></returns>
    public RoadStatus GetUnpillagedRoad() => RoadIsPillaged ? RoadStatus.None : RoadStatus;

    public TileImprovement? GetUnpillagedRoadImprovement() => RoadIsPillaged ? null : GetRoadTileImprovement();

    public TileImprovement? GetImprovementToPillage()
    {
        if (CanPillageTileImprovement())
            return TileImprovement!;
        if (CanPillageRoad())
            return GetRoadTileImprovement()!;
        return null;
    }

    /// <summary>same as above, but slightly quicker</summary>
    public string? GetImprovementToPillageName()
    {
        if (CanPillageTileImprovement())
            return Improvement;
        if (CanPillageRoad())
            return RoadStatus.ToString();
        return null;
    }

    public TileImprovement? GetImprovementToRepair()
    {
        if (Improvement != null && ImprovementIsPillaged)
            return TileImprovement!;
        if (RoadStatus != RoadStatus.None && RoadIsPillaged)
            return GetRoadTileImprovement()!;
        return null;
    }

    public bool CanPillageTile() => CanPillageTileImprovement() || CanPillageRoad();

    public bool CanPillageTileImprovement()
    {
        var improvement = TileImprovement;
        if (improvement == null) return false;
        var uniques = (IHasUniques)improvement;
        return !ImprovementIsPillaged &&
            !uniques.HasUnique(UniqueType.Unpillagable) &&
            !uniques.HasUnique(UniqueType.Irremovable);
    }

    public bool CanPillageRoad()
    {
        return RoadStatus != RoadStatus.None &&
            !RoadIsPillaged &&
            !((IHasUniques)GetRoadTileImprovement()!).HasUnique(UniqueType.Unpillagable) &&
            !((IHasUniques)GetRoadTileImprovement()!).HasUnique(UniqueType.Irremovable);
    }

    /// <summary>
    /// Improvement to display, accounting for knowledge about a Tile possibly getting stale when a human player is no longer actively watching it.
    /// Relies on a Civilization's lastSeenImprovement always being up to date while the civ can see the Tile.
    /// </summary>
    /// <param name="viewingCiv"><c>null</c> means civ-agnostic and thus always showing the actual improvement</param>
    /// <returns>The improvement name, or <c>null</c> if no improvement should be shown</returns>
    public string? GetShownImprovement(IGameContextCivilization? viewingCiv) =>
        viewingCiv == null || viewingCiv.PlayerType == PlayerType.AI || viewingCiv.IsSpectator() ? Improvement
        : viewingCiv.GetLastSeenImprovement(Position);

    /// <summary>Returns true if this tile has fallout or an equivalent terrain feature</summary>
    public bool HasFalloutEquivalent() =>
        TerrainFeatures.Any(it => ((IHasUniques)Ruleset.Terrains[it]).HasUnique(UniqueType.NullifyYields));

    public int GetRow() => HexMath.GetRow(Position);
    public int GetColumn() => HexMath.GetColumn(Position);

    public Terrain GetBaseTerrain() =>
        baseTerrainObject ?? throw new InvalidOperationException("lateinit property baseTerrainObject has not been initialized");

    public IGameContextCivilization? GetOwner() => GetCity()?.Civ;

    public IGameContextCivilization? GetRoadOwner() => roadOwnerObject ?? GetOwner();

    public bool IsFriendlyTerritory(IGameContextCivilization civInfo)
    {
        var tileOwner = GetOwner();
        if (tileOwner == null) return false;
        if (tileOwner == civInfo) return true;
        if (!civInfo.Knows(tileOwner)) return false;
        return tileOwner.IsConsideredFriendlyTerritoryOf(civInfo);
    }

    public bool IsEnemyTerritory(IGameContextCivilization civInfo)
    {
        var tileOwner = GetOwner();
        if (tileOwner == null) return false;
        return civInfo.IsAtWarWith(tileOwner);
    }

    public bool IsRoughTerrain() => AllTerrains.Any(it => it.IsRough);

    /// <summary>Checks whether any of the TERRAINS of this tile has a certain unique</summary>
    public bool TerrainHasUnique(UniqueType uniqueType, GameContext? state = null) =>
        CachedTerrainData.Uniques.HasMatchingUnique(uniqueType, state ?? StateThisTile);

    /// <summary>Get all uniques of this type that any TERRAIN on this tile has</summary>
    public IEnumerable<Unique> GetTerrainMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null) =>
        CachedTerrainData.Uniques.GetMatchingUniques(uniqueType, gameContext ?? StateThisTile);

    /// <summary>Get all uniques of this type that any part of this tile has: terrains, improvement, resource</summary>
    public IEnumerable<Unique> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null)
    {
        var context = gameContext ?? StateThisTile;
        var uniques = GetTerrainMatchingUniques(uniqueType, context);
        var tileImprovement = GetUnpillagedTileImprovement();
        if (tileImprovement != null)
            uniques = uniques.Concat(((IHasUniques)tileImprovement).GetMatchingUniques(uniqueType, context));
        if (TileResource != null)
            uniques = uniques.Concat(((IHasUniques)TileResource).GetMatchingUniques(uniqueType, context));
        return uniques;
    }

    public IGameContextCity? GetWorkingCity()
    {
        var civInfo = GetOwner();
        if (civInfo == null) return null;
        if (OwningCity?.IsWorked(this) == true) return OwningCity; // common case
        return civInfo.Cities.FirstOrDefault(it => it != OwningCity && it.IsWorked(this));
    }

    public bool IsBlockaded()
    {
        var owner = GetOwner();
        if (owner == null) return false;
        var unit = MilitaryUnit;

        if (unit != null)
        {
            if (unit.Civ == owner) return false;            // Own - unblocks tile;
            if (unit.Civ.IsAtWarWith(owner)) return true;   // Enemy - blocks tile;
            return false;                                   // Neutral - unblocks tile;
        }
        if (IsLand) // Only water tiles are blocked if empty
            return false;

        // For water tiles need also to check neighbors:
        // enemy military naval units blockade all adjacent water tiles.
        foreach (var neighbor in Neighbors.Where(it => it.IsWater))
        {
            var neighborUnit = neighbor.MilitaryUnit;
            if (neighborUnit == null) continue;

            // Embarked units do not blockade adjacent tiles
            if (neighborUnit.Civ.IsAtWarWith(owner) && !neighborUnit.IsEmbarked())
                return true;
        }
        return false;
    }

    public bool IsWorked() => GetWorkingCity() != null;

    public bool ProvidesYield()
    {
        if (GetCity() == null) return false;
        return IsCityCenter()
            || IsWorked()
            || (GetUnpillagedTileImprovement() is { } improvement
                && ((IHasUniques)improvement).HasUnique(UniqueType.TileProvidesYieldWithoutPopulation, StateThisTile))
            || TerrainHasUnique(UniqueType.TileProvidesYieldWithoutPopulation);
    }

    public bool IsLocked()
    {
        var workingCity = GetWorkingCity();
        return workingCity != null && workingCity.IsTileLocked(Position);
    }

    public bool ProvidesResources(IGameContextCivilization civInfo)
    {
        var resource = TileResource; // To avoid additional checks later
        if (!civInfo.CanSeeResource(resource)) return false;
        // canSeeResource is false for a null resource, so resource is not null from here on
        if (IsCityCenter())
        {
            var possibleImprovements = resource!.GetImprovements();
            if (possibleImprovements.Count == 0) return true;
            // Per Civ V, resources under city tiles require the *possibility of extraction* -
            //  that is, there needs to be a tile improvement you have the tech for.
            // Does NOT take all GetImprovementBuildingProblems into account.
            return possibleImprovements.Any(improvement =>
                Ruleset.TileImprovements.GetValueOrDefault(improvement) is { } it &&
                    it.TurnsToBuild != -1 && // Buildable by workers (not just 'placeable')
                    (it.TechRequired == null || civInfo.IsTechResearched(it.TechRequired)));
        }
        var unpillagedImprovement = GetUnpillagedTileImprovement();
        if (unpillagedImprovement != null && resource!.GetImprovements().Contains(unpillagedImprovement.Name)
                && (unpillagedImprovement.TechRequired == null || civInfo.IsTechResearched(unpillagedImprovement.TechRequired))
            ) return true;
        // TODO(T054): Generic-ify to unique (upstream note)
        return resource!.ResourceType == ResourceType.Strategic &&
            unpillagedImprovement != null &&
            unpillagedImprovement.IsGreatImprovement();
    }

    /// <summary>This should be the only adjacency function</summary>
    public bool IsAdjacentTo(string terrainFilter, IGameContextCivilization? observingCiv = null)
    {
        // Rivers are odd, as they aren't technically part of any specific tile but still count towards adjacency
        if (terrainFilter == Constants.River) return IsAdjacentToRiver();
        if (terrainFilter == Constants.FreshWater && IsAdjacentToRiver()) return true;
        return Neighbors.Any(neighbor => neighbor.MatchesFilter(terrainFilter, observingCiv));
    }

    /// <summary>Implements <c>UniqueParameterType.TileFilter</c></summary>
    public bool MatchesFilter(string filter, IGameContextCivilization? civInfo = null) =>
        MultiFilter.Filter(filter, it => MatchesSingleFilter(it, civInfo));

    private bool MatchesSingleFilter(string filter, IGameContextCivilization? civInfo = null)
    {
        if (MatchesSingleTerrainFilter(filter, civInfo)) return true;
        if ((Improvement == null || ImprovementIsPillaged) && filter == "unimproved") return true;
        if (Improvement != null && !ImprovementIsPillaged && filter == "improved") return true;
        if (IsPillaged() && filter == "pillaged") return true;
        if (filter == "worked" && IsWorked()) return true;
        if (GetUnpillagedTileImprovement()?.MatchesFilter(filter, StateThisTile, false) == true) return true;
        return GetUnpillagedRoadImprovement()?.MatchesFilter(filter, StateThisTile, false) == true;
    }

    /// <summary>Implements <c>UniqueParameterType.TerrainFilter</c></summary>
    public bool MatchesTerrainFilter(string filter, IGameContextCivilization? observingCiv, bool multiFilter = true) =>
        multiFilter ? MultiFilter.Filter(filter, it => MatchesSingleTerrainFilter(it, observingCiv))
        : MatchesSingleTerrainFilter(filter, observingCiv);

    private bool MatchesSingleTerrainFilter(string filter, IGameContextCivilization? observingCiv)
    {
        switch (filter)
        {
            case "Terrain": return true;
            case Constants.UppercaseAll:
            case Constants.LowercaseAll: return true;
            case "Water": return IsWater;
            case "Land": return IsLand;
            case Constants.Coastal: return IsAdjacentToCoast();
            case Constants.River: return IsAdjacentToRiver();
            case "Unowned": return GetOwner() == null;
            case "your": return observingCiv != null && GetOwner() == observingCiv;
            case "Foreign Land":
            case "Foreign": return observingCiv != null && !IsFriendlyTerritory(observingCiv);
            case "Friendly Land":
            case "Friendly": return observingCiv != null && IsFriendlyTerritory(observingCiv);
            case "Enemy Land":
            case "Enemy": return observingCiv != null && IsEnemyTerritory(observingCiv);
            case "resource": return observingCiv != null && observingCiv.CanSeeResource(TileResource);
            case "Water resource": return IsWater && observingCiv != null && observingCiv.CanSeeResource(TileResource);
            case "Featureless": return TerrainFeatures.Count == 0;
            case "Open terrain": return AllTerrains.All(it => !it.IsRough); // special case - if *one* terrain is open, we don't care, we need *all*
            case Constants.FreshWaterFilter:
                return IsAdjacentTo(Constants.FreshWater, observingCiv);
        }

        if (filter == baseTerrain) return true;
        if (filter == Resource) return observingCiv == null || observingCiv.CanSeeResource(TileResource);

        if (AllTerrains.Any(it => it.MatchesFilter(filter, StateThisTile, false))) return true;

        var owner = GetOwner();
        if (owner != null && owner.MatchesFilter(filter, StateThisTile, false)) return true;

        // Checks 'luxury resource', 'strategic resource' and 'bonus resource' - only those that are visible of course
        // not using canSeeResource as observingCiv is often not passed in,
        // and we want to be able to at least test for non-strategic in that case.
        // Resource type check is last - cannot succeed if no resource here
        var resourceObject = TileResource;
        if (resourceObject == null) return false;
        var hasResourceWithFilter =
            resourceObject.Name == filter ||
                ((IHasUniques)resourceObject).HasTagUnique(filter, StateThisTile) ||
                (filter.EndsWith(" resource", StringComparison.Ordinal) ? filter[..^" resource".Length] : filter) == resourceObject.ResourceType.ToString();
        if (!hasResourceWithFilter) return false;

        // Now that we know that this resource matches the filter - can the observer see that there's a resource here?
        if (resourceObject.RevealedBy == null) return true;  // no need for tech
        if (observingCiv == null) return true;  // can't check tech, assume visible - consistent with the direct resource-name check above
        return observingCiv.CanSeeResource(resourceObject);
    }

    public bool IsAdjacentToCoast() => IsLand && IsAdjacentToCoastLazy;

    public List<Tile> GetViewableTilesList(int distance) => TileMap.GetViewableTiles(Position, distance);
    public IEnumerable<Tile> GetTilesInDistance(int distance) => TileMap.GetTilesInDistance(Position, distance);
    public IEnumerable<Tile> GetTilesInDistanceRange(IntRange range) => TileMap.GetTilesInDistanceRange(Position, range);
    public IEnumerable<Tile> GetTilesAtDistance(int distance) => TileMap.GetTilesAtDistance(Position, distance);

    public void ForEachTileInDistance(int distance, Action<Tile> op) => TileMap.ForEachTileInDistance(Position, distance, _ => true, op);
    public void ForEachTileInDistance(int distance, Func<Tile, bool> filter, Action<Tile> op) => TileMap.ForEachTileInDistance(Position, distance, filter, op);
    public void ForEachTileInDistanceRange(IntRange range, Action<Tile> op) => TileMap.ForEachTileInDistanceRange(Position, range, _ => true, op);
    public void ForEachTileInDistanceRange(IntRange range, Func<Tile, bool> filter, Action<Tile> op) => TileMap.ForEachTileInDistanceRange(Position, range, filter, op);
    public void ForEachTileAtDistance(int distance, Action<Tile> op) => TileMap.ForEachTileAtDistance(Position, distance, _ => true, op);
    public void ForEachTileAtDistance(int distance, Func<Tile, bool> filter, Action<Tile> op) => TileMap.ForEachTileAtDistance(Position, distance, filter, op);

    public float GetDefensiveBonus(bool includeImprovementBonus = true, IGameContextUnit? unit = null)
    {
        var bonus = baseTerrainObject!.DefenceBonus;
        if (TerrainFeatureObjects.Count > 0)
        {
            var otherTerrainBonus = TerrainFeatureObjects.Max(it => it.DefenceBonus);
            if (otherTerrainBonus != 0f) bonus = otherTerrainBonus;  // replaces baseTerrainObject
        }
        if (NaturalWonder != null) bonus += GetNaturalWonder().DefenceBonus;
        var tileImprovement = GetUnpillagedTileImprovement();
        if (tileImprovement != null && includeImprovementBonus)
        {
            var uniques = (IHasUniques)tileImprovement;
            uniques.ForEachMatchingUnique(UniqueType.DefensiveBonus, unit?.CacheState ?? StateThisTile, unique =>
            {
                bonus += ParseFloat(unique.Params[0]) / 100;
            });
        }
        return bonus;
    }

    /// <param name="otherTile">Destination tile</param>
    /// <returns>Shortest distance from this <see cref="Tile"/> to <paramref name="otherTile"/> in count of tiles including impassable tiles but not including origin tile</returns>
    public int AerialDistanceTo(Tile otherTile)
    {
        var positionHexcoord = Position;
        var otherPositionHexcoord = otherTile.Position;

        var xDelta = positionHexcoord.x - otherPositionHexcoord.x;
        var yDelta = positionHexcoord.y - otherPositionHexcoord.y;
        var distance = Math.Max(Math.Max(Math.Abs(xDelta), Math.Abs(yDelta)), Math.Abs(xDelta - yDelta));

        if (!TileMap.MapParameters.WorldWrap || distance <= TileMap.Width / 2) return distance;

        var otherTileUnwrappedPos = TileMap.GetUnwrappedPosition(otherPositionHexcoord);
        var xDeltaWrapped = positionHexcoord.x - otherTileUnwrappedPos.x;
        var yDeltaWrapped = positionHexcoord.y - otherTileUnwrappedPos.y;
        var wrappedDistance = Math.Max(Math.Max(Math.Abs(xDeltaWrapped), Math.Abs(yDeltaWrapped)), Math.Abs(xDeltaWrapped - yDeltaWrapped));

        return Math.Min(distance, wrappedDistance);
    }

    public bool CanBeSettled(IGameContextCivilization civ)
    {
        var modConstants = TileMap.GameInfo.GetModConstants();
        if (IsWater || IsImpassible()) return false;
        if (GetTilesInDistance(modConstants.MinimalCityDistanceOnDifferentContinents)
                .Any(it => it.IsCityCenter() && it.GetContinent() != GetContinent())) return false;
        if (GetTilesInDistance(modConstants.MinimalCityDistance)
                .Any(it => it.IsCityCenter() && it.GetContinent() == GetContinent())) return false;
        // cannot settle in someone else's territory
        if (OwningCity != null && OwningCity.Civ != civ) return false;
        return true;
    }

    /// <summary>The two tiles have a river between them</summary>
    public bool IsConnectedByRiver(Tile otherTile)
    {
        if (otherTile == this) throw new InvalidOperationException("Should not be called to compare to self!");

        return TileMap.GetNeighborTileClockPosition(this, otherTile) switch
        {
            2 => otherTile.HasBottomLeftRiver, // we're to the bottom-left of it
            4 => HasBottomRightRiver, // we're to the top-left of it
            6 => HasBottomRiver, // we're directly above it
            8 => HasBottomLeftRiver, // we're to the top-right of it
            10 => otherTile.HasBottomRightRiver, // we're to the bottom-right of it
            12 => otherTile.HasBottomRiver, // we're directly below it
            _ => throw new InvalidOperationException("Should never call this function on a non-neighbor!"),
        };
    }

    private bool isAdjacentToRiver;
    private bool isAdjacentToRiverKnown;

    public bool IsAdjacentToRiver()
    {
        if (!isAdjacentToRiverKnown)
        {
            isAdjacentToRiver =
                // These are so if you add a river at the bottom of the map (no neighboring tile to be connected to)
                //   that tile is still considered adjacent to river
                HasBottomLeftRiver || HasBottomRiver || HasBottomRightRiver
                    || Neighbors.Any(IsConnectedByRiver);
            isAdjacentToRiverKnown = true;
        }
        return isAdjacentToRiver;
    }

    /// <summary>Allows resetting the cached value <see cref="IsAdjacentToRiver"/> will return</summary>
    /// <param name="isKnownTrue">Set this to indicate you need to update the cache due to <b>adding</b> a river edge
    /// (removing would need to look at other edges, and that is what isAdjacentToRiver will do)</param>
    private void ResetAdjacentToRiverTransient(bool isKnownTrue = false)
    {
        isAdjacentToRiver = isKnownTrue;
        isAdjacentToRiverKnown = isKnownTrue;
    }

    /// <returns>whether units of <paramref name="civInfo"/> can pass through this tile, considering only civ-wide filters.
    /// Use <c>UnitMovement.canPassThrough</c> to check whether a specific unit can pass through a tile.</returns>
    public bool CanCivPassThrough(IGameContextCivilization civInfo)
    {
        var tileOwner = GetOwner();
        // comparing the CivInfo objects is cheaper than comparing strings
        if (tileOwner == null || tileOwner == civInfo) return true;
        if (IsCityCenter() && civInfo.IsAtWarWith(tileOwner)
                && !GetCity()!.HasJustBeenConquered) return false;
        return civInfo.CanPassThroughTiles(tileOwner);
    }

    public bool HasConnection(IGameContextCivilization civInfo) =>
        GetUnpillagedRoad() != RoadStatus.None || ForestOrJungleAreRoads(civInfo);

    public bool HasRoadConnection(IGameContextCivilization civInfo, bool mustBeUnpillaged) =>
        mustBeUnpillaged
            ? GetUnpillagedRoad() == RoadStatus.Road || ForestOrJungleAreRoads(civInfo)
            : RoadStatus == RoadStatus.Road || ForestOrJungleAreRoads(civInfo);

    public bool HasRailroadConnection(bool mustBeUnpillaged) =>
        mustBeUnpillaged
            ? GetUnpillagedRoad() == RoadStatus.Railroad
            : RoadStatus == RoadStatus.Railroad;

    public RoadStatus GetConnectionStatus(IGameContextCivilization civInfo)
    {
        var roadType = GetUnpillagedRoad();
        if (roadType != RoadStatus.None)
            return roadType;
        if (ForestOrJungleAreRoads(civInfo))
            return RoadStatus.Road;
        return RoadStatus.None;
    }

    private bool ForestOrJungleAreRoads(IGameContextCivilization civInfo) =>
        civInfo.ForestsAndJunglesAreRoads
            && (TerrainFeatures.Contains(Constants.Jungle) || TerrainFeatures.Contains(Constants.Forest))
            && IsFriendlyTerritory(civInfo);

    /// <remarks>Deviation: Kotlin returns a <c>HashSet</c> (JVM hash order), this one keeps insertion order.</remarks>
    public LinkedHashSet<string> GetRulesetIncompatibility(Ruleset ruleset)
    {
        var output = new LinkedHashSet<string>();
        if (!ruleset.Terrains.ContainsKey(BaseTerrain))
            output.Add($"Base terrain [{BaseTerrain}] does not exist in ruleset!");
        foreach (var terrainFeature in TerrainFeatures.Where(it => !ruleset.Terrains.ContainsKey(it)))
            output.Add($"Terrain feature [{terrainFeature}] does not exist in ruleset!");
        if (Resource != null && !ruleset.TileResources.ContainsKey(Resource))
            output.Add($"Resource [{Resource}] does not exist in ruleset!");
        if (Improvement != null && !ruleset.TileImprovements.ContainsKey(Improvement))
            output.Add($"Improvement [{Improvement}] does not exist in ruleset!");
        if (NaturalWonder != null && !ruleset.Terrains.ContainsKey(NaturalWonder))
            output.Add($"Natural Wonder [{NaturalWonder}] does not exist in ruleset!");
        return output;
    }

    public int GetContinent() => continent;

    /// <summary>Checks if this tile is marked as target tile for a building with a <see cref="UniqueType.CreatesOneImprovement"/> unique</summary>
    public bool IsMarkedForCreatesOneImprovement() =>
        TurnsToImprovement < 0 && ImprovementInProgress != null;

    /// <summary>Checks if this tile is marked as target tile for a building with a <see cref="UniqueType.CreatesOneImprovement"/> unique creating a specific <paramref name="improvement"/></summary>
    public bool IsMarkedForCreatesOneImprovement(string improvement) =>
        TurnsToImprovement < 0 && ImprovementInProgress == improvement;

    private double ApproximateMajorDepositDistribution()
    {
        // We can't replicate the MapRegions resource distributor, so let's try to get
        // a close probability of major deposits per tile
        var probability = 0.0;
        foreach (var unique in AllTerrains.SelectMany(it => ((IHasUniques)it).GetMatchingUniques(UniqueType.MajorStrategicFrequency)))
        {
            if (!int.TryParse(unique.Params[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frequency)) continue;
            if (frequency <= 0) continue;
            // The unique param is literally "every N tiles", so to get a probability p=1/f
            probability += 1.0 / frequency;
        }
        return probability == 0.0 ? 0.04  // This is the default of 1 per 25 tiles
            : probability;
    }

    /// <summary>Will be false if this is a "fake tile" - either created for calculation purposes,
    /// or to display how things look e.g. in Civilopedia</summary>
    public bool IsTilemapInitialized() => tileMap != null;

    #endregion
    #region State-changing functions

    /// <summary>Do not run this on cloned tiles, since then the cloned *units* will be assigned to the civs
    /// Instead run setTerrainTransients</summary>
    public void SetTransients()
    {
        SetTerrainTransients();
        SetUnitTransients(true);
        SetOwnerTransients();
    }

    public void SetTerrainTransients()
    {
        if (!Ruleset.Terrains.ContainsKey(BaseTerrain))
            throw new InvalidOperationException($"Terrain {BaseTerrain} does not exist in ruleset!");
        baseTerrainObject = Ruleset.Terrains[BaseTerrain];
        SetTerrainFeatures(TerrainFeatures);
        IsWater = GetBaseTerrain().Type == TerrainType.Water;
        IsLand = GetBaseTerrain().Type == TerrainType.Land;
        IsOcean = GetBaseTerrain().IsOcean;

        // Resource amounts missing - Old save or bad mapgen?
        if (IsTilemapInitialized() && TileResource?.ResourceType == ResourceType.Strategic && ResourceAmount == 0)
        {
            // Let's assume it's a small deposit
            SetTileResource(TileResource, majorDeposit: false);
        }
    }

    public void SetUnitTransients(bool unitCivTransients)
    {
        foreach (var unit in GetUnits())
        {
            unit.SetCurrentTile(this);
            if (unitCivTransients)
                unit.AssignOwner(TileMap.GameInfo.GetCivilization(unit.Owner), false);
            unit.SetTransients(Ruleset);
        }
    }

    public void SetOwnerTransients()
    {
        // If it has an owning city, the state was already set in setOwningCity
        if (OwningCity == null)
            StateThisTile = new GameContext(tile: this,
                // When generating maps we call this function but there's no gameinfo
                gameInfo: TileMap.HasGameInfo() ? TileMap.GameInfo : null);

        if (OwningCity == null && roadOwner != "")
        {
            if (roadOwnerObject == null && TileMap.HasGameInfo())
                roadOwnerObject = TileMap.GameInfo.GetCivilization(roadOwner);
            GetRoadOwner()!.NeutralRoads.Add(Position);
        }
    }

    public void SetOwningCity(IGameContextCity? city)
    {
        if (roadOwner != "" && roadOwnerObject == null)
            roadOwnerObject = TileMap.GameInfo.GetCivilization(roadOwner);
        if (city != null)
        {
            if (RoadStatus != RoadStatus.None && roadOwner != "")
            {
                // remove previous neutral tile owner
                GetRoadOwner()!.NeutralRoads.Remove(Position);
            }
            roadOwner = city.Civ.CivId; // only when taking control, otherwise last owner
            roadOwnerObject = city.Civ;
        }
        else if (RoadStatus != RoadStatus.None && OwningCity != null)
        {
            // Razing City! Remove owner
            roadOwner = "";
            roadOwnerObject = null;
        }
        OwningCity = city;
        StateThisTile = new GameContext(tile: this, city: city, gameInfo: TileMap.GameInfo);
        isCityCenterInternal = GetCity()?.Location == Position;
    }

    /// <summary>
    /// Sets this tile's <see cref="Resource"/> field but not its <see cref="ResourceAmount"/> fields. If <paramref name="updateCache"/> is <c>true</c>
    /// it will also set the <see cref="TileResource"/> transient field
    /// </summary>
    public void SetTileResource(string? newResource, bool updateCache = true)
    {
        if (updateCache)
        {
            TileResource = newResource == null ? null : Ruleset.TileResources.GetValueOrDefault(newResource);
            return;
        }
        Resource = newResource;
    }

    /// <summary>
    /// Sets this tile's <see cref="Resource"/> and, if <paramref name="newResource"/> is a Strategic resource, <see cref="ResourceAmount"/> fields.
    /// <see cref="ResourceAmount"/> is determined by <see cref="MapParameters.MapResources"/> and <paramref name="majorDeposit"/>, and
    /// if the latter is <c>null</c> a random choice between major and minor deposit is made, approximating
    /// the frequency MapRegions would use.
    /// </summary>
    /// <param name="newResource">The resource to place.</param>
    /// <param name="majorDeposit"><c>true</c> major, <c>false</c> minor, <c>null</c> chosen randomly.</param>
    /// <param name="rng">The randomness source for that step. Deviation: Kotlin defaults to <c>Random.Default</c>; the engine has no
    /// unseeded randomness, so <c>null</c> with a <c>null</c> <paramref name="majorDeposit"/> throws.</param>
    public void SetTileResource(TileResource newResource, bool? majorDeposit = null, GameRandom? rng = null)
    {
        TileResource = newResource;

        if (newResource.ResourceType != ResourceType.Strategic)
        {
            ResourceAmount = 0;
            return;
        }

        foreach (var unique in ((IHasUniques)newResource).GetMatchingUniques(UniqueType.ResourceAmountOnTiles, StateThisTile))
        {
            if (MatchesTerrainFilter(unique.Params[0], null))
            {
                ResourceAmount = int.Parse(unique.Params[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
                return;
            }
        }

        var majorDepositFinal = majorDeposit
            ?? (rng ?? throw new InvalidOperationException("A random source is required to choose the deposit size")).NextDouble() < ApproximateMajorDepositDistribution();
        var depositAmounts = majorDepositFinal ? newResource.MajorDepositAmount : newResource.MinorDepositAmount;
        var mapResources = TileMap.MapParameters.MapResources;
        ResourceAmount = mapResources == MapResourceSetting.Sparse.GetLabel() ? depositAmounts.Sparse
            : mapResources == MapResourceSetting.Abundant.GetLabel() ? depositAmounts.Abundant
            : depositAmounts.Default;
    }

    public void SetTerrainFeatures(IReadOnlyList<string> terrainFeatureList)
    {
        TerrainFeatures = terrainFeatureList;
        TerrainFeatureObjects = terrainFeatureList.Select(it => Ruleset.Terrains.GetValueOrDefault(it)).OfType<Terrain>().ToList();
        var all = new List<Terrain> { baseTerrainObject! }; // There is an assumption here that base terrains do not change
        if (NaturalWonder != null) all.Add(GetNaturalWonder());
        all.AddRange(TerrainFeatureObjects);
        AllTerrains = all; // Save in memory

        UpdateUniqueMap();

        if (TerrainFeatures.Count > 0)
            LastTerrain = Ruleset.Terrains.GetValueOrDefault(TerrainFeatures[^1])
                ?? GetBaseTerrain();  // defense against rare edge cases involving baseTerrain Hill deprecation
        else if (NaturalWonder != null)
            LastTerrain = GetNaturalWonder();
        else
            LastTerrain = GetBaseTerrain();

        UnitHeight = AllTerrains.SelectMany(it => ((IHasUniques)it).GetMatchingUniques(UniqueType.VisibilityElevation))
            .Sum(it => ParseInt(it.Params[0]));
        TileHeight = TerrainHasUnique(UniqueType.BlocksLineOfSightAtSameElevation) ? UnitHeight + 1
            : UnitHeight;
    }

    private void UpdateUniqueMap()
    {
        if (!IsTilemapInitialized()) return; // This tile is a fake tile, for visual display only (e.g. map editor, civilopedia)
        var terrainNameList = AllTerrains.Select(it => it.Name).ToList();

        // List hash is function of all its items, so the same items in the same order will always give the same hash
        CachedTerrainData = TileMap.GetOrPutTileUniqueMap(terrainNameList, () => new TerrainListData(
            new UniqueMap(AllTerrains.SelectMany(it => ((IHasUniques)it).UniqueObjects)),
            new HashSet<string>(terrainNameList)));
    }

    public void AddTerrainFeature(string terrainFeature)
    {
        if (!TerrainFeatures.Contains(terrainFeature))
            SetTerrainFeatures([.. TerrainFeatures, terrainFeature]);
    }

    public void RemoveTerrainFeature(string terrainFeature)
    {
        if (TerrainFeatures.Contains(terrainFeature))
        {
            var list = new List<string>(TerrainFeatures);
            list.Remove(terrainFeature);
            SetTerrainFeatures(list);
        }
    }

    public void RemoveTerrainFeatures() => SetTerrainFeatures([]);

    /// <summary>Clean stuff missing in <paramref name="ruleset"/> - called from TileMap.removeMissingTerrainModReferences.
    /// Must be able to run before <see cref="SetTransients"/> - and does not need to fix transients.</summary>
    public void RemoveMissingTerrainModReferences(Ruleset ruleset)
    {
        TerrainFeatures = TerrainFeatures.Where(it => ruleset.Terrains.ContainsKey(it)).ToList();
        if (Resource != null && !ruleset.TileResources.ContainsKey(Resource))
            TileResource = null;
        if (Improvement != null && !ruleset.TileImprovements.ContainsKey(Improvement))
            Improvement = null;
        if (ImprovementQueue.Any(it => !ruleset.TileImprovements.ContainsKey(it.Improvement)))
            ImprovementQueue.Clear(); // Just get rid of everything, all bets are off
        if (NaturalWonder != null && !ruleset.Terrains.ContainsKey(NaturalWonder))
            NaturalWonder = null;
    }

    /// <summary>If the unit isn't in the ruleset we can't even know what type of unit this is! So check each place
    /// This works with no transients so can be called from gameInfo.setTransients with no fear</summary>
    public void RemoveUnit(IGameContextUnit mapUnit)
    {
        if (AirUnits.Contains(mapUnit)) AirUnits.Remove(mapUnit);
        else if (CivilianUnit == mapUnit) CivilianUnit = null;
        else if (MilitaryUnit == mapUnit) MilitaryUnit = null;
    }

    public void SetImprovementBasic(TileImprovement? tileImprovement)
    {
        if (tileImprovement == null)
        {
            TileImprovement = null;
            Improvement = null;
        }
        else
        {
            TileImprovement = tileImprovement;
            Improvement = tileImprovement.Name;
        }
    }

    public void SetImprovementBasic(string improvementName)
    {
        var tileImprovement = Ruleset.TileImprovements.GetValueOrDefault(improvementName);
        TileImprovement = tileImprovement;
        Improvement = tileImprovement?.Name;
    }

    // function handling when removing a road from the tile
    public void RemoveRoad() => SetRoadStatus(RoadStatus.None, null);

    public void SetRoadStatus(RoadStatus newRoadStatus, IGameContextCivilization? creatingCivInfo)
    {
        RoadStatus = newRoadStatus;
        RoadIsPillaged = false;
        var currentOwner = GetOwner();
        if (newRoadStatus == RoadStatus.None && OwningCity == null)
            GetRoadOwner()?.NeutralRoads.Remove(Position);
        else if (currentOwner != null)
        {
            // Owned tiles must not fall through to the neutral-road branch just because
            // the owner field is already correct (city founding sets ownership first).
            if (currentOwner != roadOwnerObject)
            {
                roadOwner = currentOwner.CivId;
                roadOwnerObject = currentOwner;
            }
        }
        else if (creatingCivInfo != null)
        {
            roadOwner = creatingCivInfo.CivId; // neutral tile, use building unit
            roadOwnerObject = creatingCivInfo;
            creatingCivInfo.NeutralRoads.Add(Position);
        }
    }

    /// <summary>Clear road owner, for console <c>civ remove</c></summary>
    public void RemoveRoadOwner()
    {
        roadOwner = "";
        roadOwnerObject = null;
    }

    /// <summary>Clears <see cref="ImprovementQueue"/></summary>
    public void StopWorkingOnImprovement()
    {
        if (IsMarkedForCreatesOneImprovement()) return;
        ImprovementQueue.Clear();
    }

    /// <summary>Adds an entry to the <see cref="ImprovementQueue"/> with explicit <paramref name="turnsToImprovement"/></summary>
    public void QueueImprovement(string improvementName, int turnsToImprovement) =>
        ImprovementQueue.Add(new ImprovementQueueEntry(improvementName, turnsToImprovement));

    public void SetRepaired()
    {
        ImprovementQueue.Clear();
        if (ImprovementIsPillaged)
            ImprovementIsPillaged = false;
        else
            RoadIsPillaged = false;

        OwningCity?.ReassignPopulationDeferred();
    }

    public void SetExplored(IGameContextCivilization player, bool isExplored, HexCoord? explorerPosition = null)
    {
        if (isExplored)
        {
            // Upstream disables the undo button here (GUI.clearUndoCheckpoints): a client concern.
            if (!exploredBy.Contains(player.CivId))
                exploredBy = ((IReadOnlySet<string>)exploredBy).WithItem(player.CivId);

            if (player.PlayerType == PlayerType.Human)
                player.CheckExploredRegionTilePosition(Position, explorerPosition);
        }
        else
        {
            exploredBy = ((IReadOnlySet<string>)exploredBy).WithoutItem(player.CivId);
        }
    }

    /// <summary>
    /// Assign a continent ID to this tile.
    /// Should only be set once at map generation.
    /// </summary>
    /// <param name="continent">Numeric ID &gt;= 0</param>
    /// <exception cref="InvalidOperationException">when tile already has a continent ID</exception>
    public void SetContinent(int continent)
    {
        if (this.continent != -1)
            throw new InvalidOperationException($"Continent already assigned @ {Position}");
        this.continent = continent;
    }

    /// <summary>Clear continent ID, for map editor</summary>
    public void ClearContinent() => continent = -1;

    /// <summary>
    /// Sets the "has river" state of one edge of this Tile. Works for all six directions.
    /// </summary>
    /// <param name="otherTile">The neighbor tile in the direction the river we wish to change is (If it's not a neighbor, this does nothing).</param>
    /// <param name="newValue">The new river edge state: <c>true</c> to create a river, <c>false</c> to remove one.</param>
    /// <returns>The state did change (<c>false</c>: the edge already had the <paramref name="newValue"/>)</returns>
    public bool SetConnectedByRiver(Tile otherTile, bool newValue)
    {
        Tile owner;
        int edge; // 0 = bottom-left, 1 = bottom-right, 2 = bottom
        switch (TileMap.GetNeighborTileClockPosition(this, otherTile))
        {
            case 2: (owner, edge) = (otherTile, 0); break; // we're to the bottom-left of it
            case 4: (owner, edge) = (this, 1); break; // we're to the top-left of it
            case 6: (owner, edge) = (this, 2); break; // we're directly above it
            case 8: (owner, edge) = (this, 0); break; // we're to the top-right of it
            case 10: (owner, edge) = (otherTile, 1); break; // we're to the bottom-right of it
            case 12: (owner, edge) = (otherTile, 2); break; // we're directly below it
            default: return false;
        }
        var current = edge == 0 ? owner.HasBottomLeftRiver : edge == 1 ? owner.HasBottomRightRiver : owner.HasBottomRiver;
        if (current == newValue) return false;
        if (edge == 0) owner.HasBottomLeftRiver = newValue;
        else if (edge == 1) owner.HasBottomRightRiver = newValue;
        else owner.HasBottomRiver = newValue;
        foreach (var tile in new[] { this, otherTile })
            tile.ResetAdjacentToRiverTransient(newValue);
        return true;
    }

    #endregion

    private static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    #region Overrides

    /// <summary>Shows important properties of this tile for debugging _only_, it helps to see what you're doing</summary>
    public override string ToString()
    {
        var lineList = new List<string> { $"Tile @{Position}" };
        if (baseTerrain == null) return lineList[0] + ", uninitialized";
        if (IsCityCenter()) lineList.Add(GetCity()!.Name);
        lineList.Add(BaseTerrain);
        foreach (var terrainFeature in TerrainFeatures) lineList.Add(terrainFeature);
        if (TileResource != null)
        {
            lineList.Add(TileResource.ResourceType == ResourceType.Strategic
                ? $"{{{ResourceAmount}}} {{{Resource}}}"
                : Resource!);
        }
        if (NaturalWonder != null) lineList.Add(NaturalWonder);
        if (RoadStatus != RoadStatus.None && !IsCityCenter()) lineList.Add(RoadStatus.ToString());
        if (Improvement != null) lineList.Add(Improvement);
        if (CivilianUnit != null) lineList.Add(CivilianUnit.Name + " - " + CivilianUnit.Civ.CivId);
        if (MilitaryUnit != null) lineList.Add(MilitaryUnit.Name + " - " + MilitaryUnit.Civ.CivId);
        if (baseTerrainObject != null && IsImpassible()) lineList.Add(Constants.Impassable);
        return string.Join(", ", lineList);
    }

    #endregion
}
