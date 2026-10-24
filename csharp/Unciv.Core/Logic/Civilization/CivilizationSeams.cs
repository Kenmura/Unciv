using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization;

// T055 (Civilization): the seams of Civilization, which is ported BEFORE its managers, City, MapUnit and GameInfo (seam rule, T053
// notes). Each interface holds exactly the members Civilization.kt (or an already existing IGameContext* seam that Civilization
// implements) calls on that type; the ticket that ports the type implements it, retypes the field/parameter in Civilization to the
// concrete class and, for the manager fields, replaces the lateinit-style property by Kotlin's initializer (`= new TechManager()`).
// Member names mirror the Kotlin members. Where a Kotlin member is a Sequence that is only iterated, an IEnumerable is returned.

/// <summary>What Civilization reads from <c>GameInfo</c>, on top of <see cref="IConstructionGameInfo"/> (GameInfo implements both).</summary>
public interface IGameInfoState : IConstructionGameInfo
{
    /// <summary><c>gameInfo.ruleset</c></summary>
    new Ruleset Ruleset { get; }
    /// <summary><c>gameInfo.speed</c></summary>
    new Speed Speed { get; }
    /// <summary><c>gameInfo.civilizations</c></summary>
    new IReadOnlyList<Civilization> Civilizations { get; }
    /// <summary><c>gameInfo.currentPlayerCiv</c></summary>
    Civilization? CurrentPlayerCiv { get; }
    /// <summary><c>gameInfo.isSimulation()</c></summary>
    bool IsSimulation();
    /// <summary><c>gameInfo.getGlobalUniques()</c></summary>
    GlobalUniques GetGlobalUniques();
    /// <summary><c>gameInfo.diplomaticVictoryVotesCast</c> (<c>HashMap&lt;String, String?&gt;</c>).</summary>
    IDictionary<string, string?> DiplomaticVictoryVotesCast { get; }
    /// <summary><c>gameInfo.religions</c> (<c>HashMap&lt;String, Religion&gt;</c>), in java.util.HashMap order. TODO(T046): becomes a GameInfo field.</summary>
    JavaHashMap<string, Religion> Religions { get; }
    /// <summary><c>gameInfo.getCivilization(civID)</c>; throws when there is no such civilization (Kotlin <c>first</c>).</summary>
    new Civilization GetCivilization(string civId);
    /// <summary><c>gameInfo.getCities()</c>, in civilization then city order.</summary>
    new IEnumerable<ICityState> GetCities();
    /// <summary><c>gameInfo.getAliveMajorCivs()</c></summary>
    List<Civilization> GetAliveMajorCivs();
}

/// <summary>What Civilization reads from <c>TechManager</c>.</summary>
public interface ITechManager
{
    ITechManager Clone();
    /// <summary><c>tech.setTransients(civInfo)</c></summary>
    void SetTransients(Civilization civInfo);
    bool IsRevealed(TileResource resource);
    bool IsResearched(string techName);
    /// <summary><c>tech.isResearched(construction)</c></summary>
    bool IsResearched(INonPerpetualConstruction construction);
    /// <summary><c>tech.canBeResearched(techName)</c></summary>
    bool CanBeResearched(string techName);
    /// <summary><c>tech.addTechnology(techName, showNotification)</c></summary>
    void AddTechnology(string techName, bool showNotification = true);
    UniqueMap TechUniques { get; }
    Era Era { get; }
    bool CanResearchTech();
    int FreeTechs { get; }
    Technology? CurrentTechnology();
    int ResearchOfTech(string techName);
    void AddScience(int scienceGet);
    /// <summary><c>tech.researchedTechnologies</c>, in list order.</summary>
    IReadOnlyList<Technology> ResearchedTechnologies { get; }
    /// <summary><c>tech.techsResearched</c> (a set of names).</summary>
    IReadOnlyCollection<string> TechsResearched { get; }
    int RepeatingTechsResearched { get; }
}

/// <summary>What Civilization reads from <c>PolicyManager</c>.</summary>
public interface IPolicyManager
{
    IPolicyManager Clone();
    void SetTransients(Civilization civInfo);
    UniqueMap PolicyUniques { get; }
    /// <summary><c>policies.adoptedPolicies</c> (a set of names).</summary>
    IReadOnlyCollection<string> AdoptedPolicies { get; }
    bool IsAdopted(string policyName);
    /// <summary><c>policies.completedBranches</c> (a set of branch names).</summary>
    IReadOnlyCollection<string> CompletedBranches { get; }
    /// <summary><c>policies.getAdoptedPoliciesMatching(filter, gameContext)</c></summary>
    IEnumerable<Policy> GetAdoptedPoliciesMatching(string filter, GameContext gameContext);
    void AddCulture(int culture);
    int StoredCulture { get; }
}

/// <summary>What Civilization reads from <c>GoldenAgeManager</c>.</summary>
public interface IGoldenAgeManager
{
    IGoldenAgeManager Clone();
    /// <summary><c>goldenAges.civInfo</c>, a <c>lateinit var</c> set by <c>Civilization.setTransients</c>.</summary>
    Civilization CivInfo { get; set; }
    bool IsGoldenAge();
    void AddHappiness(int amount);
    int StoredHappiness { get; }
}

/// <summary>What Civilization reads from <c>GreatPersonManager</c>.</summary>
public interface IGreatPersonManager
{
    IGreatPersonManager Clone();
    /// <summary><c>greatPeople.civInfo</c>, a <c>lateinit var</c> set by <c>Civilization.setTransients</c>.</summary>
    Civilization CivInfo { get; set; }
}

/// <summary>What Civilization reads from <c>QuestManager</c>.</summary>
public interface IQuestManager
{
    IQuestManager Clone();
    void SetTransients(Civilization civInfo);
}

/// <summary>What Civilization reads from <c>CivConstructions</c>.</summary>
public interface ICivConstructions
{
    ICivConstructions Clone();
    void SetTransients(Civilization civInfo);
    /// <summary><c>civConstructions.builtItemsWithIncreasingCost[name]</c></summary>
    int GetBuiltItemsWithIncreasingCost(string name);
    /// <summary><c>civConstructions.boughtItemsWithIncreasingPrice[name]</c></summary>
    int GetBoughtItemsWithIncreasingPrice(string name);
    /// <summary><c>civConstructions.boughtItemsWithIncreasingPrice.add(name, amount)</c></summary>
    void AddBoughtItemsWithIncreasingPrice(string name, int amount);
}

/// <summary>What Civilization reads from <c>DiplomacyManager</c>.</summary>
public interface IDiplomacyManager
{
    IDiplomacyManager Clone();
    string OtherCivName { get; }
    Civilization OtherCiv { get; }
    /// <summary><c>diplomacyManager.civInfo</c>, a <c>lateinit var</c> set by <c>Civilization.setTransients</c>.</summary>
    Civilization CivInfo { get; set; }
    void UpdateHasOpenBorders();
    bool HasOpenBorders { get; }
    /// <summary><c>isRelationshipLevelGE(RelationshipLevel.Friend)</c></summary>
    bool IsRelationshipLevelGEFriend();
    /// <summary><c>diplomaticStatus == DiplomaticStatus.War</c></summary>
    bool IsAtWarStatus { get; }
    bool IsConsideredFriendlyTerritory();
    /// <summary><c>diplomacyManager.trades.clear()</c></summary>
    void ClearTrades();
    /// <summary><c>diplomacyManager.otherCivDiplomacy()</c></summary>
    IDiplomacyManager OtherCivDiplomacy();
    /// <summary><c>diplomacyManager.addModifier(DiplomaticModifiers.SpiedOnUs, amount)</c></summary>
    void AddSpiedOnUsModifier(float amount);
    /// <summary><c>diplomacyManager.setFlag(DiplomacyFlags.DiscoveredSpiesInOurCities, amount, adjustWithGameSpeed)</c></summary>
    void SetDiscoveredSpiesInOurCitiesFlag(int amount, bool adjustWithGameSpeed);
    /// <summary><c>diplomacyManager.hasFlag(DiplomacyFlags.AgreedToNotSendSpies)</c></summary>
    bool HasAgreedToNotSendSpiesFlag();
    float GetInfluence();
    void SetInfluence(float amount);
    void AddInfluence(float amount);
    void ReduceInfluence(float amount);
}

/// <summary>What Civilization reads from <c>DiplomacyFunctions</c>.</summary>
public interface IDiplomacyFunctions
{
    void MakeCivilizationsMeet(Civilization otherCiv);
    bool IsAtWarWith(Civilization otherCiv);
    bool CanPassThroughTiles(Civilization otherCiv);
}

/// <summary>What Civilization reads from <c>TradeRequest</c>.</summary>
public interface ITradeRequest
{
    /// <summary><c>tradeRequest.requestingCiv</c> (the civ's id)</summary>
    string RequestingCiv { get; }
}

/// <summary>What Civilization reads from <c>CityStateFunctions</c>.</summary>
public interface ICityStateFunctions
{
    IEnumerable<UniqueObject> GetUniquesProvidedByCityStates(UniqueType uniqueType, GameContext gameContext);
    void ForEachUniqueProvidedByCityStates(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op);
    void UpdateAllyCivForCityState();
}

/// <summary>What Civilization reads from <c>CivInfoTransientCache</c>.</summary>
public interface ICivInfoTransientCache
{
    void SetTransients();
    Proximity UpdateProximity(Civilization otherCiv, Proximity? preCalculated);
    /// <summary><c>cache.citiesConnectedToCapitalToMediums.keys.contains(city)</c></summary>
    bool IsCityConnectedToCapital(ICityState city);
    IEnumerable<Building> UniqueBuildings { get; }
    IEnumerable<TileImprovement> UniqueImprovements { get; }
    IEnumerable<BaseUnit> UniqueUnits { get; }
    void UpdateCivResources();
}

/// <summary>What Civilization reads from <c>CivInfoStatsForNextTurn</c>.</summary>
public interface ICivInfoStats
{
    int Happiness { get; set; }
    /// <summary><c>stats.getHappinessBreakdown().values</c>, in map order (the float sum depends on the order).</summary>
    IEnumerable<float> GetHappinessBreakdownValues();
    /// <summary><c>stats.getStatMapForNextTurn().values</c>, in map order.</summary>
    IEnumerable<Stats> GetStatMapForNextTurnValues();
    Stats StatsForNextTurn { get; set; }
}

/// <summary>What Civilization reads from <c>UnitManager</c>.</summary>
public interface IUnitManager
{
    int GetCivUnitsSize();
    IEnumerable<IMapUnitState> GetCivUnits();
    /// <summary><c>civ.units.removeUnit(unit, updateCivInfo)</c></summary>
    void RemoveUnit(IGameContextUnit unit, bool updateCivInfo);
    /// <summary><c>baseUnit.newMapUnit(civInfo, unitId)</c></summary>
    IGameContextUnit NewMapUnit(BaseUnit baseUnit, int? unitId);
    /// <summary><c>civ.units.addUnit(baseUnit, city)</c>; <c>null</c> when the unit cannot be placed.</summary>
    IMapUnitState? AddUnit(BaseUnit baseUnit, ICityState? city = null);
}

/// <summary>What Civilization reads from <c>MapUnit</c>.</summary>
public interface IMapUnitState : IGameContextUnit
{
    void Destroy();
    int GetForceEvaluation();
    /// <summary><c>unit.civ</c></summary>
    new Civilization Civ { get; }
    /// <summary><c>unit.getTile()</c></summary>
    new Tile GetTile();
    /// <summary><c>unit.religion</c></summary>
    string? Religion { get; set; }
    /// <summary><c>unit.hasUnique(uniqueType, gameContext)</c></summary>
    bool HasUnique(UniqueType uniqueType, GameContext? gameContext = null);
}

/// <summary>What the religion managers read from <c>CityReligionManager</c> (<c>city.religion</c>).</summary>
public interface ICityReligionManager
{
    string? ReligionThisIsTheHolyCityOf { get; set; }
    void AddPressure(string religionName, int amount, bool shouldUpdateFollowers = true);
    Religion? GetMajorityReligion();
    int GetFollowersOf(string religion);
    bool IsProtectedByInquisitor(string? fromReligion = null);
}

/// <summary>What Civilization reads from <c>City</c>.</summary>
public interface ICityState : IGameContextCity
{
    ICityState Clone();
    /// <summary><c>city.civ</c></summary>
    new Civilization Civ { get; }
    /// <summary><c>city.religion</c></summary>
    ICityReligionManager Religion { get; }
    /// <summary><c>city.isHolyCity()</c></summary>
    bool IsHolyCity();
    /// <summary><c>city.isHolyCityOf(religionName)</c></summary>
    bool IsHolyCityOf(string? religionName);
    /// <summary><c>city.foundingCivObject</c></summary>
    Civilization? FoundingCivObject { get; }
    /// <summary><c>city.getMatchingUniques(uniqueType, gameContext, includeCivUniques)</c></summary>
    IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext gameContext, bool includeCivUniques);
    /// <summary><c>city.forEachMatchingUnique(uniqueType, gameContext, includeCivUniques, op)</c></summary>
    void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, bool includeCivUniques, Action<UniqueObject> op);
    /// <summary><c>city.setTransients(civInfo)</c></summary>
    void SetTransients(Civilization civInfo);
    /// <summary><c>city.state</c></summary>
    GameContext State { get; }
    new string Name { get; set; }
    bool IsOriginalCapital { get; }
    bool IsPuppet { get; }
    bool IsBeingRazed { get; set; }
    void AnnexCity();
    /// <summary><c>city.population.population</c></summary>
    int Population { get; }
    /// <summary><c>city.tiles</c> (a set of positions).</summary>
    IReadOnlyCollection<HexCoord> Tiles { get; }
    new IEnumerable<Tile> GetTiles();
    new IEnumerable<Tile> GetWorkedTiles();
    void StopWorkingTile(Tile tile);
    /// <summary><c>city.cityStats.update(updateCivStats)</c></summary>
    void UpdateCityStats(bool updateCivStats);
    /// <summary><c>city.cityConstructions.builtBuildingUniqueMap</c></summary>
    UniqueMap BuiltBuildingUniqueMap { get; }
    /// <summary><c>city.cityConstructions.getBuiltBuildings()</c></summary>
    IEnumerable<Building> GetBuiltBuildings();
    void AddBuilding(Building building);
    void RemoveBuilding(Building building);
    /// <summary><c>city.cityConstructions.chooseNextConstruction()</c></summary>
    void ChooseNextConstruction();
    IEnumerable<UniqueObject> GetMatchingUniquesWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext);
    void ForEachMatchingUniqueWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op);
}
