using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Logic.City;

// T060 (City): the seams of City, which is ported BEFORE its sub-objects (seam rule, T053 notes). City.kt owns a CityPopulationManager,
// CityConstructions, CityExpansionManager, CityReligionManager (T061/T063) and a CityStats (T062); each interface holds exactly the
// members City.kt, CityResources.kt or the already ported code (through the former ICityState and the IGameContextCity seams) call on that
// object. The ticket that ports the class implements the interface, retypes the City property to the concrete class and replaces the
// lateinit-style property by Kotlin's initializer (`= new CityPopulationManager()`), then drops the members only the seam needed.
// Member names mirror the Kotlin members.

/// <summary>What City reads from <c>CityConstructions</c> (<c>city.cityConstructions</c>). TODO(T063)</summary>
public interface ICityConstructions
{
    /// <summary><c>cityConstructions.city</c> (assigned by <c>City.setTransients</c>).</summary>
    City City { get; set; }
    void SetTransients();
    ICityConstructions Clone();
    /// <summary><c>cityConstructions.builtBuildingUniqueMap</c></summary>
    UniqueMap BuiltBuildingUniqueMap { get; }
    /// <summary><c>cityConstructions.getBuiltBuildings()</c>, in the order of the built building set.</summary>
    IEnumerable<Building> GetBuiltBuildings();
    /// <summary><c>cityConstructions.builtBuildings</c> (a set of names).</summary>
    IReadOnlySet<string> BuiltBuildings { get; }
    bool ContainsBuildingOrEquivalent(string buildingName);
    void AddProductionPoints(int productionToAdd);
    int GetWorkDone(string constructionName);
    /// <summary><c>cityConstructions.getCurrentConstruction()</c></summary>
    IConstruction GetCurrentConstruction();
    void AddBuilding(Building building);
    void RemoveBuilding(Building building);
    void ChooseNextConstruction();
    /// <summary><c>cityConstructions.editQueue(edit)</c>: edits a copy of the queue and then replaces the queue by it.</summary>
    void EditQueue(Action<List<string>> edit);
    void ValidateInProgressConstructions();
    /// <summary><c>cityConstructions.freeBuildingsProvidedFromThisCity</c> (a <c>HashMap&lt;String, HashSet&lt;String&gt;&gt;</c>).</summary>
    Dictionary<string, HashSet<string>> FreeBuildingsProvidedFromThisCity { get; }
    Building? CheapestStatBuilding(Stat stat);
    void CompleteConstruction(Building building);
    bool IsBeingConstructedOrEnqueued(string constructionName);
    /// <summary><c>building.isBuildable(cityConstructions)</c></summary>
    bool IsBuildable(Building building);
    void RemoveCreateOneImprovementConstruction(string improvement);
}

/// <summary>What City reads from <c>CityStats</c> (<c>city.cityStats</c>). TODO(T062)</summary>
public interface ICityStats
{
    /// <summary><c>cityStats.update(currentConstruction, updateTileStats, updateCivStats, calculateGrowthModifiers)</c>; <c>null</c> is the city's current construction.</summary>
    void Update(IConstruction? currentConstruction = null, bool updateTileStats = true, bool updateCivStats = true, bool calculateGrowthModifiers = true);
    /// <summary><c>cityStats.currentCityStats</c></summary>
    Stats CurrentCityStats { get; }
    /// <summary><c>cityStats.hasExtraAnnexUnhappiness()</c></summary>
    bool HasExtraAnnexUnhappiness();
    /// <summary><c>cityStats.finalStatList</c> (a <c>LinkedHashMap</c>), in map order.</summary>
    IEnumerable<KeyValuePair<string, Stats>> FinalStatList { get; }
    /// <summary><c>cityStats.happinessList</c> (a <c>LinkedHashMap</c>), in map order.</summary>
    IEnumerable<KeyValuePair<string, float>> HappinessList { get; }
    /// <summary>
    /// <c>GreatPersonPointsBreakdown(city).sum()</c>. A seam member only because GreatPersonPointsBreakdown is ported with CityStats (T062),
    /// which then calls it from <c>City.GetGreatPersonPoints</c> directly and drops this member.
    /// </summary>
    Counter<string> GetGreatPersonPoints();
}

/// <summary>What City reads from <c>CityPopulationManager</c> (<c>city.population</c>). TODO(T061)</summary>
public interface ICityPopulationManager
{
    /// <summary><c>population.city</c> (assigned by <c>City.setTransients</c>).</summary>
    City City { get; set; }
    ICityPopulationManager Clone();
    /// <summary><c>population.population</c></summary>
    int Population { get; }
    /// <summary><c>population.foodStored</c></summary>
    int FoodStored { get; set; }
    /// <summary><c>population.specialistAllocations</c></summary>
    Counter<string> SpecialistAllocations { get; }
    void AutoAssignPopulation();
    void UnassignExtraPopulation();
    int GetPopulationFilterAmount(string filter);
}

/// <summary>What City reads from <c>CityExpansionManager</c> (<c>city.expansion</c>). TODO(T061)</summary>
public interface ICityExpansionManager
{
    /// <summary><c>expansion.city</c> (assigned by <c>City.setTransients</c>).</summary>
    City City { get; set; }
    void SetTransients();
    ICityExpansionManager Clone();
    /// <summary><c>expansion.relinquishOwnership(tile)</c></summary>
    void RelinquishOwnership(Tile tile);
}

/// <summary>
/// What City stores of <c>UnitPromotions</c> (<c>city.unitToPromotions</c> values; only copied by <c>City.clone</c>). TODO(T066): replaced by UnitPromotions.
/// </summary>
public interface IUnitPromotions;
