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
    // T062 (CityStats)
    /// <summary><c>population.getNewSpecialists()</c> (the specialist allocations).</summary>
    Counter<string> GetNewSpecialists();
    /// <summary><c>population.getFreePopulation()</c></summary>
    int GetFreePopulation();
    /// <summary><c>population.getNumberOfSpecialists()</c></summary>
    int GetNumberOfSpecialists();
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
public interface IUnitPromotions
{
    /// <summary><c>promotions.XP</c></summary>
    int XP { get; set; }
    /// <summary><c>promotions.promotions</c> (the promotion names).</summary>
    IEnumerable<string> Promotions { get; }
    /// <summary><c>promotions.xpForNextPromotion()</c></summary>
    int XpForNextPromotion();
    /// <summary><c>promotions.addPromotion(name, isFree)</c></summary>
    void AddPromotion(string promotionName, bool isFree = false);
}
