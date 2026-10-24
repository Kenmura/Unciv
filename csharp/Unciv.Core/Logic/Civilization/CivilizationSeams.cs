using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
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
    /// <summary><c>gameInfo.notifyExploredResources(civInfo, resource, maxDistance)</c></summary>
    void NotifyExploredResources(Civilization civInfo, TileResource resource, int maxDistance);
    /// <summary>
    /// <c>gameInfo.getCivsSorted(includeCityStates, includeDefeated, civToSortFirst, additionalFilter)</c> (T059: DiplomacyFunctions.getKnownCivsSorted).
    /// Barbarians and spectators are always excluded; the sort (the player's civ first, then majors, then by translated name) belongs to GameInfo.
    /// </summary>
    IEnumerable<Civilization> GetCivsSorted(bool includeCityStates = true, bool includeDefeated = false, Civilization? civToSortFirst = null, Func<Civilization, bool>? additionalFilter = null);
}

/// <summary>What Civilization reads from <c>QuestManager</c>.</summary>
public interface IQuestManager
{
    IQuestManager Clone();
    void SetTransients(Civilization civInfo);
    /// <summary><c>questManager.justMet(otherCiv)</c> (T059: DiplomacyFunctions.meetCiv)</summary>
    void JustMet(Civilization otherCiv);
}

/// <summary>What Civilization reads from <c>CityStateFunctions</c>.</summary>
public interface ICityStateFunctions
{
    /// <summary><c>cityStateFunctions.getTributeWillingness(demandingCiv, demandingWorker)</c> (T059: DiplomacyManager.relationshipLevel)</summary>
    int GetTributeWillingness(Civilization demandingCiv, bool demandingWorker = false);
    /// <summary><c>cityStateFunctions.updateAllyCivForCityState()</c> (T059: DiplomacyManager.setInfluence)</summary>
    void UpdateAllyCivForCityState();
    /// <summary><c>cityStateFunctions.canProvideStat(statType)</c> (T059: DiplomacyFunctions.meetCiv)</summary>
    bool CanProvideStat(Stat statType);
    IEnumerable<UniqueObject> GetUniquesProvidedByCityStates(UniqueType uniqueType, GameContext gameContext);
    void ForEachUniqueProvidedByCityStates(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op);
    /// <summary><c>cityStateFunctions.getCityStateResourcesForAlly()</c> (T058).</summary>
    ResourceSupplyList GetCityStateResourcesForAlly();
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
    /// <summary><c>unit.currentTile</c> (T059: getOwner() of the tile the unit stands on)</summary>
    Tile CurrentTile { get; }
    /// <summary><c>unit.movement.teleportToClosestMoveableTile()</c> (T059: DiplomacyManager.updateHasOpenBorders / makePeaceOneSide)</summary>
    void TeleportToClosestMoveableTile();
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
    // T058
    /// <summary><c>unit.getMatchingUniques(uniqueType, gameContext)</c></summary>
    IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null);
    /// <summary><c>unit.viewableTiles</c></summary>
    IEnumerable<Tile> ViewableTiles { get; }
    bool CanGarrison();
    /// <summary><c>unit.getResourceRequirementsPerTurn()</c></summary>
    Counter<string> GetResourceRequirementsPerTurn();
}

/// <summary>What the religion managers read from <c>CityReligionManager</c> (<c>city.religion</c>).</summary>
public interface ICityReligionManager
{
    string? ReligionThisIsTheHolyCityOf { get; set; }
    void AddPressure(string religionName, int amount, bool shouldUpdateFollowers = true);
    Religion? GetMajorityReligion();
    /// <summary><c>city.religion.getMajorityReligionName()</c> (T059: DiplomacyManager)</summary>
    string? GetMajorityReligionName();
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
    /// <summary><c>city.getCenterTile()</c> (T059: DiplomacyFunctions.meetCiv)</summary>
    new Tile GetCenterTile();
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
    /// <summary><c>city.getGreatPersonPoints()</c></summary>
    Counter<string> GetGreatPersonPoints();
    /// <summary><c>city.cityConstructions.editQueue(edit)</c>: edits a copy of the queue and then replaces the queue by it.</summary>
    void EditConstructionQueue(Action<List<string>> edit);
    /// <summary><c>city.cityConstructions.validateInProgressConstructions()</c></summary>
    void ValidateInProgressConstructions();
    IEnumerable<UniqueObject> GetMatchingUniquesWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext);
    void ForEachMatchingUniqueWithNonLocalEffects(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op);
    // T058
    /// <summary><c>city.connectedToCapitalStatus</c></summary>
    bool ConnectedToCapitalStatus { get; set; }
    /// <summary><c>city.containsBuildingUnique(uniqueType)</c></summary>
    bool ContainsBuildingUnique(UniqueType uniqueType);
    bool IsBlockaded();
    ResourceSupplyList GetResourcesGeneratedByCity();
    /// <summary><c>city.cityStats.finalStatList</c> (a <c>LinkedHashMap</c>), in map order.</summary>
    IEnumerable<KeyValuePair<string, Stats>> FinalStatList { get; }
    /// <summary><c>city.cityStats.happinessList</c> (a <c>LinkedHashMap</c>), in map order.</summary>
    IEnumerable<KeyValuePair<string, float>> HappinessList { get; }
    IEnumerable<UniqueObject> GetLocalMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null);
    /// <summary><c>city.cityConstructions.freeBuildingsProvidedFromThisCity</c> (a <c>HashMap&lt;String, HashSet&lt;String&gt;&gt;</c>).</summary>
    Dictionary<string, HashSet<string>> FreeBuildingsProvidedFromThisCity { get; }
    /// <summary><c>city.cityConstructions.cheapestStatBuilding(stat)</c></summary>
    Building? CheapestStatBuilding(Stat stat);
    /// <summary><c>city.cityConstructions.completeConstruction(building)</c></summary>
    void CompleteConstruction(Building building);
    /// <summary><c>city.cityConstructions.isBeingConstructedOrEnqueued(name)</c></summary>
    bool IsBeingConstructedOrEnqueued(string constructionName);
    /// <summary><c>building.isBuildable(city.cityConstructions)</c></summary>
    bool IsBuildable(Building building);
}
