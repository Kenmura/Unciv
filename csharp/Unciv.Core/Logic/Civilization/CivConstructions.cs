using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Logic.Civilization;

// Ported from core/src/com/unciv/logic/civilization/CivConstructions.kt (T058).
//
// Deviations:
// - City and CityConstructions are not ported yet (T060/T063): they are the ICityState seam (CivilizationSeams.cs), retyped then.
// - The three private HashMap<String, HashSet<String>> maps are System Dictionary/HashSet (as the AddToMapOfSets helper is), so the
//   iteration order of getFreeBuildingNames' result is insertion order, not java.util.HashSet order; nothing in the rules iterates it.
// - The Kotlin lazy `sequence { ... }` of free building names is a C# iterator; `hasFreeBuildingByName` stops at the first match like the
//   Kotlin sequence does.
// - @Readonly purity annotations are dropped. No serialization attributes yet: GameInfo serialization (T046) owns the field mapping.

public class CivConstructions : IsPartOfGameInfoSerialization
{
    private Civilization? civInfo;

    /// <summary>Kotlin <c>@Transient lateinit var civInfo</c>: reading before <see cref="SetTransients"/> throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        private set => civInfo = value;
    }

    /// <summary>Maps construction names to the amount of times bought</summary>
    public Counter<string> BoughtItemsWithIncreasingPrice { get; private set; } = new();

    /// <summary>Maps construction names to the amount of times built</summary>
    public Counter<string> BuiltItemsWithIncreasingCost { get; private set; } = new();

    /// <summary>
    /// Maps cities by id to a set of all free buildings by name they contain.
    /// The building name is the Nation-specific equivalent if available.
    /// Sources: <see cref="UniqueType.FreeStatBuildings"/> **and** <see cref="UniqueType.FreeSpecificBuildings"/>
    /// This is persisted and _never_ cleared or elements removed (per civ and game).
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> freeBuildings = [];

    /// <summary>
    /// Maps stat names to a set of cities by id that have received a building of that stat.
    /// Source: <see cref="UniqueType.FreeStatBuildings"/>
    /// This is persisted and _never_ cleared or elements removed (per civ and game).
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> freeStatBuildingsProvided = [];

    /// <summary>
    /// Maps buildings by name to a set of cities by id that have received that building.
    /// The building name is the Nation-specific equivalent if available.
    /// Source: <see cref="UniqueType.FreeSpecificBuildings"/>
    /// This is persisted and _never_ cleared or elements removed (per civ and game).
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> freeSpecificBuildingsProvided = [];

    public CivConstructions Clone()
    {
        var toReturn = new CivConstructions { civInfo = civInfo };
        foreach (var (key, value) in freeBuildings) toReturn.freeBuildings[key] = [.. value];
        foreach (var (key, value) in freeStatBuildingsProvided) toReturn.freeStatBuildingsProvided[key] = [.. value];
        foreach (var (key, value) in freeSpecificBuildingsProvided) toReturn.freeSpecificBuildingsProvided[key] = [.. value];
        toReturn.BoughtItemsWithIncreasingPrice.Add(BoughtItemsWithIncreasingPrice); // add copies
        toReturn.BuiltItemsWithIncreasingCost.Add(BuiltItemsWithIncreasingCost);
        return toReturn;
    }

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
    }

    // IConstructionCivilization seam members (Civilization reads the counters by name)
    public int GetBuiltItemsWithIncreasingCost(string name) => BuiltItemsWithIncreasingCost[name];
    public int GetBoughtItemsWithIncreasingPrice(string name) => BoughtItemsWithIncreasingPrice[name];

    public void StartTurn()
    {
        TryAddFreeBuildings();
    }

    public void TryAddFreeBuildings()
    {
        AddFreeStatsBuildings();
        AddFreeSpecificBuildings();
        AddFreeBuildings();
    }

    /// <summary>
    /// Common to <see cref="HasFreeBuildingByName"/> and <see cref="GetFreeBuildingNames"/> - 'has' doesn't need the whole set, one enumeration is enough.
    /// Note: Operates on String city.id and String building name, close to the serialized and stored form.
    /// When/if we do a transient cache for these using our objects, please rewrite this.
    /// </summary>
    private IEnumerable<string> GetFreeBuildingNamesSequence(string cityId)
    {
        if (freeBuildings.TryGetValue(cityId, out var own))
            foreach (var name in own) yield return name;
        foreach (var city in CivInfo.Cities)
            if (city.FreeBuildingsProvidedFromThisCity.TryGetValue(cityId, out var provided))
                foreach (var name in provided) yield return name;
    }

    /// <summary>Gets a Set of all building names the <paramref name="city"/> has for free, from nationwide sources or buildings in other cities</summary>
    public LinkedHashSet<string> GetFreeBuildingNames(ICityState city) =>
        new(GetFreeBuildingNamesSequence(city.Id));

    /// <summary>Tests whether the <paramref name="city"/> has <paramref name="building"/> for free, from nationwide sources or buildings in other cities</summary>
    public bool HasFreeBuilding(ICityState city, Building building) =>
        HasFreeBuildingByName(city.Id, building.Name);

    /// <summary>Tests whether a city by <paramref name="cityId"/> has a building named <paramref name="buildingName"/> for free, from nationwide sources or buildings in other cities</summary>
    private bool HasFreeBuildingByName(string cityId, string buildingName) =>
        GetFreeBuildingNamesSequence(cityId).Contains(buildingName);

    private void AddFreeBuilding(string cityId, string building)
    {
        freeBuildings.AddToMapOfSets(cityId, CivInfo.GetEquivalentBuilding(building).Name);
    }

    private void AddFreeStatsBuildings()
    {
        // groupBy keeps first-occurrence order of the keys (LinkedHashMap)
        var statUniquesData = CivInfo.GetMatchingUniques(UniqueType.FreeStatBuildings)
            .Where(it => !it.HasTriggerConditional())
            .GroupBy(it => it.Params[0])
            .Select(group => (Stat: StatExtensions.ValueOf(group.Key), Amount: group.SumOf(it => it.Params[1].ToInt())))
            .ToList();

        foreach (var (stat, amount) in statUniquesData)
            AddFreeStatBuildings(stat, amount);
    }

    public void AddFreeStatBuildings(Stat stat, int amount)
    {
        foreach (var city in CivInfo.Cities.Take(amount))
        {
            if (freeStatBuildingsProvided.Contains(stat.ToString(), city.Id)) continue;
            var building = city.CheapestStatBuilding(stat);
            if (building == null) continue;

            freeStatBuildingsProvided.AddToMapOfSets(stat.ToString(), city.Id);
            AddFreeBuilding(city.Id, building.Name);
            city.CompleteConstruction(building);
        }
    }

    private void AddFreeSpecificBuildings()
    {
        var buildingsUniquesData = CivInfo.GetMatchingUniques(UniqueType.FreeSpecificBuildings)
            .Where(it => !it.HasTriggerConditional())
            .GroupBy(it => it.Params[0])
            .Select(group => (Building: group.Key, Amount: group.SumOf(it => it.Params[1].ToInt())))
            .ToList();

        foreach (var (building, amount) in buildingsUniquesData)
        {
            var civBuildingEquivalent = CivInfo.GetEquivalentBuilding(building);
            AddFreeBuildings(civBuildingEquivalent, amount);
        }
    }

    public void AddFreeBuildings(Building building, int amount)
    {
        var equivalentBuilding = CivInfo.GetEquivalentBuilding(building);
        foreach (var city in CivInfo.Cities.Take(amount))
        {
            if (freeSpecificBuildingsProvided.Contains(equivalentBuilding.Name, city.Id)
                || city.ContainsBuildingOrEquivalent(building.Name)) continue;

            freeSpecificBuildingsProvided.AddToMapOfSets(equivalentBuilding.Name, city.Id);
            AddFreeBuilding(city.Id, equivalentBuilding.Name);
            city.CompleteConstruction(equivalentBuilding);
        }
    }

    public void AddFreeBuildings()
    {
        var autoGrantedBuildings = CivInfo.GameInfo.Ruleset.Buildings.Values
            .Where(it => ((IHasUniques)it).HasUnique(UniqueType.GainBuildingWhereBuildable))
            .ToList();

        // "Gain a free [buildingName] [cityFilter]"
        var freeBuildingsFromCiv = CivInfo.GetMatchingUniques(UniqueType.GainFreeBuildings, GameContext.IgnoreConditionalsState).ToList();
        foreach (var city in CivInfo.Cities)
        {
            var freeBuildingsFromCity = city.GetLocalMatchingUniques(UniqueType.GainFreeBuildings, GameContext.IgnoreConditionalsState);
            var freeBuildingUniques = freeBuildingsFromCiv.Concat(freeBuildingsFromCity)
                .Where(it => city.MatchesFilter(it.Params[1], null) && it.ConditionalsApply(city.State)
                    && !it.HasTriggerConditional());

            // When adding a building, the list of applicable free buildings can change! Hence, toList()
            foreach (var unique in freeBuildingUniques.ToList())
            {
                var freeBuilding = city.Civ.GetEquivalentBuilding(unique.Params[0]);
                city.FreeBuildingsProvidedFromThisCity.AddToMapOfSets(city.Id, freeBuilding.Name);

                if (city.ContainsBuildingOrEquivalent(freeBuilding.Name)) continue;
                city.CompleteConstruction(freeBuilding);
            }

            foreach (var building in autoGrantedBuildings)
                if (city.IsBuildable(building))
                    city.CompleteConstruction(building);
        }
    }

    /// <summary>
    /// Calculates a civ-wide total for <paramref name="objectToCount"/>.
    ///
    /// It counts:
    /// * "Spaceship part" units added to "spaceship" in capital
    /// * Built buildings or those in a construction queue
    /// * Units on the map or being constructed
    /// </summary>
    /// <param name="objectToCount">The building or unit to count.</param>
    /// <param name="ignoreQueueOf">If given, that city's own construction queue is not counted,
    /// so an already-enqueued entry doesn't count against itself when its availability is re-checked.</param>
    public int CountConstructedObjects(INonPerpetualConstruction objectToCount, ICityState? ignoreQueueOf = null)
    {
        var amountInSpaceShip = CivInfo.VictoryManager.CurrentsSpaceshipParts[objectToCount.Name];

        return amountInSpaceShip + objectToCount switch
        {
            Building => CivInfo.Cities.Count(it =>
                it.ContainsBuildingOrEquivalent(objectToCount.Name)
                || (!ReferenceEquals(it, ignoreQueueOf) && it.IsBeingConstructedOrEnqueued(objectToCount.Name))),
            BaseUnit => CivInfo.Units.GetCivUnits().Count(it => it.Name == objectToCount.Name) +
                CivInfo.Cities.Count(it => !ReferenceEquals(it, ignoreQueueOf) && it.IsBeingConstructedOrEnqueued(objectToCount.Name)),
            _ => 0,
        };
    }
}
