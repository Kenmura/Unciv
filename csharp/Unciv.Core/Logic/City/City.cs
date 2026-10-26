using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.City;

// Ported from core/src/com/unciv/logic/city/City.kt (T060).
//
// City is ported BEFORE its sub-objects (seam rule, T053 notes): population, cityConstructions, expansion, religion and cityStats are typed
// by their seams in CivilizationContracts.cs, as lateinit-style properties (reading before assignment throws) because the Kotlin
// initializers (`var population = CityPopulationManager()`) have nothing to construct yet. The ticket porting a sub-object replaces its
// property by `= new XManager()` and retypes it (T061 did population, expansion, religion; T062: cityStats; T063: cityConstructions).
// Kotlin City has no equals/hashCode: this class keeps reference identity.
//
// Stripped / deferred:
// - timeThis profiling wrappers and the purity annotations.
// - T093: getLandAttackPath / getAmphibiousAttackPath / getRoadPath / getRoadPathToAny, their PathingMap caches and clearCaches live in
//   CityPathing.cs (a partial of this class).
// - annexCity / puppetCity / liberateCity / moveToCiv: CityConquestFunctions (T064).
// - resetSpecialistsControl reads UncivGame.Current.settings (client state): its two inputs are explicit parameters.
// - T128: getGarrison, unitToPromotions and isConnectedToCapital(predicate) live in CityUnitAndConnectionAccess.cs (a partial of this class) so
//   this file does not depend on the MapUnits / Civilization.Transients namespaces.
// - Kotlin HashSet<Tile> tilesInRange is a LinkedHashSet (identity-hashed Tile keys have no deterministic JVM order); it is only read by
//   getWorkableTiles / workTile, in tile order.

/// <summary>Kotlin <c>CityFlags</c></summary>
public enum CityFlags
{
    WeLoveTheKing,
    ResourceDemand,
    Resistance,
}

public partial class City : IsPartOfGameInfoSerialization, INamed, IConstructionCity, ILocalUniqueCacheCity
{
    private static T Lateinit<T>(T? value, string name) where T : class =>
        value ?? throw new InvalidOperationException($"lateinit property {name} has not been initialized");

    #region Transient fields
    private CivilizationObject? civ;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public CivilizationObject Civ
    {
        get => Lateinit(civ, "civ");
        set => civ = value;
    }

    private TileClass? centerTile; // cached for better performance

    private TileMap? tileMap;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public TileMap TileMap
    {
        get => Lateinit(tileMap, "tileMap");
        set => tileMap = value;
    }

    private LinkedHashSet<TileClass>? tilesInRange;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public LinkedHashSet<TileClass> TilesInRange
    {
        get => Lateinit(tilesInRange, "tilesInRange");
        set => tilesInRange = value;
    }

    [JsonIgnore]
    public GameContext State { get; set; } = GameContext.EmptyState;

    /// <summary>Transient. This is so that military units can enter the city, even before we decide what to do with it</summary>
    [JsonIgnore]
    public bool HasJustBeenConquered { get; set; }
    #endregion

    #region Serialized fields
    [JsonPropertyName("location")]
    public HexCoord Location { get; set; } = new();
    [JsonPropertyName("id")]
    public string Id { get; set; } = NoId;
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    /// <summary>Serialization field for <see cref="FoundingCivObject"/>. Is equivalent to <c>foundingCivObject.civName</c></summary>
    [JsonPropertyName("foundingCiv")]
    [JsonInclude]
    private string foundingCiv = "";
    /// <summary>This is so that cities in resistance that are recaptured aren't in resistance anymore</summary>
    [JsonPropertyName("previousOwner")]
    public string PreviousOwner { get; set; } = "";
    [JsonPropertyName("turnAcquired")]
    public int TurnAcquired { get; set; }
    [JsonPropertyName("health")]
    public int Health { get; set; } = 200;

    [JsonPropertyName("population")]
    public CityPopulationManager Population { get; set; } = new();

    [JsonPropertyName("cityConstructions")]
    public CityConstructions CityConstructions { get; set; } = new();

    [JsonPropertyName("expansion")]
    public CityExpansionManager Expansion { get; set; } = new();

    [JsonPropertyName("religion")]
    public CityReligionManager Religion { get; set; } = new();

    /// <summary>Transient: class carries no persisted fields.</summary>
    [JsonIgnore]
    public CityEspionageManager Espionage { get; set; } = new();

    /// <summary>Effect: moved to disabled section in construction list, and not built during automation</summary>
    [JsonPropertyName("disabledConstructions")]
    [JsonInclude]
    public JavaHashSet<string> DisabledConstructions { get; private set; } = new();

    public void ResetDisabledConstructions()
    {
        DisabledConstructions.Clear();
        if (Civ.IsHuman())
            foreach (var construction in Civ.DisabledCityConstructions) DisabledConstructions.Add(construction);
    }

    private CityStats? cityStats;
    /// <summary>Transient: CityStats has no serializable fields (Kotlin <c>var cityStats = CityStats(this)</c>).</summary>
    [JsonIgnore]
    public CityStats CityStats
    {
        get => cityStats ??= new CityStats(this);
        set => cityStats = value;
    }

    [JsonPropertyName("resourceStockpiles")]
    public Counter<string> ResourceStockpiles { get; set; } = new();

    /// <summary>All tiles that this city controls</summary>
    [JsonPropertyName("tiles")]
    public JavaHashSet<HexCoord> Tiles { get; set; } = new();

    /// <summary>Tiles that have population assigned to them</summary>
    [JsonPropertyName("workedTiles")]
    public JavaHashSet<HexCoord> WorkedTiles { get; set; } = new();

    /// <summary>Tiles that the population in them won't be reassigned</summary>
    [JsonPropertyName("lockedTiles")]
    public JavaHashSet<HexCoord> LockedTiles { get; set; } = new();

    [JsonPropertyName("manualSpecialists")]
    public bool ManualSpecialists { get; set; }

    /// <summary>
    /// Kotlin <c>resetSpecialistsControl()</c> reads <c>UncivGame.Current.settings</c>, which is client state a headless engine does not have:
    /// <paramref name="autoAssignSpecialistsInNewCities"/> is the civ's own <c>autoAssignSpecialistsInNewCities</c> (T183: the UserId check of
    /// "if we skip a player's turn in multiplayer, let's not apply our settings" is gone, each civ has its own settings).
    /// </summary>
    public void ResetSpecialistsControl(bool autoAssignSpecialistsInNewCities)
    {
        ManualSpecialists = Civ.IsHuman() && !autoAssignSpecialistsInNewCities;
    }

    [JsonPropertyName("isBeingRazed")]
    public bool IsBeingRazed { get; set; }
    [JsonPropertyName("attackedThisTurn")]
    public bool AttackedThisTurn { get; set; }
    [JsonPropertyName("hasSoldBuildingThisTurn")]
    public bool HasSoldBuildingThisTurn { get; set; }
    [JsonPropertyName("isPuppet")]
    public bool IsPuppet { get; set; }
    /// <summary>flag so that on startTurn() we reassign population</summary>
    [JsonPropertyName("shouldReassignPopulation")]
    public bool ShouldReassignPopulation { get; set; }

    [JsonPropertyName("unitShouldUseSavedPromotion")]
    public JavaHashMap<string, bool> UnitShouldUseSavedPromotion { get; set; } = new();

    [JsonPropertyName("cityAIFocus")]
    [JsonInclude]
    private string cityAIFocus = CityFocus.NoFocus.ToString();

    [JsonPropertyName("avoidGrowth")]
    public bool AvoidGrowth { get; set; }
    /// <summary>Transient. temporary variable saved for rankSpecialist()</summary>
    [JsonIgnore]
    public int CurrentGPPBonus { get; set; }

    /// <summary>
    /// The very first found city is the _original_ capital,
    /// while the _current_ capital can be any other city after the original one is captured.
    /// It is important to distinguish them since the original cannot be razed and defines the Domination Victory.
    /// </summary>
    [JsonPropertyName("isOriginalCapital")]
    public bool IsOriginalCapital { get; set; }

    /// <summary>For We Love the King Day</summary>
    [JsonPropertyName("demandedResource")]
    public string DemandedResource { get; set; } = "";

    [JsonPropertyName("flagsCountdown")]
    [JsonInclude]
    internal JavaHashMap<string, int> FlagsCountdown { get; set; } = new();

    /// <summary>Persisted connected-to-capital (by any medium) to allow "disconnected" notifications after loading</summary>
    [JsonPropertyName("connectedToCapitalStatus")]
    public bool ConnectedToCapitalStatus { get; set; }
    #endregion

    /// <summary>Neighboring explored cities, within <paramref name="maxDistance"/> tiles</summary>
    public List<City> GetNeighboringCities(int maxDistance = 12) =>
        Civ.GameInfo.GetCities().Where(it => it != this
            && it.GetCenterTile().AerialDistanceTo(GetCenterTile()) <= maxDistance
            && it.GetCenterTile().IsExplored(Civ)).ToList();

    public CityFocus GetCityFocus() => CityFocusExtensions.Entries.FirstOrDefault(it => it.ToString() == cityAIFocus, CityFocus.NoFocus);
    public void SetCityFocus(CityFocus cityFocus) { cityAIFocus = cityFocus.ToString(); }

    private CivilizationObject? foundingCivObject;
    /// <summary>
    /// Civ object for the original founder of this city
    ///
    /// Setting this also sets its backing serialization string <c>foundingCiv</c>
    /// </summary>
    [JsonIgnore]
    public CivilizationObject? FoundingCivObject
    {
        get
        {
            if (foundingCivObject == null && foundingCiv.Length != 0) foundingCivObject = (CivilizationObject)Civ.GameInfo.GetCivilization(foundingCiv);
            return foundingCivObject;
        }
        set
        {
            foundingCivObject = value;
            foundingCiv = value?.CivId ?? "";
        }
    }

    public bool HasDiplomaticMarriage() => FoundingCivObject == null;

    #region pure functions
    public City Clone()
    {
        var toReturn = new City
        {
            Location = Location,
            Id = Id != NoId ? Id : PseudoRandomId(Civ),
            Name = Name,
            Health = Health,
            Population = Population.Clone(),
            CityConstructions = CityConstructions.Clone(),
            Expansion = Expansion.Clone(),
            Religion = Religion.Clone(),
        };
        foreach (var construction in DisabledConstructions) toReturn.DisabledConstructions.Add(construction);
        // T219: Kotlin shares these with the clone; the clone gets its own copies (iterating exactly alike), keeping the source's aliasing
        // (ReassignPopulation makes WorkedTiles the very set LockedTiles is).
        toReturn.Tiles = Tiles.Clone();
        toReturn.LockedTiles = LockedTiles.Clone();
        toReturn.WorkedTiles = ReferenceEquals(WorkedTiles, LockedTiles) ? toReturn.LockedTiles : WorkedTiles.Clone();
        toReturn.ResourceStockpiles = ResourceStockpiles.Clone();
        toReturn.IsBeingRazed = IsBeingRazed;
        toReturn.AttackedThisTurn = AttackedThisTurn;
        toReturn.HasSoldBuildingThisTurn = HasSoldBuildingThisTurn;
        toReturn.foundingCiv = foundingCiv;
        toReturn.PreviousOwner = PreviousOwner;
        toReturn.TurnAcquired = TurnAcquired;
        toReturn.IsPuppet = IsPuppet;
        toReturn.IsOriginalCapital = IsOriginalCapital;
        toReturn.FlagsCountdown.PutAll(FlagsCountdown);
        toReturn.DemandedResource = DemandedResource;
        toReturn.ShouldReassignPopulation = ShouldReassignPopulation;
        toReturn.cityAIFocus = cityAIFocus;
        toReturn.AvoidGrowth = AvoidGrowth;
        toReturn.ManualSpecialists = ManualSpecialists;
        toReturn.ConnectedToCapitalStatus = ConnectedToCapitalStatus;
        toReturn.UnitShouldUseSavedPromotion = UnitShouldUseSavedPromotion.Clone();
        toReturn.UnitToPromotions = UnitToPromotions.Clone(it => it.Clone());
        return toReturn;
    }

    public bool CanBombard() => GetBombardRange() > 0 && !AttackedThisTurn && !IsInResistance();
    public TileClass GetCenterTile() => Lateinit(centerTile, "centerTile");
    public TileClass? GetCenterTileOrNull() => centerTile;
    public IEnumerable<TileClass> GetTiles() => Tiles.Select(it => TileMap[it]);
    public IEnumerable<TileClass> GetWorkableTiles() => TilesInRange.Where(it => ReferenceEquals(it.GetOwner(), Civ));
    public IEnumerable<TileClass> GetWorkedTiles() => WorkedTiles.Select(it => TileMap[it]);
    public bool IsWorked(TileClass tile) => WorkedTiles.Contains(tile.Position);

    public bool IsCapital() => CityConstructions.BuiltBuildingUniqueMap.HasUnique(UniqueType.IndicatesCapital, State);
    public bool IsCoastal() => GetCenterTile().IsAdjacentToCoast();
    public bool IsNaval() => GetCenterTile().IsWater || IsCoastal();

    public int GetBombardRange() => Civ.GameInfo.Ruleset.ModOptions.Constants.BaseCityBombardRange;
    public int GetWorkRange() => Civ.GameInfo.Ruleset.ModOptions.Constants.CityWorkRange;
    public int GetExpandRange() => Civ.GameInfo.Ruleset.ModOptions.Constants.CityExpandRange;

    public bool IsConnectedToCapital() => Civ.Cache.CitiesConnectedToCapitalToMediums.ContainsKey(this);

    public bool IsGarrisoned() => GetGarrison() != null;

    public bool HasFlag(CityFlags flag) => FlagsCountdown.ContainsKey(flag.ToString());
    public int GetFlag(CityFlags flag) => FlagsCountdown[flag.ToString()];

    public bool IsWeLoveTheKingDayActive() => HasFlag(CityFlags.WeLoveTheKing);
    public bool IsInResistance() => HasFlag(CityFlags.Resistance);

    public bool IsBlockaded()
    {
        // Coastal cities are blocked if every adjacent water tile is blocked
        if (!IsCoastal()) return false;
        return GetCenterTile().Neighbors.Where(it => it.IsWater).All(it => it.IsBlockaded());
    }

    public Ruleset GetRuleset() => Civ.GameInfo.Ruleset;

    public ResourceSupplyList GetResourcesGeneratedByCity() => CityResources.GetResourcesGeneratedByCity(this);
    public int GetAvailableResourceAmount(string resourceName) => CityResources.GetAvailableResourceAmount(this, resourceName);
    public int GetAvailableResourceAmount(TileResource resource) => CityResources.GetAvailableResourceAmount(this, resource);

    public bool IsGrowing() => FoodForNextTurn() > 0;
    public bool IsStarving() => FoodForNextTurn() < 0;

    public int FoodForNextTurn() => KotlinMath.RoundToInt(CityStats.CurrentCityStats.Food);

    public bool ContainsBuildingUnique(UniqueType uniqueType, GameContext? state = null) =>
        CityConstructions.BuiltBuildingUniqueMap.GetMatchingUniques(uniqueType, state ?? State).Any();

    public int GetGreatPersonPercentageBonus() => GreatPersonPointsBreakdown.GetGreatPersonPercentageBonus(this);
    public Counter<string> GetGreatPersonPoints() => new GreatPersonPointsBreakdown(this).Sum();

    public void GainStockpiledResource(TileResource resource, int amount)
    {
        if (resource.IsCityWide) ResourceStockpiles.Add(resource.Name, amount);
        else Civ.ResourceStockpiles.Add(resource.Name, amount);
    }

    public void AddStat(Stat stat, int amount)
    {
        switch (stat)
        {
            case Stat.Production: CityConstructions.AddProductionPoints(amount); break;
            case Stat.Food: Population.FoodStored += amount; break;
            default: Civ.AddStat(stat, amount); break;
        }
    }

    /// <summary>Kotlin takes a <c>GameResource</c> (Stat, SubStat or TileResource), passed as <c>object</c> like Civilization does.</summary>
    public int GetGameResource(object gameResource) => gameResource switch
    {
        TileResource resource => GetAvailableResourceAmount(resource),
        Stat stat => GetStatReserve(stat),
        SubStat.StoredFood => Population.FoodStored,
        _ => Civ.GetGameResource(gameResource), // assume it's a global substat - as of now only golden age points
    };

    public void AddGameResource(object stat, int amount)
    {
        if (stat is TileResource resource)
        {
            if (!resource.IsStockpiled) return;
            GainStockpiledResource(resource, amount);
            return;
        }
        if (stat is Stat.Production)
        {
            CityConstructions.AddProductionPoints(amount);
        }
        else if (stat is Stat.Food or SubStat.StoredFood)
        {
            Population.FoodStored += amount;
        }
        else
        {
            Civ.AddGameResource(stat, amount);
        }
    }

    public int GetStatReserve(Stat stat) => stat switch
    {
        Stat.Production => CityConstructions.GetWorkDone(CityConstructions.GetCurrentConstruction().Name),
        Stat.Food => Population.FoodStored,
        _ => Civ.GetStatReserve(stat),
    };

    public bool HasStatToBuy(Stat stat, int price) =>
        Civ.GameInfo.GameParameters.GodMode ? true
        : price == 0 ? true
        : GetStatReserve(stat) >= price;

    internal int GetMaxHealth() => 200 + CityConstructions.GetBuiltBuildings().Aggregate(0, (sum, it) => unchecked(sum + it.CityHealth));

    public float GetStrength()
    {
        // Kotlin sumOf { Double } then toFloat()
        double sum = 0.0;
        foreach (var building in CityConstructions.GetBuiltBuildings()) sum += building.CityStrength;
        return (float)sum;
    }

    /// <summary>Gets max air units that can remain in the city untransported</summary>
    public int GetMaxAirUnits() => Civ.GameInfo.Ruleset.ModOptions.Constants.CityAirUnitCapacity +
        GetMatchingUniques(UniqueType.CarryExtraAirUnits)
            .Where(it => it.Params[1] == "Air")
            .Aggregate(0, (sum, it) => unchecked(sum + int.Parse(it.Params[0], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture)));

    public override string ToString() => Name; // for debug

    public bool IsHolyCity() => Religion.ReligionThisIsTheHolyCityOf != null && !Religion.IsBlockedHolyCity;
    public bool IsHolyCityOf(string? religionName) => IsHolyCity() && Religion.ReligionThisIsTheHolyCityOf == religionName;

    public bool CanBeDestroyed(bool justCaptured = false)
    {
        if (Civ.GameInfo.GameParameters.NoCityRazing) return false;

        var allowRazeCapital = ((IHasUniques)Civ.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.AllowRazeCapital);
        var allowRazeHolyCity = ((IHasUniques)Civ.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.AllowRazeHolyCity);

        if (IsOriginalCapital && !allowRazeCapital) return false;
        if (IsHolyCity() && !allowRazeHolyCity) return false;
        if (IsCapital() && !justCaptured && !allowRazeCapital) return false;

        return true;
    }
    #endregion

    #region state-changing functions
    public bool LockTile(TileClass tile)
    {
        if (!IsWorked(tile)) throw new ArgumentException($"Cannot lock tile {tile.Position} — not worked by {Name}");
        return LockedTiles.Add(tile.Position);
    }

    public bool UnlockTile(TileClass tile) => LockedTiles.Remove(tile.Position);

    public bool WorkTile(TileClass tile)
    {
        if (!GetWorkableTiles().Contains(tile)) throw new ArgumentException($"Tile {tile.Position} is not workable by {Name}");
        if (IsWorked(tile)) return false;
        WorkedTiles = WithItem(WorkedTiles, tile.Position);
        return true;
    }

    public bool StopWorkingTile(TileClass tile)
    {
        if (!IsWorked(tile)) return false;
        UnlockTile(tile);
        WorkedTiles = WithoutItem(WorkedTiles, tile.Position);
        return true;
    }

    public void ClearWorkedTiles()
    {
        WorkedTiles = JavaHashSet<HexCoord>.HashSetOf();
        LockedTiles.Clear();
    }

    public void SetTransients(CivilizationObject civInfo)
    {
        Civ = civInfo;
        Id = Id != NoId ? Id : PseudoRandomId(Civ);
        TileMap = civInfo.GameInfo.TileMap;
        centerTile = TileMap[Location];
        State = GameContext.ForCity(this);
        TilesInRange = new LinkedHashSet<TileClass>(GetCenterTile().GetTilesInDistance(GetWorkRange()));
        Population.City = this;
        Expansion.City = this;
        Expansion.SetTransients();
        CityConstructions.City = this;
        Religion.SetTransients(this);
        CityConstructions.SetTransients();
        Espionage.SetTransients(this);
    }

    public void SetFlag(CityFlags flag, int amount, bool adjustWithGameSpeed = false)
    {
        FlagsCountdown.Put(flag.ToString(),
            adjustWithGameSpeed ? KotlinMath.RoundToInt(amount * Civ.GameInfo.Speed.Modifier)
            : amount);
    }

    public void RemoveFlag(CityFlags flag)
    {
        FlagsCountdown.Remove(flag.ToString());
    }

    public void ResetWLTKD()
    {
        // Removes the flags for we love the king & resource demand
        // The resource demand flag will automatically be readded with 15 turns remaining, see startTurn()
        RemoveFlag(CityFlags.WeLoveTheKing);
        RemoveFlag(CityFlags.ResourceDemand);
        DemandedResource = "";
    }

    /// <summary>
    /// Reassign all Specialists and Unlock all tiles
    /// Mainly for automated cities, Puppets, just captured
    /// </summary>
    public void ReassignAllPopulation()
    {
        ManualSpecialists = false;
        ReassignPopulation(resetLocked: true);
    }

    /// <summary>
    /// Apply worked tiles optimization (aka CityFocus) - Expensive!
    ///
    /// If the next City.startTurn is soon enough, then use <see cref="ReassignPopulationDeferred"/> instead.
    /// </summary>
    public void ReassignPopulation(bool resetLocked = false)
    {
        if (resetLocked)
        {
            WorkedTiles = JavaHashSet<HexCoord>.HashSetOf();
            LockedTiles = JavaHashSet<HexCoord>.HashSetOf();
        }
        else if (cityAIFocus != CityFocus.Manual.ToString())
        {
            WorkedTiles = LockedTiles;
        }
        if (!ManualSpecialists)
            Population.SpecialistAllocations.Clear();
        ShouldReassignPopulation = false;
        Population.AutoAssignPopulation();
    }

    /// <summary>
    /// Apply worked tiles optimization (aka CityFocus) -
    /// immediately for a human player whoes turn it is (interactive),
    /// or deferred to the next startTurn while nextTurn is running (for AI)
    /// </summary>
    /// <seealso cref="ShouldReassignPopulation"/>
    public void ReassignPopulationDeferred()
    {
        if (Civ.IsActive() && Civ.IsHuman()) ReassignPopulation();
        else ShouldReassignPopulation = true;
    }

    public void DestroyCity(bool overrideSafeties = false)
    {
        // Original capitals and holy cities cannot be destroyed,
        // unless, of course, they are captured by a one-city-challenger.
        if (!CanBeDestroyed() && !overrideSafeties) return;
        GameEventHooks.CityDestroyed(this);

        // Destroy planes stationed in city
        foreach (var airUnit in GetCenterTile().AirUnits.ToList()) airUnit.Destroy();

        // Evacuate spies BEFORE relinquishing tile ownership, because spy lookup uses tile.owningCity
        // to find which city a spy is stationed in (after save/load when the transient city field is null).
        // If we relinquish ownership first, owningCity becomes null and spies are not found/evacuated.
        Espionage.RemoveAllPresentSpies(SpyFleeReason.CityDestroyed);

        // The relinquish ownership MUST come before removing the city,
        // because it updates the city stats which assumes there is a capital, so if you remove the capital it crashes
        foreach (var tile in GetTiles())
        {
            Expansion.RelinquishOwnership(tile);
        }

        // Move the capital if destroyed (by a nuke or by razing)
        // Must be before removing existing capital because we may be annexing a puppet which means city stats update - see #8337
        if (IsCapital()) Civ.MoveCapitalToNextLargest(null);

        Civ.Cities = Civ.Cities.WithoutItem(this);

        if (GetRuleset().TileImprovements.ContainsKey("City ruins"))
            GetCenterTile().SetImprovement("City ruins");

        // Edge case! What if a water unit is in a city, and you raze the city?
        // Well, the water unit has to return to the water!
        foreach (var unit in GetCenterTile().GetUnits().ToList())
        {
            if (!unit.Movement.CanPassThrough(GetCenterTile()))
                unit.Movement.TeleportToClosestMoveableTile();
        }

        // Update proximity rankings for all civs
        foreach (var otherCiv in Civ.GameInfo.GetAliveMajorCivs())
        {
            Civ.UpdateProximity(otherCiv,
                otherCiv.UpdateProximity(Civ));
        }
        foreach (var otherCiv in Civ.GameInfo.GetAliveCityStates())
        {
            Civ.UpdateProximity(otherCiv,
                otherCiv.UpdateProximity(Civ));
        }
    }

    public void AnnexCity() => new CityConquestFunctions(this).AnnexCity();

    /// <summary>This happens when we either puppet OR annex, basically whenever we conquer a city and don't liberate it</summary>
    public void PuppetCity(CivilizationObject conqueringCiv) => new CityConquestFunctions(this).PuppetCity(conqueringCiv);

    /// <summary>Liberating is returning a city to its founder - makes you LOSE warmongering points</summary>
    public void LiberateCity(CivilizationObject conqueringCiv) => new CityConquestFunctions(this).LiberateCity(conqueringCiv);

    public void MoveToCiv(CivilizationObject newCivInfo) => new CityConquestFunctions(this).MoveToCiv(newCivInfo);

    /// <summary>Seam for <c>Battle</c> and <c>TradeLogic</c> (ported, T083/T086): tests install a stand-in. Not serialized, not cloned.</summary>
    internal ICityConquestExternals ConquestExternals { get; set; } = CityManagerHooks.RealConquest;

    internal void TryUpdateRoadStatus()
    {
        var railroad = GetRuleset().RailroadImprovement;
        var road = GetRuleset().RoadImprovement;
        var requiredRoad =
            railroad != null && (railroad.TechRequired == null || Civ.Tech.TechsResearched.Contains(railroad.TechRequired)) ? RoadStatus.Railroad
            : road != null && (road.TechRequired == null || Civ.Tech.TechsResearched.Contains(road.TechRequired)) ? RoadStatus.Road
            : RoadStatus.None;
        GetCenterTile().SetRoadStatus(requiredRoad, Civ);
    }

    public int GetGoldForSellingBuilding(string buildingName) =>
        GetRuleset().Buildings[buildingName].Cost / 10;

    public void SellBuilding(string buildingName)
    {
        SellBuilding(GetRuleset().Buildings[buildingName]);
    }

    public void SellBuilding(Building building)
    {
        CityConstructions.RemoveBuilding(building);
        Civ.AddGold(GetGoldForSellingBuilding(building.Name));
        HasSoldBuildingThisTurn = true;

        Population.UnassignExtraPopulation(); // If the building provided specialists, release them to other work
        Population.AutoAssignPopulation(); // also updates city stats
        Civ.Cache.UpdateCivResources(); // this building could be a resource-requiring one
    }

    public bool CanPlaceNewUnit(BaseUnit construction)
    {
        var tile = GetCenterTile();
        return construction.IsCivilian() ? tile.CivilianUnit == null
            : construction.IsAirUnit() ? true // Dealt with in MapUnit.getRejectionReasons
            : tile.MilitaryUnit == null;
    }

    /// <summary>Implements UniqueParameterType.CityFilter</summary>
    public bool MatchesFilter(string filter) => MatchesFilter(filter, Civ, true);

    /// <summary>Implements UniqueParameterType.CityFilter; Kotlin's default of <paramref name="viewingCiv"/> is <c>civ</c>, see the overload above.</summary>
    public bool MatchesFilter(string filter, CivilizationObject? viewingCiv, bool multiFilter = true) =>
        multiFilter
            ? MultiFilter.Filter(filter, it => MatchesSingleFilter(it, viewingCiv))
            : MatchesSingleFilter(filter, viewingCiv);

    private bool MatchesSingleFilter(string filter, CivilizationObject? viewingCiv)
    {
        switch (filter)
        {
            case "in this city": return true; // Filtered by the way uniques are found
            case "in all cities": return true;
            case Constants.LowercaseAll: return true;
            case Constants.UppercaseAll: return true;
            case "in your cities":
            case "Your": return ReferenceEquals(viewingCiv, Civ);
            case "in all coastal cities":
            case "Coastal": return IsCoastal();
            case "in capital":
            case "Capital": return IsCapital();
            case "in all non-occupied cities":
            case "Non-occupied": return !CityStats.HasExtraAnnexUnhappiness() || IsPuppet;
            case "in all cities with a world wonder":
                return CityConstructions.GetBuiltBuildings().Any(it => it.IsWonder);
            case "in all cities connected to capital": return IsConnectedToCapital();
            case "in all cities with a garrison":
            case "Garrisoned": return IsGarrisoned();
            case "in all cities in which the majority religion is a major religion":
                return Religion.GetMajorityReligionName() != null
                    && Religion.GetMajorityReligion().NotNull().IsMajorReligion();
            case "in all cities in which the majority religion is an enhanced religion":
                return Religion.GetMajorityReligionName() != null
                    && Religion.GetMajorityReligion().NotNull().IsEnhancedReligion();
            case "in non-enemy foreign cities":
                return viewingCiv != null && !ReferenceEquals(viewingCiv, Civ)
                    && !Civ.IsAtWarWith(viewingCiv);
            case "in enemy cities":
            case "Enemy": return Civ.IsAtWarWith(viewingCiv ?? Civ);
            case "in foreign cities":
            case "Foreign": return viewingCiv != null && !ReferenceEquals(viewingCiv, Civ);
            case "in annexed cities":
            case "Annexed": return !ReferenceEquals(FoundingCivObject, Civ) && !IsPuppet;
            case "in puppeted cities":
            case "Puppeted": return IsPuppet;
            case "in resisting cities":
            case "Resisting": return IsInResistance();
            case "in cities being razed":
            case "Razing": return IsBeingRazed;
            case "in holy cities":
            case "Holy": return IsHolyCity();
            case "in City-State cities": return Civ.IsCityState;
            // This is only used in communication to the user indicating that only in cities with this
            // religion a unique is active. However, since religion uniques only come from the city itself,
            // this will always be true when checked.
            case "in cities following this religion": return true;
            case "in cities following our religion":
                return Equals(viewingCiv?.ReligionManager.Religion, Religion.GetMajorityReligion());
            default: return Civ.MatchesFilter(filter, State, false);
        }
    }

    // So everywhere in the codebase there were continuous calls to either
    // `cityConstructions.builtBuildingUniqueMap.getUniques()` or `cityConstructions.builtBuildingMap.getAllUniques()`,
    // which was fine as long as those were the only uniques that cities could provide.
    // However, with the introduction of religion, cities might also get uniques from the religion the city follows.
    // Adding both calls to `builtBuildingsUniqueMap` and `Religion` every time is not really modular and also ugly, so something had to be done.
    // Looking at all the use cases, the following functions were written to handle all findMatchingUniques() problems.
    // Sadly, due to the large disparity between use cases, there needed to be lots of functions.

    /// <summary>Finds matching uniques provided from both local and non-local sources. (<c>forEachMatchingUnique</c> is faster.)</summary>
    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null, bool includeCivUniques = true)
    {
        gameContext ??= State;
        if (includeCivUniques)
            return Civ.GetMatchingUniques(uniqueType, gameContext)
                .Concat(GetLocalMatchingUniques(uniqueType, gameContext));
        return CityConstructions.BuiltBuildingUniqueMap.GetUniques(uniqueType)
            .Concat(Religion.GetUniques(uniqueType))
            .Where(it => !it.IsTimedTriggerable && it.ConditionalsApply(gameContext))
            .SelectMany(it => it.GetMultiplied(gameContext));
    }

    public void ForEachMatchingUnique(UniqueType uniqueType, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, State, true, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op) =>
        ForEachMatchingUnique(uniqueType, gameContext, true, op);

    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, bool includeCivUniques, Action<UniqueObject> op)
    {
        if (includeCivUniques)
        {
            Civ.ForEachMatchingUnique(uniqueType, gameContext, op);
            ForEachLocalMatchingUnique(uniqueType, gameContext, op);
        }
        else
        {
            // Kotlin passes `state`, not `gameContext`, here
            CityConstructions.BuiltBuildingUniqueMap.ForEachMatchingUnique(uniqueType, State, IsTimedUniqueFilter, op);
            Religion.ForEachMatchingUnique(uniqueType, State, IsTimedUniqueFilter, op);
        }
    }

    /// <summary>Uniques special to this city</summary>
    public IEnumerable<UniqueObject> GetLocalMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null)
    {
        gameContext ??= State;
        var uniques = CityConstructions.BuiltBuildingUniqueMap.GetUniques(uniqueType).Where(it => it.IsLocalEffect)
            .Concat(Religion.GetUniques(uniqueType));
        return uniques.Where(it => !it.IsTimedTriggerable && it.ConditionalsApply(gameContext))
            .SelectMany(it => it.GetMultiplied(gameContext));
    }

    /// <summary>Uniques special to this city</summary>
    public void ForEachLocalMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        CityConstructions.BuiltBuildingUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, IsLocalUniqueFilter, op);
        Religion.ForEachMatchingUnique(uniqueType, gameContext, op);
    }

    /// <summary>Uniques coming from this city, but that should be provided globally</summary>
    public IEnumerable<UniqueObject> GetMatchingUniquesWithNonLocalEffects(UniqueType uniqueType, GameContext? gameContext = null)
    {
        gameContext ??= State;
        var uniques = CityConstructions.BuiltBuildingUniqueMap.GetUniques(uniqueType).ToList();
        // Memory performance showed that this function was very memory intensive, thus we only create the filter if needed
        return uniques.Count != 0
            ? uniques.Where(it => !it.IsLocalEffect && !it.IsTimedTriggerable && it.ConditionalsApply(gameContext))
                .SelectMany(it => it.GetMultiplied(gameContext))
            : uniques;
    }

    /// <summary>Uniques coming from this city, but that should be provided globally</summary>
    public void ForEachMatchingUniqueWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op) =>
        CityConstructions.BuiltBuildingUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, NonLocalUniqueFilter, op);

    /// <summary>
    /// All uniques affecting this city: both local uniques and civ uniques.
    /// This replaces LocalUniqueCache#forCityGetMatchingUniques
    /// </summary>
    public void ForEachAffectingMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        ForEachLocalMatchingUnique(uniqueType, gameContext, op);
        Civ.ForEachMatchingUnique(uniqueType, gameContext, op);
    }

    public IEnumerable<UniqueObject> GetTriggeredUniques(
        UniqueType trigger,
        GameContext? gameContext = null,
        Func<UniqueObject, bool>? triggerFilter = null,
        bool includeCivUniques = true)
    {
        gameContext ??= State;
        triggerFilter ??= _ => true;
        if (includeCivUniques)
        {
            return Civ.GetTriggeredUniques(trigger, gameContext, triggerFilter)
                .Concat(GetLocalTriggeredUniques(trigger, gameContext, triggerFilter));
        }
        var uniques = CityConstructions.BuiltBuildingUniqueMap.GetAllUniques().Concat(Religion.GetAllUniques());
        return uniques
            .Where(it => it.GetModifiers(trigger).Any(triggerFilter) && it.ConditionalsApply(gameContext))
            .SelectMany(it => it.GetMultiplied(gameContext));
    }

    public void ForEachTriggeredUnique(
        UniqueType trigger,
        GameContext? gameContext,
        Func<UniqueObject, bool>? triggerFilter,
        bool includeCivUniques,
        Action<UniqueObject> op)
    {
        gameContext ??= State;
        triggerFilter ??= _ => true;
        if (includeCivUniques)
        {
            Civ.ForEachTriggeredUnique(trigger, gameContext, triggerFilter, op);
            ForEachLocalTriggeredUnique(trigger, gameContext, triggerFilter, op);
        }
        else
        {
            bool Filter(UniqueObject unique) =>
                unique.GetModifiers(trigger).Any(triggerFilter) && unique.ConditionalsApply(gameContext);
            void MultipliedOp(UniqueObject unique) => unique.ForEachMultiplied(gameContext, op);
            CityConstructions.BuiltBuildingUniqueMap.ForEachUnique(Filter, MultipliedOp);
            Religion.ForEachUnique(Filter, MultipliedOp);
        }
    }

    public IEnumerable<UniqueObject> GetLocalTriggeredUniques(UniqueType trigger, GameContext? gameContext = null,
        Func<UniqueObject, bool>? triggerFilter = null)
    {
        gameContext ??= State;
        triggerFilter ??= _ => true;
        var uniques =
            CityConstructions.BuiltBuildingUniqueMap.GetAllUniques().Where(it => it.IsLocalEffect).Concat(Religion.GetAllUniques());
        return uniques
            .Where(it => it.GetModifiers(trigger).Any(triggerFilter) && it.ConditionalsApply(gameContext))
            .SelectMany(it => it.GetMultiplied(gameContext));
    }

    public void ForEachLocalTriggeredUnique(UniqueType trigger, GameContext gameContext, Action<UniqueObject> op) =>
        ForEachLocalTriggeredUnique(trigger, gameContext, _ => true, op);

    // UniqueMap lacks a way to iterate over all Uniques without allocations, so this is not *dramatically* faster than getLocalTriggeredUniques
    public void ForEachLocalTriggeredUnique(UniqueType trigger, GameContext gameContext,
        Func<UniqueObject, bool> triggerFilter, Action<UniqueObject> op)
    {
        bool UniqueFilter(UniqueObject unique) =>
            unique.GetModifiers(trigger).Any(triggerFilter) && unique.ConditionalsApply(gameContext);
        bool BuildingFilter(UniqueObject unique) =>
            unique.IsLocalEffect && UniqueFilter(unique);
        void MultipliedOp(UniqueObject unique) => unique.ForEachMultiplied(gameContext, op);
        CityConstructions.BuiltBuildingUniqueMap.ForEachUnique(BuildingFilter, MultipliedOp);
        Religion.ForEachUnique(UniqueFilter, MultipliedOp);
    }
    #endregion

    #region seam implementations
    // IGameContextCity and friends: the members the ported GameContext / Conditionals / Tile code reads (see the GameContext*.cs files).
    // They are explicit because Kotlin City has no such members: the Kotlin callers go through the sub-objects.
    IGameContextCivilization IGameContextCity.Civ => Civ;
    IConstructionCivilization IConstructionCity.Civ => Civ;
    ILocalUniqueCacheCivilization ILocalUniqueCacheCity.Civ => Civ;
    IGameContextTile? IGameContextCity.GetCenterTileOrNull() => centerTile;
    int IGameContextCity.GetStatReserve(Stat stat) => GetStatReserve(stat);
    bool IGameContextCity.IsTileLocked(HexCoord position) => LockedTiles.Contains(position);
    void IGameContextCity.ForEachMatchingUnique(UniqueType uniqueType, GameContext? gameContext, Action<UniqueObject> action) =>
        ForEachMatchingUnique(uniqueType, gameContext ?? State, action);
    void IGameContextCity.UpdateCityStats() => CityStats.Update();
    void IGameContextCity.RemoveCreateOneImprovementConstruction(string improvement) =>
        CityConstructions.RemoveCreateOneImprovementConstruction(improvement);
    bool IGameContextCity.MatchesFilter(string filter, IGameContextCivilization? viewingCiv) =>
        MatchesFilter(filter, (CivilizationObject?)viewingCiv, true);
    bool IGameContextCity.ContainsBuildingOrEquivalent(string buildingName) => CityConstructions.ContainsBuildingOrEquivalent(buildingName);
    IReadOnlySet<string> IGameContextCity.BuiltBuildings => new ReadOnlyJavaHashSet<string>(CityConstructions.BuiltBuildings);
    int IGameContextCity.CountBuiltBuildingsMatchingFilter(string filter, GameContext state) =>
        CityConstructions.GetBuiltBuildings().Count(it => it.MatchesFilter(filter, state));
    float IGameContextCity.GetCurrentCityStat(Stat stat) => CityStats.CurrentCityStats[stat];
    int IGameContextCity.GetResourcesGeneratedByCityAmount(string resourceName) => GetResourcesGeneratedByCity().SumBy(resourceName);
    IGameContextReligion? IGameContextCity.GetMajorityReligion() => Religion.GetMajorityReligion();
    int IGameContextCity.GetPopulationFilterAmount(string filter) => Population.GetPopulationFilterAmount(filter);
    bool IGameContextCity.CenterTileMilitaryUnitCanGarrison() => GetCenterTile().MilitaryUnit?.CanGarrison() == true;
    IEnumerable<IGameContextTile> IGameContextCity.GetWorkedTiles() => GetWorkedTiles();
    IEnumerable<IGameContextTile> IGameContextCity.GetTiles() => GetTiles();
    IEnumerable<UniqueObject> IConstructionCity.GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext) =>
        GetMatchingUniques(uniqueType, gameContext);
    #endregion

    public const string NoId = "00000000-0000-0000-0000-000000000000";

    public static string PseudoRandomId(CivilizationObject civ) =>
        Randomness.PseudoRandomUuid(new GameContext(civ).StateBasedRandom("City.Id", civ.Cities.Count)).ToString();

    public static readonly Func<UniqueObject, bool> IsLocalUniqueFilter = unique => unique.IsLocalEffect && !unique.IsTimedTriggerable;
    public static readonly Func<UniqueObject, bool> NonLocalUniqueFilter = unique => !unique.IsLocalEffect;
    public static readonly Func<UniqueObject, bool> IsTimedUniqueFilter = unique => !unique.IsTimedTriggerable;

    // Kotlin Set.withItem / withoutItem for HashSet: a copy (java.util.HashSet(Collection)) with the change; the original is not changed.
    private static JavaHashSet<T> WithItem<T>(JavaHashSet<T> set, T item) where T : notnull
    {
        var newSet = new JavaHashSet<T>(set);
        newSet.Add(item);
        return newSet;
    }

    private static JavaHashSet<T> WithoutItem<T>(JavaHashSet<T> set, T item) where T : notnull
    {
        var newSet = new JavaHashSet<T>(set);
        newSet.Remove(item);
        return newSet;
    }
}
