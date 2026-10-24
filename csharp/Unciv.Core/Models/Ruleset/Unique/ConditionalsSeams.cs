using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Metadata;
using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset.Unique;

// The members of GameInfo, Civilization, City, MapUnit, Tile, Region, the combatants and Ruleset that Conditionals.kt, Countables.kt and
// the expression Nodes read, as partial extensions of the interfaces GameContext.cs declares (see the comment there).
// The classes are ported from T046 onwards and implement these; nothing in Conditionals or Countables changes then.
// Member names mirror the Kotlin members; a Kotlin lambda over a collection that is only iterated is kept as an enumerable so
// the iteration (and its order) stays where Kotlin has it.

/// <summary>What the game-based conditionals read from <c>Ruleset</c> beyond <see cref="IRulesetParameterView"/>.</summary>
public interface IGameContextRuleset : IRulesetParameterView
{
    /// <summary><c>ruleset.eras[eraName]?.eraNumber</c></summary>
    int? GetEraNumber(string eraName);
    /// <summary><c>ruleset.difficulties.keys</c>, in declaration order.</summary>
    IReadOnlyList<string> DifficultyNames { get; }
    /// <summary><c>ruleset.buildings.values.filter { it.matchesFilter(filter) }.map { it.name }</c>, in ruleset order.</summary>
    IEnumerable<string> GetBuildingNamesMatchingFilter(string filter);
    /// <summary><c>ruleset.tileResources.values.filter { it.matchesFilter(filter, state) }.map { it.name }</c>, in ruleset order.</summary>
    IEnumerable<string> GetTileResourceNamesMatchingFilter(string filter, GameContext state);
    /// <summary><c>ruleset.technologies[techName]?.matchesFilter(filter, state)</c>; <c>null</c> when there is no such technology.</summary>
    bool? TechnologyMatchesFilter(string techName, string filter, GameContext state);
}

/// <summary>What the countables read from the game speed (<c>Speed</c>), all Kotlin <c>Float</c>.</summary>
public interface IGameContextSpeed
{
    float Modifier { get; }
    float GoldCostModifier { get; }
    float ProductionCostModifier { get; }
    float ScienceCostModifier { get; }
    float CultureCostModifier { get; }
    float FaithCostModifier { get; }
    /// <summary><c>speed.statCostModifiers[stat]!!</c></summary>
    float GetStatCostModifier(Stat stat);
}

/// <summary>What the conditionals read from <c>Religion</c>.</summary>
public interface IGameContextReligion
{
    /// <summary><c>religion.matchesFilter(filter, state, civ)</c></summary>
    bool MatchesFilter(string filter, GameContext state, IGameContextCivilization? civ);
    bool IsMajorReligion();
    bool IsEnhancedReligion();
    bool HasBelief(string belief);
}

public partial interface IGameContextGameInfo
{
    IGameContextRuleset Ruleset { get; }
    GameParameters GameParameters { get; }
    IGameContextSpeed Speed { get; }
    /// <summary><c>getDifficulty().name</c></summary>
    string GetDifficultyName();
    bool IsReligionEnabled();
    bool IsEspionageEnabled();
    /// <summary><c>getYear(turnOffset)</c></summary>
    int GetYear(int turnOffset);
    IReadOnlyList<IGameContextCivilization> Civilizations { get; }
    /// <summary><c>getCities()</c></summary>
    IEnumerable<IGameContextCity> GetCities();
    /// <summary><c>tileMap.tileList</c>, or <c>null</c> when there is no tile map.</summary>
    IEnumerable<IGameContextTile>? TileList { get; }
}

public partial interface IGameContextCivilization
{
    IReadOnlyList<IGameContextCity> Cities { get; }
    bool IsMajorCiv();
    bool IsAlive();
    bool IsCityState { get; }
    bool IsAtWar();
    /// <summary><c>civ.stats.happiness</c></summary>
    int Happiness { get; }
    /// <summary><c>civ.goldenAges.isGoldenAge()</c></summary>
    bool IsGoldenAge();
    int GetEraNumber();
    /// <summary><c>civ.matchesFilter(filter, state)</c></summary>
    bool MatchesFilter(string filter, GameContext? state);
    IGameContextCity? GetCapital();
    IEnumerable<IGameContextCivilization> GetKnownCivs();

    /// <summary><c>civ.tech.isResearched(techName)</c></summary>
    bool IsTechResearched(string techName);
    /// <summary><c>civ.tech.researchedTechnologies.any { it.matchesFilter(filter) }</c></summary>
    bool AnyResearchedTechnologyMatchesFilter(string filter);
    /// <summary><c>civ.tech.currentTechnology()?.matchesFilter(filter) == true</c></summary>
    bool CurrentTechnologyMatchesFilter(string filter);
    /// <summary><c>civ.tech.techsResearched</c> (a set of names; only counted).</summary>
    IEnumerable<string> TechsResearched { get; }

    /// <summary><c>civ.policies.isAdopted(policyName)</c></summary>
    bool IsPolicyAdopted(string policyName);
    /// <summary><c>civ.policies.getAdoptedPoliciesMatching(filter, state).count()</c></summary>
    int CountAdoptedPoliciesMatching(string filter, GameContext state);
    int GetCompletedPolicyBranchesCount();

    /// <summary><c>civ.religionManager.religion</c></summary>
    IGameContextReligion? Religion { get; }
    ReligionState ReligionState { get; }
    int GreatProphetsEarned();

    /// <summary><c>civ.units.getCivUnitsSize()</c></summary>
    int GetCivUnitsSize();
    /// <summary><c>civ.units.getCivUnits()</c></summary>
    IEnumerable<IGameContextUnit> GetCivUnits();

    /// <summary><c>civ.stats.getStatMapForNextTurn().values.map { it[stat] }</c>, in map order.</summary>
    IEnumerable<float> GetStatMapForNextTurnValues(Stat stat);
    /// <summary><c>civ.getCivResourceSupply().sumBy(resource)</c></summary>
    int GetCivResourceSupplyAmount(string resourceName);
}

public partial interface IGameContextCity
{
    /// <summary><c>city.matchesFilter(filter, viewingCiv)</c></summary>
    bool MatchesFilter(string filter, IGameContextCivilization? viewingCiv);
    /// <summary><c>city.cityConstructions.containsBuildingOrEquivalent(name)</c></summary>
    bool ContainsBuildingOrEquivalent(string buildingName);
    /// <summary><c>city.cityConstructions.builtBuildings</c> (a set of names).</summary>
    IReadOnlySet<string> BuiltBuildings { get; }
    /// <summary><c>city.cityConstructions.getBuiltBuildings().count { it.matchesFilter(filter, state) }</c></summary>
    int CountBuiltBuildingsMatchingFilter(string filter, GameContext state);
    /// <summary><c>city.cityStats.currentCityStats[stat]</c></summary>
    float GetCurrentCityStat(Stat stat);
    /// <summary><c>city.getResourcesGeneratedByCity().sumBy(resource)</c></summary>
    int GetResourcesGeneratedByCityAmount(string resourceName);
    bool IsConnectedToCapital();
    bool IsWeLoveTheKingDayActive();
    /// <summary><c>city.religion.getMajorityReligion()</c></summary>
    IGameContextReligion? GetMajorityReligion();
    /// <summary><c>city.population.getPopulationFilterAmount(filter)</c></summary>
    int GetPopulationFilterAmount(string filter);
    /// <summary><c>city.getCenterTile().militaryUnit?.canGarrison() == true</c></summary>
    bool CenterTileMilitaryUnitCanGarrison();
    IEnumerable<IGameContextTile> GetWorkedTiles();
    IEnumerable<IGameContextTile> GetTiles();
}

public partial interface IGameContextUnit
{
    bool MatchesFilter(string filter);
    /// <summary><c>unit.promotions.promotions.contains(name) || unit.hasStatus(name)</c></summary>
    bool HasPromotionOrStatus(string name);
    int Health { get; }
    bool IsTransported { get; }
    /// <summary><c>unit.abilityToTimesUsed.isEmpty()</c></summary>
    bool AbilityToTimesUsedIsEmpty { get; }
}

public partial interface IGameContextTile
{
    /// <summary><c>tile.matchesFilter(filter, civ)</c></summary>
    bool MatchesFilter(string filter, IGameContextCivilization? civ);
    /// <summary><c>tile.isAdjacentTo(filter, civ)</c></summary>
    bool IsAdjacentTo(string filter, IGameContextCivilization? civ);
    IEnumerable<IGameContextTile> GetTilesInDistance(int distance);
    IEnumerable<IGameContextUnit> GetUnits();
    /// <summary><c>tile.airUnits</c></summary>
    IEnumerable<IGameContextUnit> AirUnits { get; }
    IGameContextUnit? MilitaryUnit { get; }
    int GetContinent();
}

public partial interface IGameContextCombatant
{
    /// <summary><c>combatant.matchesFilter(filter, multiFilter)</c></summary>
    bool MatchesFilter(string filter, bool multiFilter);
    int GetHealth();
}

public partial interface IGameContextRegion
{
    int ContinentId { get; }
    string Type { get; }
}
