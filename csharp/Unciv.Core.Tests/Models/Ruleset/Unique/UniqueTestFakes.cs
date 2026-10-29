using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>Minimal implementations of the game-state interfaces <see cref="GameContext"/> reads, standing in for the not yet ported classes.</summary>
internal partial class FakeGameInfo : IGameContextGameInfo
{
    public int Turns { get; set; }
    public string? GameId { get; init; }
    public long? MapSeed { get; init; }

    // Members read by Conditionals and Countables. Data-driven: tests fill what they need.
    public FakeRuleset FakeRulesetData { get; } = new();
    public IGameContextRuleset Ruleset => FakeRulesetData;
    public GameParameters GameParameters { get; } = new();
    public FakeSpeed FakeSpeedData { get; } = new();
    public IGameContextSpeed Speed => FakeSpeedData;
    public string DifficultyName { get; set; } = "Prince";
    public string GetDifficultyName() => DifficultyName;
    public bool ReligionEnabled { get; set; }
    public bool EspionageEnabled { get; set; }
    public bool IsReligionEnabled() => ReligionEnabled;
    public bool IsEspionageEnabled() => EspionageEnabled;
    public int YearStart { get; set; } = -4000;
    public int GetYear(int turnOffset) => YearStart + (Turns + turnOffset) * 40;
    public List<FakeCiv> FakeCivs { get; } = new();
    public IReadOnlyList<IGameContextCivilization> Civilizations => FakeCivs;
    public IEnumerable<IGameContextCity> GetCities() => FakeCivs.SelectMany(it => it.FakeCities);
    public List<FakeTile>? FakeTileList { get; set; }
    public IEnumerable<IGameContextTile>? TileList => FakeTileList;
}

internal sealed class FakeSpeed : IGameContextSpeed
{
    public float Modifier { get; set; } = 1f;
    public float GoldCostModifier { get; set; } = 1f;
    public float ProductionCostModifier { get; set; } = 1f;
    public float ScienceCostModifier { get; set; } = 1f;
    public float CultureCostModifier { get; set; } = 1f;
    public float FaithCostModifier { get; set; } = 1f;
    public Dictionary<Stat, float> StatCostModifiers { get; } = new();
    public float GetStatCostModifier(Stat stat) => StatCostModifiers[stat];
}

internal sealed class FakeNamed(string name) : IRulesetResourceParameterView
{
    public string Name { get; } = name;
    public IReadOnlyCollection<string> Uniques { get; } = Array.Empty<string>();
    public bool IsStockpiled => false;
}

/// <summary>A ruleset with only the maps the tests fill; everything else is empty.</summary>
internal sealed class FakeRuleset : IGameContextRuleset
{
    private readonly EmptyRulesetParameterView empty = EmptyRulesetParameterView.Instance;
    public Dictionary<string, IRulesetResourceParameterView> ResourceMap { get; } = new();
    public Dictionary<string, IRulesetObjectParameterView> TechMap { get; } = new();
    public Dictionary<string, int> EraNumbers { get; } = new();
    public List<string> DifficultyList { get; } = new();
    public List<string> BuildingNames { get; } = new();
    public Dictionary<string, HashSet<string>> BuildingsByFilter { get; } = new();
    public Dictionary<string, HashSet<string>> ResourcesByFilter { get; } = new();

    public void AddResource(string name) => ResourceMap[name] = new FakeNamed(name);
    public void AddTech(string name) => TechMap[name] = new FakeNamed(name);

    public IRulesetMapView<IRulesetResourceParameterView> TileResources => new RulesetMapView<IRulesetResourceParameterView>(ResourceMap);
    public IRulesetMapView<IRulesetObjectParameterView> Technologies => new RulesetMapView<IRulesetObjectParameterView>(TechMap);
    public IRulesetMapView<IRulesetUnitParameterView> Units => empty.Units;
    public IRulesetMapView<IRulesetObjectParameterView> UnitTypes => empty.UnitTypes;
    public IRulesetMapView<IRulesetObjectParameterView> UnitPromotions => empty.UnitPromotions;
    public IRulesetMapView<IRulesetObjectParameterView> Nations => empty.Nations;
    public IRulesetMapView<IRulesetObjectParameterView> Buildings => empty.Buildings;
    public IRulesetMapView<IRulesetTerrainParameterView> Terrains => empty.Terrains;
    public IRulesetMapView<IRulesetObjectParameterView> TileImprovements => empty.TileImprovements;
    public IRulesetMapView<IRulesetObjectParameterView> Beliefs => empty.Beliefs;
    public IRulesetMapView<IRulesetObjectParameterView> Policies => empty.Policies;
    public IRulesetMapView<IRulesetObjectParameterView> UnitNameGroups => empty.UnitNameGroups;
    public IRulesetMapView<object> Specialists => empty.Specialists;
    public IRulesetMapView<object> GreatWorks => empty.GreatWorks;
    public IRulesetMapView<object> Eras => empty.Eras;
    public IRulesetMapView<object> Speeds => empty.Speeds;
    IRulesetMapView<object> IRulesetParameterView.Difficulties => empty.Difficulties;
    public IRulesetMapView<object> Events => empty.Events;
    public IRulesetMapView<object> PolicyBranches => empty.PolicyBranches;
    public IRulesetMapView<object> Victories => empty.Victories;
    public IReadOnlyCollection<string> Religions => empty.Religions;

    public Unciv.Models.Ruleset.Tech.Era? GetEra(string eraName) => null;
    public int? GetEraNumber(string eraName) => EraNumbers.TryGetValue(eraName, out int n) ? n : null;
    public IReadOnlyList<string> DifficultyNames => DifficultyList;
    public IEnumerable<string> GetBuildingNamesMatchingFilter(string filter) =>
        BuildingsByFilter.TryGetValue(filter, out var set) ? BuildingNames.Where(set.Contains) : Enumerable.Empty<string>();
    public IEnumerable<string> GetTileResourceNamesMatchingFilter(string filter, GameContext state) =>
        ResourcesByFilter.TryGetValue(filter, out var set) ? ResourceMap.Keys.Where(set.Contains) : Enumerable.Empty<string>();
    public bool? TechnologyMatchesFilter(string techName, string filter, GameContext state) =>
        TechMap.ContainsKey(techName) ? techName == filter || filter == "all" : null;
}

internal partial class FakeCiv(string civId, IGameContextGameInfo? gameInfo = null) : IGameContextCivilization, ILocalUniqueCacheCivilization
{
    public IGameContextGameInfo? GameInfo { get; } = gameInfo;
    public string CivId { get; } = civId;
    public Dictionary<string, int> Resources { get; } = new();
    public Dictionary<Stat, int> StatReserves { get; } = new();
    public int GetResourceAmount(string resourceName) => Resources.GetValueOrDefault(resourceName);
    public int GetStatReserve(Stat stat) => StatReserves.GetValueOrDefault(stat);

    // Members read by Conditionals and Countables
    public List<FakeCity> FakeCities { get; } = new();
    public IReadOnlyList<IGameContextCity> Cities => FakeCities;
    public bool Major { get; set; } = true;
    public bool Alive { get; set; } = true;
    public bool IsMajorCiv() => Major;
    public bool IsAlive() => Alive;
    public bool IsCityState { get; set; }
    public bool AtWar { get; set; }
    public bool IsAtWar() => AtWar;
    public int Happiness { get; set; }
    public bool GoldenAge { get; set; }
    public bool IsGoldenAge() => GoldenAge;
    public int EraNumber { get; set; }
    public int GetEraNumber() => EraNumber;
    public Unciv.Models.Ruleset.Nation.Personality GetPersonality() => Unciv.Models.Ruleset.Nation.Personality.NeutralPersonality;
    public HashSet<string> MatchingCivFilters { get; } = new();
    public bool MatchesFilter(string filter, GameContext? state) => MatchingCivFilters.Contains(filter);
    public FakeCity? Capital { get; set; }
    public IGameContextCity? GetCapital() => Capital;
    public List<FakeCiv> KnownCivs { get; } = new();
    public IEnumerable<IGameContextCivilization> GetKnownCivs() => KnownCivs;
    public HashSet<string> ResearchedTechs { get; } = new();
    public bool IsTechResearched(string techName) => ResearchedTechs.Contains(techName);
    public HashSet<string> TechFilterMatches { get; } = new();
    public bool AnyResearchedTechnologyMatchesFilter(string filter) => TechFilterMatches.Contains(filter);
    public string? CurrentTech { get; set; }
    public bool CurrentTechnologyMatchesFilter(string filter) => CurrentTech == filter;
    public IEnumerable<string> TechsResearched => ResearchedTechs;
    public HashSet<string> AdoptedPolicies { get; } = new();
    public bool IsPolicyAdopted(string policyName) => AdoptedPolicies.Contains(policyName);
    public int CountAdoptedPoliciesMatching(string filter, GameContext state) => AdoptedPolicies.Count(it => it == filter || filter == "all");
    public int CompletedPolicyBranches { get; set; }
    public int GetCompletedPolicyBranchesCount() => CompletedPolicyBranches;
    public IGameContextReligion? Religion { get; set; }
    public ReligionState ReligionState { get; set; }
    public int GreatProphets { get; set; }
    public int GreatProphetsEarned() => GreatProphets;
    public List<FakeUnit> FakeUnits { get; } = new();
    public int GetCivUnitsSize() => FakeUnits.Count;
    public IEnumerable<IGameContextUnit> GetCivUnits() => FakeUnits;
    public Dictionary<Stat, List<float>> StatMapValues { get; } = new();
    public IEnumerable<float> GetStatMapForNextTurnValues(Stat stat) => StatMapValues.GetValueOrDefault(stat) ?? new List<float>();
    public int GetCivResourceSupplyAmount(string resourceName) => Resources.GetValueOrDefault(resourceName);

    // ILocalUniqueCacheCivilization
    public string CivName => CivId;
    public GameContext State => GameContext.ForCiv(this);
    public int GetMatchingUniquesCalls { get; private set; } // evaluations
    public List<UniqueObject> CivUniques { get; } = new();
    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext gameContext)
    {
        GetMatchingUniquesCalls++; // counted when the sequence is iterated, like the work Kotlin's lazy sequence does
        foreach (var unique in CivUniques.Where(it => it.Type == uniqueType)) yield return unique;
    }
}

internal sealed class FakeTile(HexCoord position) : IGameContextTile
{
    public HexCoord Position { get; } = position;
    public IGameContextGameInfo? GameInfo { get; init; }
    public IGameContextCity? City { get; set; }
    public List<FakeTile> NeighborTiles { get; } = new();
    public HashSet<string> Matching { get; } = new();
    public IGameContextCity? GetCity() => City;
    public IEnumerable<IGameContextTile> Neighbors => NeighborTiles;
    public bool MatchesFilter(string filter) => Matching.Contains(filter);
    public bool MatchesFilter(string filter, IGameContextCivilization? civ) => Matching.Contains(filter);
    public HashSet<string> AdjacentFilters { get; } = new();
    public bool IsAdjacentTo(string filter, IGameContextCivilization? civ) => AdjacentFilters.Contains(filter);
    public IEnumerable<IGameContextTile> GetTilesInDistance(int distance) => NeighborTiles;
    public List<FakeUnit> UnitsOnTile { get; } = new();
    public IEnumerable<IGameContextUnit> GetUnits() => UnitsOnTile;
    public List<FakeUnit> FakeAirUnits { get; } = new();
    public IEnumerable<IGameContextUnit> AirUnits => FakeAirUnits;
    public IGameContextUnit? MilitaryUnit { get; set; }
    public int Continent { get; set; }
    public int GetContinent() => Continent;
}

internal partial class FakeCity(string id, FakeCiv civ, FakeTile? center = null) : IGameContextCity, ILocalUniqueCacheCity
{
    public string Id { get; } = id;
    public virtual IGameContextCivilization Civ => civ;
    public Dictionary<string, int> Resources { get; } = new();
    public Dictionary<Stat, int> StatReserves { get; } = new();
    public IGameContextTile? GetCenterTileOrNull() => center;
    public int GetAvailableResourceAmount(string resourceName) => Resources.GetValueOrDefault(resourceName);
    public int GetStatReserve(Stat stat) => StatReserves.GetValueOrDefault(stat);

    // Members read by Conditionals and Countables
    public HashSet<string> CityFilters { get; } = new();
    public bool MatchesFilter(string filter, IGameContextCivilization? viewingCiv) => CityFilters.Contains(filter);
    public HashSet<string> Buildings { get; } = new();
    public bool ContainsBuildingOrEquivalent(string buildingName) => Buildings.Contains(buildingName);
    public IReadOnlySet<string> BuiltBuildings => Buildings;
    public int CountBuiltBuildingsMatchingFilter(string filter, GameContext state) => Buildings.Count(it => it == filter);
    public Dictionary<Stat, float> CurrentCityStats { get; } = new();
    public float GetCurrentCityStat(Stat stat) => CurrentCityStats.GetValueOrDefault(stat);
    public Dictionary<string, int> Generated { get; } = new();
    public int GetResourcesGeneratedByCityAmount(string resourceName) => Generated.GetValueOrDefault(resourceName);
    public bool Connected { get; set; }
    public bool IsConnectedToCapital() => Connected;
    public bool Wltkd { get; set; }
    public bool IsWeLoveTheKingDayActive() => Wltkd;
    public IGameContextReligion? MajorityReligion { get; set; }
    public IGameContextReligion? GetMajorityReligion() => MajorityReligion;
    public Dictionary<string, int> PopulationByFilter { get; } = new();
    public int GetPopulationFilterAmount(string filter) => PopulationByFilter.GetValueOrDefault(filter);
    public bool CanGarrison { get; set; }
    public bool CenterTileMilitaryUnitCanGarrison() => CanGarrison;
    public List<FakeTile> WorkedTiles { get; } = new();
    public IEnumerable<IGameContextTile> GetWorkedTiles() => WorkedTiles;
    public List<FakeTile> OwnedTiles { get; } = new();
    public IEnumerable<IGameContextTile> GetTiles() => OwnedTiles;

    // ILocalUniqueCacheCity
    ILocalUniqueCacheCivilization ILocalUniqueCacheCity.Civ => civ;
    public GameContext State => GameContext.ForCity(this);
    public int GetLocalMatchingUniquesCalls { get; private set; }
    public List<UniqueObject> LocalUniques { get; } = new();
    public IEnumerable<UniqueObject> GetLocalMatchingUniques(UniqueType uniqueType, GameContext gameContext)
    {
        GetLocalMatchingUniquesCalls++; // counted when the sequence is iterated, like the work Kotlin's lazy sequence does
        foreach (var unique in LocalUniques.Where(it => it.Type == uniqueType)) yield return unique;
    }
}

internal partial class FakeUnit(string name, float currentMovement, FakeCiv civ, FakeTile? tile = null) : IGameContextUnit
{
    public string Name { get; } = name;
    public IGameContextCivilization Civ { get; } = civ;
    public float CurrentMovement { get; } = currentMovement;
    public bool HasTile() => tile != null;
    public IGameContextTile GetTile() => tile ?? throw new InvalidOperationException("lateinit property currentTile has not been initialized");

    public HashSet<string> UnitFilters { get; } = new();
    public virtual bool MatchesFilter(string filter) => UnitFilters.Contains(filter);
    public HashSet<string> PromotionsAndStatuses { get; } = new();
    public bool HasPromotionOrStatus(string name) => PromotionsAndStatuses.Contains(name);
    public int Health { get; set; } = 100;
    public bool IsTransported { get; set; }
    public bool AbilityToTimesUsedIsEmpty { get; set; } = true;
}

/// <summary>A MapUnitCombatant (<see cref="Unit"/> set) or a CityCombatant (<see cref="City"/> set).</summary>
internal sealed class FakeCombatant(FakeCiv civ, FakeTile tile, string name, FakeUnit? unit = null, FakeCity? city = null) : IGameContextCombatant
{
    public IGameContextCivilization GetCivInfo() => civ;
    public IGameContextTile GetTile() => tile;
    public string GetName() => name;
    public IGameContextUnit? Unit { get; } = unit;
    public IGameContextCity? City { get; } = city;
    public HashSet<string> Filters { get; } = new();
    public bool MatchesFilter(string filter, bool multiFilter) => Filters.Contains(filter);
    public int Health { get; set; } = 100;
    public int GetHealth() => Health;
}
