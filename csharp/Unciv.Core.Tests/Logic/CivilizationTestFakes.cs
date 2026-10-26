using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic;
using Unciv.Logic.City;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
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

// T055: stand-ins for the manager/city/unit/game-info seams of Civilization (CivilizationContracts.cs). Each fake holds data for what the
// tests read and throws for the rest.

internal sealed class FakeGameInfoState : FakeGameInfo, IGameInfoState
{
    public Ruleset RulesetData { get; set; } = new();
    public SpeedObject SpeedData { get; set; } = new() { Turns = [new Dictionary<string, float> { ["yearsPerTurn"] = 1f, ["untilTurn"] = 500f }] };
    public List<Civilization> Civs { get; } = [];
    public Civilization? CurrentPlayer { get; set; }
    public bool Simulation { get; set; }
    public GlobalUniques Globals { get; } = new();
    public JavaHashMap<string, string?> Votes { get; } = new();
    public DifficultyObject Difficulty { get; set; } = new() { Name = "Prince" };
    public TileMap? Map { get; set; }

    Ruleset IGameInfoState.Ruleset => RulesetData;
    SpeedObject IGameInfoState.Speed => SpeedData;
    IReadOnlyList<Civilization> IGameInfoState.Civilizations => Civs;
    Civilization? IGameInfoState.CurrentPlayerCiv => CurrentPlayer;
    bool IGameInfoState.IsSimultaneousTurns => false;
    bool IGameInfoState.IsCivActive(Civilization civ) => false;
    bool IGameInfoState.IsSimulation() => Simulation;
    GlobalUniques IGameInfoState.GetGlobalUniques() => Globals;
    JavaHashMap<string, string?> IGameInfoState.DiplomaticVictoryVotesCast => Votes;
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
    public int DiplomaticVictoryProcessed { get; private set; }
    void IGameInfoState.ProcessDiplomaticVictory() => DiplomaticVictoryProcessed++;
    public HashSet<string> SpaceResourceNames { get; } = [];
    IReadOnlySet<string> IGameInfoState.SpaceResources => SpaceResourceNames;
    public VictoryData? VictoryData { get; set; }
    public GameClientState ClientStateData { get; } = new();
    GameClientState IGameInfoState.ClientState => ClientStateData;
    public Dictionary<string, Unciv.Logic.PlayerSettings> PlayerSettingsByCiv { get; } = [];
    Unciv.Logic.PlayerSettings IGameInfoState.GetPlayerSettings(string civId) =>
        PlayerSettingsByCiv.GetValueOrDefault(civId) ?? Unciv.Logic.PlayerSettings.Default;
    IComparer<string> IGameInfoState.Collator => StringComparer.InvariantCulture;
    bool IGameInfoState.IsFullAutoPlayAI => false;
    public int LastUnitId { get; set; }
    int IGameInfoState.GetNextUnitId() => ++LastUnitId;
    public bool AlternateCycleOrder { get; set; }
    bool IGameInfoState.AlternateUnitCycleOrder => AlternateCycleOrder;
    public List<string> TutorialTasks { get; } = [];
    void IGameInfoState.AddCompletedTutorialTask(string civId, string task) => TutorialTasks.Add(task);
    List<string> IGameInfoState.UnitNamesTaken { get; } = [];
    bool IGameInfoState.IsUnavailableBySettingsCached(IHasUniques construction) => construction.IsUnavailableBySettings(this);
    public FakeBarbarianManager BarbarianFake { get; } = new();
    /// <summary>When set, <see cref="IGameInfoState.Barbarians"/> is this real manager instead of <see cref="BarbarianFake"/>.</summary>
    public BarbarianManager? RealBarbarians { get; set; }
    BarbarianManager IGameInfoState.Barbarians => RealBarbarians ?? BarbarianFake;
    Civilization IGameInfoState.GetBarbarianCivilization() => Civs.First(it => it.CivId == Constants.Barbarians);
    public override TileMap TileMap => Map ?? throw new NotSupportedException();
    public override IGameContextCivilization GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
    Civilization IGameInfoState.GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
    Civilization? IGameInfoState.GetCivilizationOrNull(string civId) => Civs.FirstOrDefault(it => it.CivId == civId);
    IEnumerable<CityObject> IGameInfoState.GetCities() => Civs.SelectMany(it => it.Cities);
    List<Civilization> IGameInfoState.GetAliveCityStates() => Civs.Where(it => it.IsAlive() && it.IsCityState).ToList();
    List<Civilization> IGameInfoState.GetAliveMajorCivs() => Civs.Where(it => it.IsAlive() && it.IsMajorCiv()).ToList();
    public List<(Civilization Civ, TileResourceObject Resource, int MaxDistance)> ExploredResourceNotifications { get; } = [];
    public void NotifyExploredResources(Civilization civInfo, TileResourceObject resource, int maxDistance) =>
        ExploredResourceNotifications.Add((civInfo, resource, maxDistance));
}

internal sealed class FakeBarbarianManager : BarbarianManager
{
    public List<MapUnit> Spawned { get; } = [];
    public MapUnit? SpawnResult { get; set; }
    public override MapUnit? SpawnBarbarian(TileClass tile, Civilization? allegiance = null)
    {
        if (SpawnResult != null) Spawned.Add(SpawnResult);
        return SpawnResult;
    }
}

internal class TestUnitManager(Civilization civInfo) : UnitManager(civInfo)
{
    public int? OverrideCount { get; set; }
    public int Count { get => GetCivUnitsSize(); set => OverrideCount = value; }
    public override int GetCivUnitsSize() => OverrideCount ?? base.GetCivUnitsSize();

    public List<(MapUnit Unit, bool UpdateCivInfo)> AssignedUnits { get; } = [];
    public List<(MapUnit Unit, bool UpdateCivInfo)> RemovedUnits { get; } = [];
    public List<(BaseUnit Unit, CityObject? City)> AddedUnits { get; } = [];
    public MapUnit? AddedUnitResult { get; set; }

    public override void AddUnit(MapUnit mapUnit, bool updateCivInfo = true)
    {
        AssignedUnits.Add((mapUnit, updateCivInfo));
        try
        {
            base.AddUnit(mapUnit, updateCivInfo);
        }
        catch (InvalidOperationException)
        {
            // Allowed in unit tests when baseUnit or other properties are uninitialized
        }
    }

    public override void RemoveUnit(MapUnit mapUnit, bool updateCivInfo = true)
    {
        RemovedUnits.Add((mapUnit, updateCivInfo));
        try
        {
            base.RemoveUnit(mapUnit, updateCivInfo);
        }
        catch (InvalidOperationException)
        {
            // Allowed in unit tests when baseUnit or other properties are uninitialized
        }
    }

    public override MapUnit? AddUnit(BaseUnit baseUnit, CityObject? city = null)
    {
        AddedUnits.Add((baseUnit, city));
        return AddedUnitResult;
    }
}

internal sealed class FakeQuestManager : QuestManager
{
    public List<string> Met { get; } = [];
    public override void JustMet(Civilization otherCiv) => Met.Add(otherCiv.CivId);
    public List<(string Civ, HexCoord Position)> ClearedCamps { get; } = [];
    public override void BarbarianCampCleared(Civilization civ, HexCoord position) => ClearedCamps.Add((civ.CivId, position));
    public int IndividualQuestsHandled { get; private set; }
    public override void HandleIndividualQuests() => IndividualQuestsHandled++;

    // T068 (CityStateFunctions)
    public float InvestmentMultiplier { get; set; } = 1f;
    public override float GetInvestmentMultiplier(Civilization donorCiv) => InvestmentMultiplier;
    public List<string> GoldGifts { get; } = [];
    public override void ReceivedGoldGift(Civilization donorCiv) => GoldGifts.Add(donorCiv.CivId);
    public List<(string CityState, string Bully)> Bullied { get; } = [];
    public override void CityStateBullied(Civilization cityState, Civilization bully) => Bullied.Add((cityState.CivId, bully.CivId));
    public List<(string CityState, string Attacker)> Conquered { get; } = [];
    public override void CityStateConquered(Civilization cityState, Civilization attacker) => Conquered.Add((cityState.CivId, attacker.CivId));
    public List<string> AttackedBy { get; } = [];
    public override void WasAttackedBy(Civilization attacker) => AttackedBy.Add(attacker.CivId);
    public HashSet<string> WantsDeadCivs { get; } = [];
    public override bool WantsDead(string civId) => WantsDeadCivs.Contains(civId);
}

internal class FakeCityStateFunctions : ICityStateFunctions
{
    public int TributeWillingness { get; set; }
    public int GetTributeWillingness(Civilization demandingCiv, bool demandingWorker = false) => TributeWillingness;
    public int AllyUpdates { get; private set; }
    public Action? OnAllyUpdate { get; set; }
    public void UpdateAllyCivForCityState() { AllyUpdates++; OnAllyUpdate?.Invoke(); }
    public bool CanProvideStat(Stat statType) => false;
    public List<UniqueObject> Provided { get; } = [];
    public IEnumerable<UniqueObject> GetUniquesProvidedByCityStates(UniqueType uniqueType, GameContext gameContext) => Provided.Where(it => it.Type == uniqueType);
    public void ForEachUniqueProvidedByCityStates(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        foreach (var unique in GetUniquesProvidedByCityStates(uniqueType, gameContext)) op(unique);
    }
    public ResourceSupplyList GetCityStateResourcesForAlly() => new();
    public bool InitCityState(Unciv.Models.Ruleset.Ruleset ruleset, string startingEra, IEnumerable<Unciv.Models.Ruleset.Nation.Nation> usedMajorCivs, GameRandom rng) => true;
    public IEnumerable<NotificationAction> GetNotificationActions() => [];
    public List<string> Attacked { get; } = [];
    public void CityStateAttacked(Civilization attacker) => Attacked.Add(attacker.CivId);
    public List<Civilization> Protectors { get; } = [];
    public List<Civilization> GetProtectorCivs() => Protectors;
    public List<(string Civ, bool Forced)> RemovedProtectors { get; } = [];
    public void RemoveProtectorCiv(Civilization otherCiv, bool forced = false) => RemovedProtectors.Add((otherCiv.CivId, forced));
    public List<string> MilitaryUnitsGiven { get; } = [];
    public void GiveMilitaryUnitToPatron(Civilization receivingCiv) => MilitaryUnitsGiven.Add(receivingCiv.CivId);
    public List<string> UnitGiftRequests { get; } = [];
    public void AskForUnitGifts(Civilization attacker) => UnitGiftRequests.Add(attacker.CivId);
    public int GetNumThreateningBarbarians() => 0;
    public int ElectionsHeld { get; private set; }
    public void HoldElections() => ElectionsHeld++;
    public int GreatPersonTurns { get; set; } = 20;
    public int TurnsForGreatPersonFromCityState() => GreatPersonTurns;
    public List<Civilization> PatronGifts { get; } = [];
    public void GiveGreatPersonToPatron(Civilization receivingCiv) => PatronGifts.Add(receivingCiv);
    public List<string> BarbarianKillers { get; } = [];
    public void ThreateningBarbarianKilledBy(Civilization otherCiv) => BarbarianKillers.Add(otherCiv.CivId);
    public List<string> DestroyedBy { get; } = [];
    public void CityStateDestroyed(Civilization attacker) => DestroyedBy.Add(attacker.CivId);
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
    public IReadOnlyList<string> ConstructionQueue => ConstructionsFake.ConstructionQueue;
    public int InProgressValidations => ConstructionsFake.InProgressValidations;
    public JavaHashMap<string, JavaHashSet<string>> FreeBuildingsProvidedFromThisCity => ConstructionsFake.FreeBuildingsProvidedFromThisCity;
    public JavaHashSet<string> Buildings => ConstructionsFake.BuiltNames;
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

internal sealed class FakeCityPopulation : CityPopulationManager
{
    public int PopulationValue { get => Population; set => Population = value; }
    public int AutoAssignCalls { get; private set; }
    internal override void AutoAssignPopulation() => AutoAssignCalls++;
    public int UnassignCalls { get; private set; }
    public override void UnassignExtraPopulation() => UnassignCalls++;
    public Dictionary<string, int> ByFilter { get; } = [];
    public override int GetPopulationFilterAmount(string filter) => ByFilter.GetValueOrDefault(filter);
    public int FreePopulation { get; set; }
    public override int GetFreePopulation() => FreePopulation;
}

internal sealed class FakeCityExpansion : CityExpansionManager
{
    public override void SetTransients() { }
    public List<Tile> Relinquished { get; } = [];
    public override void RelinquishOwnership(Tile tile) => Relinquished.Add(tile);
}

internal sealed class FakeCityStats(CityObject city) : CityStats(city)
{
    public int UpdateCalls { get; private set; }
    public void ResetCounters() => UpdateCalls = 0;
    public List<bool> UpdateCivStatsArguments { get; } = [];
    public override void Update(IConstruction? currentConstruction = null, bool updateTileStats = true, bool updateCivStats = true, bool calculateGrowthModifiers = true)
    {
        UpdateCalls++;
        UpdateCivStatsArguments.Add(updateCivStats);
    }
    public bool ExtraAnnexUnhappiness { get; set; }
    public override bool HasExtraAnnexUnhappiness() => ExtraAnnexUnhappiness;
}

internal sealed class FakeCityConstructions : CityConstructions
{
    public FakeCityConstructions? CloneResult { get; set; }
    public override void SetTransients() { }
    public override CityConstructions Clone() => CloneResult ?? throw new NotSupportedException();
    public UniqueMap BuiltUniques => BuiltBuildingUniqueMap;
    public List<Building> BuiltBuildingObjects { get; } = [];
    public override IEnumerable<Building> GetBuiltBuildings() => BuiltBuildingObjects;
    public JavaHashSet<string> BuiltNames => BuiltBuildings;
    public override bool ContainsBuildingOrEquivalent(string buildingName) => BuiltNames.Contains(buildingName);
    public override void AddBuilding(Building building, bool tryAddFreeBuildings = true) => BuiltBuildingObjects.Add(building);
    public override void RemoveBuilding(Building building) => BuiltBuildingObjects.Remove(building);
    public int ProductionAdded { get; private set; }
    public override void AddProductionPoints(int productionToAdd) => ProductionAdded += productionToAdd;
    public override int GetWorkDone(string constructionName) => 0;
    public IConstruction? CurrentConstruction { get; set; }
    public override IConstruction GetCurrentConstruction() => CurrentConstruction ?? throw new NotSupportedException();
    public bool ChooseNextConstructionIsNoOp { get; set; }
    public override void ChooseNextConstruction()
    {
        if (!ChooseNextConstructionIsNoOp) throw new NotSupportedException();
    }
    public int InProgressValidations { get; private set; }
    public override void ValidateInProgressConstructions() => InProgressValidations++;
    public override Building? CheapestStatBuilding(Stat stat) => null;
    public List<Building> Completed { get; } = [];
    public Action<Building>? OnCompleteConstruction { get; set; }
    public override bool CompleteConstruction(INonPerpetualConstruction construction)
    {
        var building = (Building)construction;
        Completed.Add(building);
        OnCompleteConstruction?.Invoke(building);
        BuiltNames.Add(building.Name);
        return true;
    }
    public override bool IsBeingConstructedOrEnqueued(string constructionName) => false;
    public override void RemoveCreateOneImprovementConstruction(string improvement) { }
    public string CurrentConstructionNameData { get; set; } = "";
    public override string CurrentConstructionName() => CurrentConstructionNameData;
    public StatTreeNode StatsData { get; set; } = new();
    public override StatTreeNode GetStats() => StatsData;
    public float MaintenanceCosts { get; set; }
    public override float GetMaintenanceCosts() => MaintenanceCosts;
}

internal sealed class FakeCityReligion : CityReligionManager
{
    public bool BlockedHolyCity { get => IsBlockedHolyCity; set => IsBlockedHolyCity = value; }
    public Dictionary<string, int> Pressure { get; } = [];
    public Religion? Majority { get; set; }
    public override void AddPressure(string religionName, int amount, bool shouldUpdateFollowers = true) =>
        Pressure[religionName] = Pressure.GetValueOrDefault(religionName) + amount;
    public override Religion? GetMajorityReligion() => Majority;
    public override string? GetMajorityReligionName() => Majority?.Name;
    public Dictionary<string, int> Followers { get; } = [];
    public override int GetFollowersOf(string religion) => Followers.GetValueOrDefault(religion);
    public int FollowersOfMajority { get; set; }
    public override int GetFollowersOfMajorityReligion() => FollowersOfMajority;
    public override bool IsProtectedByInquisitor(string? fromReligion = null) => false;
}
