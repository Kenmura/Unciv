using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization;

// T055 (Civilization): the contracts Civilization has with GameInfo, its managers, City and MapUnit. Each interface holds exactly the
// members Civilization.kt (or an existing IGameContext* interface that Civilization implements) calls on that type; the class implements
// it, and tests fake it.
// Member names mirror the Kotlin members. Where a Kotlin member is a Sequence that is only iterated, an IEnumerable is returned.

/// <summary>What Civilization reads from <c>GameInfo</c>, on top of <see cref="IConstructionGameInfo"/> (GameInfo implements both).</summary>
public interface IGameInfoState : IConstructionGameInfo
{
    /// <summary>T186: where the engine reports events; <c>null</c> (the default) when nobody listens.</summary>
    GameInfoEventSink? EventSink => null;
    /// <summary>B016: the percent off the cost of a tech for <paramref name="civ"/> from the enacted Scholars in Residence resolution (0 without a league).</summary>
    int LeagueTechDiscountPercent(Civilization civ, string techName) => 0;
    /// <summary>B016: the percent added to the spread strength of a religion by the enacted World Religion resolutions (0 without a league).</summary>
    int LeagueReligionSpreadPercent(string? religion) => 0;
    /// <summary><c>gameInfo.ruleset</c></summary>
    new Ruleset Ruleset { get; }
    /// <summary><c>gameInfo.speed</c></summary>
    new Speed Speed { get; }
    /// <summary><c>gameInfo.civilizations</c></summary>
    new IReadOnlyList<Civilization> Civilizations { get; }
    /// <summary><c>gameInfo.currentPlayerCiv</c></summary>
    Civilization? CurrentPlayerCiv { get; }
    /// <summary>T201: whether turns are simultaneous (<see cref="GameInfo.TurnMode"/>); a game state without turn modes is Sequential.</summary>
    bool IsSimultaneousTurns { get; }
    /// <summary>T201: <see cref="GameInfo.IsActive"/>; only asked in simultaneous games.</summary>
    bool IsCivActive(Civilization civ);
    /// <summary><c>gameInfo.isSimulation()</c></summary>
    bool IsSimulation();
    /// <summary><c>gameInfo.getGlobalUniques()</c></summary>
    GlobalUniques GetGlobalUniques();
    /// <summary><c>gameInfo.diplomaticVictoryVotesCast</c> (<c>HashMap&lt;String, String?&gt;</c>).</summary>
    JavaHashMap<string, string?> DiplomaticVictoryVotesCast { get; }
    /// <summary><c>gameInfo.religions</c> (<c>HashMap&lt;String, Religion&gt;</c>), in java.util.HashMap order.</summary>
    JavaHashMap<string, Religion> Religions { get; }
    /// <summary><c>gameInfo.getCivilization(civID)</c>; throws when there is no such civilization (Kotlin <c>first</c>).</summary>
    new Civilization GetCivilization(string civId);
    /// <summary><c>gameInfo.getCivilizationOrNull(civID)</c> (T069: QuestImplementation)</summary>
    Civilization? GetCivilizationOrNull(string civId);
    /// <summary><c>gameInfo.getCities()</c>, in civilization then city order.</summary>
    new IEnumerable<CityObject> GetCities();
    /// <summary><c>gameInfo.getAliveCityStates()</c> (T060: City.destroyCity)</summary>
    List<Civilization> GetAliveCityStates();
    /// <summary><c>gameInfo.getAliveMajorCivs()</c></summary>
    List<Civilization> GetAliveMajorCivs();
    /// <summary><c>gameInfo.barbarians</c> (T065: MapUnit.clearEncampment)</summary>
    BarbarianManager Barbarians { get; }
    /// <summary><c>gameInfo.getBarbarianCivilization()</c> (T078); throws when there is no barbarian civilization (Kotlin <c>first</c>).</summary>
    Civilization GetBarbarianCivilization();
    /// <summary><c>gameInfo.notifyExploredResources(civInfo, resource, maxDistance)</c></summary>
    void NotifyExploredResources(Civilization civInfo, TileResource resource, int maxDistance);
    /// <summary>
    /// <c>gameInfo.getCivsSorted(includeCityStates, includeDefeated, civToSortFirst, additionalFilter)</c> (T059: DiplomacyFunctions.getKnownCivsSorted).
    /// Barbarians and spectators are always excluded; the sort (the player's civ first, then majors, then by translated name) belongs to GameInfo.
    /// </summary>
    IEnumerable<Civilization> GetCivsSorted(bool includeCityStates = true, bool includeDefeated = false, Civilization? civToSortFirst = null, Func<Civilization, bool>? additionalFilter = null);
    // T063 (CityConstructions, Building, BaseUnit)
    /// <summary><c>gameInfo.isUnavailableBySettingsCached(obj)</c></summary>
    bool IsUnavailableBySettingsCached(IHasUniques construction);
    /// <summary><c>gameInfo.spaceResources</c></summary>
    IReadOnlySet<string> SpaceResources { get; }
    /// <summary>T183: the per-civ state-affecting settings that Kotlin reads from <c>UncivGame.Current.settings</c>.</summary>
    Unciv.Logic.PlayerSettings GetPlayerSettings(string civId);
    /// <summary><c>UncivGame.Current.worldScreen?.autoPlay?.isAutoPlayingAndFullAutoPlayAI() == true</c> (client state).</summary>
    bool IsFullAutoPlayAI { get; }
    /// <summary><c>UncivGame.Current.settings.getCollatorFromLocale()</c> (client setting; T126 PromotionTree).</summary>
    IComparer<string> Collator { get; }
    /// <summary><c>gameInfo.getNextUnitId()</c> (T066: BaseUnit.newMapUnit)</summary>
    int GetNextUnitId();
    /// <summary><c>GUI.getSettings().alternateUnitCycleOrder</c> (client setting).</summary>
    bool AlternateUnitCycleOrder { get; }
    /// <summary><c>UncivGame.Current.settings.addCompletedTutorialTask(task)</c> (client setting; T066: Tile.doWorkerTurn).</summary>
    void AddCompletedTutorialTask(string civId, string task);
    /// <summary><c>gameInfo.unitNamesTaken</c> (T052: OneTimeUnitGetsName)</summary>
    List<string> UnitNamesTaken { get; }
    // T070 (TurnManager)
    /// <summary><c>gameInfo.tileMap</c></summary>
    new TileMap TileMap { get; }
    /// <summary><c>gameInfo.victoryData</c></summary>
    VictoryData? VictoryData { get; set; }
    /// <summary><c>gameInfo.processDiplomaticVictory()</c> (GameInfo turn handling, T071).</summary>
    void ProcessDiplomaticVictory();
    /// <summary>The client settings and hooks that stand in for <c>UncivGame.Current</c> (T046).</summary>
    GameClientState ClientState { get; }
    // B020 (BNW trade routes): not in Kotlin; a state that is not a real game has no routes and ignores writes
    /// <summary><see cref="GameInfo.TradeRoutes"/></summary>
    List<TradeRoute>? TradeRoutes { get => null; set { } }
    /// <summary><see cref="GameInfo.LastTradeRouteId"/></summary>
    int LastTradeRouteId { get => 0; set { } }
    /// <summary><see cref="GameInfo.EndedTradeRoutes"/></summary>
    List<EndedTradeRoute>? EndedTradeRoutes { get => null; set { } }
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
    /// <summary>
    /// <c>cityStateFunctions.initCityState(ruleset, startingEra, usedMajorCivs, rng)</c> (T099: GameStarter.addCivilizations). Returns true if successful.
    /// </summary>
    bool InitCityState(Ruleset ruleset, string startingEra, IEnumerable<NationObject> usedMajorCivs, GameRandom rng);
    // T068 (DeclareWar, DiplomacyTurnManager); the concrete CityStateFunctions implements all of these
    /// <summary><c>cityStateFunctions.getNotificationActions()</c></summary>
    IEnumerable<NotificationAction> GetNotificationActions();
    /// <summary><c>cityStateFunctions.cityStateAttacked(attacker)</c></summary>
    void CityStateAttacked(Civilization attacker);
    /// <summary><c>cityStateFunctions.getProtectorCivs()</c></summary>
    List<Civilization> GetProtectorCivs();
    /// <summary><c>cityStateFunctions.removeProtectorCiv(otherCiv, forced)</c></summary>
    void RemoveProtectorCiv(Civilization otherCiv, bool forced = false);
    /// <summary><c>cityStateFunctions.giveMilitaryUnitToPatron(receivingCiv)</c></summary>
    void GiveMilitaryUnitToPatron(Civilization receivingCiv);
    /// <summary><c>cityStateFunctions.askForUnitGifts(attacker)</c></summary>
    void AskForUnitGifts(Civilization attacker);
    /// <summary><c>cityStateFunctions.getNumThreateningBarbarians()</c> (T069: QuestManager.tryBarbarianInvasion)</summary>
    int GetNumThreateningBarbarians();
    // T070 (TurnManager)
    /// <summary><c>cityStateFunctions.holdElections()</c></summary>
    void HoldElections();
    /// <summary><c>cityStateFunctions.turnsForGreatPersonFromCityState()</c></summary>
    int TurnsForGreatPersonFromCityState();
    /// <summary><c>cityStateFunctions.giveGreatPersonToPatron(receivingCiv)</c></summary>
    void GiveGreatPersonToPatron(Civilization receivingCiv);
    // T083 (Battle)
    /// <summary><c>cityStateFunctions.threateningBarbarianKilledBy(otherCiv)</c></summary>
    void ThreateningBarbarianKilledBy(Civilization otherCiv);
    /// <summary><c>cityStateFunctions.cityStateDestroyed(attacker)</c></summary>
    void CityStateDestroyed(Civilization attacker);
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
