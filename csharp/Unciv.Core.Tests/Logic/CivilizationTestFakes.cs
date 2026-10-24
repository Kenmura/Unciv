using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using DifficultyObject = Unciv.Models.Ruleset.Nation.Difficulty;
using EraObject = Unciv.Models.Ruleset.Tech.Era;
using PolicyObject = Unciv.Models.Ruleset.Policy;
using SpeedObject = Unciv.Models.Ruleset.Speed;
using StatsObject = Unciv.Models.StatTypes.Stats;
using TechnologyObject = Unciv.Models.Ruleset.Tech.Technology;
using TileResourceObject = Unciv.Models.Ruleset.Tile.TileResource;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic;

// T055: stand-ins for the manager/city/unit/game-info seams of Civilization (CivilizationSeams.cs). Each fake holds data for what the
// tests read and throws for the rest.

internal sealed class FakeGameInfoState : FakeGameInfo, IGameInfoState
{
    public Ruleset RulesetData { get; set; } = new();
    public SpeedObject SpeedData { get; set; } = new();
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
    public override TileMap TileMap => Map ?? throw new NotSupportedException();
    public override IGameContextCivilization GetCivilization(string civId) => Civs.First(it => it.CivId == civId);
}

internal sealed class FakePolicies : IPolicyManager
{
    public UniqueMap PolicyUniques { get; } = new();
    public HashSet<string> Adopted { get; } = [];
    public int Culture { get; set; }
    public IReadOnlyCollection<string> AdoptedPolicies => Adopted;
    public int StoredCulture => Culture;
    public void AddCulture(int added) => Culture += added;
    public bool IsAdopted(string policyName) => Adopted.Contains(policyName);
    public IEnumerable<PolicyObject> GetAdoptedPoliciesMatching(string filter, GameContext gameContext) => throw new NotSupportedException();
    public IPolicyManager Clone() => throw new NotSupportedException();
    public void SetTransients(Civilization civInfo) => Transients++;
    public int Transients { get; private set; }
}

internal sealed class FakeTech : ITechManager
{
    public UniqueMap TechUniques { get; } = new();
    public EraObject EraData { get; set; } = new() { Name = "Ancient era", EraNumber = 0 };
    public HashSet<string> Researched { get; } = [];
    public TechnologyObject? Current { get; set; }
    public int Science { get; set; }
    public int Repeating { get; set; }
    public EraObject Era => EraData;
    public int FreeTechs { get; set; }
    public bool CanResearch { get; set; } = true;
    public IReadOnlyList<TechnologyObject> ResearchedTechnologies { get; set; } = [];
    public IReadOnlyCollection<string> TechsResearched => Researched;
    public int RepeatingTechsResearched => Repeating;
    public bool IsRevealed(TileResourceObject resource) => Researched.Contains(resource.Name);
    public bool IsResearched(string techName) => Researched.Contains(techName);
    public bool CanResearchTech() => CanResearch;
    public TechnologyObject? CurrentTechnology() => Current;
    public int ResearchOfTech(string techName) => 42;
    public void AddScience(int scienceGet) => Science += scienceGet;
    public ITechManager Clone() => throw new NotSupportedException();
    public void SetTransients(Civilization civInfo) => throw new NotSupportedException();
}

internal sealed class FakeReligionManager : IReligionManager
{
    public IFounderReligion? Religion { get; set; }
    public int StoredFaith { get; set; }
    public Unciv.Logic.Civilization.Managers.ReligionState ReligionState => Unciv.Logic.Civilization.Managers.ReligionState.None;
    public int GreatProphetsEarned() => 0;
    public IReligionManager Clone() => throw new NotSupportedException();
    public void SetTransients(Civilization civInfo) => throw new NotSupportedException();
}

internal sealed class FakeFounderReligion(UniqueMap map) : IFounderReligion
{
    public UniqueMap FounderBeliefUniqueMap { get; } = map;
    public bool MatchesFilter(string filter, GameContext state, IGameContextCivilization? civ) => throw new NotSupportedException();
    public bool IsMajorReligion() => throw new NotSupportedException();
    public bool IsEnhancedReligion() => throw new NotSupportedException();
    public bool HasBelief(string belief) => throw new NotSupportedException();
}

internal sealed class FakeGoldenAges : IGoldenAgeManager
{
    public int Happiness { get; set; }
    public Civilization CivInfo { get; set; } = null!;
    public bool IsGoldenAge() => false;
    public void AddHappiness(int amount) => Happiness += amount;
    public int StoredHappiness => Happiness;
    public IGoldenAgeManager Clone() => throw new NotSupportedException();
}

internal sealed class FakeCivStats : ICivInfoStats
{
    public int Happiness { get; set; }
    public List<float> Breakdown { get; } = [];
    public List<StatsObject> StatMap { get; } = [];
    public StatsObject StatsForNextTurn { get; set; } = new();
    public IEnumerable<float> GetHappinessBreakdownValues() => Breakdown;
    public IEnumerable<StatsObject> GetStatMapForNextTurnValues() => StatMap;
}

internal sealed class FakeCivCache : ICivInfoTransientCache
{
    public List<Building> UniqueBuildingsData { get; } = [];
    public IEnumerable<Building> UniqueBuildings => UniqueBuildingsData;
    public IEnumerable<TileImprovement> UniqueImprovements => [];
    public IEnumerable<BaseUnit> UniqueUnits => [];
    public void SetTransients() => throw new NotSupportedException();
    public Proximity UpdateProximity(Civilization otherCiv, Proximity? preCalculated) => throw new NotSupportedException();
    public bool IsCityConnectedToCapital(ICityState city) => throw new NotSupportedException();
    public void UpdateCivResources() => throw new NotSupportedException();
}

internal sealed class FakeUnitManager : IUnitManager
{
    public int Count { get; set; }
    public int GetCivUnitsSize() => Count;
    public IEnumerable<IMapUnitState> GetCivUnits() => [];
    public void RemoveUnit(IGameContextUnit unit, bool updateCivInfo) => throw new NotSupportedException();
    public IGameContextUnit NewMapUnit(BaseUnit baseUnit, int? unitId) => throw new NotSupportedException();
}

internal sealed class FakeCityStateFunctions : ICityStateFunctions
{
    public List<UniqueObject> Provided { get; } = [];
    public IEnumerable<UniqueObject> GetUniquesProvidedByCityStates(UniqueType uniqueType, GameContext gameContext) => Provided.Where(it => it.Type == uniqueType);
    public void ForEachUniqueProvidedByCityStates(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        foreach (var unique in GetUniquesProvidedByCityStates(uniqueType, gameContext)) op(unique);
    }
}

internal sealed class FakeDiplomacyFunctions : IDiplomacyFunctions
{
    public HashSet<string> AtWarWith { get; } = [];
    public List<string> Met { get; } = [];
    public void MakeCivilizationsMeet(Civilization otherCiv) => Met.Add(otherCiv.CivId);
    public bool IsAtWarWith(Civilization otherCiv) => AtWarWith.Contains(otherCiv.CivId);
    public bool CanPassThroughTiles(Civilization otherCiv) => throw new NotSupportedException();
}

internal sealed class FakeDiplomacyManager(Civilization otherCiv) : IDiplomacyManager
{
    public string OtherCivName => otherCiv.CivId;
    public Civilization OtherCiv => otherCiv;
    public Civilization CivInfo { get; set; } = null!;
    public bool HasOpenBorders { get; set; }
    public bool Friend { get; set; }
    public bool AtWar { get; set; }
    public int TradesCleared { get; private set; }
    public bool IsRelationshipLevelGEFriend() => Friend;
    public bool IsAtWarStatus => AtWar;
    public bool IsConsideredFriendlyTerritory() => Friend;
    public void ClearTrades() => TradesCleared++;
    public IDiplomacyManager OtherCivDiplomacy() => otherCiv.Diplomacy[CivInfo.CivId];
    public void UpdateHasOpenBorders() => throw new NotSupportedException();
    public IDiplomacyManager Clone() => throw new NotSupportedException();
}

internal sealed class FakeTradeRequest(string requestingCiv) : ITradeRequest
{
    public string RequestingCiv { get; } = requestingCiv;
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
    public void SetTransients(Civilization civInfo) => throw new NotSupportedException();
    GameContext ICityState.State => GameContext.ForCity(this);
    public bool Capital { get; set; }
    public override bool IsCapital() => Capital;
    public bool IsOriginalCapital { get; set; }
    public bool IsPuppet { get; set; }
    public bool IsBeingRazed { get; set; }
    public void AnnexCity() => throw new NotSupportedException();
    public int Population => PopulationData;
    public IReadOnlyCollection<HexCoord> Tiles => TileLocations;
    IEnumerable<Tile> ICityState.GetTiles() => TileObjects;
    IEnumerable<Tile> ICityState.GetWorkedTiles() => [];
    public void StopWorkingTile(Tile tile) => throw new NotSupportedException();
    public void UpdateCityStats(bool updateCivStats) => throw new NotSupportedException();
    public UniqueMap BuiltBuildingUniqueMap => BuiltUniques;
    public IEnumerable<Building> GetBuiltBuildings() => BuiltBuildingObjects;
    public void AddBuilding(Building building) => BuiltBuildingObjects.Add(building);
    public void RemoveBuilding(Building building) => BuiltBuildingObjects.Remove(building);
    public void ChooseNextConstruction() => throw new NotSupportedException();
    public IEnumerable<UniqueObject> GetMatchingUniquesWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext) =>
        NonLocalUniques.Where(it => it.Type == uniqueType);
    public void ForEachMatchingUniqueWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        foreach (var unique in GetMatchingUniquesWithNonLocalEffects(uniqueType, gameContext)) op(unique);
    }
}
