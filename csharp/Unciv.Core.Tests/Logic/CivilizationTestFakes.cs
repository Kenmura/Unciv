using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using DifficultyObject = Unciv.Models.Ruleset.Nation.Difficulty;
using SpeedObject = Unciv.Models.Ruleset.Speed;
using StatsObject = Unciv.Models.StatTypes.Stats;
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
    public override TileMap TileMap => Map ?? throw new NotSupportedException();
    public override IGameContextCivilization GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
    Civilization IGameInfoState.GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
    IEnumerable<ICityState> IGameInfoState.GetCities() => Civs.SelectMany(it => it.Cities);
    List<Civilization> IGameInfoState.GetAliveMajorCivs() => Civs.Where(it => it.IsAlive() && it.IsMajorCiv()).ToList();
    public List<(Civilization Civ, TileResourceObject Resource, int MaxDistance)> ExploredResourceNotifications { get; } = [];
    public void NotifyExploredResources(Civilization civInfo, TileResourceObject resource, int maxDistance) =>
        ExploredResourceNotifications.Add((civInfo, resource, maxDistance));
}

internal sealed class FakeUnitManager : IUnitManager
{
    public int Count { get; set; }
    public int GetCivUnitsSize() => Count;
    public List<IMapUnitState> CivUnits { get; } = [];
    public IEnumerable<IMapUnitState> GetCivUnits() => CivUnits;
    public void RemoveUnit(IGameContextUnit unit, bool updateCivInfo) => throw new NotSupportedException();
    public IGameContextUnit NewMapUnit(BaseUnit baseUnit, int? unitId) => throw new NotSupportedException();
    public List<(BaseUnit Unit, ICityState? City)> AddedUnits { get; } = [];
    public IMapUnitState? AddedUnitResult { get; set; }
    public IMapUnitState? AddUnit(BaseUnit baseUnit, ICityState? city = null)
    {
        AddedUnits.Add((baseUnit, city));
        return AddedUnitResult;
    }
}

internal sealed class FakeQuestManager : IQuestManager
{
    public List<string> Met { get; } = [];
    public void JustMet(Civilization otherCiv) => Met.Add(otherCiv.CivId);
    public IQuestManager Clone() => throw new NotSupportedException();
    public void SetTransients(Civilization civInfo) => throw new NotSupportedException();
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

/// <summary>A city of a <see cref="Civilization"/>: the seam members Civilization calls, over data the tests fill.</summary>
internal sealed class FakeCityState(string id, FakeCiv civ) : FakeCity(id, civ), ICityState
{
    private string cityName = id;
    public int PopulationData { get; set; }
    public List<HexCoord> TileLocations { get; } = [];
    public List<Tile> TileObjects { get; } = [];
    public UniqueMap BuiltUniques { get; } = new();
    public List<UniqueObject> NonLocalUniques { get; } = [];
    public List<Building> BuiltBuildingObjects { get; } = [];

    public override string Name => cityName;
    string ICityState.Name { get => cityName; set => cityName = value; }
    public ICityState Clone() => throw new NotSupportedException();
    public Civilization? CivData { get; set; }
    Civilization ICityState.Civ => CivData ?? throw new NotSupportedException();
    public HexCoord LocationData { get; set; }
    public override HexCoord Location => LocationData;
    public FakeCityReligion ReligionData { get; } = new();
    public ICityReligionManager Religion => ReligionData;
    public bool HolyCity { get; set; }
    public bool IsHolyCity() => HolyCity;
    public bool IsHolyCityOf(string? religionName) => HolyCity && ReligionData.ReligionThisIsTheHolyCityOf == religionName;
    public Civilization? FoundingCivObject { get; set; }
    public List<UniqueObject> SpyUniques { get; } = [];
    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext gameContext, bool includeCivUniques) =>
        SpyUniques.Where(it => it.Type == uniqueType);
    public void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, bool includeCivUniques, Action<UniqueObject> op)
    {
        foreach (var unique in GetMatchingUniques(uniqueType, gameContext, includeCivUniques)) op(unique);
    }
    public void SetTransients(Civilization civInfo) => throw new NotSupportedException();
    GameContext ICityState.State => GameContext.ForCity(this);
    public bool Capital { get; set; }
    public override bool IsCapital() => Capital;
    public bool IsOriginalCapital { get; set; }
    public bool IsPuppet { get; set; }
    public bool IsBeingRazed { get; set; }
    public void AnnexCity() => throw new NotSupportedException();
    public int Population => PopulationData;
    Tile ICityState.GetCenterTile() => throw new NotSupportedException();
    public IReadOnlyCollection<HexCoord> Tiles => TileLocations;
    IEnumerable<Tile> ICityState.GetTiles() => TileObjects;
    IEnumerable<Tile> ICityState.GetWorkedTiles() => [];
    public void StopWorkingTile(Tile tile) => throw new NotSupportedException();
    public int StatsUpdateCalls { get; private set; }
    public void UpdateCityStats(bool updateCivStats) => StatsUpdateCalls++;
    public UniqueMap BuiltBuildingUniqueMap => BuiltUniques;
    public IEnumerable<Building> GetBuiltBuildings() => BuiltBuildingObjects;
    public void AddBuilding(Building building) => BuiltBuildingObjects.Add(building);
    public void RemoveBuilding(Building building) => BuiltBuildingObjects.Remove(building);
    public void ChooseNextConstruction() => throw new NotSupportedException();
    public bool ConnectedToCapitalStatus { get; set; }
    public bool ContainsBuildingUnique(UniqueType uniqueType) => false;
    public bool IsBlockaded() => false;
    public ResourceSupplyList GetResourcesGeneratedByCity() => new();
    public IEnumerable<KeyValuePair<string, StatsObject>> FinalStatList => [];
    public IEnumerable<KeyValuePair<string, float>> HappinessList => [];
    IEnumerable<UniqueObject> ICityState.GetLocalMatchingUniques(UniqueType uniqueType, GameContext? gameContext) =>
        GetLocalMatchingUniques(uniqueType, gameContext ?? GameContext.EmptyState);
    public Dictionary<string, HashSet<string>> FreeBuildingsProvidedFromThisCity { get; } = [];
    public Building? CheapestStatBuilding(Stat stat) => null;
    public List<Building> Completed { get; } = [];
    public Action<Building>? OnCompleteConstruction { get; set; }
    public void CompleteConstruction(Building building)
    {
        Completed.Add(building);
        OnCompleteConstruction?.Invoke(building);
        Buildings.Add(building.Name);
    }
    public bool IsBeingConstructedOrEnqueued(string constructionName) => false;
    public bool IsBuildable(Building building) => false;
    public Counter<string> GreatPersonPoints { get; } = new();
    public Counter<string> GetGreatPersonPoints() => GreatPersonPoints;
    public List<string> ConstructionQueue { get; } = [];
    public void EditConstructionQueue(Action<List<string>> edit)
    {
        var copy = new List<string>(ConstructionQueue);
        edit(copy);
        ConstructionQueue.Clear();
        ConstructionQueue.AddRange(copy);
    }
    public int PopulationReassignments { get; private set; }
    public override void ReassignPopulationDeferred() => PopulationReassignments++;
    public int InProgressValidations { get; private set; }
    public void ValidateInProgressConstructions() => InProgressValidations++;
    public IEnumerable<UniqueObject> GetMatchingUniquesWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext) =>
        NonLocalUniques.Where(it => it.Type == uniqueType);
    public void ForEachMatchingUniqueWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        foreach (var unique in GetMatchingUniquesWithNonLocalEffects(uniqueType, gameContext)) op(unique);
    }
}

internal sealed class FakeCityReligion : ICityReligionManager
{
    public string? ReligionThisIsTheHolyCityOf { get; set; }
    public Dictionary<string, int> Pressure { get; } = [];
    public Religion? Majority { get; set; }
    public void AddPressure(string religionName, int amount, bool shouldUpdateFollowers = true) =>
        Pressure[religionName] = Pressure.GetValueOrDefault(religionName) + amount;
    public Religion? GetMajorityReligion() => Majority;
    public string? GetMajorityReligionName() => Majority?.Name;
    public Dictionary<string, int> Followers { get; } = [];
    public int GetFollowersOf(string religion) => Followers.GetValueOrDefault(religion);
    public bool IsProtectedByInquisitor(string? fromReligion = null) => false;
}

