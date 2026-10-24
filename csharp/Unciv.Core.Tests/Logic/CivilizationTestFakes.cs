using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using DifficultyObject = Unciv.Models.Ruleset.Nation.Difficulty;
using MapUnit = Unciv.Logic.Map.MapUnits.MapUnit;
using SpeedObject = Unciv.Models.Ruleset.Speed;
using StatsObject = Unciv.Models.StatTypes.Stats;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using TileResourceObject = Unciv.Models.Ruleset.Tile.TileResource;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic;

// T055: stand-ins for the manager/city/unit/game-info seams of Civilization (CivilizationSeams.cs). Each fake holds data for what the
// tests read and throws for the rest.

internal sealed class FakeGameInfoState : FakeGameInfo, IGameInfoState
{
    public Ruleset RulesetData { get; set; } = new();
    public SpeedObject SpeedData { get; set; } = new() { Turns = [new Dictionary<string, float> { ["yearsPerTurn"] = 1f, ["untilTurn"] = 500f }] };
    public List<Civilization> Civs { get; } = [];
    public Civilization? CurrentPlayer { get; set; }
    public bool Simulation { get; set; }
    public GlobalUniques Globals { get; } = new();
    public Dictionary<string, string?> Votes { get; } = [];
    public DifficultyObject Difficulty { get; set; } = new() { Name = "Prince" };
    public TileMap? Map { get; set; }

    Ruleset IGameInfoState.Ruleset => RulesetData;
    SpeedObject IGameInfoState.Speed => SpeedData;
    IReadOnlyList<Civilization> IGameInfoState.Civilizations => Civs;
    Civilization? IGameInfoState.CurrentPlayerCiv => CurrentPlayer;
    bool IGameInfoState.IsSimulation() => Simulation;
    GlobalUniques IGameInfoState.GetGlobalUniques() => Globals;
    IDictionary<string, string?> IGameInfoState.DiplomaticVictoryVotesCast => Votes;
    DifficultyObject IConstructionGameInfo.GetDifficulty() => Difficulty;
    public JavaHashMap<string, Religion> ReligionMap { get; } = new();
    JavaHashMap<string, Religion> IGameInfoState.Religions => ReligionMap;
    public List<(bool IncludeCityStates, bool IncludeDefeated, Civilization? SortFirst)> CivsSortedCalls { get; } = [];
    /// <summary>Stand-in for GameInfo.getCivsSorted (T046): records the arguments and applies only the additional filter, in civ order.</summary>
    IEnumerable<Civilization> IGameInfoState.GetCivsSorted(bool includeCityStates, bool includeDefeated, Civilization? civToSortFirst, Func<Civilization, bool>? additionalFilter)
    {
        CivsSortedCalls.Add((includeCityStates, includeDefeated, civToSortFirst));
        return Civs.Where(it => additionalFilter == null || additionalFilter(it)).ToList();
    }
    public FakeBarbarianManager BarbarianFake { get; } = new();
    IBarbarianManager IGameInfoState.Barbarians => BarbarianFake;
    public override TileMap TileMap => Map ?? throw new NotSupportedException();
    public override IGameContextCivilization GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
    Civilization IGameInfoState.GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
    IEnumerable<CityObject> IGameInfoState.GetCities() => Civs.SelectMany(it => it.Cities);
    List<Civilization> IGameInfoState.GetAliveCityStates() => Civs.Where(it => it.IsAlive() && it.IsCityState).ToList();
    List<Civilization> IGameInfoState.GetAliveMajorCivs() => Civs.Where(it => it.IsAlive() && it.IsMajorCiv()).ToList();
    public List<(Civilization Civ, TileResourceObject Resource, int MaxDistance)> ExploredResourceNotifications { get; } = [];
    public void NotifyExploredResources(Civilization civInfo, TileResourceObject resource, int maxDistance) =>
        ExploredResourceNotifications.Add((civInfo, resource, maxDistance));
}

internal sealed class FakeBarbarianManager : IBarbarianManager
{
    public List<MapUnit> Spawned { get; } = [];
    public MapUnit? SpawnResult { get; set; }
    public MapUnit? SpawnBarbarian(TileClass tile, Civilization civ)
    {
        if (SpawnResult != null) Spawned.Add(SpawnResult);
        return SpawnResult;
    }
}

internal sealed class FakeUnitManager : IUnitManager
{
    public int Count { get; set; }
    public int GetCivUnitsSize() => Count;
    public List<IMapUnitState> CivUnits { get; } = [];
    public IEnumerable<IMapUnitState> GetCivUnits() => CivUnits;
    public List<(IGameContextUnit Unit, bool UpdateCivInfo)> RemovedUnits { get; } = [];
    public void RemoveUnit(IGameContextUnit unit, bool updateCivInfo) => RemovedUnits.Add((unit, updateCivInfo));
    public IGameContextUnit NewMapUnit(BaseUnit baseUnit, int? unitId) => throw new NotSupportedException();
    public List<(BaseUnit Unit, CityObject? City)> AddedUnits { get; } = [];
    public IMapUnitState? AddedUnitResult { get; set; }
    public IMapUnitState? AddUnit(BaseUnit baseUnit, CityObject? city = null)
    {
        AddedUnits.Add((baseUnit, city));
        return AddedUnitResult;
    }
    public List<(MapUnit Unit, bool UpdateCivInfo)> AssignedUnits { get; } = [];
    public void AddUnit(MapUnit mapUnit, bool updateCivInfo = true) => AssignedUnits.Add((mapUnit, updateCivInfo));
}

internal sealed class FakeQuestManager : IQuestManager
{
    public List<string> Met { get; } = [];
    public void JustMet(Civilization otherCiv) => Met.Add(otherCiv.CivId);
    public IQuestManager Clone() => throw new NotSupportedException();
    public void SetTransients(Civilization civInfo) => throw new NotSupportedException();
    public List<(string Civ, HexCoord Position)> ClearedCamps { get; } = [];
    public void BarbarianCampCleared(Civilization civ, HexCoord position) => ClearedCamps.Add((civ.CivId, position));
}

internal sealed class FakeCityStateFunctions : ICityStateFunctions
{
    public int TributeWillingness { get; set; }
    public int GetTributeWillingness(Civilization demandingCiv, bool demandingWorker = false) => TributeWillingness;
    public int AllyUpdates { get; private set; }
    public void UpdateAllyCivForCityState() => AllyUpdates++;
    public bool CanProvideStat(Stat statType) => false;
    public List<UniqueObject> Provided { get; } = [];
    public IEnumerable<UniqueObject> GetUniquesProvidedByCityStates(UniqueType uniqueType, GameContext gameContext) => Provided.Where(it => it.Type == uniqueType);
    public void ForEachUniqueProvidedByCityStates(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        foreach (var unique in GetUniquesProvidedByCityStates(uniqueType, gameContext)) op(unique);
    }
    public ResourceSupplyList GetCityStateResourcesForAlly() => new();
}

/// <summary>
/// A real <see cref="CityObject"/> whose sub-objects (population, constructions, stats, expansion, religion) are the data-holding fakes below,
/// which the tests fill. Converts implicitly to the city.
/// </summary>
internal sealed class FakeCityState
{
    public CityObject City { get; } = new();
    public FakeCityPopulation PopulationFake { get; } = new();
    public FakeCityConstructions ConstructionsFake { get; } = new();
    public FakeCityStats StatsFake { get; }
    public FakeCityExpansion ExpansionFake { get; } = new();
    public FakeCityReligion ReligionData { get; } = new();

    public FakeCityState(string id, FakeCiv civ)
    {
        _ = civ;
        StatsFake = new FakeCityStats(City);
        City.Id = id;
        City.Name = id;
        City.Population = PopulationFake;
        City.CityConstructions = ConstructionsFake;
        City.CityStats = StatsFake;
        City.Expansion = ExpansionFake;
        City.Religion = ReligionData;
        Registry.Add(City, this);
    }

    public static implicit operator CityObject(FakeCityState fake) => fake.City;

    public Civilization? CivData { set => City.Civ = value!; }
    public HexCoord LocationData { get => City.Location; set => City.Location = value; }
    public int PopulationData { get => PopulationFake.PopulationValue; set => PopulationFake.PopulationValue = value; }
    public UniqueMap BuiltUniques => ConstructionsFake.BuiltUniques;
    public List<Building> BuiltBuildingObjects => ConstructionsFake.BuiltBuildingObjects;

    private Building? greatPersonPointsBuilding;
    /// <summary>
    /// The base points <c>GreatPersonPointsBreakdown(city).sum()</c> returns (before the "is a unit of the ruleset" filter): the
    /// <c>greatPersonPoints</c> of a synthetic built building.
    /// </summary>
    public Counter<string> GreatPersonPoints
    {
        get
        {
            if (greatPersonPointsBuilding == null)
            {
                greatPersonPointsBuilding = new Building { Name = "Great person points test building" };
                BuiltBuildingObjects.Add(greatPersonPointsBuilding);
            }
            return greatPersonPointsBuilding.GreatPersonPoints;
        }
    }
    public List<Building> Completed => ConstructionsFake.Completed;
    public List<string> ConstructionQueue => ConstructionsFake.ConstructionQueue;
    public int InProgressValidations => ConstructionsFake.InProgressValidations;
    public Dictionary<string, HashSet<string>> FreeBuildingsProvidedFromThisCity => ConstructionsFake.FreeBuildingsProvidedFromThisCity;
    public HashSet<string> Buildings => ConstructionsFake.BuiltNames;
    public int StatsUpdateCalls => StatsFake.UpdateCalls;
    public Action<Building>? OnCompleteConstruction { get => ConstructionsFake.OnCompleteConstruction; set => ConstructionsFake.OnCompleteConstruction = value; }

    /// <summary>Whether the built buildings include one that "Indicates the capital city".</summary>
    public bool Capital
    {
        set
        {
            if (value) ConstructionsFake.BuiltUniques.AddUnique(new UniqueObject("Indicates the capital city", UniqueTarget.Building, "Palace"));
            else foreach (var unique in ConstructionsFake.BuiltUniques.GetAllUniques().ToList()) ConstructionsFake.BuiltUniques.RemoveUnique(unique);
        }
    }

    /// <summary>The tiles the city owns, by position (<c>city.tiles</c>).</summary>
    public TileLocationAdder TileLocations => new(City);

    internal sealed class TileLocationAdder(CityObject city)
    {
        public void AddRange(IEnumerable<HexCoord> positions)
        {
            foreach (var position in positions) city.Tiles.Add(position);
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CityObject, FakeCityState> Registry = new();

    /// <summary>The fake that built <paramref name="city"/>.</summary>
    public static FakeCityState Of(CityObject city) => Registry.TryGetValue(city, out var fake) ? fake : throw new InvalidOperationException("Not a FakeCityState city");

    /// <summary><c>city.shouldReassignPopulation</c> as the 0/1 count of deferred reassignments the tests care about.</summary>
    public int PopulationReassignments => City.ShouldReassignPopulation ? 1 : 0;

    /// <summary><c>city.cityStats.currentCityStats</c></summary>
    public StatsObject CurrentCityStats => StatsFake.CurrentCityStats;

    /// <summary>The uniques of the built buildings that only apply in this city (<c>builtBuildingUniqueMap</c> entries with "in this city").</summary>
    public UniqueMap LocalUniques => ConstructionsFake.BuiltUniques;

}

internal sealed class FakeCityPopulation : ICityPopulationManager
{
    public CityObject City { get; set; } = null!;
    public FakeCityPopulation? CloneResult { get; set; }
    public ICityPopulationManager Clone() => CloneResult ?? throw new NotSupportedException();
    public int PopulationValue { get; set; }
    public int Population => PopulationValue;
    public int FoodStored { get; set; }
    public Counter<string> SpecialistAllocations { get; } = new();
    public int AutoAssignCalls { get; private set; }
    public void AutoAssignPopulation() => AutoAssignCalls++;
    public int UnassignCalls { get; private set; }
    public void UnassignExtraPopulation() => UnassignCalls++;
    public Dictionary<string, int> ByFilter { get; } = [];
    public int GetPopulationFilterAmount(string filter) => ByFilter.GetValueOrDefault(filter);
    public Counter<string> GetNewSpecialists() => SpecialistAllocations;
    public int FreePopulation { get; set; }
    public int GetFreePopulation() => FreePopulation;
    public int GetNumberOfSpecialists() => SpecialistAllocations.SumValues();
}

internal sealed class FakeCityExpansion : ICityExpansionManager
{
    public CityObject City { get; set; } = null!;
    public void SetTransients() { }
    public FakeCityExpansion? CloneResult { get; set; }
    public ICityExpansionManager Clone() => CloneResult ?? throw new NotSupportedException();
    public List<Tile> Relinquished { get; } = [];
    public void RelinquishOwnership(Tile tile) => Relinquished.Add(tile);
}

internal sealed class FakeCityStats(CityObject city) : CityStats(city)
{
    public int UpdateCalls { get; private set; }
    public List<bool> UpdateCivStatsArguments { get; } = [];
    public override void Update(IConstruction? currentConstruction = null, bool updateTileStats = true, bool updateCivStats = true, bool calculateGrowthModifiers = true)
    {
        UpdateCalls++;
        UpdateCivStatsArguments.Add(updateCivStats);
    }
    public bool ExtraAnnexUnhappiness { get; set; }
    public override bool HasExtraAnnexUnhappiness() => ExtraAnnexUnhappiness;
}

internal sealed class FakeCityConstructions : ICityConstructions
{
    public CityObject City { get; set; } = null!;
    public void SetTransients() { }
    public FakeCityConstructions? CloneResult { get; set; }
    public ICityConstructions Clone() => CloneResult ?? throw new NotSupportedException();
    public UniqueMap BuiltUniques { get; } = new();
    public UniqueMap BuiltBuildingUniqueMap => BuiltUniques;
    public List<Building> BuiltBuildingObjects { get; } = [];
    public IEnumerable<Building> GetBuiltBuildings() => BuiltBuildingObjects;
    public HashSet<string> BuiltNames { get; } = [];
    public IReadOnlySet<string> BuiltBuildings => BuiltNames;
    public bool ContainsBuildingOrEquivalent(string buildingName) => BuiltNames.Contains(buildingName);
    public int ProductionAdded { get; private set; }
    public void AddProductionPoints(int productionToAdd) => ProductionAdded += productionToAdd;
    public int GetWorkDone(string constructionName) => 0;
    public IConstruction? CurrentConstruction { get; set; }
    public IConstruction GetCurrentConstruction() => CurrentConstruction ?? throw new NotSupportedException();
    public void AddBuilding(Building building) => BuiltBuildingObjects.Add(building);
    public void RemoveBuilding(Building building) => BuiltBuildingObjects.Remove(building);
    public void ChooseNextConstruction() => throw new NotSupportedException();
    public List<string> ConstructionQueue { get; } = [];
    public void EditQueue(Action<List<string>> edit)
    {
        var copy = new List<string>(ConstructionQueue);
        edit(copy);
        ConstructionQueue.Clear();
        ConstructionQueue.AddRange(copy);
    }
    public int InProgressValidations { get; private set; }
    public void ValidateInProgressConstructions() => InProgressValidations++;
    public Dictionary<string, HashSet<string>> FreeBuildingsProvidedFromThisCity { get; } = [];
    public Building? CheapestStatBuilding(Stat stat) => null;
    public List<Building> Completed { get; } = [];
    public Action<Building>? OnCompleteConstruction { get; set; }
    public void CompleteConstruction(Building building)
    {
        Completed.Add(building);
        OnCompleteConstruction?.Invoke(building);
        BuiltNames.Add(building.Name);
    }
    public bool IsBeingConstructedOrEnqueued(string constructionName) => false;
    public bool IsBuildable(Building building) => false;
    public void RemoveCreateOneImprovementConstruction(string improvement) { }
    public string CurrentConstructionNameData { get; set; } = "";
    public string CurrentConstructionName() => CurrentConstructionNameData;
    public StatTreeNode StatsData { get; set; } = new();
    public StatTreeNode GetStats() => StatsData;
    public float MaintenanceCosts { get; set; }
    public float GetMaintenanceCosts() => MaintenanceCosts;
}

internal sealed class FakeCityReligion : ICityReligionManager
{
    public string? ReligionThisIsTheHolyCityOf { get; set; }
    public bool BlockedHolyCity { get; set; }
    public bool IsBlockedHolyCity => BlockedHolyCity;
    public Dictionary<string, int> Pressure { get; } = [];
    public Religion? Majority { get; set; }
    public void AddPressure(string religionName, int amount, bool shouldUpdateFollowers = true) =>
        Pressure[religionName] = Pressure.GetValueOrDefault(religionName) + amount;
    public Religion? GetMajorityReligion() => Majority;
    public string? GetMajorityReligionName() => Majority?.Name;
    public Dictionary<string, int> Followers { get; } = [];
    public int GetFollowersOf(string religion) => Followers.GetValueOrDefault(religion);
    public int FollowersOfMajority { get; set; }
    public int GetFollowersOfMajorityReligion() => FollowersOfMajority;
    public bool IsProtectedByInquisitor(string? fromReligion = null) => false;
    public FakeCityReligion? CloneResult { get; set; }
    public ICityReligionManager Clone() => CloneResult ?? throw new NotSupportedException();
    public void SetTransients(CityObject city) { }
    public IEnumerable<UniqueObject> GetUniques(UniqueType uniqueType) => [];
    public IEnumerable<UniqueObject> GetAllUniques() => [];
    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op) { }
    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Func<UniqueObject, bool> filter, Action<UniqueObject> op) { }
    public void ForEachUnique(Func<UniqueObject, bool> filter, Action<UniqueObject> op) { }
}

